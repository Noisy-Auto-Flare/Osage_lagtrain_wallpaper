using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OsageLagtrain.App.WindowMonitor;

public sealed class NativeWindowInterop : IWindowInterop
{
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public IntPtr GetDesktopWindow() => NativeMethods.GetDesktopWindow();
    public IntPtr GetShellWindow() => NativeMethods.GetShellWindow();
    public string GetClassName(IntPtr hwnd)
    {
        Span<char> buf = stackalloc char[256];
        int len = NativeMethods.GetClassNameW(hwnd, buf);
        if (len <= 0) return string.Empty;
        return new string(buf[..len]);
    }
    public bool IsZoomed(IntPtr hwnd) => NativeMethods.IsZoomed(hwnd);
    public bool IsWindowVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);
    public bool IsIconic(IntPtr hwnd) => NativeMethods.IsIconic(hwnd);
    public bool IsCloaked(IntPtr hwnd)
    {
        int cloaked = 0;
        int hr = NativeMethods.DwmGetWindowAttribute(hwnd, (int)WindowMonitorConstants.DWMWA_CLOAKED, ref cloaked, Marshal.SizeOf<int>());
        return hr == 0 && cloaked != 0;
    }
    public bool IsToolWindow(IntPtr hwnd)
    {
        var ex = (uint)NativeMethods.GetWindowLongW(hwnd, WindowMonitorConstants.GWL_EXSTYLE);
        return (ex & WindowMonitorConstants.WS_EX_TOOLWINDOW) != 0;
    }
    public bool IsSelfAncestor(IntPtr hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, WindowMonitorConstants.GA_ROOT);
        return root == hwnd;
    }
    public bool GetExtendedFrameBounds(IntPtr hwnd, out Rect rect)
    {
        rect = default;
        RECT r = default;
        int hr = NativeMethods.DwmGetWindowAttribute(hwnd, (int)WindowMonitorConstants.DWMWA_EXTENDED_FRAME_BOUNDS, ref r, Marshal.SizeOf<RECT>());
        if (hr != 0) return false;
        rect = new Rect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return true;
    }
    public bool GetMonitorBounds(IntPtr hwnd, out MonitorBounds bounds)
    {
        bounds = default;
        var hMon = NativeMethods.MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/);
        if (hMon == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(hMon, ref mi)) return false;
        bounds = new MonitorBounds
        {
            MonitorHandle = hMon,
            RcMonitor = new Rect { Left = mi.rcMonitor.Left, Top = mi.rcMonitor.Top, Right = mi.rcMonitor.Right, Bottom = mi.rcMonitor.Bottom },
            RcWork = new Rect { Left = mi.rcWork.Left, Top = mi.rcWork.Top, Right = mi.rcWork.Right, Bottom = mi.rcWork.Bottom },
        };
        return true;
    }
    public QUNS GetNotificationState()
    {
        int st = 0;
        int hr = NativeMethods.SHQueryUserNotificationState(out st);
        if (hr != 0) return QUNS.QUNS_ACCEPTS_NOTIFICATIONS;
        return (QUNS)st;
    }
    public uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid) => NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
    public string GetExeName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;
            using var p = Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            try
            {
                var file = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(file)) return Path.GetFileName(file);
            }
            catch { }
            return name + ".exe";
        }
        catch { return string.Empty; }
    }
    public IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WindowMonitorWinEventDelegate del, uint idProcess, uint idThread, uint dwFlags)
    {
        // Adapt delegate
        WinEventProc proc = (hHook, eventType, hwnd, idObj, idChild, tid, time) => del(hHook, eventType, hwnd, idObj, idChild, tid, time);
        var gch = GCHandle.Alloc(proc);
        // Caller keeps GCHandle; we leak here intentionally if caller doesn't hold — but NativeWindowInterop tracks via WindowMonitor.
        // For native path, WindowMonitor keeps its own GCHandle; this wrapper just forwards.
        return NativeMethods.SetWinEventHook(eventMin, eventMax, hmod, proc, idProcess, idThread, dwFlags);
    }
    public bool UnhookWinEvent(IntPtr hWinEventHook) => NativeMethods.UnhookWinEvent(hWinEventHook);
    public void Sleep(int ms) => Thread.Sleep(ms);

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, Span<char> lpClassName);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, int gaFlags);
        [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, ref RECT pvAttribute, int cbAttribute);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int pquns);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
}
