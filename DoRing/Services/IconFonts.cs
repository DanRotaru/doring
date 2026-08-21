using System.Windows.Media;
using DoRing.Models;

namespace DoRing.Services;

/// <summary>
/// The two icon fonts a ring action can draw from: the system Fluent icon font
/// and the bundled Simple Icons brand font (Assets/SimpleIcons.ttf).
/// </summary>
public static class IconFonts
{
    public static FontFamily Fluent { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>
    /// Loaded from the packed resource, so it needs no machine-wide install.
    /// The name after '#' is the font's family name, not the file name.
    /// </summary>
    public static FontFamily SimpleIcons { get; } =
        new(new Uri("pack://application:,,,/"), "./Assets/#Simple Icons");

    public static FontFamily Emoji { get; } = new("Segoe UI Emoji, Segoe UI Symbol");

    public static FontFamily For(ActionIconKind kind) => kind switch
    {
        ActionIconKind.SimpleIcon => SimpleIcons,
        ActionIconKind.Emoji => Emoji,
        _ => Fluent,
    };

    /// <summary>
    /// The color to draw a glyph in, or null to keep the ring foreground. A
    /// per-action color wins; otherwise branded glyphs use their brand color
    /// while colored icons are turned on.
    /// </summary>
    public static Brush? IconBrush(ActionIconKind kind, string glyph, string? customColor, bool colored)
    {
        if (kind == ActionIconKind.AppIcon) return null;
        if (!string.IsNullOrWhiteSpace(customColor))
        {
            var color = AcrylicBrushes.ParseColor(customColor, Colors.Transparent);
            if (color != Colors.Transparent)
            {
                Brush custom = new SolidColorBrush(color);
                custom.Freeze();
                return custom;
            }
        }
        return colored && kind == ActionIconKind.SimpleIcon ? SimpleIconLibrary.BrushFor(glyph) : null;
    }
}
