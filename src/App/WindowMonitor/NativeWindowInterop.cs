using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OsageLagtrain.App.WindowMonitor;

public sealed class NativeWindowInterop : IWindowInterop, IDisposable
{
    private readonly Dictionary<IntPtr, GCHandle> _hookHandles = new();
    private readonly object _handleLock = new();
    private bool _disposed;

    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public IntPtr GetDesktopWindow() => NativeMethods.GetDesktopWindow();
    public IntPtr GetShellWindow() => NativeMethods.GetShellWindow();
    public string GetClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        int len = NativeMethods.GetClassNameW(hwnd, sb, sb.Capacity);
        if (len <= 0) return string.Empty;
        return sb.ToString();
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
        WinEventProc proc = (hHook, eventType, hwnd, idObj, idChild, tid, time) => del(hHook, eventType, hwnd, idObj, idChild, tid, time);
        var gch = GCHandle.Alloc(proc);
        var hook = NativeMethods.SetWinEventHook(eventMin, eventMax, hmod, proc, idProcess, idThread, dwFlags);
        if (hook != IntPtr.Zero)
        {
            lock (_handleLock) _hookHandles[hook] = gch;
        }
        else
        {
            if (gch.IsAllocated) gch.Free();
        }
        return hook;
    }
    public bool UnhookWinEvent(IntPtr hWinEventHook)
    {
        var ok = NativeMethods.UnhookWinEvent(hWinEventHook);
        lock (_handleLock)
        {
            if (_hookHandles.TryGetValue(hWinEventHook, out var gch))
            {
                if (gch.IsAllocated) gch.Free();
                _hookHandles.Remove(hWinEventHook);
            }
        }
        return ok;
    }
    public void Sleep(int ms) => Thread.Sleep(ms);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_handleLock)
        {
            foreach (var kv in _hookHandles)
                if (kv.Value.IsAllocated) kv.Value.Free();
            _hookHandles.Clear();
        }
        GC.SuppressFinalize(this);
    }

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
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
