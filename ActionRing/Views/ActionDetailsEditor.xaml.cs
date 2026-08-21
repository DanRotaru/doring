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
    public event EventHandler? ChooseActionRequested;

    private ActionItemViewModel? Action => DataContext as ActionItemViewModel;

    private void ActionSummary_Click(object sender, RoutedEventArgs e)
    {
        ChooseActionRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

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
