using System.Runtime.InteropServices;

namespace OsageLagtrain.App.Desktop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

public interface IDesktopInterop
{
    IntPtr FindWindow(string? className, string? windowName);
    IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);
    nint GetWindowLongPtr(IntPtr hWnd, int nIndex);
    nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);
    IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    bool SetParent(IntPtr child, IntPtr newParent);
    bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    uint RegisterWindowMessage(string lpString);
    IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    bool UnhookWinEvent(IntPtr hWinEventHook);
    uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    bool GetWindowRect(IntPtr hWnd, out RECT rect);
    int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT rect, uint cPoints);
    int GetDpiForWindow(IntPtr hwnd);
    int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);
    bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);
    int GetSystemMetrics(int nIndex);
    void Sleep(int millisecondsTimeout);
    IntPtr GetShellDefView();
    // Default impl for backward compat with existing test mocks
    bool ShowWindow(IntPtr hWnd, int nCmdShow) => true;
    // DPI helper
    uint GetDpiForSystem();
    IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
}
