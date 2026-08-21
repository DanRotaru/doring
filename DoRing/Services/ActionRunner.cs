using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Input;
using DoRing.Interop;
using DoRing.Models;

namespace DoRing.Services;

/// <summary>Executes a chosen ring action.</summary>
public static class ActionRunner
{
    /// <param name="restoreTo">
    /// The window that was in the foreground before the ring opened. Keystroke
    /// actions are meaningless unless we hand focus back to it first.
    /// </param>
    public static void Run(RingAction action, IntPtr restoreTo)
    {
        try
        {
            switch (action.Kind)
            {
                case ActionKind.Launch:
                    Start(action.Target, action.Arguments);
                    break;

                case ActionKind.Group:
                    // Holds children only; hovering it is what does the work.
                    break;

                case ActionKind.Url:
                    Start(action.Target, "");
                    break;

                case ActionKind.Keys:
                    if (restoreTo != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(restoreTo);
                        // Give the target a moment to actually take focus before
                        // we inject; without this the keys land nowhere.
                        Thread.Sleep(60);
                    }
                    SendCombo(action.Target);
                    break;

                case ActionKind.Command:
                    RestoreFocus(restoreTo);
                    RunCommand(action.Target);
                    break;

                case ActionKind.PasteText:
                    RestoreFocus(restoreTo);
                    SendText(action.Target);
                    break;

                case ActionKind.MousePosition:
                    MoveMouse(action.Target);
                    break;

                case ActionKind.DateTime:
                    RestoreFocus(restoreTo);
                    SendText(FormatDateTime(action.Target));
                    break;

                case ActionKind.Clipboard:
                    RestoreFocus(restoreTo);
                    RunClipboard(action.Target);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Action '{action.Label}' failed: {ex.Message}");
        }
    }

    public static void RunScroll(RingAction action, int delta)
    {
        if (delta == 0) return;
        if (action.ScrollBehavior == ScrollBehavior.Volume)
            SystemVolume.Change(delta > 0 ? 2 : -2);
    }

    private static void RestoreFocus(IntPtr restoreTo)
    {
        if (restoreTo == IntPtr.Zero) return;
        NativeMethods.SetForegroundWindow(restoreTo);
        Thread.Sleep(60);
    }

    private static void RunCommand(string command)
    {
        switch (command)
        {
            case "MediaPlayPause": SendVirtualKey(0xB3); break;
            case "MediaPreviousTrack": SendVirtualKey(0xB1); break;
            case "MediaNextTrack": SendVirtualKey(0xB0); break;
            case "MediaStop": SendVirtualKey(0xB2); break;
            case "VolumeMute": SendVirtualKey(0xAD); break;
            case "VolumeUp": SendVirtualKey(0xAF); break;
            case "VolumeDown": SendVirtualKey(0xAE); break;
            case "Volume": break; // Display/scroll-only action.
            case "MouseLeftClick": SendMouseButton(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP); break;
            case "MouseRightClick": SendMouseButton(NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP); break;
            case "MouseMiddleClick": SendMouseButton(NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP); break;
            case "MouseCenter": NativeMethods.SetCursorPos(NativeMethods.GetSystemMetrics(0) / 2, NativeMethods.GetSystemMetrics(1) / 2); break;
            case "WindowCenter": CenterForegroundWindow(); break;
            case "WindowsSettings": Start("ms-settings:", ""); break;
            case "ClearClipboard": Clipboard.Clear(); break;
            default: SendCombo(command); break;
        }
    }

    private static void SendVirtualKey(ushort vk)
    {
        var inputs = new[] { KeyInput(vk, true), KeyInput(vk, false) };
        NativeMethods.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void CenterForegroundWindow()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        if (NativeMethods.IsZoomed(window)) NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
        if (!NativeMethods.GetWindowRect(window, out var bounds)) return;

        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var x = info.rcWork.Left + (info.rcWork.Right - info.rcWork.Left - width) / 2;
        var y = info.rcWork.Top + (info.rcWork.Bottom - info.rcWork.Top - height) / 2;
        NativeMethods.SetWindowPos(window, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private static void SendMouseButton(uint down, uint up)
    {
        var inputs = new[] { MouseInput(down), MouseInput(up) };
        NativeMethods.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT MouseInput(uint flags) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } },
    };

    private static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(UnicodeInput(ch, false));
            inputs.Add(UnicodeInput(ch, true));
        }
        var array = inputs.ToArray();
        NativeMethods.SendInput((uint)array.Length, array,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT UnicodeInput(char ch, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wScan = ch,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
            }
        }
    };

    private static void MoveMouse(string coordinates)
    {
        var parts = coordinates.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
            NativeMethods.SetCursorPos(x, y);
    }

    private static string FormatDateTime(string specification)
    {
        if (specification.Equals("unix", StringComparison.OrdinalIgnoreCase))
            return DateTimeOffset.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (specification.Equals("week", StringComparison.OrdinalIgnoreCase))
            return ISOWeek.GetWeekOfYear(DateTime.Now).ToString(CultureInfo.InvariantCulture);
        var format = string.IsNullOrWhiteSpace(specification) ? "yyyy-MM-dd" : specification;
        try { return DateTime.Now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException) { return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    private static void RunClipboard(string operation)
    {
        if (operation == "copy") { SendCombo("Ctrl+C"); return; }
        if (operation == "paste") { SendCombo("Ctrl+V"); return; }
        if (operation == "cut") { SendCombo("Ctrl+X"); return; }
        if (operation == "clear") { Clipboard.Clear(); return; }
        if (!Clipboard.ContainsText()) return;
        var value = Clipboard.GetText();
        value = operation switch
        {
            "url-encode" => Uri.EscapeDataString(value),
            "url-decode" => Uri.UnescapeDataString(value),
            "html-encode" => WebUtility.HtmlEncode(value),
            "html-decode" => WebUtility.HtmlDecode(value),
            "upper" => value.ToUpper(CultureInfo.CurrentCulture),
            "lower" => value.ToLower(CultureInfo.CurrentCulture),
            "trim" => value.Trim(),
            _ => value,
        };
        Clipboard.SetText(value);
    }

    private static void Start(string target, string arguments)
    {
        if (string.IsNullOrWhiteSpace(target)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(target),
            Arguments = Environment.ExpandEnvironmentVariables(arguments),
            UseShellExecute = true, // lets us pass URLs, folders and documents
        });
    }

    /// <summary>Sends a combo like "Ctrl+Shift+S" via SendInput.</summary>
    private static void SendCombo(string combo)
    {
        if (!HotKeyParser.TryParse(combo, out var modifiers, out var key)) return;

        var vks = new List<ushort>();
        if (modifiers.HasFlag(ModifierKeys.Control)) vks.Add(0x11); // VK_CONTROL
        if (modifiers.HasFlag(ModifierKeys.Shift)) vks.Add(0x10);   // VK_SHIFT
        if (modifiers.HasFlag(ModifierKeys.Alt)) vks.Add(0x12);     // VK_MENU
        if (modifiers.HasFlag(ModifierKeys.Windows)) vks.Add(0x5B); // VK_LWIN
        vks.Add((ushort)KeyInterop.VirtualKeyFromKey(key));

        // Press in order, release in reverse — otherwise modifiers get stuck.
        var inputs = new List<NativeMethods.INPUT>(vks.Count * 2);
        foreach (var vk in vks) inputs.Add(KeyInput(vk, down: true));
        for (var i = vks.Count - 1; i >= 0; i--) inputs.Add(KeyInput(vks[i], down: false));

        var array = inputs.ToArray();
        NativeMethods.SendInput(
            (uint)array.Length, array,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyInput(ushort vk, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = down ? 0 : NativeMethods.KEYEVENTF_KEYUP,
            }
        }
    };
}
