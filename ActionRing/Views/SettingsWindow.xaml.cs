using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using ActionRing.Models;
using ActionRing.Services;

namespace ActionRing.Views;

public sealed class ActionItemViewModel : INotifyPropertyChanged
{
    private string _label;
    private string _glyph;
    private ActionKind _kind;
    private string _target;
    private string _arguments;
    private string _accent;

    public ActionItemViewModel(RingAction action, ActionItemViewModel? parent = null)
    {
        Parent = parent;
        _label = action.Label;
        _glyph = action.Glyph;
        _kind = action.Kind;
        _target = action.Target;
        _arguments = action.Arguments;
        _accent = action.Accent ?? "";
        foreach (var child in action.Items) Children.Add(new ActionItemViewModel(child, this));
    }

    public static Array Kinds { get; } = Enum.GetValues<ActionKind>();
    public IReadOnlyList<string> AvailableGlyphs => FluentGlyphs.All;
    public ActionItemViewModel? Parent { get; }
    public ObservableCollection<ActionItemViewModel> Children { get; } = new();

    public string Label { get => _label; set { _label = value; Changed(); Changed(nameof(DisplayLabel)); } }
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "Unnamed action" : Label;
    public string Glyph { get => _glyph; set { _glyph = value; Changed(); } }
    public ActionKind Kind { get => _kind; set { _kind = value; Changed(); } }
    public string Target { get => _target; set { _target = value; Changed(); } }
    public string Arguments { get => _arguments; set { _arguments = value; Changed(); } }
    public string Accent { get => _accent; set { _accent = value; Changed(); } }

    public RingAction ToModel() => new()
    {
        Label = Label.Trim(), Glyph = Glyph, Kind = Kind, Target = Target.Trim(),
        Arguments = Arguments.Trim(), Accent = string.IsNullOrWhiteSpace(Accent) ? null : Accent.Trim(),
        Items = Children.Select(child => child.ToModel()).ToList(),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class FluentGlyphs
{
    public static IReadOnlyList<string> All { get; } = Load();

    private static IReadOnlyList<string> Load()
    {
        var typeface = new Typeface(
            new FontFamily("Segoe Fluent Icons"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        if (!typeface.TryGetGlyphTypeface(out var glyphTypeface)) return Array.Empty<string>();

        return glyphTypeface.CharacterToGlyphMap.Keys
            .Where(codePoint => codePoint is >= 0xE000 and <= 0xF8FF)
            .OrderBy(codePoint => codePoint)
            .Select(char.ConvertFromUtf32)
            .ToArray();
    }
}

public partial class SettingsWindow : Window
{
    private readonly Func<RingConfig, string?> _save;
    private ActionItemViewModel? _selectedAction;

    public SettingsWindow(RingConfig config, Func<RingConfig, string?> save)
    {
        InitializeComponent();
        _save = save;
        Actions = new ObservableCollection<ActionItemViewModel>(
            config.Actions.Select(action => new ActionItemViewModel(action)));
        DataContext = this;
        LoadGeneral(config);
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
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTargetHelp();

    private void UpdateTargetHelp()
    {
        if (_selectedAction is null) return;
        ArgumentsPanel.Visibility = _selectedAction.Kind == ActionKind.Launch ? Visibility.Visible : Visibility.Collapsed;
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
