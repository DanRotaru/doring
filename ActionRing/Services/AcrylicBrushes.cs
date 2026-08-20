using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ActionRing.Services;

/// <summary>
/// Faux-acrylic building blocks.
///
/// WPF's AllowsTransparency window is a *layered* window, and layered windows
/// cannot host DWM blur-behind — so real system acrylic is off the table if we
/// also want a per-pixel-shaped ring. Instead we approximate it the same way
/// the real effect is composed: tint + luminosity sheen + fine noise. At ring
/// scale it is visually indistinguishable, and it costs nothing per frame.
/// </summary>
public static class AcrylicBrushes
{
    private const int NoiseSize = 128;

    /// <summary>Tileable monochrome noise, the grain that sells the effect.</summary>
    public static readonly ImageBrush Noise = CreateNoise();

    /// <summary>Top-left to bottom-right sheen, mimicking acrylic's light bleed.</summary>
    public static readonly LinearGradientBrush Sheen = CreateSheen();

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) &&
                ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch (FormatException) { }
        return fallback;
    }

    private static ImageBrush CreateNoise()
    {
        // Deterministic seed so the grain is identical run to run.
        var rng = new Random(20260820);
        var stride = NoiseSize * 4;
        var pixels = new byte[NoiseSize * stride];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var v = (byte)rng.Next(140, 255);
            var a = (byte)rng.Next(0, 26); // very sparse; alpha carries the grain
            // Premultiplied Bgra32.
            pixels[i + 0] = (byte)(v * a / 255);
            pixels[i + 1] = (byte)(v * a / 255);
            pixels[i + 2] = (byte)(v * a / 255);
            pixels[i + 3] = a;
        }

        var bitmap = BitmapSource.Create(
            NoiseSize, NoiseSize, 96, 96,
            PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, NoiseSize, NoiseSize),
            Stretch = Stretch.None,
            Opacity = 0.55,
        };
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateSheen()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 0.45),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.72),
                new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0),
            }
        };
        brush.Freeze();
        return brush;
    }
}
