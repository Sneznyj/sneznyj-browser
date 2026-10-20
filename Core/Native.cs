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

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(POINT p);

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWA_CAPTION_COLOR = 35;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;

    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>Тёмная или светлая рамка окна Windows и скругление — под текущую тему.</summary>
    public static void StyleWindow(Window w, bool rounded = true)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        var p = Themes.Current;
        int dark = p.IsDark ? 1 : 0;
        DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int corners = rounded ? 2 : 1;
        DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, 4);
        int border = ColorRef(Themes.ToColor(p.Line));
        DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref border, 4);
        int caption = ColorRef(Themes.ToColor(p.Bg));
        DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref caption, 4);
    }

    /// <summary>Окно-подсказка: не забирает фокус при клике, не видно в Alt+Tab; с clickThrough — пропускает мышь насквозь.</summary>
    public static void MakeOverlay(Window w, bool clickThrough = false)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
    }

    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static bool AnyMouseButtonDown() => IsKeyDown(0x01) || IsKeyDown(0x02) || IsKeyDown(0x04);

    /// <summary>Курсор в пикселях экрана.</summary>
    public static Point CursorPx()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    /// <summary>Верхнее окно под курсором (главное окно программы, которой принадлежит точка).</summary>
    public static IntPtr RootWindowUnderCursor()
    {
        GetCursorPos(out var p);
        var hwnd = WindowFromPoint(p);
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, 2 /* GA_ROOT */);
    }

    public static IntPtr Foreground() => GetForegroundWindow();

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;

    public static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

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

    /// <summary>Прямоугольник элемента в пикселях экрана.</summary>
    public static Rect ScreenRectPx(FrameworkElement e)
    {
        if (PresentationSource.FromVisual(e) == null) return Rect.Empty;
        var tl = e.PointToScreen(new Point(0, 0));
        var br = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
        return new Rect(tl, br);
    }

    /// <summary>Пиксели экрана → единицы WPF (для Left/Top окон).</summary>
    public static Point PxToDip(Visual v, Point px)
    {
        var m = PresentationSource.FromVisual(v)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return m.Transform(px);
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
