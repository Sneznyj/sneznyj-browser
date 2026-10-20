using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace SneznyjBrowser;

/// <summary>
/// Жесты мыши, как в Яндексе: зажать правую кнопку и провести (← назад, → вперёд, ↑↓ обновить, ↓→ закрыть вкладку…).
/// Мышь только наблюдается (события не перехватываются и не блокируются) и только над страницей
/// активного окна браузера; слушатель работает в своём потоке, чтобы не тормозить мышь, даже если браузер занят.
/// </summary>
public static class Gestures
{
    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }

    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_QUIT = 0x12;

    static Thread? _thread;
    static uint _threadId;
    static LowLevelMouseProc? _proc;
    static IntPtr _hook;
    static Dispatcher? _ui;

    // Пишутся из окна, читаются в потоке жестов.
    static IntPtr _target;
    static int _left, _top, _right, _bottom;
    static long _suppressUntil;

    // Состояние текущего жеста (только поток жестов).
    static bool _tracking, _moved;
    static int _ax, _ay, _sx, _sy;
    static string _dirs = "";
    static long _lastTrail;

    /// <summary>Распознан жест: строка направлений, например "L", "UD", "DR".</summary>
    public static event Action<string>? Recognized;
    /// <summary>Курсор в пикселях экрана и направления на этот момент (для следа и подсказки); null — жест закончился.</summary>
    public static event Action<Point?, string>? Progress;

    public static bool Running => _thread != null;

    public static void SetTarget(IntPtr window, Rect pagePx)
    {
        _target = window;
        Interlocked.Exchange(ref _left, (int)pagePx.Left);
        Interlocked.Exchange(ref _top, (int)pagePx.Top);
        Interlocked.Exchange(ref _right, (int)pagePx.Right);
        Interlocked.Exchange(ref _bottom, (int)pagePx.Bottom);
    }

    public static void Start(Dispatcher ui)
    {
        if (_thread != null) return;
        _ui = ui;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _proc = HookProc;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        { IsBackground = true, Name = "mouse-gestures" };
        _thread.Start();
        ready.Wait(2000);
    }

    public static void Stop()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread = null;
        _tracking = false;
    }

    /// <summary>Только что был жест правой кнопкой — контекстное меню страницы показывать не надо.</summary>
    public static bool ConsumeContextMenu()
    {
        long until = Interlocked.Read(ref _suppressUntil);
        if (until == 0 || Environment.TickCount64 > until) return false;
        Interlocked.Exchange(ref _suppressUntil, 0);
        return true;
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                OnMouse((int)wParam, m.x, m.y);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static void OnMouse(int msg, int x, int y)
    {
        switch (msg)
        {
            case WM_RBUTTONDOWN:
                _tracking = _target != IntPtr.Zero && GetForegroundWindow() == _target &&
                            x >= _left && x < _right && y >= _top && y < _bottom;
                _moved = false;
                _dirs = "";
                _ax = _sx = x;
                _ay = _sy = y;
                break;
            case WM_MOUSEMOVE when _tracking:
            {
                int dx = x - _ax, dy = y - _ay;
                const int step = 26;
                if (Math.Abs(dx) >= step || Math.Abs(dy) >= step)
                {
                    char dir = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 'R' : 'L') : (dy > 0 ? 'D' : 'U');
                    if (_dirs.Length == 0 || _dirs[^1] != dir) _dirs += dir;
                    _ax = x;
                    _ay = y;
                }
                if (!_moved && (Math.Abs(x - _sx) > 12 || Math.Abs(y - _sy) > 12)) _moved = true;
                long now = Environment.TickCount64;
                if (_moved && now - _lastTrail >= 15)
                {
                    _lastTrail = now;
                    var dirs = _dirs;
                    _ui?.BeginInvoke(() => Progress?.Invoke(new Point(x, y), dirs), DispatcherPriority.Render);
                }
                break;
            }
            case WM_LBUTTONDOWN when _tracking:
                _tracking = false;
                _ui?.BeginInvoke(() => Progress?.Invoke(null, ""));
                break;
            case WM_RBUTTONUP when _tracking:
            {
                _tracking = false;
                var dirs = _dirs;
                if (_moved) _ui?.BeginInvoke(() => Progress?.Invoke(null, ""));
                if (dirs.Length > 0 && dirs.Length <= 4)
                {
                    // Меню страницы появится сразу после отпускания кнопки — его надо пропустить.
                    Interlocked.Exchange(ref _suppressUntil, Environment.TickCount64 + 900);
                    _ui?.BeginInvoke(() => Recognized?.Invoke(dirs));
                }
                else if (dirs.Length > 4) Interlocked.Exchange(ref _suppressUntil, Environment.TickCount64 + 900);
                break;
            }
        }
    }
}
