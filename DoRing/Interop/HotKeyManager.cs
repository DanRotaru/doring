using System.Windows.Input;

namespace DoRing.Interop;

/// <summary>
/// Registers a system-wide hotkey against a message-only window.
///
/// The window is built from raw Win32 rather than WPF's HwndSource on purpose:
/// an HwndSource initialises WPF's rendering stack, and this app exists to sit
/// in the tray doing nothing. Keeping WPF asleep until the ring is first
/// summoned is the difference between a ~100 MB idle process and a small one.
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private const int HotKeyId = 0xA11E;
    private const string ClassName = "DoRing.HotKeySink";

    // Held in a field so the GC can't collect the delegate out from under
    // Windows, which would take the process with it.
    private readonly NativeMethods.WindowProc _proc;
    private readonly IntPtr _hwnd;
    private bool _registered;

    public event Action? Pressed;

    public HotKeyManager()
    {
        _proc = WndProc;

        var module = NativeMethods.GetModuleHandle(null);

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _proc,
            hInstance = module,
            lpszClassName = ClassName,
        };

        if (NativeMethods.RegisterClassEx(ref wc) == 0)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if (error != NativeMethods.ERROR_CLASS_ALREADY_EXISTS)
            {
                throw new System.ComponentModel.Win32Exception(error);
            }
        }

        _hwnd = NativeMethods.CreateWindowEx(
            0, ClassName, null, 0, 0, 0, 0, 0,
            NativeMethods.HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    /// <summary>Registers e.g. Ctrl+Space. Returns false if taken by another app.</summary>
    public bool Register(ModifierKeys modifiers, Key key)
    {
        Unregister();

        // MOD_NOREPEAT. Without it Windows re-sends WM_HOTKEY for keyboard
        // auto-repeat, so holding the combo a fraction too long fires the
        // toggle several times and the ring visibly flickers open/closed/open.
        // This makes one physical press mean exactly one notification, which is
        // what lets the toggle stay instant in both directions.
        const uint MOD_NOREPEAT = 0x4000;

        var mods = MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= 0x0008;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = NativeMethods.RegisterHotKey(_hwnd, HotKeyId, mods, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_hwnd, HotKeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Unregister();
        if (_hwnd != IntPtr.Zero) NativeMethods.DestroyWindow(_hwnd);
    }
}
