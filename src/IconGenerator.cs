using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PcUsageTimer;

public static class IconGenerator
{
    private static Icon? _icon;

    public static Icon AppIcon => _icon ??= CreateIcon();

    /// <summary>
    /// Generates a procedural hourglass icon on a 64x64 canvas.
    /// Style matches UltimatePerformanceToggle's lightning bolt aesthetic:
    /// white shape on transparent background with clean geometry.
    /// </summary>
    private static Icon CreateIcon()
    {
        const int size = 64;

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        var white = Color.FromArgb(230, 255, 255, 255);
        using var brush = new SolidBrush(white);
        using var pen = new Pen(white, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        // Hourglass shape — two triangles meeting at center
        var hourglass = new PointF[]
        {
            new(14f,  8f),   // top-left
            new(50f,  8f),   // top-right
            new(32f, 32f),   // center pinch
            new(50f, 56f),   // bottom-right
            new(14f, 56f),   // bottom-left
            new(32f, 32f),   // center pinch
        };
        g.FillPolygon(brush, hourglass);

        // Top and bottom bars
        g.DrawLine(pen, 12f, 8f, 52f, 8f);
        g.DrawLine(pen, 12f, 56f, 52f, 56f);

        // Small accent dot (blue) in bottom-right — like status indicator
        var accent = Color.FromArgb(255, 58, 130, 246); // #3A82F6
        using var ringBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillEllipse(ringBrush, 43, 43, 22, 22);
        using var accentBrush = new SolidBrush(accent);
        g.FillEllipse(accentBrush, 45, 45, 18, 18);

        return BitmapToIcon(bitmap);
    }

    private static Icon BitmapToIcon(Bitmap source)
    {
        using var small = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, 16, 16);
        }

        using var medium = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(medium))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, 32, 32);
        }

        using var ms = new MemoryStream();
        WriteIco(ms, small, medium, source);
        ms.Position = 0;
        return new Icon(ms);
    }

    private static void WriteIco(Stream stream, params Bitmap[] images)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Length);

        var pngData = new byte[images.Length][];
        for (int i = 0; i < images.Length; i++)
        {
            using var pngStream = new MemoryStream();
            images[i].Save(pngStream, ImageFormat.Png);
            pngData[i] = pngStream.ToArray();
        }

        int dataOffset = 6 + (images.Length * 16);
        for (int i = 0; i < images.Length; i++)
        {
            byte w = (byte)(images[i].Width >= 256 ? 0 : images[i].Width);
            byte h = (byte)(images[i].Height >= 256 ? 0 : images[i].Height);
            writer.Write(w);
            writer.Write(h);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(pngData[i].Length);
            writer.Write(dataOffset);
            dataOffset += pngData[i].Length;
        }

        for (int i = 0; i < images.Length; i++)
            writer.Write(pngData[i]);
    }
}
