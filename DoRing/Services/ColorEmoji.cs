using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace DoRing.Services;

/// <summary>
/// Renders an emoji in full color. WPF's text stack draws color fonts as flat
/// outlines and GDI+ is no better, so the color layers are read straight out of
/// the font: Segoe UI Emoji is a COLR/CPAL font, where each emoji is a stack of
/// plain glyph outlines plus a palette index for each one. Drawing that stack
/// gives a real, resolution-independent color emoji.
/// </summary>
public static class ColorEmoji
{
    private static readonly Dictionary<int, ImageSource?> Cache = new();
    private static readonly Lazy<ColorFont?> Font = new(ColorFont.Load);

    /// <summary>
    /// The emoji as a color drawing, or null when the font has no color layers
    /// for it - the monochrome symbols are better left to normal text drawing.
    /// </summary>
    public static ImageSource? Render(string? glyph)
    {
        if (string.IsNullOrEmpty(glyph)) return null;
        var codePoint = char.ConvertToUtf32(glyph, 0);

        lock (Cache)
        {
            if (Cache.TryGetValue(codePoint, out var cached)) return cached;
            ImageSource? image;
            try { image = Font.Value?.Render(codePoint); }
            catch (Exception) { image = null; }
            return Cache[codePoint] = image;
        }
    }

    private sealed class ColorFont
    {
        private const double EmSize = 100;

        public required GlyphTypeface Typeface { get; init; }
        public required Dictionary<ushort, (int First, int Count)> BaseGlyphs { get; init; }
        public required (ushort Glyph, ushort Palette)[] Layers { get; init; }
        public required Brush[] Palette { get; init; }

        public ImageSource? Render(int codePoint)
        {
            if (!Typeface.CharacterToGlyphMap.TryGetValue(codePoint, out var baseGlyph)) return null;
            if (!BaseGlyphs.TryGetValue((ushort)baseGlyph, out var range)) return null;

            var drawing = new DrawingGroup();
            for (var i = range.First; i < range.First + range.Count && i < Layers.Length; i++)
            {
                var (glyph, paletteIndex) = Layers[i];
                var outline = Typeface.GetGlyphOutline(glyph, EmSize, EmSize);
                if (outline.IsEmpty()) continue;
                var brush = paletteIndex < Palette.Length ? Palette[paletteIndex] : Brushes.White;
                drawing.Children.Add(new GeometryDrawing(brush, null, outline));
            }
            if (drawing.Children.Count == 0) return null;

            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        public static ColorFont? Load()
        {
            var typeface = new Typeface(IconFonts.Emoji, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphTypeface)) return null;

            using var stream = glyphTypeface.GetFontStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var font = buffer.ToArray();

            if (!TryFindTable(font, "COLR", out var colr) || !TryFindTable(font, "CPAL", out var cpal)) return null;

            // COLR v0: base glyph records (sorted by glyph id) point into a flat
            // list of layer records, each a glyph id plus a palette index.
            var baseCount = ReadUInt16(font, colr + 2);
            var baseOffset = colr + (int)ReadUInt32(font, colr + 4);
            var layerOffset = colr + (int)ReadUInt32(font, colr + 8);
            var layerCount = ReadUInt16(font, colr + 12);

            var baseGlyphs = new Dictionary<ushort, (int, int)>(baseCount);
            for (var i = 0; i < baseCount; i++)
            {
                var record = baseOffset + i * 6;
                baseGlyphs[ReadUInt16(font, record)] =
                    (ReadUInt16(font, record + 2), ReadUInt16(font, record + 4));
            }

            var layers = new (ushort, ushort)[layerCount];
            for (var i = 0; i < layerCount; i++)
            {
                var record = layerOffset + i * 4;
                layers[i] = (ReadUInt16(font, record), ReadUInt16(font, record + 2));
            }

            // CPAL: BGRA color records; palette 0 is the default palette.
            var entryCount = ReadUInt16(font, cpal + 2);
            var recordsOffset = cpal + (int)ReadUInt32(font, cpal + 8);
            var firstRecord = ReadUInt16(font, cpal + 12);
            var palette = new Brush[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                var record = recordsOffset + (firstRecord + i) * 4;
                Brush brush = new SolidColorBrush(Color.FromArgb(
                    font[record + 3], font[record + 2], font[record + 1], font[record]));
                brush.Freeze();
                palette[i] = brush;
            }

            return new ColorFont
            {
                Typeface = glyphTypeface,
                BaseGlyphs = baseGlyphs,
                Layers = layers,
                Palette = palette,
            };
        }

        /// <summary>Locates a table in the sfnt directory at the head of the file.</summary>
        private static bool TryFindTable(byte[] font, string tag, out int offset)
        {
            offset = 0;
            if (font.Length < 12) return false;
            var tables = ReadUInt16(font, 4);
            for (var i = 0; i < tables; i++)
            {
                var entry = 12 + i * 16;
                if (entry + 16 > font.Length) return false;
                if (tag[0] != font[entry] || tag[1] != font[entry + 1] ||
                    tag[2] != font[entry + 2] || tag[3] != font[entry + 3]) continue;
                offset = (int)ReadUInt32(font, entry + 8);
                return offset > 0 && offset < font.Length;
            }
            return false;
        }

        private static ushort ReadUInt16(byte[] data, int offset) =>
            BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

        private static uint ReadUInt32(byte[] data, int offset) =>
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }
}
