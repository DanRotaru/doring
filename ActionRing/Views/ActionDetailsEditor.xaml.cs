using System.Windows;
using System.Windows.Controls;
using ActionRing.Models;

namespace ActionRing.Views;

public partial class ActionDetailsEditor : UserControl
{
    public ActionDetailsEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Prepare();
    }

    public event EventHandler<ActionItemViewModel>? ActionCreated;

    private ActionItemViewModel? Action => DataContext as ActionItemViewModel;

    private void Prepare()
    {
        if (Action is null) return;
        GlyphSearch.Clear();
        FluentGlyphs.Select(Action.Glyph);
        RefreshGlyphs(scrollToSelection: true);
        UpdateTargetHelp();
        EditorScroll.ScrollToTop();
    }

    private void GlyphSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GlyphSearchPlaceholder is not null)
            GlyphSearchPlaceholder.Visibility = string.IsNullOrEmpty(GlyphSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshGlyphs();
    }

    private void RefreshGlyphs(bool scrollToSelection = false)
    {
        if (GlyphList is null) return;
        var rows = FluentGlyphs.Filter(GlyphSearch?.Text);
        GlyphList.ItemsSource = rows;
        if (!scrollToSelection || Action is null) return;
        var selectedRow = rows.FirstOrDefault(row => row.Any(option => option.Glyph == Action.Glyph));
        if (selectedRow is not null)
            Dispatcher.BeginInvoke(() => GlyphList.ScrollIntoView(selectedRow));
    }

    private void Glyph_Click(object sender, RoutedEventArgs e)
    {
        if (Action is null || sender is not Button { DataContext: GlyphOption option }) return;
        Action.Glyph = option.Glyph;
        FluentGlyphs.Select(option.Glyph);
        RefreshGlyphs();
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTargetHelp();

    private void UpdateTargetHelp()
    {
        if (Action is null || ArgumentsPanel is null) return;
        ArgumentsPanel.Visibility = Action.Kind == ActionKind.Launch ? Visibility.Visible : Visibility.Collapsed;
        BrowseTargetButton.Visibility = Action.Kind == ActionKind.Launch ? Visibility.Visible : Visibility.Collapsed;
        TargetBox.IsEnabled = Action.Kind != ActionKind.Group;
        TargetLabel.Text = Action.Kind switch
        {
            ActionKind.Launch => "File, folder, or app", ActionKind.Url => "Web address",
            ActionKind.Keys => "Keyboard shortcut", _ => "Target",
        };
        TargetHint.Text = Action.Kind switch
        {
            ActionKind.Launch => "Example: wt.exe or C:\\Tools\\app.exe",
            ActionKind.Url => "Example: https://example.com",
            ActionKind.Keys => "Example: Ctrl+Shift+S",
            _ => "Groups only contain sub-actions and do not run on their own.",
        };
    }

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
            Title = "Choose a file or app", Filter = "Applications|*.exe;*.com;*.bat;*.cmd|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Action.Target = dialog.FileName;
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
