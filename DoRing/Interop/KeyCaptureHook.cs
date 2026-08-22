using System.Windows.Input;

namespace DoRing.Interop;

/// <summary>
/// Swallows every keystroke while active and reports the first real key press,
/// so the shortcut picker can capture combos the shell has already claimed.
/// Without this, pressing Win+Z in the picker just opens Snap Layouts: hotkeys
/// registered by other apps are dispatched ahead of the focused window, and a
/// low-level hook is the only thing that runs earlier still.
/// </summary>
public sealed class KeyCaptureHook : IDisposable
{
    // Windows keeps a raw pointer to the callback, so it lives in a field.
    private readonly NativeMethods.HookProc _proc;
    private IntPtr _hook;

    /// <summary>Raised on the UI thread with the pressed key and its modifiers.</summary>
    public event Action<Key, ModifierKeys>? Captured;

    public KeyCaptureHook() => _proc = Callback;

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(null), 0);

        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var info = System.Runtime.InteropServices.Marshal
            .PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

        if ((info.flags & NativeMethods.LLKHF_INJECTED) != 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var msg = wParam.ToInt32();
        var down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        var up = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
        if (!down && !up) return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        if (down)
        {
            var key = KeyInterop.KeyFromVirtualKey((int)info.vkCode);
            // WPF never saw the modifier presses - we ate them - so its
            // Keyboard.Modifiers reads empty. Ask the keyboard directly.
            if (!IsModifier(info.vkCode)) Captured?.Invoke(key, CurrentModifiers());
        }

        // Eat presses and releases alike, modifiers included: a Win release the
        // shell sees unpaired pops the Start menu over the settings window.
        return new IntPtr(1);
    }

    private static ModifierKeys CurrentModifiers()
    {
        var mods = ModifierKeys.None;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL)) mods |= ModifierKeys.Control;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_MENU)) mods |= ModifierKeys.Alt;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)) mods |= ModifierKeys.Shift;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) ||
            NativeMethods.IsKeyDown(NativeMethods.VK_RWIN))
        {
            mods |= ModifierKeys.Windows;
        }

        return mods;
    }

    /// <summary>Shift, Ctrl, Alt and Win, in both their generic and sided forms.</summary>
    private static bool IsModifier(uint vk) => vk is
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public void Dispose() => Stop();
}
