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
    private TrayMenuWindow? _trayMenuWindow;
    private RingWindow? _ring;
    private SettingsWindow? _settings;
    private RingConfig _config = new();

    // The combo the ring is currently bound to. The hold gesture has to know
    // which keys to watch for a release, and WM_HOTKEY doesn't say.
    private System.Windows.Input.ModifierKeys _hotKeyModifiers;
    private System.Windows.Input.Key _hotKeyKey;

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
        NativeMethods.EnableDarkSystemMenus();

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
        else if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            ShowSettings();
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

        _ring = new RingWindow(_config, ShowSettings);
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
        _trayMenuWindow = new TrayMenuWindow();

        var icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = $"Action Ring  -  {_config.HotKey}",
        };
        icon.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) Ring().ShowRing();
            else if (args.Button == MouseButtons.Right) ShowTrayMenu();
        };
        return icon;
    }

    private void ShowTrayMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 1, "Show ring");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 2, "Settings...");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 3, "Edit JSON...");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 4, "Reload settings");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 5, "Exit");

            if (!NativeMethods.GetCursorPos(out var cursor)) return;

            // A native popup must have a real owner window. The NotifyIcon's
            // internal handle is private, so use our own lightweight hidden one.
            var owner = _trayMenuWindow?.Handle ?? IntPtr.Zero;
            if (owner == IntPtr.Zero) return;

            NativeMethods.SetForegroundWindow(owner);
            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                owner,
                IntPtr.Zero);
            NativeMethods.PostMessage(owner, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case 1: Ring().ShowRing(); break;
                case 2: ShowSettings(); break;
                case 3: OpenConfig(); break;
                case 4: ReloadConfig(); break;
                case 5: Shutdown(); break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var stream = GetResourceStream(new Uri("Assets/ring.ico", UriKind.Relative))?.Stream;
        return stream is not null
            ? new System.Drawing.Icon(stream)
            : System.Drawing.SystemIcons.Application;
    }

    // ---- config ---------------------------------------------------------

    private void ShowSettings()
    {
        if (_settings is not null)
        {
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_config, SaveFromSettings);
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            ScheduleTrim(TimeSpan.FromSeconds(2));
        };
        _settings.Show();
        _settings.Activate();
    }

    private string? SaveFromSettings(RingConfig config)
    {
        if (!config.TrySave(out var error))
            return $"Could not save settings: {error ?? "Unknown file error."}";

        _config = config;
        _ring?.Apply(_config);
        if (_tray is not null) _tray.Text = $"Action Ring  -  {_config.HotKey}";
        RegisterHotKey();
        return null;
    }

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

        _hotKeyModifiers = modifiers;
        _hotKeyKey = key;

        if (!_hotKeys.Register(modifiers, key))
        {
            Warn($"Another app already owns {_config.HotKey}. Pick a different one in " +
                 $"{Path.GetFileName(RingConfig.ConfigPath)}, then choose Reload config.");
        }
    }

    private void OnHotKey() => Ring().Toggle(_hotKeyModifiers, _hotKeyKey);

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

        _trayMenuWindow?.Dispose();
        _trayMenuWindow = null;

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
