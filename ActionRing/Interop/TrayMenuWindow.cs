using System.Windows.Forms;

namespace ActionRing.Interop;

/// <summary>
/// Supplies the HWND required to own and correctly dismiss a native tray menu.
/// NativeWindow avoids creating a visible form or waking WPF's window stack.
/// </summary>
internal sealed class TrayMenuWindow : NativeWindow, IDisposable
{
    public TrayMenuWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "ActionRing.TrayMenuOwner",
            Style = NativeMethods.WS_POPUP,
            ExStyle = NativeMethods.WS_EX_TOOLWINDOW,
        });
    }

    public void Dispose()
    {
        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}
