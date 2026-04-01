using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using DrawingColor = System.Drawing.Color;
using DrawingBrush = System.Drawing.SolidBrush;
using DrawingPen = System.Drawing.Pen;
using DrawingLinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;

namespace AutomationLauncher.App;

public static class AppIconFactory
{
    private static readonly object SyncRoot = new();
    private static Icon? _trayIcon;
    private static Icon? _startupTrayIcon;
    private static Icon? _stopTrayIcon;
    private static Icon? _archiveTrayIcon;
    private static ImageSource? _windowIcon;

    public static Icon GetTrayIcon()
    {
        lock (SyncRoot)
        {
            _trayIcon ??= BuildIcon();
            return (Icon)_trayIcon.Clone();
        }
    }

    public static Icon GetStartupTrayIcon()
    {
        lock (SyncRoot)
        {
            _startupTrayIcon ??= BuildIcon(
                DrawingColor.FromArgb(22, 124, 70),
                DrawingColor.FromArgb(48, 181, 102),
                DrawingColor.FromArgb(245, 255, 245));
            return (Icon)_startupTrayIcon.Clone();
        }
    }

    public static Icon GetStopTrayIcon()
    {
        lock (SyncRoot)
        {
            _stopTrayIcon ??= BuildIcon(
                DrawingColor.FromArgb(173, 28, 28),
                DrawingColor.FromArgb(230, 74, 25),
                DrawingColor.FromArgb(255, 252, 242, 242));
            return (Icon)_stopTrayIcon.Clone();
        }
    }

    public static Icon GetArchiveTrayIcon()
    {
        lock (SyncRoot)
        {
            _archiveTrayIcon ??= BuildIcon(
                DrawingColor.FromArgb(20, 87, 169),
                DrawingColor.FromArgb(64, 156, 255),
                DrawingColor.FromArgb(242, 248, 255));
            return (Icon)_archiveTrayIcon.Clone();
        }
    }

    public static ImageSource GetWindowIcon()
    {
        lock (SyncRoot)
        {
            if (_windowIcon is not null)
            {
                return _windowIcon;
            }

            using var icon = GetTrayIcon();
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            source.Freeze();
            _windowIcon = source;
            return _windowIcon;
        }
    }

    private static Icon BuildIcon()
    {
        return BuildIcon(
            DrawingColor.FromArgb(18, 52, 86),
            DrawingColor.FromArgb(23, 110, 74),
            DrawingColor.FromArgb(255, 245, 248, 250));
    }

    private static Icon BuildIcon(DrawingColor primaryColor, DrawingColor secondaryColor, DrawingColor textColor)
    {
        using var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(DrawingColor.Transparent);

            using var backgroundBrush = new DrawingLinearGradientBrush(
                new Rectangle(0, 0, 64, 64),
                primaryColor,
                secondaryColor,
                45f);
            using var accentBrush = new DrawingBrush(textColor);
            using var borderPen = new DrawingPen(DrawingColor.FromArgb(180, 255, 255, 255), 2f);
            using var font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var letterFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            var path = CreateRoundedRectangle(new Rectangle(4, 4, 56, 56), 14);
            graphics.FillPath(backgroundBrush, path);
            graphics.DrawPath(borderPen, path);
            graphics.DrawString("AL", font, accentBrush, new RectangleF(4, 8, 56, 48), letterFormat);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(handle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}