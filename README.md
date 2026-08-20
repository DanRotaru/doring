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

- Hover a button — it pops, tints with its accent, and names itself on a pill beside it
- Left-click to invoke; `1`–`9` invoke a button directly
- The red centre button, `Esc`, right-click, or clicking away all dismiss it

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

## Configuring actions

On first run the app writes `actionring.json` next to the exe (kept beside the
binary so the whole thing stays portable). Edit it, then choose **Reload config**
from the tray menu.

```jsonc
{
  "HotKey": "Ctrl+Alt+Space",
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
    RingLayout.cs           button placement + nearest-centre hit testing
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

**Hit testing is nearest-centre, not visual.** `RingLayout.HitTest` measures distance
to each button centre with a little slop. Relying on WPF hit testing against the
visuals means the glyph text and hairline strokes steal hits inside their own button.

## Ideas not built yet

- Drag-out gesture selection (press hotkey, flick, release) instead of click
- Nested rings — a button that opens a sub-ring
- A settings UI, so `actionring.json` isn't the only editor
- Per-app rings, keyed off the foreground window's process name
- Start-with-Windows toggle (a shortcut in `shell:startup`)
