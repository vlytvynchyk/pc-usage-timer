using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PcUsageTimer;

public static class IconGenerator
{
    private static Icon? _idleIcon;
    private static Icon? _activeIcon;

    public static Icon IdleIcon => _idleIcon ??= CreateTrayIcon(active: false);
    public static Icon ActiveIcon => _activeIcon ??= CreateTrayIcon(active: true);

    private static Icon CreateTrayIcon(bool active)
    {
        const int canvasSize = 64;
        const int dotSize = 18;
        const int dotMargin = 1;

        using var canvas = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // Two triangles — simple hourglass, 15% smaller
        float inset = canvasSize * 0.075f;
        float s = (canvasSize - inset * 2) / 64f;
        var white = Color.FromArgb(230, 255, 255, 255);
        using var brush = new SolidBrush(white);

        // Top triangle (pointing down)
        g.FillPolygon(brush, [
            new PointF(inset + 10f * s, inset + 4f * s),
            new PointF(inset + 54f * s, inset + 4f * s),
            new PointF(inset + 32f * s, inset + 30f * s),
        ]);
        // Bottom triangle (pointing up)
        g.FillPolygon(brush, [
            new PointF(inset + 10f * s, inset + 60f * s),
            new PointF(inset + 54f * s, inset + 60f * s),
            new PointF(inset + 32f * s, inset + 34f * s),
        ]);

        // Status dot in bottom-right corner
        int dotX = canvasSize - dotSize - dotMargin;
        int dotY = canvasSize - dotSize - dotMargin;

        // Dark ring behind dot for contrast
        using var ringBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillEllipse(ringBrush, dotX - 2, dotY - 2, dotSize + 4, dotSize + 4);

        // Colored dot: blue when timer active, gray when idle
        var dotColor = active
            ? Color.FromArgb(255, 58, 130, 246)    // #3A82F6 blue
            : Color.FromArgb(255, 140, 140, 140);   // gray
        using var dotBrush = new SolidBrush(dotColor);
        g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

        return BitmapToIcon(canvas);
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
