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

    /// <summary>
    /// Registers e.g. Ctrl+Alt+Space. Returns false only if the combo could be
    /// claimed neither as a system hotkey nor through the keyboard hook.
    /// </summary>
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
        if (_registered) return true;

        // The shell owns most Win+<key> combos (Win+Z is Snap Layouts, and so
        // on) and will not hand them over, so RegisterHotKey fails no matter
        // what. Rather than tell the user to pick something else, take the
        // combo anyway: a low-level hook sees the keystroke before the raw
        // input thread dispatches the shell's hotkey, so swallowing it there
        // overrides the owner.
        return HookInstead(modifiers, key);
    }

    public void Unregister()
    {
        RemoveHook();

        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_hwnd, HotKeyId);
        _registered = false;
    }

    // ---- keyboard-hook fallback ----------------------------------------

    // Same reason the window proc is held in a field: Windows keeps only a raw
    // pointer to the callback, so the GC must not move or collect it.
    private NativeMethods.HookProc? _hookProc;
    private IntPtr _hook;
    private uint _hookVk;
    private ModifierKeys _hookModifiers;
    private bool _hookKeyDown;

    private bool HookInstead(ModifierKeys modifiers, Key key)
    {
        _hookVk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _hookModifiers = modifiers;
        _hookKeyDown = false;
        _hookProc = HookCallback;

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _hookProc,
            NativeMethods.GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero) _hookProc = null;
        return _hook != IntPtr.Zero;
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _hookProc = null;
        _hookKeyDown = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION) return Next(nCode, wParam, lParam);

        var info = System.Runtime.InteropServices.Marshal
            .PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

        // Ignore anything we or another automation tool synthesised, including
        // the modifier tap below - otherwise the hook can chase its own tail.
        if ((info.flags & NativeMethods.LLKHF_INJECTED) != 0 || info.vkCode != _hookVk)
        {
            return Next(nCode, wParam, lParam);
        }

        var msg = wParam.ToInt32();

        if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
        {
            if (!_hookKeyDown) return Next(nCode, wParam, lParam);

            // Swallow the matching release too: the owning app never saw the
            // press, so letting the release through only confuses it.
            _hookKeyDown = false;
            return new IntPtr(1);
        }

        if (msg is not (NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN))
        {
            return Next(nCode, wParam, lParam);
        }

        if (!ModifiersMatch()) return Next(nCode, wParam, lParam);

        // Auto-repeat, same as MOD_NOREPEAT above: one physical press is one
        // notification, or the ring flickers open and closed while held.
        if (_hookKeyDown) return new IntPtr(1);
        _hookKeyDown = true;

        if (_hookModifiers.HasFlag(ModifierKeys.Windows)) SwallowStartMenu();

        Pressed?.Invoke();
        return new IntPtr(1);
    }

    private IntPtr Next(int nCode, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

    /// <summary>Exact match, so Ctrl+Win+Z doesn't fire a Win+Z binding.</summary>
    private bool ModifiersMatch()
    {
        var down = ModifierKeys.None;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL)) down |= ModifierKeys.Control;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_MENU)) down |= ModifierKeys.Alt;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)) down |= ModifierKeys.Shift;
        if (NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) ||
            NativeMethods.IsKeyDown(NativeMethods.VK_RWIN))
        {
            down |= ModifierKeys.Windows;
        }

        return down == _hookModifiers;
    }

    /// <summary>
    /// Because we ate the key, the shell sees Win pressed and released with
    /// nothing in between and pops the Start menu. Injecting a stray Ctrl tap
    /// makes the Win press count as part of a combo, which cancels that.
    /// </summary>
    private static void SwallowStartMenu()
    {
        var inputs = new NativeMethods.INPUT[2];
        for (var i = 0; i < 2; i++)
        {
            inputs[i].type = NativeMethods.INPUT_KEYBOARD;
            inputs[i].U.ki.wVk = (ushort)NativeMethods.VK_CONTROL;
        }

        inputs[1].U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
        NativeMethods.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
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
