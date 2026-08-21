using System.Collections.ObjectModel;
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
    private readonly Func<RingConfig, string?> _save;
    private readonly RingConfig _defaults = RingConfig.CreateDefault();
    private ActionItemViewModel? _selectedAction;
    private TextBox? _colorTarget;
    private double _pickerHue;
    private double _pickerSaturation;
    private double _pickerValue;

    public SettingsWindow(RingConfig config, Func<RingConfig, string?> save)
    {
        InitializeComponent();
        // Resolve the installed icon font once while the window is being
        // created. Opening the picker then only shows an already-built,
        // virtualized six-column list instead of enumerating glyphs on demand.
        _ = FluentGlyphs.All;
        _save = save;
        Actions = new ObservableCollection<ActionItemViewModel>(
            config.Actions.Select(action => new ActionItemViewModel(action)));
        DataContext = this;
        LoadGeneral(config);
        WatchGeneralChanges();
        UpdateResetButtons();
        ShowGeneral();
    }

    public ObservableCollection<ActionItemViewModel> Actions { get; }
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
        HardwareAccelerationCheck.IsChecked = config.HardwareAcceleration;
    }

    private void WatchGeneralChanges()
    {
        foreach (var box in new[] { HotKeyBox, HoldThresholdBox, ButtonRadiusBox, OrbitRadiusBox,
                     HubRadiusBox, TintBox, AccentBox })
            box.TextChanged += (_, _) => UpdateResetButtons();

        foreach (var check in new[] { HoldToActivateCheck, FollowCursorCheck,
                     ShowLabelsCheck, HardwareAccelerationCheck })
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

    private void ShowGeneral()
    {
        GeneralPage.Visibility = Visibility.Visible;
        ActionsPage.Visibility = Visibility.Collapsed;
        GeneralNav.Background = (Brush)FindResource("NavSelectedBrush");
        ActionsNav.Background = Brushes.Transparent;
        GeneralIndicator.Visibility = Visibility.Visible;
        ActionsIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowActions()
    {
        GeneralPage.Visibility = Visibility.Collapsed;
        ActionsPage.Visibility = Visibility.Visible;
        GeneralNav.Background = Brushes.Transparent;
        ActionsNav.Background = (Brush)FindResource("NavSelectedBrush");
        GeneralIndicator.Visibility = Visibility.Collapsed;
        ActionsIndicator.Visibility = Visibility.Visible;
    }

    private void ActionsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedAction = e.NewValue as ActionItemViewModel;
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
        ActionEditor.Visibility = Visibility.Collapsed;
        EmptyActionMessage.Visibility = Visibility.Visible;
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
