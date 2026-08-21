using System.Drawing;
using System.Windows.Forms;

namespace ActionRing.Views;

/// <summary>
/// Keeps the WinForms tray menu visually consistent with Action Ring's dark UI.
/// ContextMenuStrip does not inherit the WPF theme used by the rest of the app.
/// </summary>
internal sealed class DarkTrayMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkTrayMenuRenderer() : base(new DarkTrayMenuColors())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(0xF5, 0xF5, 0xF5)
            : Color.FromArgb(0x78, 0x78, 0x78);
        base.OnRenderItemText(e);
    }

    private sealed class DarkTrayMenuColors : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(0x29, 0x29, 0x29);
        private static readonly Color Hover = Color.FromArgb(0x3D, 0x3D, 0x3D);
        private static readonly Color Border = Color.FromArgb(0x4A, 0x4A, 0x4A);
        private static readonly Color Separator = Color.FromArgb(0x46, 0x46, 0x46);

        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }
}
