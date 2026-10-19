using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SneznyjBrowser;

static class Native
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWA_CAPTION_COLOR = 35;

    static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    public static void StyleWindow(Window w, bool rounded = true)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        int dark = 1;
        DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int corners = rounded ? 2 : 1;
        DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, 4);
        int border = ColorRef(0x26, 0x32, 0x41);
        DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref border, 4);
        int caption = ColorRef(0x0E, 0x13, 0x19);
        DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref caption, 4);
    }

    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>Сколько окно «вылезает» за экран в развёрнутом виде (рамка + отступ), в единицах WPF.</summary>
    public static Thickness MaximizedOverhang(Window w)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        double pad = GetSystemMetrics(92 /* SM_CXPADDEDBORDER */) / dpi.DpiScaleX;
        var t = SystemParameters.WindowResizeBorderThickness;
        return new Thickness(t.Left + pad, t.Top + pad, t.Right + pad, t.Bottom + pad);
    }

    /// <summary>Границы монитора, на котором окно, в единицах WPF.</summary>
    public static Rect MonitorBounds(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(h, 2), ref info);
        var m = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var tl = m.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
        var br = m.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));
        return new Rect(tl, br);
    }
}

static class Snowflake
{
    /// <summary>Снежинка для логотипа: шесть лучей с веточками.</summary>
    public static Geometry Create(double size)
    {
        var g = new StreamGeometry();
        double c = size / 2, r = size / 2 * 0.92;
        Point P(double d, double a) => new(c + d * Math.Cos(a), c - d * Math.Sin(a));
        using (var ctx = g.Open())
        {
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 2 + i * Math.PI / 3;
                ctx.BeginFigure(P(0, a), false, false);
                ctx.LineTo(P(r, a), true, false);
                foreach (var (d, len) in new[] { (r * 0.5, r * 0.3), (r * 0.76, r * 0.2) })
                {
                    var b = P(d, a);
                    foreach (var side in new[] { -1, 1 })
                    {
                        double ba = a + side * Math.PI / 4;
                        ctx.BeginFigure(b, false, false);
                        ctx.LineTo(new Point(b.X + len * Math.Cos(ba), b.Y - len * Math.Sin(ba)), true, false);
                    }
                }
            }
        }
        g.Freeze();
        return g;
    }
}
