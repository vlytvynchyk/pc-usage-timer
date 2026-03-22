using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcUsageTimer;

public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color BackColor = Color.FromArgb(255, 44, 44, 44);
    private static readonly Color HoverColor = Color.FromArgb(255, 65, 65, 65);
    private static readonly Color SeparatorColor = Color.FromArgb(255, 70, 70, 70);
    private static readonly Color TextColor = Color.FromArgb(255, 240, 240, 240);
    private static readonly Color DisabledTextColor = Color.FromArgb(255, 170, 170, 170);

    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using var bgBrush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(bgBrush, new Rectangle(Point.Empty, e.Item.Size));

        if (e.Item.Selected && e.Item.Enabled)
        {
            var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(HoverColor);
            FillRoundedRect(e.Graphics, brush, rect, 4);
        }
    }

    private const int TextLeft = 42;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var color = e.Item.Enabled ? TextColor : DisabledTextColor;
        using var font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        var rect = new Rectangle(TextLeft, 0, e.Item.Width - TextLeft - 4, e.Item.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, font, rect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var bgBrush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(bgBrush, new Rectangle(Point.Empty, e.Item.Size));
        int y = e.Item.Height / 2;
        using var pen = new Pen(SeparatorColor);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image != null)
        {
            int iconSize = e.ImageRectangle.Width;
            int x = (TextLeft - iconSize) / 2;
            int y = (e.Item.Height - iconSize) / 2;
            var dest = new Rectangle(x, y, iconSize, iconSize);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(e.Image, dest);
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = TextColor;
        base.OnRenderArrow(e);
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(255, 60, 60, 60);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => HoverColor;
        public override Color MenuStripGradientBegin => BackColor;
        public override Color MenuStripGradientEnd => BackColor;
        public override Color MenuItemSelectedGradientBegin => HoverColor;
        public override Color MenuItemSelectedGradientEnd => HoverColor;
        public override Color MenuItemPressedGradientBegin => HoverColor;
        public override Color MenuItemPressedGradientEnd => HoverColor;
        public override Color ImageMarginGradientBegin => BackColor;
        public override Color ImageMarginGradientMiddle => BackColor;
        public override Color ImageMarginGradientEnd => BackColor;
        public override Color SeparatorDark => SeparatorColor;
        public override Color SeparatorLight => SeparatorColor;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color ToolStripDropDownBackground => BackColor;
    }
}
