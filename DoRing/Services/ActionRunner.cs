using System.Diagnostics;
using System.Globalization;
using System.IO;
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
                    Start(action.Target, action.Arguments, restoreTo);
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

                case ActionKind.ToggleWindow:
                    ToggleWindow(action.Target, action.Arguments, restoreTo);
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
            case "WindowCenter": MoveForegroundWindow(WindowSpot.Center); break;
            case "WindowLeft": MoveForegroundWindow(WindowSpot.Left); break;
            case "WindowRight": MoveForegroundWindow(WindowSpot.Right); break;
            case "WindowTopLeft": MoveForegroundWindow(WindowSpot.TopLeft); break;
            case "WindowBottomRight": MoveForegroundWindow(WindowSpot.BottomRight); break;
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

    private enum WindowSpot { Left, Center, Right, TopLeft, BottomRight }

    /// <summary>
    /// Moves - never resizes - the foreground window inside its monitor's work
    /// area. Left/Right pin it to that edge and leave the vertical position
    /// alone; Center places it in the middle on both axes; the corners pin both.
    /// </summary>
    private static void MoveForegroundWindow(WindowSpot spot)
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
        var x = spot switch
        {
            WindowSpot.Left or WindowSpot.TopLeft => info.rcWork.Left,
            WindowSpot.Right or WindowSpot.BottomRight => info.rcWork.Right - width,
            _ => info.rcWork.Left + (info.rcWork.Right - info.rcWork.Left - width) / 2,
        };
        var y = spot switch
        {
            WindowSpot.TopLeft => info.rcWork.Top,
            WindowSpot.BottomRight => info.rcWork.Bottom - height,
            WindowSpot.Center => info.rcWork.Top + (info.rcWork.Bottom - info.rcWork.Top - height) / 2,
            _ => bounds.Top,
        };
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

    /// <summary>
    /// Show a program if it is open, hide it if it is already in front, start it
    /// if it isn't running - the whole point being that one button both summons
    /// and dismisses the same window.
    /// </summary>
    /// <param name="target">
    /// A process name ("WindowsTerminal.exe"), or a full path to the executable;
    /// the file name is what the running processes are matched on either way.
    /// </param>
    private static void ToggleWindow(string target, string arguments, IntPtr restoreTo)
    {
        if (string.IsNullOrWhiteSpace(target)) return;

        var expanded = Environment.ExpandEnvironmentVariables(
            ExplorerContext.Expand(target, restoreTo)).Trim().Trim('"');
        var window = FindProcessWindow(ProcessNameOf(expanded));

        if (window == IntPtr.Zero)
        {
            Start(target, arguments, restoreTo);
            return;
        }

        // restoreTo is the window that had focus before the ring opened, which
        // is the only reliable read of "was this app in front?" - by the time
        // the action runs, the foreground window is the ring or already gone.
        if (window == restoreTo || window == RootOf(restoreTo))
        {
            NativeMethods.ShowWindow(window, NativeMethods.SW_MINIMIZE);
            return;
        }

        if (NativeMethods.IsIconic(window)) NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
        Activate(window);
    }

    /// <summary>The file name without its extension, which is what Process matches on.</summary>
    private static string ProcessNameOf(string target)
    {
        var name = Path.GetFileName(target);
        if (string.IsNullOrEmpty(name)) name = target;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    /// <summary>
    /// The best window to toggle for a process name: its main window if it
    /// reports one, otherwise the first visible top-level window it owns - some
    /// apps (Explorer above all) leave MainWindowHandle empty.
    /// </summary>
    private static IntPtr FindProcessWindow(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return IntPtr.Zero;

        Process[] processes;
        try { processes = Process.GetProcessesByName(processName); }
        catch (Exception) { return IntPtr.Zero; }

        var ids = new HashSet<uint>();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero &&
                        NativeMethods.IsWindowVisible(process.MainWindowHandle))
                        return process.MainWindowHandle;
                    ids.Add((uint)process.Id);
                }
                catch (Exception)
                {
                    // A process that exited between the enumeration and the read.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        if (ids.Count == 0) return IntPtr.Zero;

        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle)) return true;
            if (NativeMethods.GetWindow(handle, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;
            if ((NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE) &
                 NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var owner);
            if (!ids.Contains(owner)) return true;
            found = handle;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The owner-less window a handle belongs to, so a dialog counts as its app.</summary>
    private static IntPtr RootOf(IntPtr window)
    {
        while (window != IntPtr.Zero)
        {
            var owner = NativeMethods.GetWindow(window, NativeMethods.GW_OWNER);
            if (owner == IntPtr.Zero) return window;
            window = owner;
        }
        return window;
    }

    /// <summary>
    /// Raises another process's window. SetForegroundWindow alone is refused
    /// unless we own the foreground, so we attach to the current foreground
    /// thread first and fall back to raising the window without focus.
    /// </summary>
    private static void Activate(IntPtr window)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var self = NativeMethods.GetCurrentThreadId();
        var owner = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);

        var attached = owner != 0 && owner != self &&
                       NativeMethods.AttachThreadInput(self, owner, true);
        try
        {
            if (!NativeMethods.SetForegroundWindow(window))
                NativeMethods.BringWindowToTop(window);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(self, owner, false);
        }
    }

    private static void Start(string target, string arguments, IntPtr explorerHint = default)
    {
        if (string.IsNullOrWhiteSpace(target)) return;

        // %path% and %sel% are ours, so they are substituted before the
        // environment gets a look - otherwise %path% would become PATH.
        target = ExplorerContext.Expand(target, explorerHint);
        arguments = ExplorerContext.Expand(arguments, explorerHint);

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
