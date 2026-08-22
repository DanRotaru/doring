using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoRing.Models;

public enum ActionKind
{
    /// <summary>Launch an executable or open a document/folder.</summary>
    Launch,
    /// <summary>Open a URL in the default browser.</summary>
    Url,
    /// <summary>Send a keystroke combo to whatever was focused before the ring opened.</summary>
    Keys,
    /// <summary>
    /// Does nothing on its own: it exists to hold <see cref="RingAction.Items"/>,
    /// which fan out when the button is hovered.
    /// </summary>
    Group,
    /// <summary>One of DoRing's built-in Windows/media commands.</summary>
    Command,
    /// <summary>Paste literal text into the previously focused window.</summary>
    PasteText,
    /// <summary>Move the pointer to an absolute screen coordinate.</summary>
    MousePosition,
    /// <summary>Paste a formatted date/time value.</summary>
    DateTime,
    /// <summary>Read or transform the Windows clipboard.</summary>
    Clipboard,
}

[JsonConverter(typeof(ScrollBehaviorJsonConverter))]
public enum ScrollBehavior
{
    None,
    Volume,
}

/// <summary>
/// Keeps configs written by versions that offered brightness scrolling
/// loadable. The removed value, and any other unknown value, becomes None.
/// </summary>
public sealed class ScrollBehaviorJsonConverter : JsonConverter<ScrollBehavior>
{
    public override ScrollBehavior Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return string.Equals(reader.GetString(), nameof(ScrollBehavior.Volume), StringComparison.OrdinalIgnoreCase)
                ? ScrollBehavior.Volume
                : ScrollBehavior.None;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
            return value == (int)ScrollBehavior.Volume ? ScrollBehavior.Volume : ScrollBehavior.None;

        throw new JsonException("ScrollBehavior must be a string or number.");
    }

    public override void Write(Utf8JsonWriter writer, ScrollBehavior value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == ScrollBehavior.Volume
            ? nameof(ScrollBehavior.Volume)
            : nameof(ScrollBehavior.None));
}

/// <summary>
/// How the ring animates. One choice covers both directions: the exit is the
/// entrance run the other way, so the two always match.
/// </summary>
[JsonConverter(typeof(RingAnimationJsonConverter))]
public enum RingAnimation
{
    /// <summary>The default: springs past full size and settles back.</summary>
    Pop,
    Elastic,
    Zoom,
    Drop,
    Spin,
    Swirl,
    SlideLeft,
    SlideRight,
    SlideTop,
    SlideBottom,
    Tilt,
    Whirl,
    Unfold,

    /// <summary>No animation at all: the ring is simply shown and hidden.</summary>
    None,
}

/// <summary>
/// Keeps a config naming an animation this build doesn't have loadable - a
/// retired one, or a hand-typed mistake: the unknown name becomes the default
/// rather than throwing, which would otherwise take the whole file down with it
/// and reset every other setting.
/// </summary>
public sealed class RingAnimationJsonConverter : JsonConverter<RingAnimation>
{
    public override RingAnimation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();

            // The four slides were one setting before they had directions.
            if (string.Equals(name, "Slide", StringComparison.OrdinalIgnoreCase))
                return RingAnimation.SlideLeft;

            return Enum.TryParse<RingAnimation>(name, ignoreCase: true, out var parsed)
                ? parsed
                : RingAnimation.Pop;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
            return Enum.IsDefined(typeof(RingAnimation), value)
                ? (RingAnimation)value
                : RingAnimation.Pop;

        throw new JsonException("Animation must be a string or number.");
    }

    public override void Write(Utf8JsonWriter writer, RingAnimation value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public enum ActionIconKind
{
    Glyph,
    AppIcon,

    /// <summary>A glyph from the bundled Simple Icons brand font.</summary>
    SimpleIcon,

    /// <summary>An emoji from the system emoji font.</summary>
    Emoji,
}

public sealed class RingAction
{
    /// <summary>Short label drawn inside the segment.</summary>
    public string Label { get; set; } = "";

    /// <summary>A single glyph (emoji or Segoe Fluent Icons codepoint) drawn above the label.</summary>
    public string Glyph { get; set; } = "";

    /// <summary>Whether the ring draws <see cref="Glyph"/> or an icon loaded from a file.</summary>
    public ActionIconKind IconKind { get; set; } = ActionIconKind.Glyph;

    /// <summary>Path to an .ico/.png/image or executable when <see cref="IconKind"/> is AppIcon.</summary>
    public string IconPath { get; set; } = "";

    /// <summary>Optional hex color for the glyph, e.g. "#FF5C7CFA". Overrides the brand color.</summary>
    public string? IconColor { get; set; }

    public ActionKind Kind { get; set; } = ActionKind.Launch;

    /// <summary>Path, URL, or key combo ("Ctrl+Shift+S") depending on <see cref="Kind"/>.</summary>
    public string Target { get; set; } = "";

    public string Arguments { get; set; } = "";

    /// <summary>Optional behavior when the pointer wheel is used over this ring item.</summary>
    public ScrollBehavior ScrollBehavior { get; set; }

    /// <summary>Optional per-button accent, e.g. "#FF5C7CFA". Falls back to the theme accent.</summary>
    public string? Accent { get; set; }

    /// <summary>
    /// Second-level actions. Any button with these becomes a group: hovering it
    /// fans the children out on an outer orbit. Only one level deep - children
    /// of children are ignored.
    /// </summary>
    public List<RingAction> Items { get; set; } = new();

    [JsonIgnore]
    public bool IsGroup => Items.Count > 0;

    /// <summary>
    /// A group whose Kind is something other than Group stays clickable itself,
    /// so a parent can both do something and hold children.
    /// </summary>
    [JsonIgnore]
    public bool IsClickable => Kind != ActionKind.Group;
}

/// <summary>A named snapshot of the actions that make up a ring.</summary>
public sealed class RingPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<RingAction> Actions { get; set; } = new();
}

public sealed class RingConfig
{
    /// <summary>Global hotkey, e.g. "Ctrl+Alt+Space".</summary>
    public string HotKey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>
    /// Keep the hotkey held, move onto an item, let go: the item runs. Tapping
    /// the hotkey instead leaves the ring up to be clicked, as before.
    /// </summary>
    public bool HoldToActivate { get; set; } = true;

    /// <summary>
    /// How long the hotkey has to stay down before a press counts as a hold
    /// rather than a tap. Short enough not to feel like a wait, long enough that
    /// an ordinary press-and-release never arms the gesture by accident.
    /// </summary>
    public int HoldThresholdMs { get; set; } = 180;

    /// <summary>Radius of each round action button, in DIPs.</summary>
    public double ButtonRadius { get; set; } = 25;

    /// <summary>
    /// Distance from the centre to each button's centre. Grown automatically if
    /// there are too many buttons to fit at this radius.
    /// </summary>
    public double OrbitRadius { get; set; } = 60;

    /// <summary>Radius of the centre dismiss button.</summary>
    public double HubRadius { get; set; } = 18;

    /// <summary>Show the hovered action's name on a pill below the ring.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>
    /// When a group fans open, slightly fade its siblings to emphasize the group.
    /// </summary>
    public bool FadeOthersOnGroupOpen { get; set; } = true;

    /// <summary>
    /// Right-clicking the centre close button opens the settings window. Off,
    /// a right click anywhere just dismisses the ring.
    /// </summary>
    public bool SettingsOnCloseRightClick { get; set; } = true;

    /// <summary>Base tint of the acrylic surface.</summary>
    public string Tint { get; set; } = "#26262E";

    /// <summary>Opacity of the tint, 0..1. Lower = more see-through.</summary>
    public double TintOpacity { get; set; } = 0.9;

    public string Accent { get; set; } = "#5C7CFA";

    /// <summary>Open the ring centred on the mouse instead of the screen centre.</summary>
    public bool FollowCursor { get; set; } = true;

    /// <summary>Which animation the ring plays, coming and going.</summary>
    public RingAnimation Animation { get; set; } = RingAnimation.Pop;

    /// <summary>
    /// Divides every duration in the chosen animation, so 2 is twice as fast.
    /// Clamped to the range the settings slider offers.
    /// </summary>
    public double AnimationSpeed { get; set; } = 1.0;

    /// <summary>
    /// Scales how far the animation moves - the growth, the rotation, the slide -
    /// without touching its timing or its easing.
    /// </summary>
    public double AnimationTravel { get; set; } = 1.0;

    /// <summary>
    /// Play the animation in reverse on the way out too. It costs a chosen action
    /// the length of that exit, because a keystroke or a paste has to land in the
    /// window that was focused before the ring, and the ring is that window until
    /// it is gone. Off, the ring vanishes and the action runs at once.
    /// </summary>
    public bool AnimateClose { get; set; } = true;

    /// <summary>Close the settings window after a successful save.</summary>
    public bool CloseAfterSaving { get; set; } = false;

    /// <summary>
    /// GPU rendering. Off by default, and deliberately: standing up WPF's D3D
    /// device costs about 100 MB of working set and 50-odd driver threads, which
    /// is a preposterous price for a ring of circles that is on screen for two
    /// seconds at a time. Software rendering draws this UI without breaking
    /// stride. Turn it on if you scale the ring up enough to notice.
    /// </summary>
    public bool HardwareAcceleration { get; set; } = false;

    /// <summary>Draw Simple Icons glyphs in their brand color instead of the ring foreground.</summary>
    public bool ColoredIcons { get; set; } = false;

    /// <summary>Draw emoji in color. Off falls back to monochrome outlines.</summary>
    public bool ColoredEmoji { get; set; } = true;

    public List<RingAction> Actions { get; set; } = new();

    /// <summary>Reusable action layouts. General appearance and hotkey settings stay global.</summary>
    public List<RingPreset> Presets { get; set; } = new();

    /// <summary>The preset most recently loaded into <see cref="Actions"/>, or null for a custom ring.</summary>
    public string? ActivePresetId { get; set; }

    // ---- persistence ---------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        // The default encoder escapes '+' as + and glyphs as \uXXXX, which
        // makes a hand-edited config unreadable. This file never leaves the
        // machine, so relaxed escaping is the right trade.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Config lives next to the .exe, so the whole app stays portable.</summary>
    public static string ConfigPath => Path.Combine(
        AppContext.BaseDirectory, "doring.json");

    public static RingConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                using var document = JsonDocument.Parse(json);
                var hadPresets = document.RootElement.EnumerateObject().Any(property =>
                    property.Name.Equals(nameof(Presets), StringComparison.OrdinalIgnoreCase));
                var loaded = JsonSerializer.Deserialize<RingConfig>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Actions ??= new();
                    loaded.Presets ??= new();
                    if (!hadPresets)
                    {
                        loaded.Presets =
                        [
                            new RingPreset
                            {
                                Id = "migrated-current",
                                Name = "Current ring",
                                Actions = loaded.Actions.Select(CloneAction).ToList(),
                            },
                            new RingPreset
                            {
                                Id = "default-media",
                                Name = "Media",
                                Actions = CreateMediaActions(),
                            },
                        ];
                        loaded.ActivePresetId = "migrated-current";
                    }
                    foreach (var preset in loaded.Presets)
                    {
                        if (string.IsNullOrWhiteSpace(preset.Id)) preset.Id = Guid.NewGuid().ToString("N");
                        preset.Actions ??= new();
                    }
                    AdoptRenamedAnimationKey(document.RootElement, loaded);
                    RemoveRetiredBrightnessActions(loaded.Actions);
                    foreach (var preset in loaded.Presets)
                        RemoveRetiredBrightnessActions(preset.Actions);
                    if (loaded.Actions.Count > 0) return loaded;
                }
            }
        }
        catch (Exception)
        {
            // A malformed config shouldn't stop the app from starting; fall
            // through to defaults and leave the bad file in place.
        }

        var defaults = CreateDefault();
        defaults.Save();
        return defaults;
    }

    /// <summary>
    /// Carries a config written while the entrance and the exit were still
    /// separate settings over to the single one that replaced them. The exit is
    /// no longer chosen, only turned on or off, so only the entrance survives.
    /// </summary>
    private static void AdoptRenamedAnimationKey(JsonElement root, RingConfig loaded)
    {
        if (root.TryGetProperty(nameof(Animation), out _)) return;

        if (root.TryGetProperty("OpenAnimation", out var open) &&
            open.ValueKind == JsonValueKind.String &&
            Enum.TryParse<RingAnimation>(open.GetString(), ignoreCase: true, out var animation))
        {
            loaded.Animation = animation;
        }
    }

    private static void RemoveRetiredBrightnessActions(List<RingAction> actions)
    {
        actions.RemoveAll(action =>
            action.Kind == ActionKind.Command &&
            (string.Equals(action.Target, "BrightnessUp", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action.Target, "BrightnessDown", StringComparison.OrdinalIgnoreCase)));

        foreach (var action in actions)
            RemoveRetiredBrightnessActions(action.Items);
    }

    public void Save()
    {
        TrySave(out _);
    }

    /// <summary>Save and report failures to callers that have a UI to show them.</summary>
    public bool TrySave(out string? error)
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            // Startup remains tolerant through Save(); the settings window can
            // use this overload to avoid pretending a failed write succeeded.
            error = exception.Message;
            return false;
        }
    }

    public static RingConfig CreateDefault() => new()
    {
        Actions = CreateEverydayActions(),
        Presets =
        {
            new RingPreset
            {
                Id = "default-everyday",
                Name = "Everyday",
                Actions = CreateEverydayActions(),
            },
            new RingPreset
            {
                Id = "default-media",
                Name = "Media",
                Actions = CreateMediaActions(),
            },
        },
        ActivePresetId = "default-everyday",
    };

    private static List<RingAction> CreateEverydayActions() =>
    [
            new RingAction { Label = "Terminal", Glyph = "", Kind = ActionKind.Launch, Target = "wt.exe" },
            new RingAction { Label = "Explorer", Glyph = "", Kind = ActionKind.Launch, Target = "explorer.exe" },
            new RingAction { Label = "Copy",     Glyph = "", Kind = ActionKind.Keys,   Target = "Ctrl+C" },
            new RingAction { Label = "Paste",    Glyph = "", Kind = ActionKind.Keys,   Target = "Ctrl+V" },
            new RingAction { Label = "Snip",     Glyph = "", Kind = ActionKind.Keys,   Target = "Win+Shift+S" },

            // Two grouped buttons: hovering either fans its children outward.
            new RingAction
            {
                Label = "Media", Glyph = "", Kind = ActionKind.Group,
                Items =
                {
                    new RingAction { Label = "Previous",   Glyph = "", Kind = ActionKind.Command, Target = "MediaPreviousTrack", ScrollBehavior = ScrollBehavior.Volume },
                    new RingAction { Label = "Play/Pause", Glyph = "", Kind = ActionKind.Command, Target = "MediaPlayPause", ScrollBehavior = ScrollBehavior.Volume },
                    new RingAction { Label = "Next",       Glyph = "", Kind = ActionKind.Command, Target = "MediaNextTrack", ScrollBehavior = ScrollBehavior.Volume },
                    new RingAction { Label = "Mute",       Glyph = "", Kind = ActionKind.Command, Target = "VolumeMute", ScrollBehavior = ScrollBehavior.Volume },
                    new RingAction { Label = "Stop",       Glyph = "", Kind = ActionKind.Command, Target = "MediaStop", ScrollBehavior = ScrollBehavior.Volume },
                    new RingAction { Label = "Volume",     Glyph = "\uE995", Kind = ActionKind.Command, Target = "Volume", ScrollBehavior = ScrollBehavior.Volume },
                }
            },
            new RingAction
            {
                Label = "Windows", Glyph = "", Kind = ActionKind.Group,
                Items =
                {
                    new RingAction { Label = "Task view",    Glyph = "", Kind = ActionKind.Keys, Target = "Win+Tab" },
                    new RingAction { Label = "Snap left",    Glyph = "", Kind = ActionKind.Keys, Target = "Win+Left" },
                    new RingAction { Label = "Snap right",   Glyph = "", Kind = ActionKind.Keys, Target = "Win+Right" },
                    new RingAction { Label = "Minimise all", Glyph = "", Kind = ActionKind.Keys, Target = "Win+D" },
                }
            },

            new RingAction { Label = "Docs", Glyph = "", Kind = ActionKind.Url, Target = "https://learn.microsoft.com/dotnet/desktop/wpf/" },
    ];

    private static List<RingAction> CreateMediaActions() =>
    [
        new RingAction { Label = "Play/Pause", Glyph = "", Kind = ActionKind.Command, Target = "MediaPlayPause", ScrollBehavior = ScrollBehavior.Volume },
        new RingAction { Label = "YouTube", Glyph = "", Kind = ActionKind.Url, Target = "https://youtube.com/" },
        new RingAction { Label = "Next", Glyph = "", Kind = ActionKind.Command, Target = "MediaNextTrack", ScrollBehavior = ScrollBehavior.Volume },
        new RingAction { Label = "Mute", Glyph = "", Kind = ActionKind.Command, Target = "VolumeMute", ScrollBehavior = ScrollBehavior.Volume },
        new RingAction { Label = "Volume", Glyph = "\uE995", Kind = ActionKind.Command, Target = "Volume", ScrollBehavior = ScrollBehavior.Volume },
        new RingAction { Label = "Stop", Glyph = "", Kind = ActionKind.Command, Target = "MediaStop", ScrollBehavior = ScrollBehavior.Volume },
        new RingAction { Label = "Previous", Glyph = "", Kind = ActionKind.Command, Target = "MediaPreviousTrack", ScrollBehavior = ScrollBehavior.Volume },
        new RingAction { Label = "Spotify", Glyph = "", Kind = ActionKind.Launch, Target = "Spotify" },
    ];

    private static RingAction CloneAction(RingAction action) => new()
    {
        Label = action.Label,
        Glyph = action.Glyph,
        IconKind = action.IconKind,
        IconPath = action.IconPath,
        IconColor = action.IconColor,
        Kind = action.Kind,
        Target = action.Target,
        Arguments = action.Arguments,
        ScrollBehavior = action.ScrollBehavior,
        Accent = action.Accent,
        Items = action.Items.Select(CloneAction).ToList(),
    };
}
