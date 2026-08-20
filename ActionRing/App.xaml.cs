using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using ActionRing.Interop;
using ActionRing.Models;
using ActionRing.Services;
using ActionRing.Views;

namespace ActionRing;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private HotKeyManager? _hotKeys;
    private NotifyIcon? _tray;
    private RingWindow? _ring;
    private RingConfig _config = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A portable exe is easy to double-click twice; a second instance would
        // just fail to register the hotkey and sit there confusingly.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\ActionRing.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        _config = RingConfig.Load();
        ApplyRenderMode();

        _tray = CreateTrayIcon();
        RegisterHotKey();

        // Startup touches pages it never needs again. Trim promptly - the point
        // is that nobody ever sees a big number, not that it settles eventually.
        ScheduleTrim(TimeSpan.FromMilliseconds(1200));

        // "ActionRing.exe --show" opens the ring straight away. Handy when you
        // want to look at the UI without the global hotkey in the way - eg. from
        // a script, or when another app has claimed the combo.
        if (e.Args.Any(a => string.Equals(a, "--show", StringComparison.OrdinalIgnoreCase)))
        {
            Ring().ShowRing();
        }
    }

    /// <summary>
    /// The ring window is built on first use, not at startup. Constructing it
    /// eagerly meant the process sat on ~100 MB from launch until the first trim
    /// swept it - a number the user sees in Task Manager and reasonably objects
    /// to. Waiting until something actually asks for the ring means that memory
    /// is never allocated in the first place.
    /// </summary>
    private RingWindow Ring()
    {
        if (_ring is not null) return _ring;

        _ring = new RingWindow(_config);
        _ring.Apply(_config);
        return _ring;
    }

    /// <summary>
    /// Must run before the first window exists - ProcessRenderMode is read when
    /// WPF spins up its render thread and ignored afterwards.
    /// </summary>
    private void ApplyRenderMode() =>
        System.Windows.Media.RenderOptions.ProcessRenderMode = _config.HardwareAcceleration
            ? System.Windows.Interop.RenderMode.Default
            : System.Windows.Interop.RenderMode.SoftwareOnly;

    private void ScheduleTrim(TimeSpan delay)
    {
        // Background priority: idle housekeeping must never compete with the UI.
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = delay,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // Never trim out from under a visible ring.
            if (_ring is null || !_ring.IsVisible) MemoryTrim.Now();
        };
        timer.Start();
    }

    // ---- tray -----------------------------------------------------------

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show ring", null, (_, _) => Ring().ShowRing());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit actions...", null, (_, _) => OpenConfig());
        menu.Items.Add("Reload config", null, (_, _) => ReloadConfig());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        var icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = $"Action Ring  -  {_config.HotKey}",
            ContextMenuStrip = menu,
        };
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) Ring().ShowRing();
        };
        return icon;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var stream = GetResourceStream(new Uri("Assets/ring.ico", UriKind.Relative))?.Stream;
        return stream is not null
            ? new System.Drawing.Icon(stream)
            : System.Drawing.SystemIcons.Application;
    }

    // ---- config ---------------------------------------------------------

    private void OpenConfig()
    {
        // Make sure the file exists before handing it to the shell.
        if (!File.Exists(RingConfig.ConfigPath)) _config.Save();

        Process.Start(new ProcessStartInfo(RingConfig.ConfigPath) { UseShellExecute = true });
    }

    private void ReloadConfig()
    {
        _config = RingConfig.Load();
        _ring?.Apply(_config);
        if (_tray is not null) _tray.Text = $"Action Ring  -  {_config.HotKey}";
        RegisterHotKey();
    }

    // ---- hotkey ---------------------------------------------------------

    private void RegisterHotKey()
    {
        _hotKeys ??= new HotKeyManager();
        _hotKeys.Pressed -= OnHotKey;
        _hotKeys.Pressed += OnHotKey;

        if (!HotKeyParser.TryParse(_config.HotKey, out var modifiers, out var key))
        {
            Warn($"'{_config.HotKey}' is not a hotkey I understand. Falling back to Ctrl+Alt+Space.");
            modifiers = System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt;
            key = System.Windows.Input.Key.Space;
        }

        if (!_hotKeys.Register(modifiers, key))
        {
            Warn($"Another app already owns {_config.HotKey}. Pick a different one in " +
                 $"{Path.GetFileName(RingConfig.ConfigPath)}, then choose Reload config.");
        }
    }

    private void OnHotKey() => Ring().Toggle();

    private static void Warn(string message) =>
        System.Windows.MessageBox.Show(message, "Action Ring",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    // ---- teardown -------------------------------------------------------

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKeys?.Dispose();

        if (_tray is not null)
        {
            // Without this the dead icon lingers in the tray until hover.
            _tray.Visible = false;
            _tray.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
