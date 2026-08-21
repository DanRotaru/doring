# Action Ring

A radial action menu for Windows — a compact ring of translucent, acrylic-styled
icon buttons summoned by a global hotkey, in the spirit of Logitech's Actions Ring.
Ships as a single portable `.exe`: no installer, no runtime prerequisite, copy it
anywhere and run it.

## Running it

```
dotnet run --project ActionRing
```

The app has no main window. It sits in the tray; press **Ctrl+Alt+Space** (or
left-click the tray icon) to summon the ring at the mouse.

Two ways to work it, and you don't choose between them up front — the same press
does both:

- **Hold and flick.** Keep the hotkey held, move onto an item, let go: it runs. No
  click anywhere in the gesture.
- **Tap and click.** Release the hotkey straight away and the ring stays up to be
  clicked, or driven with `1`–`9`.

Aiming is by direction, not by landing on a target. Each button owns the whole wedge
of the screen it sits in, out to the edge of the ring's window, so a shove upward
picks the item at the top — there is no small circle to hit. Only the centre keeps a
circular target, since it means cancel.

- Hover a button — it pops, tints with its accent, and names itself on a pill beside it
- The red centre button, `Esc`, or clicking away dismisses it; right-clicking the
  centre button opens Settings, while right-clicking elsewhere dismisses the ring
- Releasing the hotkey over the centre, or over nothing, dismisses it too

## Building the portable exe

```
dotnet publish ActionRing -c Release
```

Output: `ActionRing/bin/Release/net10.0-windows/win-x64/publish/ActionRing.exe`,
one self-contained file of roughly 66 MB.

That size is close to the floor for a self-contained WPF app. Trimming is refused
outright by the SDK (`NETSDK1168` for WPF, `NETSDK1175` for WinForms), and NativeAOT
doesn't support WPF either. The only real lever is dropping `SelfContained`, which
gets you a sub-megabyte exe but requires the .NET Desktop Runtime on every machine
you copy it to.

## Settings

Right-click the tray icon and choose **Settings...** to configure the shortcut,
gesture behaviour, appearance, and the actions around the ring. The Actions page
supports adding, deleting, reordering, and grouping actions. Changes are validated,
saved, and applied immediately; hardware acceleration is the one setting that needs
an app restart.

The settings window still writes the portable `actionring.json` beside the exe.
Choose **Edit JSON...** from the tray menu if you want to edit that file directly,
then choose **Reload settings**.

You can also open the settings window directly while debugging:

```
ActionRing.exe --settings
```

## JSON reference

On first run the app writes `actionring.json` next to the exe (kept beside the
binary so the whole thing stays portable).

```jsonc
{
  "HotKey": "Ctrl+Alt+Space",
  "HoldToActivate": true,  // hold the hotkey, move onto an item, release to run it
  "HoldThresholdMs": 180,  // how long a press must last to count as a hold, not a tap
  "ButtonRadius": 23,      // size of each round button, in DIPs
  "OrbitRadius": 60,       // centre-to-button distance; grown if buttons won't fit
  "HubRadius": 16,         // the centre dismiss button
  "ShowLabels": true,      // name the hovered action on a pill below the ring
  "Tint": "#26262E",
  "TintOpacity": 0.9,      // lower = more see-through
  "Accent": "#5C7CFA",
  "FollowCursor": true,    // open at the mouse rather than the screen centre
  "HardwareAcceleration": false,  // see "Memory" below before turning this on
  "Actions": [
    { "Label": "Terminal", "Glyph": "", "Kind": "Launch", "Target": "wt.exe" },
    { "Label": "Copy",     "Glyph": "", "Kind": "Keys",   "Target": "Ctrl+C" },
    { "Label": "Docs",     "Glyph": "", "Kind": "Url",    "Target": "https://example.com" }
  ]
}
```

`Kind` is one of:

| Kind | Target |
| --- | --- |
| `Launch` | An exe, folder, or document path. `Arguments` is passed along. Environment variables are expanded. |
| `Url` | Opened in the default browser. |
| `Keys` | A combo like `Ctrl+Shift+S`, injected into whichever window had focus before the ring opened. |

`Glyph` is a single character — a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font)
codepoint or an emoji. `Accent` on an individual action overrides the global one.

An action can display a file or application icon instead. Set `IconKind` to
`AppIcon` and `IconPath` to an `.ico`, `.png`, other image, or `.exe` path. Relative
paths are resolved beside `ActionRing.exe`; environment variables are expanded.

Button count is however long the array is. Add a tenth action and `OrbitRadius`
widens on its own rather than the buttons shrinking to fit.

## Memory

A tray app has no business holding 200 MB, and the first version held 195. Measured
on a 14-core machine:

| | working set | private | threads |
| --- | --- | --- | --- |
| Hello-world WPF app (the baseline, not our code) | 195 MB | 145 MB | 66 |
| Action Ring, as first written | 195 MB | 145 MB | 66 |
| GPU rendering off | 96 MB | 50 MB | 15 |
| ...plus nothing WPF touched until first use | 61 MB briefly, then 5 MB | 14 MB | 14 |
| While the ring is on screen | ~103 MB | 55 MB | 15 |
| A few seconds after it closes | 4 MB | | 14 |

Three changes, in descending order of how much they mattered:

**`RenderMode.SoftwareOnly`.** Standing up WPF's D3D device costs ~100 MB and 51
driver threads. For eight circles on screen two seconds at a time that is a
preposterous trade, and software rendering draws this UI without breaking stride -
every screenshot in this repo is software-rendered. Set `HardwareAcceleration: true`
if you scale the ring up far enough to want the GPU back.

**Nothing WPF-shaped exists until the ring is first summoned.** Two things used to
force WPF's rendering stack up at launch: constructing the window eagerly, and - less
obviously - the hotkey sink. `HwndSource` drags the whole MediaContext up with it, so
`HotKeyManager` registers a hand-rolled Win32 message-only window instead. Together
these mean a fresh process peaks at 61 MB for under a second (runtime and GDI+ init)
and then sits at ~5 MB, rather than holding ~100 MB from launch until the first trim
swept it.

**An idle working-set trim.** `MemoryTrim` collects and calls `EmptyWorkingSet` 1.2 s
after startup and 4 s after the ring closes. Be precise about what this buys: it
evicts pages from the resident set, so the physical RAM genuinely returns and Task
Manager genuinely reads single digits, but the ~50 MB of *committed* memory is still
committed. Windows faults back what the next summon needs in a few milliseconds.

Verified end to end: 5 MB idle before first use, 103 MB with the ring open, 4 MB a
few seconds after it closes.

What did *not* help, measured and then reverted: `InvariantGlobalization`,
`GC.ConserveMemory`, `UseSystemResourceKeys`, non-concurrent GC, and thread-pool
limits. Together they moved nothing, and each carries a real cost - worse exception
messages, changed string comparison, more GC CPU. They are absent from the csproj
deliberately; putting them back needs a measurement, not a hunch.

The 16 drop shadows are gradient discs rather than `DropShadowEffect` for a related
reason: under software rendering a real blur is CPU work on every frame of the open
animation, and at this size a static radial gradient is indistinguishable.

## Debugging

`ActionRing.exe --show` opens the ring immediately, without waiting for the hotkey.

One caveat if you script against it: a window shown this way cannot take the
foreground (Windows only grants that off real user input), so it will not respond to
`Esc` and will not auto-hide. It also means `CopyFromScreen` captures nothing for a
layered window - use `PrintWindow` with `PW_RENDERFULLCONTENT` if you need a
screenshot.

## Layout

```
ActionRing/
  App.xaml(.cs)             tray icon, hotkey registration, single-instance guard
  Views/RingWindow.xaml.cs  the ring: buttons, hub, label pill, hover, invoke
  Services/
    RingLayout.cs           button placement + wedge (direction-based) hit testing
    MemoryTrim.cs           hands idle pages back to the OS
    AcrylicBrushes.cs       the faux-acrylic tint / sheen / grain layers
    ActionRunner.cs         launches processes, injects keystrokes
    HotKeyParser.cs         "Ctrl+Alt+Space" -> (ModifierKeys, Key)
  Interop/
    NativeMethods.cs        P/Invoke surface
    HotKeyManager.cs        RegisterHotKey against a raw Win32 message-only window
```

## Three things worth knowing before you extend this

**The acrylic is simulated, deliberately.** `AllowsTransparency="True"` makes a
*layered* window, and layered windows can't host DWM blur-behind — so real system
acrylic and per-pixel-shaped content are mutually exclusive in WPF. `AcrylicBrushes`
approximates it the way the real effect is composed: tint, then a luminosity sheen,
then fine noise, clipped per button. At button scale it reads as the genuine article
and costs nothing per frame.

**The ring is positioned with `SetWindowPos` in physical pixels, not `Window.Left`
/`Top`.** Those two look like the obvious answer and are a trap: in a PerMonitorV2
process they are neither physical pixels nor DIPs of the monitor you're aiming at, so
on a multi-monitor or scaled desktop the ring lands somewhere else entirely. The
placement code resolves the target monitor, asks it for its DPI, converts the DIP
window size to real pixels, and clamps to the work area.

**Labels are placed, not laid out.** A single pill is measured and moved on hover, to
whichever side of the button faces away from the middle: beside it for buttons out on
the flanks, above or below for buttons on the vertical axis, where "beside" would
point the label back into the ring. Close is a special case - under the ring, tucked
below the first orbit rather than out past the group orbit. The window reserves room
for the widest label on every side at build time, since its size is fixed once shown.

`MeasurePill` calls `InvalidateMeasure` before `Measure`, and that call is
load-bearing. `Measure` returns immediately when an element's measure is already
valid for the same constraint, so without it a short label silently inherits the
previous long one's width and sits pushed away from its button - visible as a label
that drifts left after you have hovered a group.

**Labels are placed, not laid out.** A single pill is measured and moved on hover, to
whichever side of the button faces away from the middle: beside it for buttons out on
the flanks, above or below for buttons on the vertical axis, where "beside" would
point the label back into the ring. Close is a special case - under the ring, tucked
below the first orbit rather than out past the group orbit. The window reserves room
for the widest label on every side at build time, since its size is fixed once shown.

`MeasurePill` calls `InvalidateMeasure` before `Measure`, and that call is
load-bearing. `Measure` returns immediately when an element's measure is already
valid for the same constraint, so without it a short label silently inherits the
previous long one's width and sits pushed away from its button - visible as a label
that drifts left after you have hovered a group.

**The hotkey is registered with `MOD_NOREPEAT`, and that flag is the whole
anti-blink story.** Windows re-sends `WM_HOTKEY` for keyboard auto-repeat, so a
combo held a fraction too long fired the toggle several times: open, close, open.
It was intermittent precisely because it depended on how long the key was held - a
quick tap looked perfect. `MOD_NOREPEAT` makes one physical press mean exactly one
notification, which is what lets `Toggle` stay a plain instant show/hide with no
debounce, settle window, or other timer standing between the press and the ring.

**The show sequence is order-sensitive too.** `PrepareForShow` zeroes the opacity
*before* `Show()`, and `PositionWindow` runs *after* it. An animation holds its final
value at higher precedence than a local one, so assigning `Opacity = 0` while the
previous summon's fade is still in effect does nothing, and the first composed frame
lands at full opacity; clearing the animations with `BeginAnimation(prop, null)` fixes
that. Separately, WPF re-applies its own layout on `Show()` and can override a
`SetWindowPos` issued beforehand, snapping the ring across the screen - so it is
shown first and moved after, while still transparent.

**Hit testing is by direction, not against the visuals.** `RingLayout.SectorIndex`
turns a point into an angle and an angle into a button: each one owns the wedge it
sits in, from the hub out to wherever the window ends, which is what makes a flick in
a direction enough to choose. An open group's children own the arc they fan across,
but only past the halfway line between the two orbits — and only that arc, so
overshooting the fan leaves the group open rather than collapsing it mid-reach. The
centre is the one exception and stays a plain circle.

Two earlier approaches are worth knowing were tried. WPF hit testing against the
visuals lets the glyph text and hairline strokes steal hits inside their own button.
Nearest-centre-with-slop fixes that but keeps the targets small, so the ring still had
to be aimed at rather than thrown at.

**The wedges need `CreateInputPad` to exist at all, and the reason is not obvious.**
`AllowsTransparency` makes this a layered window, and a layered window passes mouse
input *through* pixels whose alpha is zero. Nearly all of this window is such a pixel,
so `MouseMove` only ever arrived while the pointer was over a drawn circle — the
wedges were computed correctly and never asked about. A rectangle over the whole
window filled with one unit of alpha is enough to make Windows deliver the messages
and is invisible on any display. The symptom this produced is worth recognising if it
comes back: the hold gesture worked everywhere while plain hovering only worked on the
circles, because the gesture polls `GetCursorPos` instead of waiting to be told.

**The hold gesture polls, and has to.** `RegisterHotKey` reports the press and never
the release, so `OnHoldTick` reads `GetAsyncKeyState` every 16 ms and ends the gesture
when any part of the combo comes up. It arms only after `HoldThresholdMs`, which is
what keeps a tap from being read as a hold and stops the pointer's resting position
from counting as a choice the instant the ring appears. The same tick reads the cursor
via `GetCursorPos` rather than mouse events: WPF only delivers moves over the window,
and a quick flick outruns it — polling keeps aiming honest when the pointer has shot
well past the ring.

Releasing does not run the action immediately, and that delay is load-bearing. A
combo comes up one key at a time, and a `Keys` action injected while the other half is
still physically down arrives at the target window with those modifiers folded in —
`Ctrl+C` chosen out of `Ctrl+Alt+Space` lands as `Ctrl+Alt+C`. So the release locks
the choice, stops tracking the cursor, and waits for the keyboard to clear (capped at
700 ms, in case something is stuck) before running anything.

## Ideas not built yet

- Nested rings — a button that opens a sub-ring
- Per-app rings, keyed off the foreground window's process name
- Start-with-Windows toggle (a shortcut in `shell:startup`)
