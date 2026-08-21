using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using ActionRing.Models;
using ActionRing.Services;

namespace ActionRing.Views;

public sealed record ActionPreset(RingAction Action);

public sealed record ActionPresetCategory(string Name, IReadOnlyList<ActionPreset> Items);

public sealed class ActionItemViewModel : INotifyPropertyChanged
{
    private readonly RingAction _original;
    private string _label;
    private string _glyph;
    private ActionIconKind _iconKind;
    private string _iconPath;
    private ActionKind _kind;
    private string _target;
    private string _arguments;
    private string _accent;
    private ImageSource? _displayIcon;

    public ActionItemViewModel(RingAction action, ActionItemViewModel? parent = null)
    {
        _original = new RingAction
        {
            Label = action.Label,
            Glyph = action.Glyph,
            IconKind = action.IconKind,
            IconPath = action.IconPath ?? "",
            Kind = action.Kind,
            Target = action.Target,
            Arguments = action.Arguments,
            Accent = action.Accent,
        };
        Parent = parent;
        _label = action.Label;
        _glyph = action.Glyph;
        _iconKind = action.IconKind;
        _iconPath = action.IconPath ?? "";
        _kind = action.Kind;
        _target = action.Target;
        _arguments = action.Arguments;
        _accent = action.Accent ?? "";
        RefreshDisplayIcon();
        foreach (var child in action.Items) Children.Add(new ActionItemViewModel(child, this));
    }

    public static Array Kinds { get; } = Enum.GetValues<ActionKind>();
    public ActionItemViewModel? Parent { get; }
    public ObservableCollection<ActionItemViewModel> Children { get; } = new();

    public string Label { get => _label; set { _label = value; Changed(); Changed(nameof(DisplayLabel)); } }
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "Unnamed action" : Label;
    public string Glyph { get => _glyph; set { _glyph = value; Changed(); Changed(nameof(DisplayGlyph)); } }
    public string DisplayGlyph => IconKind == ActionIconKind.AppIcon ? "" : Glyph;
    public ImageSource? DisplayIcon => _displayIcon;
    public bool UseAppImage => IconKind == ActionIconKind.AppIcon && DisplayIcon is not null;
    public bool UseDisplayGlyph => !UseAppImage;
    public ActionIconKind IconKind
    {
        get => _iconKind;
        set
        {
            _iconKind = value;
            RefreshDisplayIcon();
            Changed();
            Changed(nameof(UseGlyph));
            Changed(nameof(UseAppIcon));
            Changed(nameof(DisplayGlyph));
            Changed(nameof(DisplayIcon));
            Changed(nameof(UseAppImage));
            Changed(nameof(UseDisplayGlyph));
        }
    }
    public bool UseGlyph { get => IconKind == ActionIconKind.Glyph; set { if (value) IconKind = ActionIconKind.Glyph; } }
    public bool UseAppIcon { get => IconKind == ActionIconKind.AppIcon; set { if (value) IconKind = ActionIconKind.AppIcon; } }
    public string IconPath
    {
        get => _iconPath;
        set
        {
            _iconPath = value;
            RefreshDisplayIcon();
            Changed();
            Changed(nameof(DisplayIcon));
            Changed(nameof(UseAppImage));
            Changed(nameof(UseDisplayGlyph));
        }
    }
    public ActionKind Kind { get => _kind; set { _kind = value; Changed(); } }
    public string Target { get => _target; set { _target = value; Changed(); } }
    public string Arguments { get => _arguments; set { _arguments = value; Changed(); } }
    public string Accent { get => _accent; set { _accent = value; Changed(); } }
    public bool IsModified =>
        Label != _original.Label ||
        Glyph != _original.Glyph ||
        IconKind != _original.IconKind ||
        IconPath != (_original.IconPath ?? "") ||
        Kind != _original.Kind ||
        Target != _original.Target ||
        Arguments != _original.Arguments ||
        Accent != (_original.Accent ?? "");

    public void Revert()
    {
        Label = _original.Label;
        Glyph = _original.Glyph;
        IconKind = _original.IconKind;
        IconPath = _original.IconPath ?? "";
        Kind = _original.Kind;
        Target = _original.Target;
        Arguments = _original.Arguments;
        Accent = _original.Accent ?? "";
        Changed(nameof(IsModified));
    }

    public RingAction ToModel() => new()
    {
        Label = Label.Trim(), Glyph = Glyph, IconKind = IconKind, IconPath = IconPath.Trim(),
        Kind = Kind, Target = Target.Trim(),
        Arguments = Arguments.Trim(), Accent = string.IsNullOrWhiteSpace(Accent) ? null : Accent.Trim(),
        Items = Children.Select(child => child.ToModel()).ToList(),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RefreshDisplayIcon()
    {
        _displayIcon = IconKind == ActionIconKind.AppIcon &&
                       RingWindow.TryLoadIcon(_iconPath, 20, out var source)
            ? source
            : null;
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name != nameof(IsModified))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
    }
}

internal sealed class GlyphOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Glyph { get; init; }
    public required string Name { get; init; }
    public required string SearchText { get; init; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static partial class FluentGlyphs
{
    public static IReadOnlyList<GlyphOption> All { get; } = Load();

    public static IReadOnlyList<GlyphOption[]> Filter(string? query)
    {
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return All
            .Where(option => terms.Length == 0 || terms.All(term =>
                option.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Chunk(6)
            .ToArray();
    }

    public static void Select(string? glyph)
    {
        foreach (var option in All) option.IsSelected = option.Glyph == glyph;
    }

    private static IReadOnlyList<GlyphOption> Load()
    {
        var names = LoadNames();
        var typeface = new Typeface(
            new FontFamily("Segoe Fluent Icons"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        if (!typeface.TryGetGlyphTypeface(out var glyphTypeface)) return Array.Empty<GlyphOption>();

        return glyphTypeface.CharacterToGlyphMap.Keys
            .Where(codePoint => codePoint is >= 0xE000 and <= 0xF8FF)
            .OrderBy(codePoint => codePoint)
            .Select(codePoint =>
            {
                var hex = codePoint.ToString("X4", CultureInfo.InvariantCulture);
                var name = names.GetValueOrDefault(codePoint, $"Glyph {hex}");
                var keywords = WordBoundaryRegex().Replace(name, "$1 $2");
                return new GlyphOption
                {
                    Glyph = char.ConvertFromUtf32(codePoint),
                    Name = name,
                    SearchText = $"{name} {keywords} {hex} U+{hex}",
                };
            })
            .ToArray();
    }

    private static Dictionary<int, string> LoadNames()
    {
        var result = new Dictionary<int, string>();
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ActionRing.Resources.segoe-fluent-icons-font.md");
        if (stream is null) return result;

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var match = MetadataRowRegex().Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var codePoint))
                result[codePoint] = match.Groups[2].Value;
        }
        return result;
    }

    [GeneratedRegex("\\|\\s*([a-fA-F0-9]{4,5})\\s*\\|\\s*:::no-loc text=\"([^\"]+)\"")]
    private static partial Regex MetadataRowRegex();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundaryRegex();
}

public partial class SettingsWindow : Window
{
    private sealed class DesignerTarget
    {
        public required ActionItemViewModel Action { get; init; }
        public required Point Center { get; init; }
        public required double Radius { get; init; }
        public required FrameworkElement Button { get; init; }
        public Border? Label { get; set; }
    }

    private readonly Func<RingConfig, string?> _save;
    private readonly RingConfig _defaults = RingConfig.CreateDefault();
    private readonly IReadOnlyList<RingAction> _initialActions;
    private ActionItemViewModel? _selectedAction;
    private ActionItemViewModel? _expandedGroup;
    private FrameworkElement? _pendingDragSource;
    private Point _dragStartPoint;
    private FrameworkElement? _dragPreview;
    private FrameworkElement? _insertPreview;
    private long _dragCompletedAt;
    private bool _dropCommitted;
    private bool _suppressPresetClick;
    private readonly List<DesignerTarget> _designerTargets = new();
    private readonly List<System.Windows.Shapes.Line> _designerGroupLines = new();
    private readonly List<System.Windows.Shapes.Ellipse> _dropOutlines = new();
    private bool _restoringActions;
    private TextBox? _colorTarget;
    private double _pickerHue;
    private double _pickerSaturation;
    private double _pickerValue;

    public SettingsWindow(RingConfig config, Func<RingConfig, string?> save)
    {
        InitializeComponent();
        RingDetailsEditor.ActionCreated += (_, action) =>
        {
            SelectRingAction(action);
            ShowRingEditTab();
        };
        // Resolve the installed icon font once while the window is being
        // created. Opening the picker then only shows an already-built,
        // virtualized six-column list instead of enumerating glyphs on demand.
        _ = FluentGlyphs.All;
        _save = save;
        _initialActions = config.Actions.Select(CloneAction).ToArray();
        Actions = new ObservableCollection<ActionItemViewModel>(
            config.Actions.Select(action => new ActionItemViewModel(action)));
        ActionCategories = CreateActionCategories();
        Actions.CollectionChanged += Actions_CollectionChanged;
        foreach (var action in Actions) WatchAction(action);
        DataContext = this;
        LoadGeneral(config);
        WatchGeneralChanges();
        UpdateResetButtons();
        ShowGeneral();
    }

    public ObservableCollection<ActionItemViewModel> Actions { get; }
    public IReadOnlyList<ActionPresetCategory> ActionCategories { get; }
    private void LoadGeneral(RingConfig config)
    {
        HotKeyBox.Text = config.HotKey;
        HoldToActivateCheck.IsChecked = config.HoldToActivate;
        HoldThresholdBox.Text = config.HoldThresholdMs.ToString(CultureInfo.InvariantCulture);
        FollowCursorCheck.IsChecked = config.FollowCursor;
        ButtonRadiusBox.Text = config.ButtonRadius.ToString(CultureInfo.InvariantCulture);
        OrbitRadiusBox.Text = config.OrbitRadius.ToString(CultureInfo.InvariantCulture);
        HubRadiusBox.Text = config.HubRadius.ToString(CultureInfo.InvariantCulture);
        TintBox.Text = config.Tint;
        AccentBox.Text = config.Accent;
        TintOpacitySlider.Value = config.TintOpacity;
        ShowLabelsCheck.IsChecked = config.ShowLabels;
        FadeOthersOnGroupOpenCheck.IsChecked = config.FadeOthersOnGroupOpen;
        HardwareAccelerationCheck.IsChecked = config.HardwareAcceleration;
    }

    private void WatchGeneralChanges()
    {
        foreach (var box in new[] { HotKeyBox, HoldThresholdBox, ButtonRadiusBox, OrbitRadiusBox,
                     HubRadiusBox, TintBox, AccentBox })
            box.TextChanged += (_, _) => UpdateResetButtons();

        foreach (var check in new[] { HoldToActivateCheck, FollowCursorCheck,
                     ShowLabelsCheck, FadeOthersOnGroupOpenCheck, HardwareAccelerationCheck })
        {
            check.Checked += (_, _) => UpdateResetButtons();
            check.Unchecked += (_, _) => UpdateResetButtons();
        }

        TintOpacitySlider.ValueChanged += (_, _) => UpdateResetButtons();
    }

    private void UpdateResetButtons()
    {
        ResetHotKey.Visibility = Changed(HotKeyBox.Text, _defaults.HotKey);
        ResetHoldToActivate.Visibility = Changed(HoldToActivateCheck.IsChecked == true, _defaults.HoldToActivate);
        ResetHoldThreshold.Visibility = Changed(HoldThresholdBox.Text, _defaults.HoldThresholdMs);
        ResetFollowCursor.Visibility = Changed(FollowCursorCheck.IsChecked == true, _defaults.FollowCursor);
        ResetButtonRadius.Visibility = Changed(ButtonRadiusBox.Text, _defaults.ButtonRadius);
        ResetOrbitRadius.Visibility = Changed(OrbitRadiusBox.Text, _defaults.OrbitRadius);
        ResetHubRadius.Visibility = Changed(HubRadiusBox.Text, _defaults.HubRadius);
        ResetTint.Visibility = Changed(TintBox.Text, _defaults.Tint, ignoreCase: true);
        ResetAccent.Visibility = Changed(AccentBox.Text, _defaults.Accent, ignoreCase: true);
        ResetTintOpacity.Visibility = Changed(TintOpacitySlider.Value, _defaults.TintOpacity);
        ResetShowLabels.Visibility = Changed(ShowLabelsCheck.IsChecked == true, _defaults.ShowLabels);
        ResetFadeOthersOnGroupOpen.Visibility = Changed(FadeOthersOnGroupOpenCheck.IsChecked == true, _defaults.FadeOthersOnGroupOpen);
        ResetHardwareAcceleration.Visibility = Changed(HardwareAccelerationCheck.IsChecked == true, _defaults.HardwareAcceleration);
    }

    private static Visibility Changed(bool value, bool fallback) =>
        value == fallback ? Visibility.Hidden : Visibility.Visible;

    private static Visibility Changed(double value, double fallback) =>
        Math.Abs(value - fallback) < 0.0001 ? Visibility.Hidden : Visibility.Visible;

    private static Visibility Changed(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed == fallback
            ? Visibility.Hidden : Visibility.Visible;

    private static Visibility Changed(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        Math.Abs(parsed - fallback) < 0.0001 ? Visibility.Hidden : Visibility.Visible;

    private static Visibility Changed(string value, string fallback, bool ignoreCase = false) =>
        string.Equals(value.Trim(), fallback.ToString(CultureInfo.InvariantCulture),
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? Visibility.Hidden : Visibility.Visible;

    private void ResetSetting_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "HotKey": HotKeyBox.Text = _defaults.HotKey; break;
            case "HoldToActivate": HoldToActivateCheck.IsChecked = _defaults.HoldToActivate; break;
            case "HoldThresholdMs": HoldThresholdBox.Text = _defaults.HoldThresholdMs.ToString(CultureInfo.InvariantCulture); break;
            case "FollowCursor": FollowCursorCheck.IsChecked = _defaults.FollowCursor; break;
            case "ButtonRadius": ButtonRadiusBox.Text = _defaults.ButtonRadius.ToString(CultureInfo.InvariantCulture); break;
            case "OrbitRadius": OrbitRadiusBox.Text = _defaults.OrbitRadius.ToString(CultureInfo.InvariantCulture); break;
            case "HubRadius": HubRadiusBox.Text = _defaults.HubRadius.ToString(CultureInfo.InvariantCulture); break;
            case "Tint": TintBox.Text = _defaults.Tint; break;
            case "Accent": AccentBox.Text = _defaults.Accent; break;
            case "TintOpacity": TintOpacitySlider.Value = _defaults.TintOpacity; break;
            case "ShowLabels": ShowLabelsCheck.IsChecked = _defaults.ShowLabels; break;
            case "FadeOthersOnGroupOpen": FadeOthersOnGroupOpenCheck.IsChecked = _defaults.FadeOthersOnGroupOpen; break;
            case "HardwareAcceleration": HardwareAccelerationCheck.IsChecked = _defaults.HardwareAcceleration; break;
        }
        e.Handled = true;
    }

    private void TintColor_Click(object sender, RoutedEventArgs e) => OpenColorPicker((FrameworkElement)sender, TintBox);
    private void AccentColor_Click(object sender, RoutedEventArgs e) => OpenColorPicker((FrameworkElement)sender, AccentBox);

    private void OpenColorPicker(FrameworkElement anchor, TextBox target)
    {
        _colorTarget = target;
        var color = Colors.White;
        try { if (ColorConverter.ConvertFromString(target.Text.Trim()) is Color parsed) color = parsed; }
        catch { /* Invalid text remains editable; start the picker at white. */ }

        RgbToHsv(color, out _pickerHue, out _pickerSaturation, out _pickerValue);
        FluentColorPopup.PlacementTarget = anchor;
        UpdateColorPicker(updateTarget: false);
        FluentColorPopup.IsOpen = true;
    }

    private void HueField_MouseInput(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _pickerHue = Math.Clamp(e.GetPosition(HueField).X / Math.Max(1, HueField.ActualWidth), 0, 1) * 360;
        UpdateColorPicker(updateTarget: true);
    }

    private void ColorField_MouseInput(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(ColorField);
        _pickerSaturation = Math.Clamp(point.X / Math.Max(1, ColorField.ActualWidth), 0, 1);
        _pickerValue = 1 - Math.Clamp(point.Y / Math.Max(1, ColorField.ActualHeight), 0, 1);
        UpdateColorPicker(updateTarget: true);
    }

    private void UpdateColorPicker(bool updateTarget)
    {
        ColorFieldHue.Fill = new SolidColorBrush(HsvToRgb(_pickerHue, 1, 1));
        ColorFieldMarker.Margin = new Thickness(
            _pickerSaturation * ColorField.Width - 7,
            (1 - _pickerValue) * ColorField.Height - 7, 0, 0);

        var color = HsvToRgb(_pickerHue, _pickerSaturation, _pickerValue);
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ColorPickerPreview.Background = new SolidColorBrush(color);
        ColorPickerHex.Text = hex;
        if (updateTarget && _colorTarget is not null) _colorTarget.Text = hex;
    }

    private void ColorPickerDone_Click(object sender, RoutedEventArgs e)
    {
        if (_colorTarget is not null) _colorTarget.Text = ColorPickerHex.Text;
        FluentColorPopup.IsOpen = false;
    }

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var section = hue / 60;
        var x = chroma * (1 - Math.Abs(section % 2 - 1));
        var (r, g, b) = section switch
        {
            < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x),
        };
        var match = value - chroma;
        return Color.FromRgb((byte)Math.Round((r + match) * 255),
            (byte)Math.Round((g + match) * 255), (byte)Math.Round((b + match) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    private void GeneralNav_Click(object sender, RoutedEventArgs e) => ShowGeneral();
    private void ActionsNav_Click(object sender, RoutedEventArgs e) => ShowActions();
    private void ActionsRingNav_Click(object sender, RoutedEventArgs e) => ShowActionsRing();

    private void ShowGeneral()
    {
        GeneralPage.Visibility = Visibility.Visible;
        ActionsPage.Visibility = Visibility.Collapsed;
        ActionsRingPage.Visibility = Visibility.Collapsed;
        GeneralNav.Background = (Brush)FindResource("NavSelectedBrush");
        ActionsNav.Background = Brushes.Transparent;
        ActionsRingNav.Background = Brushes.Transparent;
        GeneralIndicator.Visibility = Visibility.Visible;
        ActionsIndicator.Visibility = Visibility.Collapsed;
        ActionsRingIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowActions()
    {
        GeneralPage.Visibility = Visibility.Collapsed;
        ActionsPage.Visibility = Visibility.Visible;
        ActionsRingPage.Visibility = Visibility.Collapsed;
        GeneralNav.Background = Brushes.Transparent;
        ActionsNav.Background = (Brush)FindResource("NavSelectedBrush");
        ActionsRingNav.Background = Brushes.Transparent;
        GeneralIndicator.Visibility = Visibility.Collapsed;
        ActionsIndicator.Visibility = Visibility.Visible;
        ActionsRingIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowActionsRing()
    {
        GeneralPage.Visibility = Visibility.Collapsed;
        ActionsPage.Visibility = Visibility.Collapsed;
        ActionsRingPage.Visibility = Visibility.Visible;
        GeneralNav.Background = Brushes.Transparent;
        ActionsNav.Background = Brushes.Transparent;
        ActionsRingNav.Background = (Brush)FindResource("NavSelectedBrush");
        GeneralIndicator.Visibility = Visibility.Collapsed;
        ActionsIndicator.Visibility = Visibility.Collapsed;
        ActionsRingIndicator.Visibility = Visibility.Visible;
        ShowRingActionsTab();
        UpdateRingSelectionUi();
        RenderRingDesigner();
    }

    private static IReadOnlyList<ActionPresetCategory> CreateActionCategories() =>
    [
        new("MEDIA & VOLUME",
        [
            Preset("Previous track", "", ActionKind.Keys, "MediaPreviousTrack"),
            Preset("Play / pause", "", ActionKind.Keys, "MediaPlayPause"),
            Preset("Next track", "", ActionKind.Keys, "MediaNextTrack"),
            Preset("Mute", "", ActionKind.Keys, "VolumeMute"),
        ]),
        new("OPEN",
        [
            Preset("Terminal", "", ActionKind.Launch, "wt.exe"),
            Preset("File Explorer", "", ActionKind.Launch, "explorer.exe"),
            Preset("Settings", "", ActionKind.Launch, "ms-settings:"),
        ]),
        new("NAVIGATION",
        [
            Preset("Task view", "", ActionKind.Keys, "Win+Tab"),
            Preset("Show desktop", "", ActionKind.Keys, "Win+D"),
            Preset("Snap left", "", ActionKind.Keys, "Win+Left"),
            Preset("Snap right", "", ActionKind.Keys, "Win+Right"),
        ]),
        new("UTILITIES",
        [
            Preset("Windows screenshot", "", ActionKind.Keys, "Win+Shift+S"),
            Preset("Clipboard history", "", ActionKind.Keys, "Win+V"),
            Preset("Search", "", ActionKind.Keys, "Win+S"),
        ]),
        new("SYSTEM",
        [
            Preset("Lock device", "", ActionKind.Keys, "Win+L"),
            Preset("Quick settings", "", ActionKind.Keys, "Win+A"),
        ]),
        new("KEYBOARD",
        [
            Preset("Copy", "", ActionKind.Keys, "Ctrl+C"),
            Preset("Paste", "", ActionKind.Keys, "Ctrl+V"),
            Preset("Undo", "", ActionKind.Keys, "Ctrl+Z"),
        ]),
    ];

    private static ActionPreset Preset(string label, string glyph, ActionKind kind, string target) =>
        new(new RingAction { Label = label, Glyph = glyph, Kind = kind, Target = target });

    private void Actions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ActionItemViewModel action in e.OldItems) UnwatchAction(action);
        if (e.NewItems is not null)
            foreach (ActionItemViewModel action in e.NewItems) WatchAction(action);
        if (!_restoringActions)
        {
            RenderRingDesigner();
            UpdateUndoRingChanges();
        }
    }

    private void WatchAction(ActionItemViewModel action)
    {
        action.PropertyChanged += RingAction_PropertyChanged;
        action.Children.CollectionChanged += ActionChildren_CollectionChanged;
        foreach (var child in action.Children) WatchAction(child);
    }

    private void UnwatchAction(ActionItemViewModel action)
    {
        action.PropertyChanged -= RingAction_PropertyChanged;
        action.Children.CollectionChanged -= ActionChildren_CollectionChanged;
        foreach (var child in action.Children) UnwatchAction(child);
    }

    private void ActionChildren_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ActionItemViewModel action in e.OldItems) UnwatchAction(action);
        if (e.NewItems is not null)
            foreach (ActionItemViewModel action in e.NewItems) WatchAction(action);
        if (!_restoringActions)
        {
            RenderRingDesigner();
            UpdateUndoRingChanges();
        }
    }

    private void RingAction_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RenderRingDesigner();
        UpdateUndoRingChanges();
    }

    private void UpdateUndoRingChanges()
    {
        if (UndoRingChangesButton is null) return;
        var unchanged = Actions.Count == _initialActions.Count &&
                        Actions.Zip(_initialActions).All(pair => SameAction(pair.First, pair.Second));
        UndoRingChangesButton.Visibility = unchanged ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool SameAction(ActionItemViewModel current, RingAction original) =>
        current.Label == original.Label &&
        current.Glyph == original.Glyph &&
        current.IconKind == original.IconKind &&
        current.IconPath == (original.IconPath ?? "") &&
        current.Kind == original.Kind &&
        current.Target == original.Target &&
        current.Arguments == original.Arguments &&
        current.Accent == (original.Accent ?? "") &&
        current.Children.Count == original.Items.Count &&
        current.Children.Zip(original.Items).All(pair => SameAction(pair.First, pair.Second));

    private void UndoRingChanges_Click(object sender, RoutedEventArgs e)
    {
        _restoringActions = true;
        try
        {
            Actions.Clear();
            foreach (var action in _initialActions)
                Actions.Add(new ActionItemViewModel(CloneAction(action)));
        }
        finally
        {
            _restoringActions = false;
        }

        _selectedAction = null;
        _expandedGroup = null;
        UpdateRingSelectionUi();
        ActionEditor.Visibility = Visibility.Collapsed;
        EmptyActionMessage.Visibility = Visibility.Visible;
        RenderRingDesigner();
        UpdateUndoRingChanges();
    }

    private void RenderRingDesigner()
    {
        if (RingDesignerCanvas is null) return;
        RingDesignerCanvas.Children.Clear();
        _designerTargets.Clear();
        _designerGroupLines.Clear();
        _dropOutlines.Clear();

        const double centreX = 195;
        const double centreY = 180;
        var count = Actions.Count;

        if (count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Drop an action here", Foreground = (Brush)FindResource("MutedBrush"),
                Width = 180, TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(empty, centreX - 90);
            Canvas.SetTop(empty, centreY - 10);
            RingDesignerCanvas.Children.Add(empty);
            return;
        }

        if (_expandedGroup is not null && !Actions.Contains(_expandedGroup)) _expandedGroup = null;

        var rawButtonRadius = DesignerNumber(ButtonRadiusBox?.Text, 23);
        var rawOrbit = RingLayout.ResolveOrbit(DesignerNumber(OrbitRadiusBox?.Text, 60), rawButtonRadius, count);
        var rawSubRadius = rawButtonRadius * 0.76;
        var rawSubOrbit = rawOrbit + rawButtonRadius + rawSubRadius + 8;
        var rawExtent = _expandedGroup is null ? rawOrbit + rawButtonRadius : rawSubOrbit + rawSubRadius;
        var scale = Math.Min(1.2, (_expandedGroup is null ? 118 : 145) / Math.Max(1, rawExtent));
        var buttonRadius = rawButtonRadius * scale;
        var orbit = rawOrbit * scale;
        var subRadius = rawSubRadius * scale;
        var subOrbit = rawSubOrbit * scale;
        var hubRadius = Math.Max(10, DesignerNumber(HubRadiusBox?.Text, 16) * scale);
        var tint = AcrylicBrushes.ParseColor(TintBox?.Text, Color.FromRgb(0x26, 0x26, 0x2E));
        var globalAccent = AcrylicBrushes.ParseColor(AccentBox?.Text, Color.FromRgb(0x5C, 0x7C, 0xFA));
        var tintBrush = new SolidColorBrush(tint) { Opacity = TintOpacitySlider?.Value ?? 0.9 };
        var labels = new List<(ActionItemViewModel Action, Point Point, double Radius)>();

        void AddButton(ActionItemViewModel action, Point point, double radius)
        {
            var selected = ReferenceEquals(action, _selectedAction);
            var accent = AcrylicBrushes.ParseColor(action.Accent, globalAccent);
            var button = CreateDesignerButton(action, radius, tintBrush, accent, globalAccent, selected);
            var dropOutline = new System.Windows.Shapes.Ellipse
            {
                Stroke = new SolidColorBrush(globalAccent), StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 2.5, 2 },
                Margin = new Thickness(2), Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            button.Children.Add(dropOutline);
            _dropOutlines.Add(dropOutline);
            button.Tag = action;
            button.DataContext = action;
            button.Cursor = Cursors.Hand;
            button.AllowDrop = true;
            button.ToolTip = $"{action.DisplayLabel}\nDrop another action here to replace it";
            button.MouseLeftButtonUp += RingDesignerItem_MouseLeftButtonUp;
            button.PreviewMouseLeftButtonDown += ActionDragSource_PreviewMouseLeftButtonDown;
            button.PreviewMouseMove += ActionDragSource_PreviewMouseMove;
            button.DragEnter += (_, _) =>
            {
                if (_expandedGroup is null || ReferenceEquals(action.Parent, _expandedGroup))
                    dropOutline.Visibility = Visibility.Visible;
            };
            button.DragLeave += (_, _) => dropOutline.Visibility = Visibility.Collapsed;
            button.DragOver += RingTarget_DragOver;
            Canvas.SetLeft(button, point.X - radius);
            Canvas.SetTop(button, point.Y - radius);
            RingDesignerCanvas.Children.Add(button);
            _designerTargets.Add(new DesignerTarget
            {
                Action = action, Center = point, Radius = radius, Button = button,
            });
            labels.Add((action, point, radius));
        }

        for (var index = 0; index < count; index++)
        {
            var action = Actions[index];
            var point = RingLayout.ButtonCenter(new Point(centreX, centreY), orbit, index, count);
            AddButton(action, point, buttonRadius);
        }

        if (_expandedGroup is not null && _expandedGroup.Children.Count > 0)
        {
            var parentIndex = Actions.IndexOf(_expandedGroup);
            if (parentIndex >= 0)
            {
                var parentAngle = RingLayout.AngleOf(parentIndex, count);
                var step = RingLayout.ChildAngleStep(subOrbit, subRadius);
                var parentPoint = RingLayout.ButtonCenter(new Point(centreX, centreY), orbit, parentIndex, count);
                for (var childIndex = 0; childIndex < _expandedGroup.Children.Count; childIndex++)
                {
                    var childPoint = RingLayout.ChildCenter(new Point(centreX, centreY), subOrbit,
                        parentAngle, childIndex, _expandedGroup.Children.Count, step);
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = parentPoint.X, Y1 = parentPoint.Y, X2 = childPoint.X, Y2 = childPoint.Y,
                        Stroke = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), StrokeThickness = 1,
                        IsHitTestVisible = false,
                    };
                    _designerGroupLines.Add(line);
                    RingDesignerCanvas.Children.Add(line);
                    AddButton(_expandedGroup.Children[childIndex], childPoint, subRadius);
                }
            }
        }

        var hub = CreateDesignerHub(hubRadius, tintBrush);
        Canvas.SetLeft(hub, centreX - hubRadius);
        Canvas.SetTop(hub, centreY - hubRadius);
        RingDesignerCanvas.Children.Add(hub);

        // Unlike the transient runtime ring, the editor keeps every pill visible
        // so the complete configuration can be scanned without hovering.
        foreach (var (action, point, radius) in labels)
        {
            if (_expandedGroup is not null) continue;

            var pill = CreateDesignerPill(action.DisplayLabel, tintBrush);
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = pill.DesiredSize;
            var dx = point.X - centreX;
            var dy = point.Y - centreY;
            double left;
            double top;
            if (Math.Abs(dy) > Math.Abs(dx))
            {
                left = point.X - size.Width / 2;
                top = dy < 0 ? point.Y - radius - 8 - size.Height : point.Y + radius + 8;
            }
            else
            {
                left = dx >= 0 ? point.X + radius + 8 : point.X - radius - 8 - size.Width;
                top = point.Y - size.Height / 2;
            }
            Canvas.SetLeft(pill, Math.Clamp(left, 2, RingDesignerCanvas.Width - size.Width - 2));
            Canvas.SetTop(pill, Math.Clamp(top, 2, RingDesignerCanvas.Height - size.Height - 74));
            RingDesignerCanvas.Children.Add(pill);
            var target = _designerTargets.FirstOrDefault(item => ReferenceEquals(item.Action, action));
            if (target is not null) target.Label = pill;
        }
    }

    private static double DesignerNumber(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static Grid CreateDesignerButton(ActionItemViewModel action, double radius, Brush tintBrush,
        Color accent, Color selectionAccent, bool selected)
    {
        var host = new Grid { Width = radius * 2, Height = radius * 2 };
        host.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = tintBrush,
            Stroke = selected ? new SolidColorBrush(selectionAccent) : new SolidColorBrush(Color.FromArgb(0x2A, 255, 255, 255)),
            StrokeThickness = selected ? 2 : 1,
        });
        host.Children.Add(new System.Windows.Shapes.Ellipse { Fill = AcrylicBrushes.Sheen });
        host.Children.Add(new System.Windows.Shapes.Ellipse { Fill = AcrylicBrushes.Noise });
        if (selected)
            host.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(selectionAccent), Opacity = 0.18 });

        if (action.UseAppImage && action.DisplayIcon is not null)
            host.Children.Add(new Image
            {
                Source = action.DisplayIcon, Width = radius * 1.05, Height = radius * 1.05,
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        else
            host.Children.Add(new TextBlock
            {
                Text = action.DisplayGlyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = radius * 0.82, Foreground = new SolidColorBrush(Color.FromArgb(0xDE, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });

        if (action.Children.Count > 0)
        {
            var diameter = Math.Max(5, radius * 0.28);
            host.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = diameter, Height = diameter, Fill = new SolidColorBrush(accent),
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(radius * 0.707, -radius * 0.707),
            });
        }
        return host;
    }

    private static Grid CreateDesignerHub(double radius, Brush tintBrush)
    {
        var host = new Grid { Width = radius * 2, Height = radius * 2, IsHitTestVisible = false };
        host.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = tintBrush, Stroke = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), StrokeThickness = 1,
        });
        host.Children.Add(new System.Windows.Shapes.Ellipse { Fill = AcrylicBrushes.Noise });
        host.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x3E, 0x52)), Opacity = 0.72,
        });
        host.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = radius * 0.7, Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return host;
    }

    private static Border CreateDesignerPill(string text, Brush tintBrush) => new()
    {
        Background = tintBrush, BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = text, MaxWidth = 130, TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11.5, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)),
            TextAlignment = TextAlignment.Center, Margin = new Thickness(10, 3, 10, 4),
        },
    };

    private void RingDesignerItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!RecentlyCompletedDrag() && sender is FrameworkElement { Tag: ActionItemViewModel action })
        {
            if (ReferenceEquals(_selectedAction, action))
            {
                DeselectRingAction();
                e.Handled = true;
                return;
            }

            if (action.Parent is null && action.Children.Count > 0)
            {
                _selectedAction = action;
                _expandedGroup = action;
                UpdateRingSelectionUi();
                RenderRingDesigner();
            }
            else
            {
                SelectRingAction(action);
            }
        }
        e.Handled = true;
    }

    private void SelectRingAction(ActionItemViewModel action)
    {
        _selectedAction = action;
        _expandedGroup = action.Children.Count > 0 ? action : action.Parent;
        UpdateRingSelectionUi();
        RenderRingDesigner();
    }

    private void UpdateRingSelectionUi()
    {
        if (SelectedRingCommands is null) return;
        var selected = _selectedAction is not null;
        SelectedRingCommands.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        RingDetailsEditor.DataContext = _selectedAction;
        RingDetailsEditor.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        RingEditEmptyMessage.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DeselectRingAction()
    {
        _selectedAction = null;
        _expandedGroup = null;
        UpdateRingSelectionUi();
        RenderRingDesigner();
    }

    private void RingActionsTab_Click(object sender, RoutedEventArgs e) => ShowRingActionsTab();
    private void RingEditTab_Click(object sender, RoutedEventArgs e) => ShowRingEditTab();

    private void ShowRingActionsTab()
    {
        RingActionsTabContent.Visibility = Visibility.Visible;
        RingEditTabContent.Visibility = Visibility.Collapsed;
        RingActionsTabIndicator.Visibility = Visibility.Visible;
        RingEditTabIndicator.Visibility = Visibility.Collapsed;
        RingActionsTab.Foreground = Brushes.White;
        RingEditTab.Foreground = (Brush)FindResource("MutedBrush");
    }

    private void ShowRingEditTab()
    {
        RingActionsTabContent.Visibility = Visibility.Collapsed;
        RingEditTabContent.Visibility = Visibility.Visible;
        RingActionsTabIndicator.Visibility = Visibility.Collapsed;
        RingEditTabIndicator.Visibility = Visibility.Visible;
        RingActionsTab.Foreground = (Brush)FindResource("MutedBrush");
        RingEditTab.Foreground = Brushes.White;
    }

    private void NewRingAction_Click(object sender, RoutedEventArgs e)
    {
        var added = NewAction(null);
        Actions.Add(added);
        SelectRingAction(added);
        ShowRingEditTab();
    }

    private void RingEditCommand_Click(object sender, RoutedEventArgs e) => ShowRingEditTab();

    private void RingRemoveCommand_Click(object sender, RoutedEventArgs e) => DeleteAction_Click(sender, e);

    private void DeselectRingAction_Click(object sender, RoutedEventArgs e) => DeselectRingAction();

    private void ActionDragSource_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source) return;
        _suppressPresetClick = false;
        _pendingDragSource = source;
        _dragStartPoint = e.GetPosition(this);
    }

    private void ActionDragSource_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement source ||
            !ReferenceEquals(source, _pendingDragSource)) return;
        if (source.DataContext is not ActionItemViewModel && source.DataContext is not ActionPreset) return;
        if (_expandedGroup is not null && source.DataContext is ActionItemViewModel ringItem &&
            !ReferenceEquals(ringItem.Parent, _expandedGroup)) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        ShowDragPreview(source.DataContext);
        _dropCommitted = false;
        _suppressPresetClick = source.DataContext is ActionPreset;
        try
        {
            DragDrop.DoDragDrop(source, source.DataContext, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            DragPreviewLayer.Children.Clear();
            ClearDropOutlines();
            ClearInsertPreview();
            _dragPreview = null;
            _pendingDragSource = null;
            _dragCompletedAt = Environment.TickCount64;
        }
    }

    private void ShowDragPreview(object data)
    {
        var action = data switch
        {
            ActionPreset preset => preset.Action,
            ActionItemViewModel item => item.ToModel(),
            _ => null,
        };
        if (action is null) return;

        var card = new Border
        {
            Width = 190, Padding = new Thickness(11, 8, 11, 8), CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xFA, 0x3F, 0x39, 0x42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255)), BorderThickness = new Thickness(1),
            Opacity = 0.94,
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new TextBlock
        {
            Text = action.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        var text = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = action.Label, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = action.Target, FontSize = 10, Foreground = (Brush)FindResource("MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(text);
        card.Child = content;
        _dragPreview = card;
        DragPreviewLayer.Children.Clear();
        DragPreviewLayer.Children.Add(card);
        var point = Mouse.GetPosition(DragPreviewLayer);
        Canvas.SetLeft(card, point.X + 14);
        Canvas.SetTop(card, point.Y + 14);
    }

    private void ActionsRingPage_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (_dragPreview is null) return;
        var point = e.GetPosition(DragPreviewLayer);
        Canvas.SetLeft(_dragPreview, point.X + 14);
        Canvas.SetTop(_dragPreview, point.Y + 14);

        var ringPoint = e.GetPosition(RingDesignerCanvas);
        ClearDropOutlines();
        var replacementTarget = FindDesignerDropTarget(ringPoint);
        if (ringPoint.X < 0 || ringPoint.Y < 0 ||
            ringPoint.X > RingDesignerCanvas.ActualWidth || ringPoint.Y > RingDesignerCanvas.ActualHeight)
        {
            ClearInsertPreview();
            return;
        }

        if (_expandedGroup is not null && !IsExpandedGroupDropArea(ringPoint))
        {
            ClearInsertPreview();
            return;
        }

        if (replacementTarget is not null)
        {
            var targetIndex = _designerTargets.FindIndex(target =>
                ReferenceEquals(target.Action, replacementTarget));
            if (targetIndex >= 0 && targetIndex < _dropOutlines.Count)
                _dropOutlines[targetIndex].Visibility = Visibility.Visible;
            ClearInsertPreview();
            return;
        }

        ShowInsertPreview(e, ringPoint);
    }

    private bool RecentlyCompletedDrag() => Environment.TickCount64 - _dragCompletedAt < 300;

    private void ClearDropOutlines()
    {
        foreach (var outline in _dropOutlines) outline.Visibility = Visibility.Collapsed;
    }

    private ActionItemViewModel? FindDesignerDropTarget(Point point) => _designerTargets
        .Where(item => _expandedGroup is null || ReferenceEquals(item.Action.Parent, _expandedGroup))
        .Select(item => (item.Action, item.Radius,
            Distance: RingLayout.Distance(point, item.Center)))
        .Where(item => item.Distance <= item.Radius + 7)
        .OrderBy(item => item.Distance)
        .Select(item => item.Action)
        .FirstOrDefault();

    private void ShowInsertPreview(DragEventArgs e, Point pointer)
    {
        var model = e.Data.GetData(typeof(ActionPreset)) is ActionPreset preset ? preset.Action
            : e.Data.GetData(typeof(ActionItemViewModel)) is ActionItemViewModel item ? item.ToModel()
            : null;
        if (model is null) return;

        if (_expandedGroup is not null)
        {
            ShowGroupInsertPreview(model, pointer,
                e.Data.GetData(typeof(ActionItemViewModel)) as ActionItemViewModel);
            return;
        }

        RemoveInsertPreviewVisual();
        RestoreDesignerPreviewLayout();

        var sourceItem = e.Data.GetData(typeof(ActionItemViewModel)) as ActionItemViewModel;
        var movingTopLevel = sourceItem is { Parent: null } && Actions.Contains(sourceItem);
        var topTargets = _designerTargets.Where(target => target.Action.Parent is null &&
            (!movingTopLevel || !ReferenceEquals(target.Action, sourceItem))).ToList();
        var index = InsertionIndex(pointer, Actions.Count);
        if (movingTopLevel && Actions.IndexOf(sourceItem!) < index) index--;
        index = Math.Clamp(index, 0, topTargets.Count);
        var newCount = topTargets.Count + 1;

        var rawRadius = DesignerNumber(ButtonRadiusBox?.Text, 23);
        var rawOrbit = RingLayout.ResolveOrbit(DesignerNumber(OrbitRadiusBox?.Text, 60), rawRadius, newCount);
        var scale = Math.Min(1.2, 118 / Math.Max(1, rawOrbit + rawRadius));
        var radius = rawRadius * scale;
        var orbit = rawOrbit * scale;
        var center = RingLayout.ButtonCenter(new Point(195, 180), orbit, index, newCount);
        var accent = AcrylicBrushes.ParseColor(AccentBox?.Text, Color.FromRgb(0x5C, 0x7C, 0xFA));
        var tint = AcrylicBrushes.ParseColor(TintBox?.Text, Color.FromRgb(0x26, 0x26, 0x2E));

        foreach (var target in _designerTargets.Where(target => target.Action.Parent is not null))
        {
            target.Button.Visibility = Visibility.Collapsed;
            if (target.Label is not null) target.Label.Visibility = Visibility.Collapsed;
        }
        foreach (var line in _designerGroupLines) line.Visibility = Visibility.Collapsed;
        if (movingTopLevel)
        {
            var sourceTarget = _designerTargets.First(target => ReferenceEquals(target.Action, sourceItem));
            sourceTarget.Button.Visibility = Visibility.Collapsed;
            if (sourceTarget.Label is not null) sourceTarget.Label.Visibility = Visibility.Collapsed;
        }

        for (var currentIndex = 0; currentIndex < topTargets.Count; currentIndex++)
        {
            var finalIndex = currentIndex >= index ? currentIndex + 1 : currentIndex;
            var target = topTargets[currentIndex];
            var targetCenter = RingLayout.ButtonCenter(new Point(195, 180), orbit, finalIndex, newCount);
            var itemScale = radius / target.Radius;
            target.Button.RenderTransformOrigin = new Point(0.5, 0.5);
            target.Button.RenderTransform = new ScaleTransform(itemScale, itemScale);
            Canvas.SetLeft(target.Button, targetCenter.X - target.Radius);
            Canvas.SetTop(target.Button, targetCenter.Y - target.Radius);
            if (target.Label is not null) PositionDesignerLabel(target.Label, targetCenter, radius);
        }

        var ghost = new Grid
        {
            Width = radius * 2, Height = radius * 2, Opacity = 0.58,
            IsHitTestVisible = false,
        };
        ghost.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new SolidColorBrush(tint) { Opacity = TintOpacitySlider?.Value ?? 0.9 },
            Stroke = new SolidColorBrush(accent), StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 2.5, 2 },
        });
        ghost.Children.Add(new System.Windows.Shapes.Ellipse { Fill = AcrylicBrushes.Sheen });
        ghost.Children.Add(new TextBlock
        {
            Text = model.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = radius * 0.82, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        _insertPreview = ghost;
        RingDesignerCanvas.Children.Add(ghost);
        Canvas.SetLeft(ghost, center.X - radius);
        Canvas.SetTop(ghost, center.Y - radius);
    }

    private bool IsExpandedGroupDropArea(Point point)
    {
        if (_expandedGroup is null) return true;
        var parent = _designerTargets.FirstOrDefault(target => ReferenceEquals(target.Action, _expandedGroup));
        var child = _designerTargets.FirstOrDefault(target => ReferenceEquals(target.Action.Parent, _expandedGroup));
        if (parent is null || child is null) return false;
        var center = new Point(195, 180);
        var threshold = (RingLayout.Distance(parent.Center, center) + RingLayout.Distance(child.Center, center)) / 2;
        return RingLayout.Distance(point, center) >= threshold;
    }

    private void ShowGroupInsertPreview(RingAction model, Point pointer, ActionItemViewModel? source)
    {
        if (_expandedGroup is null) return;
        RemoveInsertPreviewVisual();
        RestoreDesignerPreviewLayout();

        var children = _designerTargets
            .Where(target => ReferenceEquals(target.Action.Parent, _expandedGroup))
            .Where(target => !ReferenceEquals(target.Action, source))
            .ToList();
        var count = children.Count + 1;
        var index = GroupInsertionIndex(pointer, count);
        var parentTarget = _designerTargets.First(target => ReferenceEquals(target.Action, _expandedGroup));
        var center = new Point(195, 180);
        var parentAngle = RingLayout.AngleAt(center, parentTarget.Center);
        var radius = children.Count > 0 ? children[0].Radius : parentTarget.Radius * 0.76;
        var orbit = children.Count > 0
            ? RingLayout.Distance(children[0].Center, center)
            : RingLayout.Distance(parentTarget.Center, center) + parentTarget.Radius + radius + 8;
        var step = RingLayout.ChildAngleStep(orbit, radius);

        foreach (var line in _designerGroupLines) line.Visibility = Visibility.Collapsed;
        if (source is not null && ReferenceEquals(source.Parent, _expandedGroup))
        {
            var sourceTarget = _designerTargets.First(target => ReferenceEquals(target.Action, source));
            sourceTarget.Button.Visibility = Visibility.Collapsed;
            if (sourceTarget.Label is not null) sourceTarget.Label.Visibility = Visibility.Collapsed;
        }
        for (var currentIndex = 0; currentIndex < children.Count; currentIndex++)
        {
            var finalIndex = currentIndex >= index ? currentIndex + 1 : currentIndex;
            var targetCenter = RingLayout.ChildCenter(center, orbit, parentAngle, finalIndex, count, step);
            var target = children[currentIndex];
            Canvas.SetLeft(target.Button, targetCenter.X - target.Radius);
            Canvas.SetTop(target.Button, targetCenter.Y - target.Radius);
            if (target.Label is not null) PositionDesignerLabel(target.Label, targetCenter, target.Radius);
        }

        var ghostCenter = RingLayout.ChildCenter(center, orbit, parentAngle, index, count, step);
        var accent = AcrylicBrushes.ParseColor(AccentBox?.Text, Color.FromRgb(0x5C, 0x7C, 0xFA));
        var tint = AcrylicBrushes.ParseColor(TintBox?.Text, Color.FromRgb(0x26, 0x26, 0x2E));
        var ghost = CreateInsertGhost(model, radius, tint, accent);
        _insertPreview = ghost;
        RingDesignerCanvas.Children.Add(ghost);
        Canvas.SetLeft(ghost, ghostCenter.X - radius);
        Canvas.SetTop(ghost, ghostCenter.Y - radius);
    }

    private int GroupInsertionIndex(Point pointer, int count)
    {
        if (_expandedGroup is null || count <= 1) return 0;
        var center = new Point(195, 180);
        var parentTarget = _designerTargets.First(target => ReferenceEquals(target.Action, _expandedGroup));
        var childTarget = _designerTargets.FirstOrDefault(target => ReferenceEquals(target.Action.Parent, _expandedGroup));
        var radius = childTarget?.Radius ?? parentTarget.Radius * 0.76;
        var orbit = childTarget is null
            ? RingLayout.Distance(parentTarget.Center, center) + parentTarget.Radius + radius + 8
            : RingLayout.Distance(childTarget.Center, center);
        var parentAngle = RingLayout.AngleAt(center, parentTarget.Center);
        var step = RingLayout.ChildAngleStep(orbit, radius);
        var pointerAngle = RingLayout.AngleAt(center, pointer);
        return Enumerable.Range(0, count)
            .Select(index => (Index: index,
                Delta: RingLayout.AngleDelta(pointerAngle,
                    parentAngle - (count - 1) * step / 2 + index * step)))
            .OrderBy(item => item.Delta)
            .First().Index;
    }

    private Grid CreateInsertGhost(RingAction model, double radius, Color tint, Color accent)
    {
        var ghost = new Grid
        {
            Width = radius * 2, Height = radius * 2, Opacity = 0.58,
            IsHitTestVisible = false,
        };
        ghost.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new SolidColorBrush(tint) { Opacity = TintOpacitySlider?.Value ?? 0.9 },
            Stroke = new SolidColorBrush(accent), StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 2.5, 2 },
        });
        ghost.Children.Add(new System.Windows.Shapes.Ellipse { Fill = AcrylicBrushes.Sheen });
        ghost.Children.Add(new TextBlock
        {
            Text = model.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = radius * 0.82, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return ghost;
    }

    private void ClearInsertPreview()
    {
        RemoveInsertPreviewVisual();
        RestoreDesignerPreviewLayout();
    }

    private void RemoveInsertPreviewVisual()
    {
        if (_insertPreview is not null && RingDesignerCanvas.Children.Contains(_insertPreview))
            RingDesignerCanvas.Children.Remove(_insertPreview);
        _insertPreview = null;
    }

    private void RestoreDesignerPreviewLayout()
    {
        foreach (var target in _designerTargets)
        {
            target.Button.Visibility = Visibility.Visible;
            target.Button.RenderTransform = Transform.Identity;
            Canvas.SetLeft(target.Button, target.Center.X - target.Radius);
            Canvas.SetTop(target.Button, target.Center.Y - target.Radius);
            if (target.Label is not null)
            {
                target.Label.Visibility = Visibility.Visible;
                PositionDesignerLabel(target.Label, target.Center, target.Radius);
            }
        }
        foreach (var line in _designerGroupLines) line.Visibility = Visibility.Visible;
    }

    private void PositionDesignerLabel(Border pill, Point point, double radius)
    {
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = pill.DesiredSize;
        var dx = point.X - 195;
        var dy = point.Y - 180;
        double left;
        double top;
        if (Math.Abs(dy) > Math.Abs(dx))
        {
            left = point.X - size.Width / 2;
            top = dy < 0 ? point.Y - radius - 8 - size.Height : point.Y + radius + 8;
        }
        else
        {
            left = dx >= 0 ? point.X + radius + 8 : point.X - radius - 8 - size.Width;
            top = point.Y - size.Height / 2;
        }
        Canvas.SetLeft(pill, Math.Clamp(left, 2, RingDesignerCanvas.Width - size.Width - 2));
        Canvas.SetTop(pill, Math.Clamp(top, 2, RingDesignerCanvas.Height - size.Height - 74));
    }

    private void RingTarget_DragOver(object sender, DragEventArgs e)
    {
        if (_expandedGroup is not null && sender is FrameworkElement { DataContext: ActionItemViewModel target } &&
            !ReferenceEquals(target.Parent, _expandedGroup))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = e.Data.GetDataPresent(typeof(ActionItemViewModel)) ? DragDropEffects.Move
            : e.Data.GetDataPresent(typeof(ActionPreset)) ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ActionsRingPage_PreviewDrop(object sender, DragEventArgs e)
    {
        ClearDropOutlines();
        ClearInsertPreview();
        var point = e.GetPosition(RingDesignerCanvas);
        if (point.X < 0 || point.Y < 0 || point.X > RingDesignerCanvas.ActualWidth ||
            point.Y > RingDesignerCanvas.ActualHeight) return;
        if (_expandedGroup is not null && !IsExpandedGroupDropArea(point))
        {
            e.Handled = true;
            return;
        }

        if (_dropCommitted)
        {
            e.Handled = true;
            return;
        }
        _dropCommitted = true;

        var target = FindDesignerDropTarget(point);

        if (target is not null)
        {
            if (e.Data.GetData(typeof(ActionItemViewModel)) is ActionItemViewModel source)
                MoveActionTo(source, target);
            else if (e.Data.GetData(typeof(ActionPreset)) is ActionPreset preset)
                ReplaceAction(target, preset.Action);
        }
        else
        {
            InsertDroppedAction(e, point);
        }
        e.Handled = true;
    }

    private void InsertDroppedAction(DragEventArgs e, Point point)
    {
        if (_expandedGroup is not null)
        {
            InsertDroppedActionIntoGroup(e, point);
            return;
        }

        var index = InsertionIndex(point, Actions.Count);

        if (e.Data.GetData(typeof(ActionPreset)) is ActionPreset preset)
        {
            var added = new ActionItemViewModel(CloneAction(preset.Action));
            Actions.Insert(index, added);
            SelectRingAction(added);
        }
        else if (e.Data.GetData(typeof(ActionItemViewModel)) is ActionItemViewModel source)
        {
            InsertActionAt(source, index);
        }
        e.Handled = true;
    }

    private void InsertDroppedActionIntoGroup(DragEventArgs e, Point point)
    {
        if (_expandedGroup is null) return;
        var source = e.Data.GetData(typeof(ActionItemViewModel)) as ActionItemViewModel;
        var addsChild = e.Data.GetData(typeof(ActionPreset)) is ActionPreset ||
                        source is null || !ReferenceEquals(source.Parent, _expandedGroup);
        var finalCount = _expandedGroup.Children.Count + (addsChild ? 1 : 0);
        var index = GroupInsertionIndex(point, Math.Max(1, finalCount));

        if (e.Data.GetData(typeof(ActionPreset)) is ActionPreset preset)
        {
            var added = new ActionItemViewModel(CloneAction(preset.Action), _expandedGroup);
            _expandedGroup.Children.Insert(Math.Clamp(index, 0, _expandedGroup.Children.Count), added);
            SelectRingAction(added);
        }
        else if (source is not null && ReferenceEquals(source.Parent, _expandedGroup))
        {
            var oldIndex = _expandedGroup.Children.IndexOf(source);
            if (oldIndex < 0) return;
            _expandedGroup.Children.RemoveAt(oldIndex);
            _expandedGroup.Children.Insert(Math.Clamp(index, 0, _expandedGroup.Children.Count), source);
            SelectRingAction(source);
        }
        e.Handled = true;
    }

    private static int InsertionIndex(Point point, int count) => count == 0 ? 0 : Math.Min(count,
        (int)Math.Floor(RingLayout.AngleAt(new Point(195, 180), point) / (360.0 / count)) + 1);

    private void PresetAction_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressPresetClick)
        {
            _suppressPresetClick = false;
            return;
        }
        if (RecentlyCompletedDrag() || _selectedAction is null ||
            sender is not FrameworkElement { DataContext: ActionPreset preset }) return;
        ReplaceAction(_selectedAction, preset.Action);
    }

    private void ReplaceAction(ActionItemViewModel target, RingAction model)
    {
        var list = target.Parent?.Children ?? Actions;
        var index = list.IndexOf(target);
        if (index < 0) return;
        var replacement = new ActionItemViewModel(CloneAction(model), target.Parent);
        list[index] = replacement;
        if (ReferenceEquals(_expandedGroup, target)) _expandedGroup = replacement.Children.Count > 0 ? replacement : null;
        SelectRingAction(replacement);
    }

    private void MoveActionTo(ActionItemViewModel source, ActionItemViewModel target)
    {
        if (ReferenceEquals(source, target)) return;
        var sourceList = source.Parent?.Children ?? Actions;
        var targetList = target.Parent?.Children ?? Actions;
        var targetIndex = targetList.IndexOf(target);
        if (targetIndex < 0) return;

        if (ReferenceEquals(sourceList, targetList))
        {
            var sourceIndex = sourceList.IndexOf(source);
            if (sourceIndex < 0) return;
            _restoringActions = true;
            try
            {
                sourceList[sourceIndex] = target;
                sourceList[targetIndex] = source;
            }
            finally
            {
                _restoringActions = false;
            }
            UpdateUndoRingChanges();
            SelectRingAction(source);
            return;
        }

        sourceList.Remove(source);
        var moved = new ActionItemViewModel(source.ToModel(), target.Parent);
        targetList.Insert(targetIndex, moved);
        SelectRingAction(moved);
    }

    private void InsertActionAt(ActionItemViewModel source, int index)
    {
        var sourceList = source.Parent?.Children ?? Actions;
        if (ReferenceEquals(sourceList, Actions))
        {
            var oldIndex = Actions.IndexOf(source);
            if (oldIndex < 0) return;
            Actions.RemoveAt(oldIndex);
            if (oldIndex < index) index--;
            index = Math.Clamp(index, 0, Actions.Count);
            Actions.Insert(index, source);
            SelectRingAction(source);
            return;
        }

        sourceList.Remove(source);
        var moved = new ActionItemViewModel(source.ToModel());
        Actions.Insert(Math.Clamp(index, 0, Actions.Count), moved);
        SelectRingAction(moved);
    }

    private static RingAction CloneAction(RingAction action) => new()
    {
        Label = action.Label, Glyph = action.Glyph, IconKind = action.IconKind,
        IconPath = action.IconPath, Kind = action.Kind, Target = action.Target,
        Arguments = action.Arguments, Accent = action.Accent,
        Items = action.Items.Select(CloneAction).ToList(),
    };

    private void ActionsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedAction = e.NewValue as ActionItemViewModel;
        UpdateRingSelectionUi();
        ActionEditor.DataContext = _selectedAction;
        ActionEditor.Visibility = _selectedAction is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyActionMessage.Visibility = _selectedAction is null ? Visibility.Visible : Visibility.Collapsed;
        UpdateTargetHelp();
        PrepareGlyphPicker();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ActionEditor.ScrollToTop);
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTargetHelp();

    private void GlyphChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null || sender is not Button { DataContext: GlyphOption option }) return;
        _selectedAction.Glyph = option.Glyph;
        FluentGlyphs.Select(option.Glyph);
    }

    private void GlyphSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GlyphSearchPlaceholder is not null)
            GlyphSearchPlaceholder.Visibility = string.IsNullOrEmpty(GlyphSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshGlyphRows(scrollToSelection: false);
    }

    private void PrepareGlyphPicker()
    {
        if (_selectedAction is null) return;
        if (!string.IsNullOrEmpty(GlyphSearchBox.Text)) GlyphSearchBox.Clear();
        FluentGlyphs.Select(_selectedAction.Glyph);
        RefreshGlyphRows(scrollToSelection: true);
    }

    private void RefreshGlyphRows(bool scrollToSelection)
    {
        if (GlyphList is null) return;
        var rows = FluentGlyphs.Filter(GlyphSearchBox?.Text);
        GlyphList.ItemsSource = rows;
        if (!scrollToSelection || _selectedAction is null) return;

        var selectedRow = rows.FirstOrDefault(row => row.Any(option => option.Glyph == _selectedAction.Glyph));
        if (selectedRow is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            () => GlyphList.ScrollIntoView(selectedRow));
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an app icon",
            Filter = "Icon sources|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe|Icons|*.ico|Images|*.png;*.jpg;*.jpeg;*.bmp|Applications|*.exe|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) _selectedAction.IconPath = dialog.FileName;
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a file or app",
            Filter = "Applications|*.exe;*.com;*.bat;*.cmd|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) _selectedAction.Target = dialog.FileName;
    }

    private void RevertAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null) return;
        _selectedAction.Revert();
        UpdateTargetHelp();
        PrepareGlyphPicker();
    }

    private void UpdateTargetHelp()
    {
        if (_selectedAction is null) return;
        ArgumentsPanel.Visibility = _selectedAction.Kind == ActionKind.Launch ? Visibility.Visible : Visibility.Collapsed;
        TargetBrowseButton.Visibility = _selectedAction.Kind == ActionKind.Launch ? Visibility.Visible : Visibility.Collapsed;
        TargetEditor.IsEnabled = _selectedAction.Kind != ActionKind.Group;
        TargetLabel.Text = _selectedAction.Kind switch
        {
            ActionKind.Launch => "File, folder, or app",
            ActionKind.Url => "Web address",
            ActionKind.Keys => "Keyboard shortcut",
            _ => "Target",
        };
        TargetHint.Text = _selectedAction.Kind switch
        {
            ActionKind.Launch => "Example: wt.exe or C:\\Tools\\app.exe",
            ActionKind.Url => "Example: https://example.com",
            ActionKind.Keys => "Example: Ctrl+Shift+S",
            _ => "Groups only contain sub-actions and do not run on their own.",
        };
    }

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        var item = NewAction(null);
        Actions.Add(item);
        Edit(item);
    }

    private void AddChild_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null) return;
        var parent = _selectedAction.Parent ?? _selectedAction;
        var item = NewAction(parent);
        parent.Children.Add(item);
        if (parent.Kind == ActionKind.Group || string.IsNullOrWhiteSpace(parent.Target)) parent.Kind = ActionKind.Group;
        Edit(item);
    }

    private static ActionItemViewModel NewAction(ActionItemViewModel? parent) =>
        new(new RingAction { Label = "New action", Glyph = "", Kind = ActionKind.Launch }, parent);

    private void Edit(ActionItemViewModel item)
    {
        _selectedAction = item;
        ActionEditor.DataContext = item;
        ActionEditor.Visibility = Visibility.Visible;
        EmptyActionMessage.Visibility = Visibility.Collapsed;
        UpdateTargetHelp();
        PrepareGlyphPicker();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ActionEditor.ScrollToTop);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SelectActionInTree(item));
    }

    private void SelectActionInTree(ActionItemViewModel item)
    {
        ActionsTree.UpdateLayout();
        var container = FindActionContainer(ActionsTree, item);
        if (container is null) return;
        container.IsSelected = true;
        container.BringIntoView();
    }

    private static TreeViewItem? FindActionContainer(ItemsControl parent, ActionItemViewModel item)
    {
        foreach (var entry in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem container) continue;
            if (ReferenceEquals(entry, item)) return container;
            container.IsExpanded = true;
            container.UpdateLayout();
            var descendant = FindActionContainer(container, item);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null) return;
        if (_selectedAction.Parent is null) Actions.Remove(_selectedAction);
        else _selectedAction.Parent.Children.Remove(_selectedAction);
        _selectedAction = null;
        _expandedGroup = null;
        ActionEditor.Visibility = Visibility.Collapsed;
        EmptyActionMessage.Visibility = Visibility.Visible;
        UpdateRingSelectionUi();
        RenderRingDesigner();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (_selectedAction is null) return;
        var list = _selectedAction.Parent?.Children ?? Actions;
        var current = list.IndexOf(_selectedAction);
        var next = current + delta;
        if (current >= 0 && next >= 0 && next < list.Count) list.Move(current, next);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";
        if (!TryBuildConfig(out var config, out var error))
        {
            StatusText.Text = error;
            return;
        }

        var saveError = _save(config);
        if (saveError is not null)
        {
            StatusText.Text = saveError;
            return;
        }

        Close();
    }

    private bool TryBuildConfig(out RingConfig config, out string error)
    {
        config = new RingConfig();
        error = "";
        if (!HotKeyParser.TryParse(HotKeyBox.Text.Trim(), out _, out _))
        {
            error = "Enter a valid keyboard shortcut, such as Ctrl+Alt+Space.";
            return false;
        }
        if (!TryInt(HoldThresholdBox, 50, 2000, "Hold threshold", out var threshold, out error) ||
            !TryDouble(ButtonRadiusBox, 12, 80, "Action size", out var buttonRadius, out error) ||
            !TryDouble(OrbitRadiusBox, 30, 300, "Ring radius", out var orbitRadius, out error) ||
            !TryDouble(HubRadiusBox, 8, 60, "Centre size", out var hubRadius, out error)) return false;
        if (!IsColour(TintBox.Text)) { error = "Tint colour must be a hex colour such as #26262E."; return false; }
        if (!IsColour(AccentBox.Text)) { error = "Accent colour must be a hex colour such as #5C7CFA."; return false; }
        if (Actions.Count == 0) { error = "Add at least one action to the ring."; return false; }

        foreach (var action in Flatten(Actions))
        {
            if (string.IsNullOrWhiteSpace(action.Label)) { error = "Every action needs a name."; return false; }
            if (!string.IsNullOrWhiteSpace(action.Accent) && !IsColour(action.Accent)) { error = $"The accent for '{action.DisplayLabel}' is not a valid hex colour."; return false; }
            if (action.IconKind == ActionIconKind.AppIcon && string.IsNullOrWhiteSpace(action.IconPath)) { error = $"'{action.DisplayLabel}' needs an app icon path."; return false; }
            if (action.Kind != ActionKind.Group && string.IsNullOrWhiteSpace(action.Target)) { error = $"'{action.DisplayLabel}' needs a target."; return false; }
        }

        config = new RingConfig
        {
            HotKey = HotKeyBox.Text.Trim(), HoldToActivate = HoldToActivateCheck.IsChecked == true,
            HoldThresholdMs = threshold, ButtonRadius = buttonRadius, OrbitRadius = orbitRadius,
            HubRadius = hubRadius, ShowLabels = ShowLabelsCheck.IsChecked == true,
            FadeOthersOnGroupOpen = FadeOthersOnGroupOpenCheck.IsChecked == true,
            Tint = TintBox.Text.Trim(), TintOpacity = TintOpacitySlider.Value,
            Accent = AccentBox.Text.Trim(), FollowCursor = FollowCursorCheck.IsChecked == true,
            HardwareAcceleration = HardwareAccelerationCheck.IsChecked == true,
            Actions = Actions.Select(action => action.ToModel()).ToList(),
        };
        return true;
    }

    private static IEnumerable<ActionItemViewModel> Flatten(IEnumerable<ActionItemViewModel> actions)
    {
        foreach (var action in actions)
        {
            yield return action;
            foreach (var child in action.Children) yield return child;
        }
    }

    private static bool TryInt(TextBox box, int min, int max, string name, out int value, out string error)
    {
        if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max) { error = ""; return true; }
        error = $"{name} must be between {min} and {max}.";
        return false;
    }

    private static bool TryDouble(TextBox box, double min, double max, string name, out double value, out string error)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max) { error = ""; return true; }
        error = $"{name} must be between {min} and {max}.";
        return false;
    }

    private static bool IsColour(string text)
    {
        try { return ColorConverter.ConvertFromString(text.Trim()) is Color; }
        catch { return false; }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void AuthorLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
