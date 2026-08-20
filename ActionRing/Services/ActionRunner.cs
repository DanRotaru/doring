using System.Diagnostics;
using System.Windows.Input;
using ActionRing.Interop;
using ActionRing.Models;

namespace ActionRing.Services;

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
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Action '{action.Label}' failed: {ex.Message}");
        }
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
