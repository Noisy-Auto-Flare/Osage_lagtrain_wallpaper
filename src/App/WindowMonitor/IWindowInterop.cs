namespace OsageLagtrain.App.WindowMonitor;

public interface IWindowInterop
{
    IntPtr GetForegroundWindow();
    IntPtr GetDesktopWindow();
    IntPtr GetShellWindow();
    string GetClassName(IntPtr hwnd);
    bool IsZoomed(IntPtr hwnd);
    bool IsWindowVisible(IntPtr hwnd);
    bool IsIconic(IntPtr hwnd);
    bool IsCloaked(IntPtr hwnd); // DWMWA_CLOAKED !=0
    bool IsToolWindow(IntPtr hwnd); // WS_EX_TOOLWINDOW
    bool IsSelfAncestor(IntPtr hwnd); // GetAncestor(GA_ROOT) == hwnd
    bool GetExtendedFrameBounds(IntPtr hwnd, out Rect rect); // DwmGetWindowAttribute DWMWA_EXTENDED_FRAME_BOUNDS
    bool GetMonitorBounds(IntPtr hwnd, out MonitorBounds bounds); // MonitorFromWindow + GetMonitorInfo rcMonitor/rcWork
    QUNS GetNotificationState(); // SHQueryUserNotificationState
    uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    string GetExeName(IntPtr hwnd); // via pid -> Process -> FileName fallback
    IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WindowMonitorWinEventDelegate del, uint idProcess, uint idThread, uint dwFlags);
    bool UnhookWinEvent(IntPtr hWinEventHook);
    void Sleep(int ms);
}

public delegate void WindowMonitorWinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
