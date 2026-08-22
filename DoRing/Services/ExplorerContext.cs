using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DoRing.Interop;

namespace DoRing.Services;

/// <summary>
/// Reads the folder and selection of the Explorer window the user was last in,
/// so actions can pass them along as %path% and %sel%.
/// </summary>
public static class ExplorerContext
{
    private const string PathToken = "%path%";
    private const string SelectionToken = "%sel%";

    /// <summary>True when the text asks for anything Explorer has to supply.</summary>
    public static bool UsesTokens(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains(PathToken, StringComparison.OrdinalIgnoreCase) ||
         text.Contains(SelectionToken, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Substitutes %path% and %sel%. Both collapse to an empty string when no
    /// Explorer window is open, or when nothing is selected in it.
    /// </summary>
    /// <param name="preferred">
    /// The window that had focus before the ring opened. Used when it is itself
    /// an Explorer window; otherwise the topmost Explorer window wins.
    /// </param>
    public static string Expand(string text, IntPtr preferred)
    {
        if (!UsesTokens(text)) return text;

        var (folder, selection) = Capture(preferred);
        return Replace(Replace(text, PathToken, folder), SelectionToken, selection);
    }

    private static string Replace(string text, string token, string value) =>
        text.Replace(token, value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Folder and first selected item of the active Explorer tab.</summary>
    public static (string Folder, string Selection) Capture(IntPtr preferred)
    {
        try
        {
            var window = ResolveExplorerWindow(preferred);
            if (window == IntPtr.Zero) return ("", "");

            var tab = ActiveTab(window);
            var view = FindShellView(window, tab);
            if (view is null) return ("", "");

            try
            {
                dynamic document = view.Document;
                string folder = document.Folder.Self.Path ?? "";

                var selection = "";
                dynamic items = document.SelectedItems();
                if (items.Count > 0) selection = items.Item(0).Path ?? "";

                return (folder, selection);
            }
            finally
            {
                Marshal.FinalReleaseComObject(view);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Explorer context unavailable: {ex.Message}");
            return ("", "");
        }
    }

    /// <summary>The preferred window if it is Explorer, else the topmost one.</summary>
    private static IntPtr ResolveExplorerWindow(IntPtr preferred)
    {
        if (IsExplorer(preferred)) return preferred;

        // EnumWindows walks top-down in Z-order, so the first hit is the
        // Explorer window the user touched most recently.
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || !IsExplorer(hwnd)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsExplorer(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var name = new StringBuilder(64);
        if (NativeMethods.GetClassName(hwnd, name, name.Capacity) == 0) return false;
        var value = name.ToString();
        return value is "CabinetWClass" or "ExploreWClass";
    }

    /// <summary>
    /// On Windows 11 every tab of an Explorer window is its own shell view, and
    /// they all report the same top-level HWND. The active tab is the first
    /// ShellTabWindowClass child, which lets us tell them apart.
    /// </summary>
    private static IntPtr ActiveTab(IntPtr window) =>
        NativeMethods.FindWindowEx(window, IntPtr.Zero, "ShellTabWindowClass", null);

    private static dynamic? FindShellView(IntPtr window, IntPtr activeTab)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) return null;

        dynamic? fallback = null;
        try
        {
            dynamic windows = shell.Windows();
            for (var i = 0; i < windows.Count; i++)
            {
                dynamic? view = windows.Item(i);
                if (view is null) continue;

                var keep = false;
                try
                {
                    if ((IntPtr)(long)view.HWND != window) continue;

                    if (activeTab == IntPtr.Zero || TabOf(view) == activeTab)
                    {
                        keep = true;
                        if (fallback is not null) Marshal.FinalReleaseComObject(fallback);
                        return view;
                    }

                    // Wrong tab, but a stale answer beats no answer if the
                    // active one never turns up.
                    if (fallback is null) { fallback = view; keep = true; }
                }
                catch (COMException)
                {
                    // A window closing mid-enumeration; skip it.
                }
                finally
                {
                    if (!keep) Marshal.FinalReleaseComObject(view);
                }
            }

            return fallback;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>The tab window hosting a shell view, via its top-level browser.</summary>
    private static IntPtr TabOf(object view)
    {
        try
        {
            var service = SID_STopLevelBrowser;
            var iid = typeof(IShellBrowser).GUID;
            ((IServiceProvider)view).QueryService(ref service, ref iid, out var browser);
            if (browser is not IShellBrowser shellBrowser) return IntPtr.Zero;

            try
            {
                return shellBrowser.GetWindow(out var hwnd) == 0 ? hwnd : IntPtr.Zero;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellBrowser);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return IntPtr.Zero;
        }
    }

    private static Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    /// <summary>Only the inherited IOleWindow.GetWindow is declared - it is all we call.</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig]
        int GetWindow(out IntPtr phwnd);
    }
}
