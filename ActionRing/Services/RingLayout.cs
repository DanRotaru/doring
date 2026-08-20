using System.Windows;

namespace ActionRing.Services;

/// <summary>
/// Places round buttons evenly around a circle. Angles run clockwise from
/// 12 o'clock, so button 0 sits at the top, which is how a radial menu reads.
/// </summary>
public static class RingLayout
{
    /// <summary>How much bigger than a button the hover target is.</summary>
    private const double HitSlop = 1.18;

    public static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var rad = (angleDeg - 90) * Math.PI / 180.0;
        return new Point(
            center.X + radius * Math.Cos(rad),
            center.Y + radius * Math.Sin(rad));
    }

    public static Point ButtonCenter(Point center, double orbit, int index, int count) =>
        PointOnCircle(center, orbit, 360.0 / count * index);

    /// <summary>
    /// Pushes the orbit out far enough that buttons never overlap, however many
    /// there are. Six at the configured radius fit comfortably; ten would
    /// collide, so the ring grows instead of the buttons shrinking.
    /// </summary>
    public static double ResolveOrbit(double configured, double buttonRadius, int count)
    {
        if (count < 2) return configured;

        // Chord between neighbouring centres is 2 * orbit * sin(pi / count);
        // it needs to clear two radii plus a visual gap.
        var needed = (buttonRadius * 2 + 6) / (2 * Math.Sin(Math.PI / count));
        return Math.Max(configured, needed);
    }

    public static double AngleOf(int index, int count) => 360.0 / count * index;

    /// <summary>
    /// Angular spacing between two adjacent children on the outer orbit, sized
    /// so their circles clear each other.
    /// </summary>
    public static double ChildAngleStep(double subOrbit, double subRadius)
    {
        var ratio = Math.Min(1.0, (subRadius + 3) / subOrbit);
        return 2 * Math.Asin(ratio) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Children fan out symmetrically about their parent's angle, so the group
    /// reads as belonging to the button it came from.
    /// </summary>
    public static Point ChildCenter(
        Point center, double subOrbit, double parentAngle,
        int index, int count, double step)
    {
        var spread = (count - 1) * step;
        return PointOnCircle(center, subOrbit, parentAngle - spread / 2 + index * step);
    }

    public static bool IsWithin(Point p, Point center, double radius) =>
        Distance(p, center) <= radius * HitSlop;

    public static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
