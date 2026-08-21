using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DoRing.Services;

/// <summary>
/// Icon preferences that are global rather than per action. The settings
/// window binds its toggles here, so the appearance page and the action
/// editor always show the same state, and previews repaint on change.
/// </summary>
public sealed class IconPreferences : INotifyPropertyChanged
{
    public static IconPreferences Instance { get; } = new();

    private bool _colored;
    private bool _coloredEmoji = true;

    private IconPreferences() { }

    /// <summary>Draw Simple Icons glyphs in their brand color.</summary>
    public bool Colored
    {
        get => _colored;
        set
        {
            if (_colored == value) return;
            _colored = value;
            Changed();
            ColoredChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Draw emoji as color bitmaps instead of monochrome outlines.</summary>
    public bool ColoredEmoji
    {
        get => _coloredEmoji;
        set
        {
            if (_coloredEmoji == value) return;
            _coloredEmoji = value;
            Changed();
            ColoredChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when either preference changes, so previews can repaint.</summary>
    public static event EventHandler? ColoredChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
