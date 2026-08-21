using System.IO;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ActionRing.Interop;
using ActionRing.Models;
using ActionRing.Services;

namespace ActionRing.Views;

public partial class RingWindow : Window
{
    private sealed class RingButton
    {
        public required RingAction Action { get; init; }
        public required Point Center { get; init; }
        public required double Radius { get; init; }
        public required FrameworkElement Host { get; init; }
        public required Ellipse Highlight { get; init; }
        public required FrameworkElement Icon { get; init; }
        public required ScaleTransform Scale { get; init; }
    }

    /// <summary>One group's children, plus the layer they fade in and out on.</summary>
    private sealed class SubGroup
    {
        public required int ParentIndex { get; init; }
        public required List<RingButton> Children { get; init; }

        /// <summary>
        /// Where each child sits, clockwise from 12 o'clock. Aiming is done in
        /// angles rather than against the drawn circles, so the fan is kept in
        /// the form the hit test wants.
        /// </summary>
        public required List<double> Angles { get; init; }

        /// <summary>Half the gap between two children - the fan's aiming slack.</summary>
        public required double HalfStep { get; init; }

        public required FrameworkElement Layer { get; init; }
        public required ScaleTransform Scale { get; init; }
    }

    /// <summary>Nothing hovered.</summary>
    private const int None = -1;

    /// <summary>The centre dismiss button, which isn't in the action list.</summary>
    private const int HubIndex = -2;

    private const double ShadowMargin = 18;  // room for the buttons' drop shadows
    private const double HoverScale = 1.12;
    private const double OpenFrom = 0.86;    // scale the ring grows from
    private const double DimmedSibling = 0.8; // buttons outside the open group
    private const double FadedSibling = 0.12; // ...when "fade others" is on
    private const double SubScale = 0.76;    // child button size, relative to a parent
    private const double SubGap = 8;         // clearance between the two orbits
    private const double LabelGap = 10;      // button edge to its label
    private const double CloseGap = 12;      // first orbit to the Close label

    private const string CloseLabel = "Close";

    /// <summary>How often the hold gesture re-reads the keyboard and cursor.</summary>
    private static readonly TimeSpan HoldPoll = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Longest we'll wait for the combo to finish coming up before running the
    /// chosen action anyway. A backstop, not a normal path: fingers leave a key
    /// in tens of milliseconds, and something has to give if one is stuck.
    /// </summary>
    private const long HoldDrainCapMs = 700;

    private static readonly Color HubAccent = Color.FromRgb(0xE0, 0x3E, 0x52);
    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(110);

    private RingConfig _config;
    private readonly Action _showSettings;
    private readonly List<RingButton> _buttons = new();
    private readonly Dictionary<int, SubGroup> _groups = new();

    private Point _ringCenter;
    private double _buttonRadius;
    private double _subRadius;
    private double _orbit;
    private double _subOrbit;
    private double _hubRadius;

    private int _hovered = None;
    private int _hoveredChild = None;
    private int _openGroup = None;
    private IntPtr _previousForeground;

    private readonly System.Windows.Threading.DispatcherTimer _holdTimer;
    private ModifierKeys _holdModifiers;
    private Key _holdKey;
    private long _holdSince;
    private bool _holdArmed;
    private long _holdReleasedAt;
    private bool _holdReleased;

    private readonly System.Windows.Threading.DispatcherTimer _trimTimer;

    private Ellipse _hubHighlight = null!;
    private ScaleTransform _hubScale = null!;
    private FrameworkElement _pill = null!;
    private TextBlock _pillText = null!;

    public RingWindow(RingConfig config, Action showSettings)
    {
        _config = config;
        _showSettings = showSettings;
        InitializeComponent();

        Root.RenderTransformOrigin = new Point(0.5, 0.5);
        Root.RenderTransform = new ScaleTransform(1, 1);

        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => Hide();
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
        KeyDown += OnKeyDown;

        // Rendering the ring dirties a few MB of pages that are dead the moment
        // it closes. Wait a beat first, so reopening it straight away doesn't
        // pay to fault them all back in.
        _trimTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            MemoryTrim.Now();
        };

        // Input priority: this timer *is* the pointer while the gesture is
        // running, so it must not queue behind background work.
        _holdTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Input)
        {
            Interval = HoldPoll,
        };
        _holdTimer.Tick += OnHoldTick;

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _trimTimer.Stop();
            }
            else
            {
                _trimTimer.Start();
                _holdTimer.Stop();
            }
        };
    }

    // ---- window plumbing ------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        // Keep the ring out of Alt+Tab as well as out of the taskbar.
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>Rebuilds the ring from a (possibly reloaded) config.</summary>
    public void Apply(RingConfig config)
    {
        _config = config;
        Build();
    }

    // ---- construction ---------------------------------------------------

    private void Build()
    {
        Surface.Children.Clear();
        _buttons.Clear();
        _groups.Clear();
        _hovered = None;
        _hoveredChild = None;
        _openGroup = None;

        var actions = _config.Actions;
        if (actions.Count == 0) return;

        _buttonRadius = Math.Max(12, _config.ButtonRadius);
        _orbit = RingLayout.ResolveOrbit(_config.OrbitRadius, _buttonRadius, actions.Count);
        _hubRadius = Math.Max(10, _config.HubRadius);
        _subRadius = _buttonRadius * SubScale;
        _subOrbit = _orbit + _buttonRadius + _subRadius + SubGap;

        var tint = AcrylicBrushes.ParseColor(_config.Tint, Color.FromRgb(0x26, 0x26, 0x2E));
        var accent = AcrylicBrushes.ParseColor(_config.Accent, Color.FromRgb(0x5C, 0x7C, 0xFA));
        var tintBrush = new SolidColorBrush(tint) { Opacity = _config.TintOpacity };
        tintBrush.Freeze();

        // The window has to be big enough for the outer orbit up front, since
        // its size is fixed once shown - but only if something is grouped.
        var hasGroups = actions.Any(a => a.IsGroup);
        var extent = hasGroups ? _subOrbit + _subRadius : _orbit + _buttonRadius;

        // Labels sit beside the button they name, so the window needs room for
        // the widest of them on either flank before the geometry is settled.
        var labelRoomX = 0.0;
        var labelRoomY = 0.0;
        if (_config.ShowLabels)
        {
            CreatePill(tintBrush);
            var widest = WidestLabel(actions);
            labelRoomX = LabelGap + widest.Width;
            labelRoomY = LabelGap + widest.Height;
        }

        var halfWidth = extent + ShadowMargin + labelRoomX;
        var halfHeight = extent + ShadowMargin + labelRoomY;

        Width = halfWidth * 2;
        Height = halfHeight * 2;
        Surface.Width = Width;
        Surface.Height = Height;
        _ringCenter = new Point(halfWidth, halfHeight);

        Surface.Children.Add(CreateInputPad());

        for (var i = 0; i < actions.Count; i++)
        {
            var center = RingLayout.ButtonCenter(_ringCenter, _orbit, i, actions.Count);
            var button = CreateButton(actions[i], center, _buttonRadius, tintBrush, accent);
            _buttons.Add(button);
            Surface.Children.Add(button.Host);
        }

        Surface.Children.Add(CreateHub(tintBrush));

        // Group layers go on top of the first level so children are never
        // occluded by a neighbouring parent.
        for (var i = 0; i < actions.Count; i++)
        {
            if (!actions[i].IsGroup) continue;

            var group = CreateGroup(i, actions.Count, tintBrush, accent);
            _groups[i] = group;
            Surface.Children.Add(group.Layer);
        }

        // Last, so a label is never painted under a button.
        if (_config.ShowLabels) Surface.Children.Add(_pill);
    }

    /// <summary>
    /// An all-but-invisible sheet over the whole window, there purely to be hit.
    ///
    /// <c>AllowsTransparency</c> makes this a layered window, and a layered
    /// window passes mouse input straight through pixels whose alpha is zero -
    /// so without this the ring only ever heard about the pointer while it was
    /// over a drawn circle, and the wedges may as well not exist. One unit of
    /// alpha is enough to make Windows deliver the messages and is not visible
    /// on any display.
    ///
    /// The hold gesture doesn't need it - it polls the cursor rather than
    /// waiting to be told - which is exactly why the two disagreed.
    /// </summary>
    private FrameworkElement CreateInputPad()
    {
        var fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        fill.Freeze();

        return new Rectangle
        {
            Width = Width,
            Height = Height,
            Fill = fill,
        };
    }

    private RingButton CreateButton(
        RingAction action, Point center, double radius, Brush tintBrush, Color accent)
    {
        var scale = new ScaleTransform(1, 1);
        var host = new Grid
        {
            Width = radius * 2,
            Height = radius * 2,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };

        // Flat tint, luminosity sheen, grain, then the accent that fades in on
        // hover - the same layering the segmented version used, per circle.
        host.Children.Add(new Ellipse
        {
            Fill = tintBrush,
            Stroke = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
        });
        host.Children.Add(new Ellipse { Fill = AcrylicBrushes.Sheen });
        host.Children.Add(new Ellipse { Fill = AcrylicBrushes.Noise });

        var segmentAccent = AcrylicBrushes.ParseColor(action.Accent, accent);
        var highlight = new Ellipse { Fill = new SolidColorBrush(segmentAccent), Opacity = 0 };
        host.Children.Add(highlight);

        var icon = CreateIconVisual(action, radius);
        host.Children.Add(icon);

        // A dot on the shoulder marks "this one has more inside".
        if (action.IsGroup) host.Children.Add(CreateGroupBadge(radius, segmentAccent));

        Canvas.SetLeft(host, center.X - radius);
        Canvas.SetTop(host, center.Y - radius);

        return new RingButton
        {
            Action = action,
            Center = center,
            Radius = radius,
            Host = host,
            Highlight = highlight,
            Icon = icon,
            Scale = scale,
        };
    }

    private FrameworkElement CreateIconVisual(RingAction action, double radius)
    {
        if (action.IconKind == ActionIconKind.AppIcon && TryLoadIcon(action.IconPath, radius, out var source))
        {
            return new Image
            {
                Source = source,
                Width = radius * 1.05,
                Height = radius * 1.05,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return new TextBlock
        {
            Text = action.Glyph,
            FontFamily = (FontFamily)Resources["IconFont"],
            FontSize = radius * 0.82,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDE, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    internal static bool TryLoadIcon(string rawPath, double radius, out ImageSource? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(rawPath)) return false;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(rawPath.Trim());
            if (!IOPath.IsPathRooted(path)) path = IOPath.Combine(AppContext.BaseDirectory, path);
            if (!File.Exists(path)) return false;

            if (string.Equals(IOPath.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return false;
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight((int)Math.Ceiling(radius * 1.2), (int)Math.Ceiling(radius * 1.2)));
                bitmap.Freeze();
                source = bitmap;
                return true;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = (int)Math.Ceiling(radius * 2);
            image.EndInit();
            image.Freeze();
            source = image;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static UIElement CreateGroupBadge(double radius, Color accent)
    {
        var diameter = Math.Max(5, radius * 0.28);
        var offset = radius * 0.707; // the 45-degree point on the button's edge

        return new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = new SolidColorBrush(accent),
            Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform(offset, -offset),
        };
    }

    private SubGroup CreateGroup(
        int parentIndex, int siblingCount, Brush tintBrush, Color accent)
    {
        var parent = _config.Actions[parentIndex];
        var parentAngle = RingLayout.AngleOf(parentIndex, siblingCount);
        var step = RingLayout.ChildAngleStep(_subOrbit, _subRadius);

        var scale = new ScaleTransform(1, 1);
        var layer = new Canvas
        {
            Width = Width,
            Height = Height,
            Opacity = 0,
            IsHitTestVisible = false,
            // Scale about the ring centre so the children appear to fan outward
            // from the parent rather than growing in place.
            RenderTransformOrigin = new Point(_ringCenter.X / Width, _ringCenter.Y / Height),
            RenderTransform = scale,
        };

        var children = new List<RingButton>();
        var angles = new List<double>();
        var parentCenter = RingLayout.ButtonCenter(_ringCenter, _orbit, parentIndex, siblingCount);
        var spread = (parent.Items.Count - 1) * step;

        for (var i = 0; i < parent.Items.Count; i++)
        {
            var angle = parentAngle - spread / 2 + i * step;
            angles.Add(angle < 0 ? angle + 360 : angle % 360);

            var center = RingLayout.ChildCenter(
                _ringCenter, _subOrbit, parentAngle, i, parent.Items.Count, step);

            // A hairline back to the parent, so the grouping is legible.
            layer.Children.Add(new Line
            {
                X1 = parentCenter.X, Y1 = parentCenter.Y,
                X2 = center.X, Y2 = center.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });

            var child = CreateButton(parent.Items[i], center, _subRadius, tintBrush, accent);
            children.Add(child);
        }

        foreach (var child in children) layer.Children.Add(child.Host);

        Canvas.SetLeft(layer, 0);
        Canvas.SetTop(layer, 0);

        return new SubGroup
        {
            ParentIndex = parentIndex,
            Children = children,
            Angles = angles,
            HalfStep = step / 2,
            Layer = layer,
            Scale = scale,
        };
    }

    private FrameworkElement CreateHub(Brush tintBrush)
    {
        _hubScale = new ScaleTransform(1, 1);
        var host = new Grid
        {
            Width = _hubRadius * 2,
            Height = _hubRadius * 2,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _hubScale,
        };

        host.Children.Add(new Ellipse
        {
            Fill = tintBrush,
            Stroke = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
        });
        host.Children.Add(new Ellipse { Fill = AcrylicBrushes.Noise });

        _hubHighlight = new Ellipse { Fill = new SolidColorBrush(HubAccent), Opacity = 0 };
        host.Children.Add(_hubHighlight);

        host.Children.Add(new TextBlock
        {
            Text = "", // Cancel
            FontFamily = (FontFamily)Resources["IconFont"],
            FontSize = _hubRadius * 0.7,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        Canvas.SetLeft(host, _ringCenter.X - _hubRadius);
        Canvas.SetTop(host, _ringCenter.Y - _hubRadius);
        return host;
    }

    /// <summary>
    /// The buttons are icon-only, so the hovered action names itself on a small
    /// pill. One pill is built per rebuild and moved around on hover.
    /// </summary>
    private void CreatePill(Brush tintBrush)
    {
        _pillText = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(10, 3, 10, 4),
        };

        _pill = new Border
        {
            Background = tintBrush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = _pillText,
            Opacity = 0,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Bounding size of the longest label the ring can show, measured with the
    /// real pill so padding and font are accounted for.
    /// </summary>
    private Size WidestLabel(List<RingAction> actions)
    {
        var widest = MeasurePill(CloseLabel);

        foreach (var action in actions)
        {
            widest = Grow(widest, MeasurePill(action.Label));
            foreach (var child in action.Items)
            {
                widest = Grow(widest, MeasurePill(child.Label));
            }
        }

        return widest;
    }

    private static Size Grow(Size a, Size b) =>
        new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));

    private Size MeasurePill(string text)
    {
        _pillText.Text = text;

        // InvalidateMeasure is load-bearing. Measure() is a no-op when the
        // element's measure is already valid for the same constraint, so
        // without this a short label inherits the previous long label's width
        // and gets pushed away from its button.
        _pill.InvalidateMeasure();
        _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _pill.DesiredSize;
    }

    /// <summary>
    /// A label sits on the side of its button that faces away from the middle.
    /// Which side that is depends on where the button sits: buttons out on the
    /// flanks take their label beside them, buttons on the vertical axis take it
    /// above or below, since "beside" would leave the label pointing back into
    /// the ring. Close is a special case - it belongs under the ring, tucked
    /// below the first orbit rather than out past the group orbit.
    /// </summary>
    private void PlacePill(RingButton? anchor, Size size)
    {
        if (anchor is null)
        {
            Canvas.SetLeft(_pill, _ringCenter.X - size.Width / 2);
            Canvas.SetTop(_pill, _ringCenter.Y + _orbit + _buttonRadius + CloseGap);
            return;
        }

        var dx = anchor.Center.X - _ringCenter.X;
        var dy = anchor.Center.Y - _ringCenter.Y;

        double left, top;
        if (Math.Abs(dy) > Math.Abs(dx))
        {
            left = anchor.Center.X - size.Width / 2;
            top = dy < 0
                ? anchor.Center.Y - anchor.Radius - LabelGap - size.Height
                : anchor.Center.Y + anchor.Radius + LabelGap;
        }
        else
        {
            left = dx >= 0
                ? anchor.Center.X + anchor.Radius + LabelGap
                : anchor.Center.X - anchor.Radius - LabelGap - size.Width;
            top = anchor.Center.Y - size.Height / 2;
        }

        Canvas.SetLeft(_pill, left);
        Canvas.SetTop(_pill, top);
    }

    // ---- show / hide ----------------------------------------------------

    /// <summary>
    /// Hotkey entry point: instant both ways. The hotkey is registered with
    /// MOD_NOREPEAT, so one physical press arrives here exactly once and a plain
    /// toggle needs no guard against auto-repeat.
    /// </summary>
    public void Toggle(ModifierKeys modifiers = ModifierKeys.None, Key key = Key.None)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowRing();

        // Only a real hotkey press can be held; the tray menu has no combo to
        // watch, and passes none.
        if (_config.HoldToActivate && key != Key.None) BeginHoldWatch(modifiers, key);
    }

    public void ShowRing()
    {
        if (_buttons.Count == 0) Build();
        if (_buttons.Count == 0) return;

        // Remember who had focus, so keystroke actions have somewhere to land.
        _previousForeground = NativeMethods.GetForegroundWindow();

        ResetHover();

        // Order matters here, and getting it wrong is what made the ring blink.
        // The window must already be fully transparent before it is shown, and
        // it must be shown before it is moved: WPF re-applies its own layout on
        // Show, which can override a SetWindowPos issued beforehand and snap the
        // ring across the screen. Both steps happen while nothing is visible.
        PrepareForShow();
        Show();
        PositionWindow();
        Activate();
        PlayOpen();
    }

    /// <summary>
    /// Puts the ring in its pre-open state before the first frame is composed.
    ///
    /// Clearing the animations first is the subtle part: an animation holds its
    /// final value at higher precedence than the local one, so assigning
    /// Opacity while last summon's animation is still in effect does nothing,
    /// and the window's first frame lands at full opacity - a flash of the whole
    /// ring, then a jump to zero, then the fade in.
    /// </summary>
    private void PrepareForShow()
    {
        var scale = (ScaleTransform)Root.RenderTransform;

        Root.BeginAnimation(OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        Root.Opacity = 0;
        scale.ScaleX = OpenFrom;
        scale.ScaleY = OpenFrom;
    }

    /// <summary>
    /// Places the ring in physical pixels via SetWindowPos.
    ///
    /// Window.Left/Top look like the obvious answer and are a trap: in a
    /// PerMonitorV2 process they are neither physical pixels nor DIPs of the
    /// monitor you are aiming at, so on a multi-monitor or scaled desktop the
    /// ring lands somewhere else entirely. Real pixels, explicitly, is the only
    /// version that holds up.
    /// </summary>
    private void PositionWindow()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();

        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var monitor = NativeMethods.MonitorFromPoint(
            cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

        var scaleX = 1.0;
        var scaleY = 1.0;
        if (NativeMethods.GetDpiForMonitor(
                monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0)
        {
            scaleX = dpiX / 96.0;
            scaleY = dpiY / 96.0;
        }

        var width = (int)Math.Round(Width * scaleX);
        var height = (int)Math.Round(Height * scaleY);

        int targetX, targetY;
        if (_config.FollowCursor)
        {
            targetX = cursor.X;
            targetY = cursor.Y;
        }
        else
        {
            targetX = (info.rcWork.Left + info.rcWork.Right) / 2;
            targetY = (info.rcWork.Top + info.rcWork.Bottom) / 2;
        }

        // Centre the *ring* on the target, not the window: the label strip hangs
        // below the ring and would otherwise pull it off-centre.
        var x = targetX - width / 2;
        var y = targetY - (int)Math.Round(_ringCenter.Y * scaleY);

        // Keep it fully on-screen when summoned near an edge.
        x = Math.Clamp(x, info.rcWork.Left, Math.Max(info.rcWork.Left, info.rcWork.Right - width));
        y = Math.Clamp(y, info.rcWork.Top, Math.Max(info.rcWork.Top, info.rcWork.Bottom - height));

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            x, y, width, height, NativeMethods.SWP_NOACTIVATE);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (IsVisible) PositionWindow();
    }

    private void PlayOpen()
    {
        var scale = (ScaleTransform)Root.RenderTransform;

        // To-only animations, so they start from whatever PrepareForShow left
        // behind rather than snapping to a From value of their own.
        var grow = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120)));
    }

    // ---- hold gesture ---------------------------------------------------

    /// <summary>
    /// Starts watching the hotkey that just opened the ring. Hold it, move onto
    /// an item, let go, and that item runs - the whole interaction without a
    /// click. Let go straight away instead and the ring simply stays up, so the
    /// old tap-then-click way of working is untouched.
    /// </summary>
    private void BeginHoldWatch(ModifierKeys modifiers, Key key)
    {
        _holdModifiers = modifiers;
        _holdKey = key;
        _holdSince = Environment.TickCount64;
        _holdArmed = false;
        _holdReleased = false;
        _holdTimer.Start();
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _holdTimer.Stop();
            return;
        }

        // Once the release has started, the choice is already made: stop reading
        // the cursor so a twitch on the way up can't move it, and wait for the
        // keyboard to clear before running anything.
        if (_holdReleased)
        {
            if (AnyComboKeyDown()
                && Environment.TickCount64 - _holdReleasedAt < HoldDrainCapMs)
            {
                return;
            }

            _holdTimer.Stop();
            InvokeHovered(closeOnMiss: true);
            return;
        }

        if (IsComboFullyDown())
        {
            // Below the threshold the press is still ambiguous - it could yet
            // turn out to be a tap - so nothing is armed and the cursor is left
            // alone. Arming late also stops the pointer's resting position from
            // being read as a choice the instant the ring appears.
            if (!_holdArmed && Environment.TickCount64 - _holdSince >= Math.Max(0, _config.HoldThresholdMs))
            {
                _holdArmed = true;
            }

            if (_holdArmed) TrackCursor();
            return;
        }

        // Released after a tap: leave the ring up to be clicked.
        if (!_holdArmed)
        {
            _holdTimer.Stop();
            return;
        }

        // Letting go of any part of the combo ends the gesture, which is what
        // makes releasing feel like one motion rather than a sequence. Running
        // the action right here would be a mistake though: a Keys action fired
        // while the other half of the combo is still physically down arrives at
        // the target window with those modifiers folded in, so Ctrl+C sent out
        // of Ctrl+Alt+Space lands as Ctrl+Alt+C. Wait for the hand to leave the
        // keyboard first.
        _holdReleased = true;
        _holdReleasedAt = Environment.TickCount64;
    }

    /// <summary>Whether every key in the combo is still physically down.</summary>
    private bool IsComboFullyDown() =>
        NativeMethods.IsKeyDown(KeyInterop.VirtualKeyFromKey(_holdKey)) && ComboModifiers().All(NativeMethods.IsKeyDown);

    /// <summary>Whether any part of the combo is still physically down.</summary>
    private bool AnyComboKeyDown() =>
        NativeMethods.IsKeyDown(KeyInterop.VirtualKeyFromKey(_holdKey)) || ComboModifiers().Any(NativeMethods.IsKeyDown);

    /// <summary>
    /// The virtual keys behind the combo's modifiers. Left and right Windows are
    /// separate keys with no combined code, so whichever one is pressed decides -
    /// hence the pick rather than a fixed list.
    /// </summary>
    private IEnumerable<int> ComboModifiers()
    {
        if (_holdModifiers.HasFlag(ModifierKeys.Control)) yield return NativeMethods.VK_CONTROL;
        if (_holdModifiers.HasFlag(ModifierKeys.Alt)) yield return NativeMethods.VK_MENU;
        if (_holdModifiers.HasFlag(ModifierKeys.Shift)) yield return NativeMethods.VK_SHIFT;
        if (_holdModifiers.HasFlag(ModifierKeys.Windows))
        {
            yield return NativeMethods.IsKeyDown(NativeMethods.VK_RWIN)
                ? NativeMethods.VK_RWIN
                : NativeMethods.VK_LWIN;
        }
    }

    /// <summary>
    /// Reads the cursor from the system rather than from mouse events, because
    /// the wedges don't stop at the window's edge in spirit and a quick flick
    /// leaves it behind. WPF only delivers moves over the window, so the poll
    /// keeps aiming honest even when the pointer has shot well past the ring.
    /// </summary>
    private void TrackCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        UpdateHover(Surface.PointFromScreen(new Point(cursor.X, cursor.Y)));
    }

    // ---- hit testing ----------------------------------------------------

    /// <summary>
    /// Resolves a point to whatever it is aiming at.
    ///
    /// The drawn circles are not the targets: each button owns the entire wedge
    /// of the plane it sits in, from the hub out to the edge of the window. A
    /// radial menu is meant to be worked by direction - shove the pointer up and
    /// the top item is chosen - and asking for a hit on a 46-pixel circle throws
    /// that away. Only the hub keeps a circular target, because it means cancel
    /// and shouldn't be reachable by flinging the mouse anywhere in particular.
    /// </summary>
    private void UpdateHover(Point p)
    {
        if (_buttons.Count == 0) return;

        if (RingLayout.IsWithin(p, _ringCenter, _hubRadius))
        {
            SetHover(HubIndex, None, None);
            return;
        }

        var angle = RingLayout.AngleAt(_ringCenter, p);

        // Past the halfway line between the two orbits, an open group's children
        // own the arc they fan across. Everything else out there stays with the
        // parent, so overshooting the fan doesn't collapse the group mid-reach.
        if (_openGroup != None
            && _groups.TryGetValue(_openGroup, out var open)
            && RingLayout.Distance(p, _ringCenter) >= (_orbit + _subOrbit) / 2)
        {
            var child = RingLayout.NearestAngle(angle, open.Angles, open.HalfStep);
            SetHover(_openGroup, child, _openGroup);
            return;
        }

        var index = RingLayout.SectorIndex(angle, _buttons.Count);
        SetHover(index, None, _buttons[index].Action.IsGroup ? index : None);
    }

    private void OnMouseMove(object sender, MouseEventArgs e) =>
        UpdateHover(e.GetPosition(Surface));

    // ---- hover state ----------------------------------------------------

    private void ResetHover() => SetHover(None, None, None, force: true);

    private void SetHover(int hovered, int hoveredChild, int openGroup, bool force = false)
    {
        if (!force && hovered == _hovered && hoveredChild == _hoveredChild
            && openGroup == _openGroup)
        {
            return;
        }

        _hovered = hovered;
        _hoveredChild = hoveredChild;
        _openGroup = openGroup;

        for (var i = 0; i < _buttons.Count; i++)
        {
            var on = i == hovered && hoveredChild == None;
            Emphasise(_buttons[i], on);

            // Pull focus toward the open group by holding the rest back - but
            // only slightly by default, since fading them hard makes the ring
            // look broken. Some people want exactly that, hence the option.
            var dimmed = _config.FadeOthersOnGroupOpen ? FadedSibling : DimmedSibling;
            _buttons[i].Host.Opacity =
                openGroup != None && i != openGroup ? dimmed : 1.0;
        }

        foreach (var (parentIndex, group) in _groups)
        {
            var expanded = parentIndex == openGroup;

            group.Layer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(expanded ? 1.0 : 0.0, Quick));

            var pop = new DoubleAnimation(expanded ? 1.0 : 0.9, Quick)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            group.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            group.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            for (var i = 0; i < group.Children.Count; i++)
            {
                Emphasise(group.Children[i], expanded && i == hoveredChild);
            }
        }

        var hubOn = hovered == HubIndex;
        _hubHighlight.BeginAnimation(OpacityProperty,
            new DoubleAnimation(hubOn ? 0.55 : 0.0, Quick));
        var hubPop = new DoubleAnimation(hubOn ? 1.1 : 1.0, Quick);
        _hubScale.BeginAnimation(ScaleTransform.ScaleXProperty, hubPop);
        _hubScale.BeginAnimation(ScaleTransform.ScaleYProperty, hubPop);

        UpdatePill();
    }

    private static void Emphasise(RingButton button, bool on)
    {
        button.Highlight.BeginAnimation(OpacityProperty,
            new DoubleAnimation(on ? 0.34 : 0.0, Quick));

        var pop = new DoubleAnimation(on ? HoverScale : 1.0, Quick)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        button.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        button.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        button.Icon.Opacity = on ? 1.0 : 0.88;
    }

    private void UpdatePill()
    {
        if (!_config.ShowLabels) return;

        string? text = null;
        RingButton? anchor = null;

        if (_hoveredChild != None && _groups.TryGetValue(_openGroup, out var group))
        {
            anchor = group.Children[_hoveredChild];
            // No parent prefix any more: the label sits against the button it
            // belongs to, so the grouping is already on screen.
            text = anchor.Action.Label;
        }
        else if (_hovered >= 0)
        {
            anchor = _buttons[_hovered];
            text = anchor.Action.Label;
        }
        else if (_hovered == HubIndex)
        {
            text = CloseLabel;
        }

        if (text is not null)
        {
            // Measure now rather than waiting for the next layout pass, so the
            // pill is placed against a size that matches the text it is showing.
            PlacePill(anchor, MeasurePill(text));
        }

        _pill.BeginAnimation(OpacityProperty,
            new DoubleAnimation(text is null ? 0.0 : 1.0, Quick));
    }

    // ---- invoking -------------------------------------------------------

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        InvokeHovered(closeOnMiss: false);

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var openSettings = _hovered == HubIndex;
        Hide();

        if (openSettings)
            _showSettings();

        e.Handled = true;
    }

    /// <summary>
    /// Runs whatever is currently under the pointer.
    ///
    /// <paramref name="closeOnMiss"/> separates the two ways in. A click that
    /// lands on nothing runnable should leave the ring alone to be clicked again;
    /// letting go of the hotkey is the end of the gesture either way, so there is
    /// nothing left to aim with and the ring closes.
    /// </summary>
    private void InvokeHovered(bool closeOnMiss)
    {
        if (_hoveredChild != None && _groups.TryGetValue(_openGroup, out var group))
        {
            Invoke(group.Children[_hoveredChild].Action);
            return;
        }

        if (_hovered >= 0)
        {
            var action = _buttons[_hovered].Action;

            // A pure group isn't runnable - it's already showing its children,
            // and dismissing the ring on a click here would be the opposite of
            // helpful.
            if (action.IsClickable) Invoke(action);
            else if (closeOnMiss) Hide();
            return;
        }

        Hide(); // the hub and the empty space both mean cancel
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Back out of an expanded group first, then close.
            if (_openGroup != None) ResetHover();
            else Hide();

            e.Handled = true;
            return;
        }

        var digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1
            : -1;

        if (digit < 0) return;
        e.Handled = true;

        // With a group open, digits address its children; otherwise the ring.
        if (_openGroup != None && _groups.TryGetValue(_openGroup, out var group))
        {
            if (digit < group.Children.Count) Invoke(group.Children[digit].Action);
            return;
        }

        if (digit >= _buttons.Count) return;

        var action = _buttons[digit].Action;
        if (action.IsGroup && !action.IsClickable) SetHover(digit, None, digit);
        else Invoke(action);
    }

    private void Invoke(RingAction action)
    {
        var restoreTo = _previousForeground;
        Hide();

        // Run once the ring is off-screen, so restoring focus to the previous
        // window isn't racing our own teardown.
        Dispatcher.BeginInvoke(() => ActionRunner.Run(action, restoreTo));
    }
}
