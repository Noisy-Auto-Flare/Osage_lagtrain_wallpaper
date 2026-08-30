namespace OsageLagtrain.App.Desktop;

public sealed class NativeDesktopInterop : IDesktopInterop
{
    public IntPtr FindWindow(string? className, string? windowName) => DesktopNative.FindWindowW(className, windowName);

    public IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName) => DesktopNative.FindWindowExW(parent, childAfter, className, windowName);

    public nint GetWindowLongPtr(IntPtr hWnd, int nIndex) => DesktopNative.GetWindowLongPtrW(hWnd, nIndex);

    public nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong) => DesktopNative.SetWindowLongPtrW(hWnd, nIndex, (IntPtr)dwNewLong);

    public IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result)
        => DesktopNative.SendMessageTimeoutW(hWnd, msg, wParam, lParam, flags, timeoutMs, out result);

    public bool SetParent(IntPtr child, IntPtr newParent) => DesktopNative.SetParent(child, newParent) != IntPtr.Zero;

    public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags)
        => DesktopNative.SetWindowPos(hWnd, hWndInsertAfter, x, y, cx, cy, uFlags);

    public bool EnumWindows(EnumWindowsProc proc, IntPtr lParam) => DesktopNative.EnumWindows(proc, lParam);

    public uint RegisterWindowMessage(string lpString) => DesktopNative.RegisterWindowMessageW(lpString);

    public IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags)
        => DesktopNative.SetWinEventHook(eventMin, eventMax, hmodWinEventProc, lpfnWinEventProc, idProcess, idThread, dwFlags);

    public bool UnhookWinEvent(IntPtr hWinEventHook) => DesktopNative.UnhookWinEvent(hWinEventHook);

    public uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid) => DesktopNative.GetWindowThreadProcessId(hWnd, out pid);

    public bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags)
        => DesktopNative.SetLayeredWindowAttributes(hwnd, crKey, bAlpha, dwFlags);

    public bool GetWindowRect(IntPtr hWnd, out RECT rect) => DesktopNative.GetWindowRect(hWnd, out rect);

    public int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT rect, uint cPoints) => DesktopNative.MapWindowPoints(hWndFrom, hWndTo, ref rect, cPoints);

    public int GetDpiForWindow(IntPtr hwnd) => (int)DesktopNative.GetDpiForWindow(hwnd);

    public int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY) => DesktopNative.GetDpiForMonitor(hmonitor, dpiType, out dpiX, out dpiY);

    public bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni) => DesktopNative.SystemParametersInfoW(uiAction, uiParam, pvParam, fWinIni);

    public int GetSystemMetrics(int nIndex) => DesktopNative.GetSystemMetrics(nIndex);

    public void Sleep(int millisecondsTimeout) => Thread.Sleep(millisecondsTimeout);

    public IntPtr GetShellDefView()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        return FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
    }

    public uint GetDpiForSystem() => DesktopNative.GetDpiForSystem();

    public IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags) => DesktopNative.MonitorFromWindow(hwnd, dwFlags);

    public bool ShowWindow(IntPtr hWnd, int nCmdShow) => DesktopNative.ShowWindow(hWnd, nCmdShow);
}
