# DoRing

A radial action menu for Windows — a compact ring of translucent, acrylic-styled
icon buttons that appears wherever your pointer is, summoned by a global hotkey.
Point, release, the action runs.

Ships as a single portable `.exe`: no installer, no runtime prerequisite, no
registry footprint. Copy it anywhere — a folder, a USB stick, a synced drive — and
run it. Its settings live in a `doring.json` beside the binary, so the whole thing
travels together.

## Demo

<video src="https://github.com/DanRotaru/doring/raw/master/assets/DoRing-demo.mp4" controls muted playsinline width="720"></video>

*Player not showing? [Watch the demo](https://github.com/DanRotaru/doring/raw/master/assets/DoRing-demo.mp4).*

<img width="1030" height="720" alt="image" src="https://github.com/user-attachments/assets/8a08acda-3e44-448d-9a5b-9fa32e64b88b" />


## Getting started

1. Download `DoRing.exe` from the [latest release](https://github.com/DanRotaru/doring/releases/latest)
   and put it wherever you like.
2. Run it. There is no main window — it sits in the system tray.
3. Press **Ctrl+Alt+Space** (or left-click the tray icon) to summon the ring at
   your mouse.
4. Right-click the tray icon → **Settings…** to change the shortcut and build your
   own ring.

Requires Windows 10 or 11, x64. Nothing else to install.

## Using the ring

Two ways to work it, and you don't choose between them up front — the same press
does both:

- **Hold and flick.** Keep the hotkey held, shove the pointer toward an item, let
  go. It runs. There is no click anywhere in the gesture, and the whole thing takes
  about as long as a keyboard shortcut would.
- **Tap and click.** Release the hotkey straight away and the ring stays on screen
  to be clicked with the mouse, or driven with the number keys `1`–`9`.

**Aiming is by direction, not by hitting a target.** Each button owns the entire
wedge of screen it sits in, from the centre outward — so a shove upward picks the
item at the top even if the pointer flies far past it. The centre is the one
exception: it stays a small circular target, because it means *cancel*, and cancel
should require intent.

Hovering a button pops it, tints it with its accent colour, and names it on a pill
beside it.

**Dismissing** is equally forgiving — the red centre button, `Esc`, clicking away,
right-clicking outside the centre, or just releasing the hotkey over nothing.
Right-clicking the centre button opens Settings.

## What you can put on it

### Ten kinds of action

| Kind | What it does |
| --- | --- |
| **Launch** | Runs an app, opens a folder or a document. Arguments supported. |
| **Url** | Opens a link in your default browser. |
| **Keys** | Injects a key combo (`Ctrl+Shift+S`, `Win+D`, …) into the window that had focus before the ring opened. |
| **ToggleWindow** | Minimises an app's window if it's already in front, brings it forward if it's open behind something, starts it if it isn't running. |
| **Command** | Built-in system commands — media transport, volume, mouse clicks, window placement. |
| **PasteText** | Types a stored snippet of text into the previously focused window. |
| **Clipboard** | Reads or transforms the clipboard: copy, cut, paste, clear, URL encode/decode, HTML encode/decode, upper, lower, trim. |
| **DateTime** | Pastes a formatted date or time — custom formats, UNIX timestamp, ISO week number. |
| **MousePosition** | Warps the pointer to an absolute screen coordinate. |
| **Group** | Holds child actions that fan out on hover. |

### Groups — a second orbit that fans out

Any action can hold children. Hovering it fans them out on an **outer orbit**, and
they own the arc they span — but only past the halfway line between the two orbits,
so overshooting the fan leaves the group open rather than collapsing it mid-reach.
A group's parent can stay clickable itself, so one button both *does* something and
*contains* things. Nesting is one level deep on purpose: a ring you have to
navigate is no longer faster than a menu.

### A library of ready-made actions

The settings window ships a catalogue across eight categories, so building a ring
is picking rather than typing:

- **Open** — apps, files, folders, URLs, Explorer, Terminal, Task Manager, Task View, Run, Control Panel
- **Media & volume** — play/pause, previous, next, stop, mute, live volume readout
- **Keyboard** — arbitrary shortcuts, paste text, emoji picker, undo, redo, select all
- **Mouse** — left/right/middle click, move to coordinate, centre pointer
- **Windows** — show desktop, snap left/right, virtual desktops, Snip, Magnifier, close/maximise/minimise, move window to centre/left/right/corners, Windows Settings
- **System** — Quick Settings, Search, Project display, Accessibility
- **Clipboard** — copy, cut, paste, clear, URL/HTML encode and decode, upper, lower, trim, clipboard history
- **Date and time** — current date, current time, date + time, UNIX timestamp, week number

### Icons: glyphs, brand icons, emoji, or your own file

Four icon sources per action:

- **Segoe Fluent Icons** — the Windows 11 system icon set, searchable by name
- **Simple Icons** — a bundled brand-icon font (GitHub, Spotify, Figma, …),
  optionally drawn in each brand's own colour
- **Emoji**, in true colour
- **A file of your own** — `.ico`, `.png`, any image, or an `.exe` whose icon gets
  extracted

Each action can also override the global accent colour with its own.

### Presets

The **Presets** tab saves the current layout as a named snapshot. Switching a
preset swaps out the actual ring; you can update a preset from later edits, rename
it in place, or delete it. Presets carry actions and groups only — the shortcut and
appearance stay global, so switching never changes how the app looks or opens. New
configs ship with **Everyday** and **Media** presets.

### Thirteen ring animations

`Pop` (the default: springs past full size and settles back), `Elastic`, `Zoom`,
`Drop`, `Spin`, `Swirl`, four directional `Slide`s, `Tilt`, `Whirl`, `Unfold`, and
`None`. One choice covers both directions — the exit is the entrance run backwards,
so the two always match. Two sliders tune it: **speed** and **travel** (how far it
moves, without touching the timing).

### Scroll to adjust volume

Any action can carry a scroll behaviour. Rolling the wheel over a media or volume
button changes the Windows master volume, and a dedicated **Volume** button shows
the current level as a live number on its face.

### Explorer context tokens

`%path%` and `%sel%` in an action's arguments expand to the folder open in the
Explorer window you were last in and the item selected there. Both quietly collapse
to nothing when there's no Explorer window or nothing selected — so "open a terminal
here" or "run this tool on the selected file" become one-button actions.

### A shortcut picker that captures combos Windows has claimed

The picker listens at a level below the shell, so pressing `Win+Z` sets your
shortcut instead of opening Snap Layouts — and if a combo can't be registered the
normal way, DoRing delivers it anyway.

### Light on resources

~5 MB of RAM at idle, around 100 MB while the ring is actually on screen, back to
~4 MB a few seconds after it closes. A **Hardware acceleration** setting turns the
GPU back on if you scale the ring far enough up to want it.

## Settings

Right-click the tray icon and choose **Settings…**, or right-click the ring's
centre button. The window covers the shortcut, gesture behaviour, motion,
appearance, and the actions themselves, with a reset control on every individual
setting. Changes are validated, saved, and applied immediately — hardware
acceleration is the one setting that needs a restart.

Beside the list-based **Actions** page there's an **Edit** page showing the ring
laid out as it will appear. Drag an action from the library onto a ring position,
drag ring items to reorder or to drop them into a group, select one to edit it, and
undo the lot with a single **Undo all**.

The tray menu is: Show ring · Settings… · Edit JSON… · Reload settings · Exit.

## Editing `doring.json` by hand

On first run the app writes `doring.json` next to the exe. **Edit JSON…** from the
tray menu opens it; **Reload settings** picks up your changes. The file is written
with relaxed escaping specifically so it stays readable.

A config written by an older build — naming an animation this build doesn't have,
or a setting that was renamed — loads anyway: unknown values fall back to defaults
rather than taking every *other* setting down with them. A file that's malformed
outright doesn't stop the app from starting, and is left in place.

```jsonc
{
  "HotKey": "Ctrl+Alt+Space",
  "HoldToActivate": true,  // hold the hotkey, move onto an item, release to run it
  "HoldThresholdMs": 180,  // how long a press must last to count as a hold, not a tap
  "ButtonRadius": 25,      // size of each round button, in DIPs
  "OrbitRadius": 60,       // centre-to-button distance; grown if buttons won't fit
  "HubRadius": 18,         // the centre dismiss button
  "ShowLabels": true,      // name the hovered action on a pill beside it
  "FadeOthersOnGroupOpen": true,
  "SettingsOnCloseRightClick": true,  // right-click the centre button to open Settings
  "Tint": "#26262E",
  "TintOpacity": 0.9,      // lower = more see-through
  "Accent": "#5C7CFA",
  "FollowCursor": true,    // open at the mouse rather than the screen centre
  "Animation": "Pop",
  "AnimationSpeed": 1.0,   // divides every duration
  "AnimationTravel": 1.0,  // scales how far things move
  "AnimateClose": true,    // an instant close is faster for keystroke actions
  "HardwareAcceleration": false,
  "ColoredIcons": false,   // draw Simple Icons in their brand colours
  "ColoredEmoji": true,
  "Actions": [
    { "Label": "Terminal", "Kind": "Launch", "Target": "wt.exe" },
    { "Label": "Copy",     "Kind": "Keys",   "Target": "Ctrl+C" },
    { "Label": "Docs",     "Kind": "Url",    "Target": "https://example.com" }
  ],
  "Presets": [
    {
      "Id": "a-stable-generated-id",
      "Name": "Writing",
      "Actions": [ /* the saved action layout */ ]
    }
  ],
  "ActivePresetId": "a-stable-generated-id"
}
```

What `Target` means depends on `Kind`:

| Kind | Target |
| --- | --- |
| `Launch` | An exe, folder, or document path. `Arguments` is passed along; environment variables and `%path%` / `%sel%` are expanded. |
| `Url` | Opened in the default browser. |
| `Keys` | A combo like `Ctrl+Shift+S`. |
| `ToggleWindow` | A process name like `WindowsTerminal.exe`, or a full path to the exe (launched with `Arguments` if it isn't running). |
| `Command` | A built-in command name — `MediaPlayPause`, `VolumeMute`, `Volume`, `MouseLeftClick`, `WindowLeft`, `WindowsSettings`, and so on. |
| `PasteText` | The literal text to type. |
| `Clipboard` | One of `url-encode`, `url-decode`, `html-encode`, `html-decode`, `upper`, `lower`, `trim`. |
| `DateTime` | A .NET format string, or `unix` for a timestamp, or `week` for the ISO week number. |
| `MousePosition` | `x,y` in screen pixels, e.g. `1920,540`. |
| `Group` | Nothing — the children go in `Items`. |

A few per-action fields worth knowing:

- `Items` — child actions; any button with these becomes a group. A group whose
  `Kind` is something other than `Group` stays clickable itself.
- `IconKind` — `Glyph` (the default), `SimpleIcon`, `Emoji`, or `AppIcon`. For
  `AppIcon`, set `IconPath` to an `.ico`, `.png`, other image, or `.exe`; relative
  paths resolve beside `DoRing.exe`.
- `Glyph` — a single character: a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font)
  codepoint, a Simple Icons one, or an emoji.
- `Accent` / `IconColor` — override the global accent for this one button.
- `ScrollBehavior` — `Volume` to make the wheel adjust volume over this button.

Button count is however long the `Actions` array is. Add a tenth and `OrbitRadius`
widens on its own rather than the buttons shrinking to fit.

## Command-line switches

| Switch | Effect |
| --- | --- |
| `--show` | Opens the ring immediately, without waiting for the hotkey. |
| `--settings` | Opens the settings window directly. |

One caveat if you script against `--show`: a window shown this way cannot take the
foreground — Windows only grants that off real user input — so it won't respond to
`Esc` and won't auto-hide. It also means `CopyFromScreen` captures nothing for a
layered window; use `PrintWindow` with `PW_RENDERFULLCONTENT` for a screenshot.

## Building from source

Requires the .NET 10 SDK.

```
dotnet run --project DoRing          # run it
dotnet publish DoRing -c Release     # build the portable exe
```

Output: `DoRing/bin/Release/net10.0-windows/win-x64/publish/DoRing.exe`, one
self-contained file of roughly 66 MB.

That size is close to the floor for a self-contained WPF app. Trimming is refused
outright by the SDK (`NETSDK1168` for WPF, `NETSDK1175` for WinForms), and NativeAOT
doesn't support WPF either. The only real lever is dropping `SelfContained`, which
gets you a sub-megabyte exe but then requires the .NET Desktop Runtime on every
machine you copy it to — which costs the portability the whole design is built on.

## Project layout

```
DoRing/
  App.xaml(.cs)               tray icon, hotkey registration, single-instance guard
  Views/
    RingWindow.xaml(.cs)      the ring: buttons, hub, label pill, hover, invoke
    SettingsWindow.xaml(.cs)  settings, action library, visual editor, presets
    ActionDetailsEditor       the per-action editing pane
  Models/RingConfig.cs        the config model, its defaults, and migration
  Services/
    RingLayout.cs             button placement + wedge (direction-based) hit testing
    RingAnimations.cs         the thirteen entrance/exit motions
    ActionRunner.cs           launches processes, injects keystrokes, runs commands
    AcrylicBrushes.cs         the faux-acrylic tint / sheen / grain layers
    ColorEmoji.cs             COLR/CPAL colour emoji rendering
    SimpleIconLibrary.cs      the bundled brand-icon font and its colours
    ExplorerContext.cs        %path% / %sel% resolution
    SystemVolume.cs           Core Audio master volume
    MemoryTrim.cs             hands idle pages back to the OS
    HotKeyParser.cs           "Ctrl+Alt+Space" -> (ModifierKeys, Key)
  Interop/
    NativeMethods.cs          the P/Invoke surface
    HotKeyManager.cs          RegisterHotKey on a message-only window, + hook fallback
    KeyCaptureHook.cs         captures shell-owned combos for the shortcut picker
    TrayMenuWindow.cs         a real owner window for the native tray popup menu
```

## Ideas not built yet

- **Nested rings** — a button that opens a whole sub-ring rather than a fan
- **Per-app rings**, keyed off the foreground window's process name
- **Start with Windows** toggle (a shortcut in `shell:startup`)
