using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActionRing.Models;

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
}

public enum ActionIconKind
{
    Glyph,
    AppIcon,
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

    public ActionKind Kind { get; set; } = ActionKind.Launch;

    /// <summary>Path, URL, or key combo ("Ctrl+Shift+S") depending on <see cref="Kind"/>.</summary>
    public string Target { get; set; } = "";

    public string Arguments { get; set; } = "";

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
    public double ButtonRadius { get; set; } = 23;

    /// <summary>
    /// Distance from the centre to each button's centre. Grown automatically if
    /// there are too many buttons to fit at this radius.
    /// </summary>
    public double OrbitRadius { get; set; } = 60;

    /// <summary>Radius of the centre dismiss button.</summary>
    public double HubRadius { get; set; } = 16;

    /// <summary>Show the hovered action's name on a pill below the ring.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>
    /// When a group fans open, fade its siblings almost all the way out instead
    /// of holding them back slightly, so only the open group reads.
    /// </summary>
    public bool FadeOthersOnGroupOpen { get; set; } = false;

    /// <summary>Base tint of the acrylic surface.</summary>
    public string Tint { get; set; } = "#26262E";

    /// <summary>Opacity of the tint, 0..1. Lower = more see-through.</summary>
    public double TintOpacity { get; set; } = 0.9;

    public string Accent { get; set; } = "#5C7CFA";

    /// <summary>Open the ring centred on the mouse instead of the screen centre.</summary>
    public bool FollowCursor { get; set; } = true;

    /// <summary>
    /// GPU rendering. Off by default, and deliberately: standing up WPF's D3D
    /// device costs about 100 MB of working set and 50-odd driver threads, which
    /// is a preposterous price for a ring of circles that is on screen for two
    /// seconds at a time. Software rendering draws this UI without breaking
    /// stride. Turn it on if you scale the ring up enough to notice.
    /// </summary>
    public bool HardwareAcceleration { get; set; } = false;

    public List<RingAction> Actions { get; set; } = new();

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
        AppContext.BaseDirectory, "actionring.json");

    public static RingConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<RingConfig>(json, JsonOptions);
                if (loaded is { Actions.Count: > 0 }) return loaded;
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
        Actions =
        {
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
                    new RingAction { Label = "Previous",     Glyph = "", Kind = ActionKind.Keys, Target = "MediaPreviousTrack" },
                    new RingAction { Label = "Play / Pause", Glyph = "", Kind = ActionKind.Keys, Target = "MediaPlayPause" },
                    new RingAction { Label = "Next",         Glyph = "", Kind = ActionKind.Keys, Target = "MediaNextTrack" },
                    new RingAction { Label = "Mute",          Glyph = "", Kind = ActionKind.Keys, Target = "VolumeMute" },
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
        }
    };
}
