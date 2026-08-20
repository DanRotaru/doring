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

    /// <summary>
    /// Where a point sits around the ring, in the same clockwise-from-12 degrees
    /// the button angles use. Undefined at the exact centre, which is the hub's
    /// business anyway.
    /// </summary>
    public static double AngleAt(Point center, Point p)
    {
        var deg = Math.Atan2(p.X - center.X, center.Y - p.Y) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>
    /// Which button owns a given direction. The buttons don't just answer for
    /// the circles they're drawn as: each one owns the whole wedge of the plane
    /// around it, out to wherever the window ends. Aiming is then a flick in a
    /// direction rather than landing on a small target, which is the whole point
    /// of a radial menu.
    /// </summary>
    public static int SectorIndex(double angle, int count)
    {
        if (count < 1) return -1;

        var step = 360.0 / count;
        // Buttons sit at the middle of their wedge, not its edge, so the
        // boundaries fall half a step either side of each centre.
        return (int)Math.Floor((angle + step / 2) / step) % count;
    }

    /// <summary>Shortest way round between two angles, in degrees.</summary>
    public static double AngleDelta(double a, double b)
    {
        var d = Math.Abs(a - b) % 360.0;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>
    /// The child nearest a direction, or -1 if the direction lies outside the
    /// fan altogether. Children only claim the arc they actually span - the rest
    /// of the outer band belongs to their parent, so reaching past the fan
    /// doesn't collapse the group.
    /// </summary>
    public static int NearestAngle(double angle, IReadOnlyList<double> angles, double tolerance)
    {
        var best = -1;
        var bestDelta = tolerance;

        for (var i = 0; i < angles.Count; i++)
        {
            var delta = AngleDelta(angle, angles[i]);
            if (delta <= bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }

        return best;
    }

    public static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
