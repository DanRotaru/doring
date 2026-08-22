using System.Windows.Media.Animation;
using DoRing.Models;

namespace DoRing.Services;

/// <summary>
/// One entrance or exit, described as a displacement from the ring's resting
/// state plus how long it takes to cover it.
///
/// Both directions share the description: an open animates <em>from</em> these
/// values to rest, a close animates <em>to</em> them from rest. That is why
/// there is no From/To pair here - the resting state is always scale 1, no
/// rotation, no offset, fully opaque, and only the far end needs saying.
///
/// Everything is a transform on the ring as a whole, deliberately. The ring is
/// drawn on the CPU by default (see <c>RingConfig.HardwareAcceleration</c>) and
/// the open is rasterized once and composited, so a whole-tree scale, rotate,
/// translate and alpha are nearly free, while anything per-button would defeat
/// the cache and cost a full re-raster every frame.
/// </summary>
public sealed class RingMotion
{
    private readonly double _scaleX = 1;
    private readonly double _scaleY = 1;

    /// <summary>Horizontal scale at the far end of the motion; 1 leaves it alone.</summary>
    public double ScaleX { get => _scaleX; init => _scaleX = value; }

    /// <summary>Vertical scale at the far end of the motion; 1 leaves it alone.</summary>
    public double ScaleY { get => _scaleY; init => _scaleY = value; }

    /// <summary>
    /// Both axes at once, which is what all but the squashing entrances want. An
    /// axis set <em>after</em> this in the initializer overrides it; one set
    /// before is overwritten, since initializers run in the order written.
    /// </summary>
    public double Scale
    {
        init
        {
            _scaleX = value;
            _scaleY = value;
        }
    }

    /// <summary>Rotation at the far end, in degrees about the ring's centre.</summary>
    public double Rotation { get; init; }

    /// <summary>Horizontal offset at the far end, in DIPs. Negative is leftward.</summary>
    public double OffsetX { get; init; }

    /// <summary>Vertical offset at the far end, in DIPs. Negative is upward.</summary>
    public double OffsetY { get; init; }

    /// <summary>
    /// Length of the transform part, and of the animation as a whole: the fade is
    /// never allowed to outlast it, so this is what a caller waits on.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Length of the opacity part. Usually shorter than <see cref="Duration"/> on
    /// the way in - the ring reads as "there" the moment it is opaque, so that is
    /// the part that has to be quick, and the movement settling under it is what
    /// stops it looking like it blinked into place.
    /// </summary>
    public TimeSpan Fade { get; init; }

    /// <summary>
    /// An intrinsic speed for this one animation, multiplied into the config's
    /// speed slider rather than shown anywhere. It is for a variation whose
    /// timing wants to differ from the rest of the table without spending the
    /// user's slider range to say so - 1.2 is twenty percent quicker than the
    /// durations written below suggest.
    /// </summary>
    public double Speed { get; init; } = 1;

    public IEasingFunction? Easing { get; init; }

    /// <summary>No motion at all: the caller should just show or hide.</summary>
    public bool IsInstant => Duration <= TimeSpan.Zero;

    /// <summary>The fade, clamped so it can never end after the transform does.</summary>
    public TimeSpan FadeWithin => Fade > Duration ? Duration : Fade;
}

/// <summary>One entry in the settings picker.</summary>
public sealed record RingAnimationOption(RingAnimation Value, string Name, string Description)
{
    public override string ToString() => Name;
}

public static class RingAnimations
{
    /// <summary>
    /// The tuning sliders' range. Speed divides every duration, so 2x is twice
    /// as fast; travel scales the displacement, so 0 would be a plain fade and
    /// the floor keeps every animation recognisably itself.
    /// </summary>
    public const double MinSpeed = 0.5;
    public const double MaxSpeed = 2.0;
    public const double MinTravel = 0.25;
    public const double MaxTravel = 1.75;

    private static readonly RingMotion Nothing = new() { Duration = TimeSpan.Zero, Fade = TimeSpan.Zero };

    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);

    /// <summary>
    /// One of the four slides. The exit is the shorter of the two and eases in
    /// rather than out, which is what makes it read as a retreat.
    /// </summary>
    private static RingMotion Slide(double offsetX, double offsetY, bool leaving = false) => new()
    {
        Scale = 0.95,
        OffsetX = offsetX,
        OffsetY = offsetY,
        Duration = leaving ? Ms(200) : Ms(230),
        Fade = leaving ? Ms(180) : Ms(130),
        Easing = leaving
            ? new CubicEase { EasingMode = EasingMode.EaseIn }
            : new QuinticEase { EasingMode = EasingMode.EaseOut },
    };

    /// <summary>How the ring arrives, with the config's tuning applied.</summary>
    public static RingMotion Open(RingConfig config) =>
        config.Animation == RingAnimation.None
            ? Nothing
            : Tuned(Entrance(config.Animation), config);

    /// <summary>
    /// How the ring leaves: the same animation as the entrance, run the other
    /// way. Off, the ring simply stops being there - and a chosen action runs
    /// without waiting, which is the only reason to want it.
    /// </summary>
    public static RingMotion Close(RingConfig config) =>
        config.AnimateClose && config.Animation != RingAnimation.None
            ? Tuned(Exit(config.Animation), config)
            : Nothing;

    /// <summary>
    /// Applies the speed and travel sliders. Easing is left alone: it is the
    /// shape of the motion, and scaling it would turn one animation into another.
    /// </summary>
    private static RingMotion Tuned(RingMotion motion, RingConfig config)
    {
        if (motion.IsInstant) return motion;

        var speed = Math.Clamp(config.AnimationSpeed, MinSpeed, MaxSpeed) * motion.Speed;
        var travel = Math.Clamp(config.AnimationTravel, MinTravel, MaxTravel);

        return new RingMotion
        {
            // Travel is a distance from rest, and for scale rest is 1 - not 0.
            ScaleX = 1 + (motion.ScaleX - 1) * travel,
            ScaleY = 1 + (motion.ScaleY - 1) * travel,
            Rotation = motion.Rotation * travel,
            OffsetX = motion.OffsetX * travel,
            OffsetY = motion.OffsetY * travel,
            Duration = motion.Duration / speed,
            Fade = motion.Fade / speed,
            Easing = motion.Easing,
        };
    }

    /// <summary>
    /// The way in. <see cref="RingAnimation.Pop"/> is the default: it springs
    /// past full size and settles back, which reads as the ring arriving rather
    /// than merely appearing.
    /// </summary>
    private static RingMotion Entrance(RingAnimation animation) => animation switch
    {
        // A little quicker than the table's own pace: the overshoot lands inside
        // the first half, so the tail was time spent settling by an amount nobody
        // can see. The slider is left free to go faster still on top of this.
        RingAnimation.Pop => new RingMotion
        {
            Scale = 0.62,
            Duration = Ms(260),
            Fade = Ms(110),
            Speed = 1.2,
            Easing = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        },
        RingAnimation.Elastic => new RingMotion
        {
            Scale = 0.8,
            Duration = Ms(430),
            Fade = Ms(110),
            Easing = new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut },
        },
        // Starts oversized and settles inward, so the ring reads as coming
        // towards the pointer rather than out of it.
        RingAnimation.Zoom => new RingMotion
        {
            Scale = 1.35,
            Duration = Ms(190),
            Fade = Ms(120),
            Easing = new CubicEase { EasingMode = EasingMode.EaseOut },
        },
        // Comes up and across from the lower left, growing into place. The
        // sideways part is the smaller of the two on purpose: it should read as a
        // rise with a lean, not a diagonal.
        RingAnimation.Drop => new RingMotion
        {
            Scale = 0.8,
            OffsetX = -22,
            OffsetY = 46,
            Duration = Ms(240),
            Fade = Ms(130),
            Easing = new QuinticEase { EasingMode = EasingMode.EaseOut },
        },
        RingAnimation.Spin => new RingMotion
        {
            Scale = 0.6,
            Rotation = -140,
            Duration = Ms(300),
            Fade = Ms(130),
            Easing = new CubicEase { EasingMode = EasingMode.EaseOut },
        },
        RingAnimation.Swirl => new RingMotion
        {
            Scale = 1.25,
            Rotation = 55,
            Duration = Ms(280),
            Fade = Ms(140),
            Easing = new QuarticEase { EasingMode = EasingMode.EaseOut },
        },
        // The four slides are one motion in four directions, named for the side
        // the ring comes in from. Each retreats the way it came.
        RingAnimation.SlideLeft => Slide(-44, 0),
        RingAnimation.SlideRight => Slide(44, 0),
        RingAnimation.SlideTop => Slide(0, -44),
        RingAnimation.SlideBottom => Slide(0, 44),
        // Rotation carries this one on its own - the smallest of the set.
        RingAnimation.Tilt => new RingMotion
        {
            Scale = 0.97,
            Rotation = -14,
            Duration = Ms(170),
            Fade = Ms(100),
            Easing = new CubicEase { EasingMode = EasingMode.EaseOut },
        },
        // A whole turn, and long enough to read as one.
        RingAnimation.Whirl => new RingMotion
        {
            Scale = 0.5,
            Rotation = -320,
            Duration = Ms(430),
            Fade = Ms(150),
            Easing = new QuinticEase { EasingMode = EasingMode.EaseOut },
        },
        // The only non-uniform one: a flattened line that opens out vertically.
        RingAnimation.Unfold => new RingMotion
        {
            Scale = 0.55,
            ScaleY = 0.12,
            Duration = Ms(270),
            Fade = Ms(120),
            Easing = new CubicEase { EasingMode = EasingMode.EaseOut },
        },
        RingAnimation.None => Nothing,
        _ => Entrance(RingAnimation.Pop),
    };

    /// <summary>
    /// The way out, paired to the way in: same mechanic, eased the other way, and
    /// shorter, because an exit that has to be waited on is time the next thing
    /// spends waiting.
    ///
    /// A directional entrance either retreats the way it came or carries on past
    /// - whichever suits it. Drop surges up and falls back; Rise keeps going up;
    /// Drift carries on across. That choice is what gives two animations sharing
    /// a direction their own character.
    /// </summary>
    private static RingMotion Exit(RingAnimation animation) => animation switch
    {
        RingAnimation.Pop => new RingMotion
        {
            Scale = 1.18,
            Duration = Ms(160),
            Fade = Ms(150),
            Speed = 1.2,
            Easing = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Elastic => new RingMotion
        {
            Scale = 0.8,
            Duration = Ms(300),
            Fade = Ms(240),
            Easing = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Zoom => new RingMotion
        {
            Scale = 1.35,
            Duration = Ms(180),
            Fade = Ms(160),
            Easing = new CubicEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Drop => new RingMotion
        {
            Scale = 0.8,
            OffsetX = -22,
            OffsetY = 46,
            Duration = Ms(200),
            Fade = Ms(180),
            Easing = new CubicEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Spin => new RingMotion
        {
            Scale = 0.55,
            Rotation = 140,
            Duration = Ms(250),
            Fade = Ms(220),
            Easing = new CubicEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Swirl => new RingMotion
        {
            Scale = 1.25,
            Rotation = -55,
            Duration = Ms(230),
            Fade = Ms(210),
            Easing = new QuarticEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.SlideLeft => Slide(-44, 0, leaving: true),
        RingAnimation.SlideRight => Slide(44, 0, leaving: true),
        RingAnimation.SlideTop => Slide(0, -44, leaving: true),
        RingAnimation.SlideBottom => Slide(0, 44, leaving: true),
        RingAnimation.Tilt => new RingMotion
        {
            Scale = 0.97,
            Rotation = 14,
            Duration = Ms(150),
            Fade = Ms(140),
            Easing = new CubicEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Whirl => new RingMotion
        {
            Scale = 0.5,
            Rotation = 320,
            Duration = Ms(340),
            Fade = Ms(290),
            Easing = new QuinticEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.Unfold => new RingMotion
        {
            Scale = 0.55,
            ScaleY = 0.12,
            Duration = Ms(220),
            Fade = Ms(200),
            Easing = new CubicEase { EasingMode = EasingMode.EaseIn },
        },
        RingAnimation.None => Nothing,
        _ => Exit(RingAnimation.Pop),
    };

    /// <summary>Menu order for the settings picker; the default comes first.</summary>
    public static IReadOnlyList<RingAnimationOption> Options { get; } =
    [
        new(RingAnimation.Pop, "Pop (default)", "Springs past full size, then snaps back through it to leave."),
        new(RingAnimation.Tilt, "Tilt", "A small turn into place and back out - the subtlest of the set."),
        new(RingAnimation.Zoom, "Zoom", "Settles inward from oversized, and grows away to leave."),
        new(RingAnimation.Drop, "Drop", "Surges up from the lower left, growing, then falls back to it."),
        new(RingAnimation.SlideLeft, "Slide left", "Comes in from the left, and retreats the way it came."),
        new(RingAnimation.SlideRight, "Slide right", "Comes in from the right, and retreats the way it came."),
        new(RingAnimation.SlideTop, "Slide top", "Comes in from above, and retreats the way it came."),
        new(RingAnimation.SlideBottom, "Slide bottom", "Comes in from below, and retreats the way it came."),
        new(RingAnimation.Unfold, "Unfold", "Opens out from a flattened line, and folds shut to leave."),
        new(RingAnimation.Elastic, "Elastic", "Overshoots and wobbles into place, and out of it."),
        new(RingAnimation.Spin, "Spin", "Turns counter-clockwise as it grows, and unwinds to leave."),
        new(RingAnimation.Swirl, "Swirl", "Unwinds inward from oversized and rotated, then winds back out."),
        new(RingAnimation.Whirl, "Whirl", "A full turn on the way in, and another on the way out."),
        new(RingAnimation.None, "No animation", "The ring is simply shown and hidden, with nothing to wait for."),
    ];
}
