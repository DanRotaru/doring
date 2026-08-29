using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Controls;
using DoRing.Models;

namespace DoRing.Views;

public partial class ActionDetailsEditor : UserControl
{
    private ActionItemViewModel? _watched;

    /// <summary>
    /// The glyph last chosen from each source while editing the current action,
    /// so hopping between sources returns to your own pick instead of resetting
    /// to the source's first icon.
    /// </summary>
    private readonly Dictionary<ActionIconKind, string> _chosenPerSource = [];

    public ActionDetailsEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Prepare();
    }

    public event EventHandler<ActionItemViewModel>? ActionCreated;
    public event EventHandler? ChooseActionRequested;

    /// <summary>Asks the host window to drop its color picker under <c>Anchor</c>.</summary>
    public event EventHandler<ColorPickerRequest>? ColorPickerRequested;

    private ActionItemViewModel? Action => DataContext as ActionItemViewModel;

    /// <summary>Brings the top of the form back into view when the editor is reopened.</summary>
    public void ScrollToTop() => EditorScroll.ScrollToTop();

    private void ActionSummary_Click(object sender, RoutedEventArgs e)
    {
        ChooseActionRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Prepare()
    {
        Watch(Action);
        if (Action is null) return;
        _chosenPerSource.Clear();
        if (Action.IconKind != ActionIconKind.AppIcon && !string.IsNullOrEmpty(Action.Glyph))
            _chosenPerSource[Action.IconKind] = Action.Glyph;
        GlyphSearch.Clear();
        GlyphCatalog.Colored = Action.ColoredIcon;
        GlyphCatalog.Select(Action.IconKind, Action.Glyph);
        RefreshGlyphs(scrollToSelection: true);
        UpdateTargetHelp();
        EditorScroll.ScrollToTop();
    }

    /// <summary>Switching icon source swaps which font the picker lists.</summary>
    private void Watch(ActionItemViewModel? action)
    {
        if (ReferenceEquals(_watched, action)) return;
        if (_watched is not null) _watched.PropertyChanged -= Action_PropertyChanged;
        _watched = action;
        if (_watched is not null) _watched.PropertyChanged += Action_PropertyChanged;
    }

    private void Action_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Action is null) return;
        // Brand color is per action, so the picker grid previews the action
        // being edited instead of whatever the ring uses by default.
        if (e.PropertyName == nameof(ActionItemViewModel.ColoredIcon))
        {
            GlyphCatalog.Colored = Action.ColoredIcon;
            return;
        }
        if (e.PropertyName != nameof(ActionItemViewModel.IconKind)) return;
        // Custom Icons has no glyph list, so it keeps whatever glyph the action
        // carried - switching back to a font source should not have lost it.
        if (Action.IconKind == ActionIconKind.AppIcon) return;

        // A glyph only means something inside the font it came from, so a source
        // switch restores whatever was last picked from the source being opened,
        // falling back to its first icon. The search box is cleared for the same
        // reason: a query typed against the old font would hide the new list.
        GlyphSearch.Clear();
        var catalog = GlyphCatalog.For(Action.IconKind);
        if (_chosenPerSource.TryGetValue(Action.IconKind, out var remembered))
            Action.Glyph = remembered;
        else if (!catalog.Contains(Action.Glyph) && catalog.All.Count > 0)
            Action.Glyph = catalog.All[0].Glyph;

        GlyphCatalog.Select(Action.IconKind, Action.Glyph);
        RefreshGlyphs(scrollToSelection: true, scrollToTop: true);
    }

    private void GlyphSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GlyphSearchPlaceholder is not null)
            GlyphSearchPlaceholder.Visibility = string.IsNullOrEmpty(GlyphSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshGlyphs();
    }

    private void RefreshGlyphs(bool scrollToSelection = false, bool scrollToTop = false)
    {
        if (GlyphList is null || Action is null) return;
        var rows = GlyphCatalog.For(Action.IconKind).Filter(GlyphSearch?.Text);
        GlyphList.ItemsSource = rows;
        if (rows.Count == 0 || (!scrollToSelection && !scrollToTop)) return;
        // The selected glyph wins; scrollToTop is the fallback for a list where
        // nothing is selected, so a freshly opened source starts at its top.
        var target = scrollToSelection
            ? rows.FirstOrDefault(row => row.Any(option => option.Matches(Action.Glyph)))
            : null;
        target ??= scrollToTop ? rows[0] : null;
        if (target is not null)
            Dispatcher.BeginInvoke(() => GlyphList.ScrollIntoView(target));
    }

    private void Glyph_Click(object sender, RoutedEventArgs e)
    {
        if (Action is null || sender is not Button { DataContext: GlyphOption option }) return;
        Action.Glyph = option.Glyph;
        _chosenPerSource[Action.IconKind] = option.Glyph;
        GlyphCatalog.Select(Action.IconKind, option.Glyph);
        RefreshGlyphs();
    }

    private void IconColor_Click(object sender, RoutedEventArgs e) =>
        ColorPickerRequested?.Invoke(this, new ColorPickerRequest((FrameworkElement)sender, IconColorBox));

    private void AccentColor_Click(object sender, RoutedEventArgs e) =>
        ColorPickerRequested?.Invoke(this, new ColorPickerRequest((FrameworkElement)sender, AccentColorBox));

    private void SimpleIconsLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void UpdateTargetHelp() { }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Action is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an app icon",
            Filter = "Icon sources|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe|Icons|*.ico|Images|*.png;*.jpg;*.jpeg;*.bmp|Applications|*.exe|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Action.IconPath = dialog.FileName;
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        if (Action is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an app or file", Filter = "All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Action.Target = dialog.FileName;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Action is null) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Action.Target = dialog.FolderName;
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        Action?.Revert();
        Prepare();
    }

    private void AddChild_Click(object sender, RoutedEventArgs e)
    {
        if (Action is null) return;
        var parent = Action.Parent ?? Action;
        var child = new ActionItemViewModel(
            new RingAction { Label = "New action", Glyph = "", Kind = ActionKind.Launch }, parent);
        parent.Children.Add(child);
        if (parent.Kind == ActionKind.Group || string.IsNullOrWhiteSpace(parent.Target)) parent.Kind = ActionKind.Group;
        ActionCreated?.Invoke(this, child);
    }
}

/// <summary>Where to place the color picker, and which box it edits.</summary>
public sealed record ColorPickerRequest(FrameworkElement Anchor, TextBox Target);
