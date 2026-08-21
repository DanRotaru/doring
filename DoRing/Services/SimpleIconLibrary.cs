using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Media;

namespace DoRing.Services;

/// <summary>One Simple Icons glyph: its character, brand slug, and brand color.</summary>
public sealed record SimpleIconEntry(string Glyph, string Slug, Color Color);

/// <summary>
/// The bundled Simple Icons set. WPF cannot read the font's own name table, so
/// codepoint, slug, and brand color are generated from the upstream font and
/// stylesheet into Resources/simple-icons.txt as "CODEPOINT RRGGBB slug" lines.
/// </summary>
public static class SimpleIconLibrary
{
    private static readonly Lazy<IReadOnlyList<SimpleIconEntry>> Entries = new(Load);
    private static readonly Lazy<Dictionary<string, Brush>> Brushes = new(() =>
        All.ToDictionary(entry => entry.Glyph, entry =>
        {
            Brush brush = new SolidColorBrush(entry.Color);
            brush.Freeze();
            return brush;
        }));

    public static IReadOnlyList<SimpleIconEntry> All => Entries.Value;

    /// <summary>The brand color brush for a glyph, or null when it is not a Simple Icon.</summary>
    public static Brush? BrushFor(string? glyph) =>
        glyph is not null && Brushes.Value.TryGetValue(glyph, out var brush) ? brush : null;

    private static IReadOnlyList<SimpleIconEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DoRing.Resources.simple-icons.txt");
        if (stream is null) return Array.Empty<SimpleIconEntry>();

        var entries = new List<SimpleIconEntry>();
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint)) continue;
            if (!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) continue;
            if (parts[2].Length == 0) continue;

            entries.Add(new SimpleIconEntry(
                char.ConvertFromUtf32(codePoint),
                parts[2],
                Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)));
        }
        return entries;
    }
}
