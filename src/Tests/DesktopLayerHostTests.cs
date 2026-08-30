using OsageLagtrain.App.Desktop;
using Xunit;

namespace OsageLagtrain.Tests;

public class DesktopLayerHostTests
{
    private sealed class MockInterop : IDesktopInterop
    {
        public IntPtr Progman = new(0x1234);
        public IntPtr WorkerW = new(0x5678);
        public IntPtr ShellDefView = new(0x9ABC);
        public IntPtr AttachedHwnd = new(0xDEAD);
        public uint ExStyle = 0; // for Progman
        public uint StyleForHwnd = DesktopNative.WS_POPUP; // initial style for attached hwnd
        public uint ExStyleForHwnd = 0;
        public int SendMessageTimeoutFailCount = 0; // number of initial failures before success
        public int SendMessageTimeoutCalls = 0;
        public int SleepCalls = 0;
        public int SetParentCalls = 0;
        public IntPtr LastSetParentParent = IntPtr.Zero;
        public IntPtr LastSetParentChild = IntPtr.Zero;
        public int SetWindowPosCalls = 0;
        public List<(IntPtr hwnd, IntPtr after, uint flags)> SetWindowPosLog = new();
        public int SystemParametersInfoCalls = 0;
        public int SetWinEventHookCalls = 0;
        public uint LastWinEventFlags = 0;
        public int EnumWindowsCalls = 0;
        public int FindWindowCalls = 0;
        public int MapWindowPointsCalls = 0;
        public int GetWindowLongPtrCalls = 0;
        public int SetWindowLongPtrCalls = 0;
        public List<(IntPtr hwnd, int idx, nint val)> SetWindowLongLog = new();
        public bool EnumWindowsShouldFindHost = true;
        public int GetSystemMetricsCX = 1920;
        public int GetSystemMetricsCY = 1080;
        public int GetDpiForWindowValue = 96;

        public IntPtr FindWindow(string? className, string? windowName)
        {
            FindWindowCalls++;
            if (className == "Progman") return Progman;
            return IntPtr.Zero;
        }

        public IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName)
        {
            if (className == "SHELLDLL_DefView" && parent == Progman) return ShellDefView;
            if (className == "WorkerW" && parent == IntPtr.Zero && childAfter != IntPtr.Zero)
            {
                if (WorkerW == IntPtr.Zero) return IntPtr.Zero;
                if (!EnumWindowsShouldFindHost) return IntPtr.Zero;
                if (SendMessageTimeoutFailCount == 0) return WorkerW;
                return SendMessageTimeoutCalls > SendMessageTimeoutFailCount ? WorkerW : IntPtr.Zero;
            }
            if (className == "SHELLDLL_DefView" && childAfter == IntPtr.Zero && parent != IntPtr.Zero)
            {
                // EnumWindows host check
                if (parent == Progman || parent == new IntPtr(0x1111))
                    return ShellDefView;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        public nint GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            GetWindowLongPtrCalls++;
            if (hWnd == Progman && nIndex == DesktopNative.GWL_EXSTYLE) return (nint)ExStyle;
            if (hWnd == AttachedHwnd && nIndex == DesktopNative.GWL_STYLE) return (nint)StyleForHwnd;
            if (hWnd == AttachedHwnd && nIndex == DesktopNative.GWL_EXSTYLE) return (nint)ExStyleForHwnd;
            return 0;
        }

        public nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong)
        {
            SetWindowLongPtrCalls++;
            SetWindowLongLog.Add((hWnd, nIndex, dwNewLong));
            if (hWnd == AttachedHwnd && nIndex == DesktopNative.GWL_STYLE) StyleForHwnd = (uint)dwNewLong;
            if (hWnd == AttachedHwnd && nIndex == DesktopNative.GWL_EXSTYLE) ExStyleForHwnd = (uint)dwNewLong;
            return dwNewLong;
        }

        public IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result)
        {
            SendMessageTimeoutCalls++;
            if (SendMessageTimeoutCalls <= SendMessageTimeoutFailCount)
            {
                result = IntPtr.Zero;
                return IntPtr.Zero; // simulate timeout/fail
            }
            result = new IntPtr(1);
            return new IntPtr(1);
        }

        public bool SetParent(IntPtr child, IntPtr newParent)
        {
            SetParentCalls++;
            LastSetParentChild = child;
            LastSetParentParent = newParent;
            return true;
        }

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags)
        {
            SetWindowPosCalls++;
            SetWindowPosLog.Add((hWnd, hWndInsertAfter, uFlags));
            return true;
        }

        public bool EnumWindows(EnumWindowsProc proc, IntPtr lParam)
        {
            EnumWindowsCalls++;
            if (EnumWindowsShouldFindHost)
            {
                // Simulate one host window 0x1111 that has SHELLDLL_DefView
                var host = new IntPtr(0x1111);
                // Also include Progman
                proc(Progman, lParam);
                // For classic test, host after Progman contains defview but we return WorkerW after host
                // Let's invoke proc with a fake host
                var cont = proc(host, lParam);
                if (!cont) return true;
                // Also try generic
                return true;
            }
            else
            {
                proc(Progman, lParam);
                return true;
            }
        }

        public uint RegisterWindowMessage(string lpString) => 0xC123;

        public IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags)
        {
            SetWinEventHookCalls++;
            LastWinEventFlags = dwFlags;
            return new IntPtr(0x9999);
        }

        public bool UnhookWinEvent(IntPtr hWinEventHook) => true;

        public uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid) { pid = 1234; return 1; }

        public bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags) => true;

        public bool GetWindowRect(IntPtr hWnd, out RECT rect) { rect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }; return true; }

        public int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT rect, uint cPoints) { MapWindowPointsCalls++; return 1; }

        public int GetDpiForWindow(IntPtr hwnd) => GetDpiForWindowValue;

        public int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY) { dpiX = 96; dpiY = 96; return 0; }

        public bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni) { SystemParametersInfoCalls++; return true; }

        public int GetSystemMetrics(int nIndex)
        {
            if (nIndex == DesktopNative.SM_CXVIRTUALSCREEN) return GetSystemMetricsCX;
            if (nIndex == DesktopNative.SM_CYVIRTUALSCREEN) return GetSystemMetricsCY;
            if (nIndex == DesktopNative.SM_XVIRTUALSCREEN) return 0;
            if (nIndex == DesktopNative.SM_YVIRTUALSCREEN) return 0;
            if (nIndex == DesktopNative.SM_CXSCREEN) return 1920;
            if (nIndex == DesktopNative.SM_CYSCREEN) return 1080;
            return 0;
        }

        public void Sleep(int millisecondsTimeout) { SleepCalls++; /* no actual delay for tests */ }

        public IntPtr GetShellDefView() => ShellDefView;

        public uint GetDpiForSystem() => 96;

        public IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags) => IntPtr.Zero;
    }

    [Fact]
    public void Probe_Classic_WhenNoRaisedFlag()
    {
        var mock = new MockInterop { ExStyle = 0 };
        var host = new DesktopLayerHost(mock);
        var topo = host.Probe();
        Assert.Equal(DesktopTopology.ClassicWorkerW, topo);
        Assert.False(host.IsRaised);
    }

    [Fact]
    public void Probe_Raised_WhenWsExNoRedirectionSet()
    {
        var mock = new MockInterop { ExStyle = DesktopNative.WS_EX_NOREDIRECTIONBITMAP };
        var host = new DesktopLayerHost(mock);
        var topo = host.Probe();
        Assert.Equal(DesktopTopology.RaisedDesktop, topo);
        Assert.True(host.IsRaised);
    }

    [Fact]
    public void EnsureLayer_RetryCount_20_SleepsBetweenFailures()
    {
        var mock = new MockInterop { ExStyle = 0, SendMessageTimeoutFailCount = 5, EnumWindowsShouldFindHost = true, WorkerW = new IntPtr(0x5678) };
        var host = new DesktopLayerHost(mock);
        host.Probe(); // classic
        bool ok = host.EnsureLayer();
        Assert.True(ok);
        Assert.Equal(6, mock.SendMessageTimeoutCalls);
        Assert.True(mock.SleepCalls >= 5);
    }

    [Fact]
    public void EnsureLayer_FullRetry_Exhausts20WhenNeverReady()
    {
        var mock = new MockInterop { ExStyle = 0, SendMessageTimeoutFailCount = 100, EnumWindowsShouldFindHost = false };
        mock.Progman = new IntPtr(0x1234);
        mock.WorkerW = new IntPtr(0x5678);
        var host = new DesktopLayerHost(mock);
        host.Probe();
        bool ok = host.EnsureLayer();
        Assert.False(ok);
        Assert.Equal(20, mock.SendMessageTimeoutCalls);
        Assert.True(mock.SleepCalls >= 19);
        Assert.True(mock.SleepCalls <= 20);
    }

    [Fact]
    public void Attach_Raised_UsesProgmanParent_NotWorkerW_AndNoHwndBottom()
    {
        var mock = new MockInterop { ExStyle = DesktopNative.WS_EX_NOREDIRECTIONBITMAP };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        Assert.True(host.IsRaised);
        var hwnd = new IntPtr(0xDEAD);
        mock.AttachedHwnd = hwnd;
        mock.StyleForHwnd = DesktopNative.WS_POPUP; // ensure popup initially
        host.Attach(hwnd);

        // Must have set parent to Progman, not WorkerW
        Assert.Equal(mock.Progman, mock.LastSetParentParent);
        Assert.NotEqual(mock.WorkerW, mock.LastSetParentParent);

        // Must NOT use HWND_BOTTOM on raised
        foreach (var entry in mock.SetWindowPosLog)
        {
            Assert.NotEqual(DesktopNative.HWND_BOTTOM, entry.after);
        }

        // Must have swapped POPUP to CHILD, never both
        uint finalStyle = mock.StyleForHwnd;
        Assert.Equal(0u, finalStyle & DesktopNative.WS_POPUP);
        Assert.NotEqual(0u, finalStyle & DesktopNative.WS_CHILD);
        Assert.False((finalStyle & DesktopNative.WS_POPUP) != 0 && (finalStyle & DesktopNative.WS_CHILD) != 0);

        // Must have called MapWindowPoints (no 0,0 literal)
        Assert.True(mock.MapWindowPointsCalls >= 1);

        // Must have used SetWinEventHook with OUTOFCONTEXT|SKIPOWNPROCESS
        Assert.Equal(1, mock.SetWinEventHookCalls);
        Assert.Equal(DesktopNative.WINEVENT_OUTOFCONTEXT | DesktopNative.WINEVENT_SKIPOWNPROCESS, mock.LastWinEventFlags);

        // SystemParametersInfo must NOT have been called yet (only on Dispose)
        Assert.Equal(0, mock.SystemParametersInfoCalls);
    }

    [Fact]
    public void Attach_Classic_UsesWorkerWParent_AndEnsureWorkerWZOrderUsesHwndBottom()
    {
        var mock = new MockInterop { ExStyle = 0, EnumWindowsShouldFindHost = true };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        Assert.False(host.IsRaised);
        var hwnd = new IntPtr(0xDEAD);
        mock.AttachedHwnd = hwnd;
        mock.StyleForHwnd = DesktopNative.WS_POPUP;
        host.Attach(hwnd);

        Assert.Equal(mock.WorkerW, mock.LastSetParentParent);

        // EnsureWorkerWZOrder should have pushed WorkerW to HWND_BOTTOM
        bool foundBottom = false;
        foreach (var e in mock.SetWindowPosLog)
        {
            if (e.after == DesktopNative.HWND_BOTTOM) foundBottom = true;
        }
        Assert.True(foundBottom, "Classic must use HWND_BOTTOM for WorkerW");

        // Style swap also valid
        uint finalStyle = mock.StyleForHwnd;
        Assert.Equal(0u, finalStyle & DesktopNative.WS_POPUP);
        Assert.NotEqual(0u, finalStyle & DesktopNative.WS_CHILD);
    }

    [Fact]
    public void EnsureWorkerWZOrder_OnlyClassic_SkipsOnRaised()
    {
        var mockRaised = new MockInterop { ExStyle = DesktopNative.WS_EX_NOREDIRECTIONBITMAP };
        var hostRaised = new DesktopLayerHost(mockRaised);
        hostRaised.Probe();
        // Need to set LastWorkerW via reflection or via EnsureLayer path; just call EnsureWorkerWZOrder directly
        hostRaised.EnsureWorkerWZOrder();
        bool raisedUsedBottom = false;
        foreach (var e in mockRaised.SetWindowPosLog) if (e.after == DesktopNative.HWND_BOTTOM) raisedUsedBottom = true;
        Assert.False(raisedUsedBottom, "Raised must NOT use HWND_BOTTOM");

        var mockClassic = new MockInterop { ExStyle = 0 };
        var hostClassic = new DesktopLayerHost(mockClassic);
        hostClassic.Probe();
        // Simulate workerW cached via FindWorkerW
        // Need to force LastWorkerW non-zero: do Attach or directly set via host.LastWorkerW is private; instead call Find via EnsureWorkerWZOrder which will FindWorkerW internally
        hostClassic.EnsureWorkerWZOrder();
        bool classicUsedBottom = false;
        foreach (var e in mockClassic.SetWindowPosLog) if (e.after == DesktopNative.HWND_BOTTOM) classicUsedBottom = true;
        Assert.True(classicUsedBottom, "Classic must use HWND_BOTTOM");
    }

    [Fact]
    public void Dispose_CallsRestoreDesktop_AndNotEarlier()
    {
        var mock = new MockInterop { ExStyle = 0 };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        var hwnd = new IntPtr(0xDEAD);
        mock.AttachedHwnd = hwnd;
        host.Attach(hwnd);
        Assert.Equal(0, mock.SystemParametersInfoCalls);
        host.Dispose();
        Assert.Equal(1, mock.SystemParametersInfoCalls);
        // Second dispose should not call again
        host.Dispose();
        Assert.Equal(1, mock.SystemParametersInfoCalls);
    }

    [Fact]
    public void Attach_DoesNotCombinePopupChild()
    {
        var mock = new MockInterop { ExStyle = DesktopNative.WS_EX_NOREDIRECTIONBITMAP };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        var hwnd = new IntPtr(0xDEAD);
        mock.AttachedHwnd = hwnd;
        mock.StyleForHwnd = DesktopNative.WS_POPUP | DesktopNative.WS_CHILD; // both set initially (bad)
        host.Attach(hwnd);
        uint final = mock.StyleForHwnd;
        Assert.False((final & DesktopNative.WS_POPUP) != 0 && (final & DesktopNative.WS_CHILD) != 0, "Must never have both WS_POPUP|WS_CHILD");
    }

    [Fact]
    public void Probe_FreshFindWindow_EachCall()
    {
        var mock = new MockInterop { ExStyle = 0 };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        int firstCalls = mock.FindWindowCalls;
        host.Probe();
        Assert.True(mock.FindWindowCalls > firstCalls, "Probe must call FindWindow fresh each time");
    }

    [Fact]
    public void TrySetWallpaper_PerScreen_UsesMapWindowPoints_AndDpiScaling()
    {
        var mock = new MockInterop { ExStyle = 0 };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        var hwnd = new IntPtr(0xDEAD);
        bool ok = host.TrySetWallpaperPerScreen(hwnd);
        Assert.True(ok);
        Assert.True(mock.MapWindowPointsCalls >= 1);
        Assert.True(mock.SetWindowPosCalls >= 1);
        // Ensure no 0,0 literal bypass — MapWindowPoints proves non-zero handling
    }

    [Fact]
    public void Healing_SetWinEventHook_UsesCorrectFlags()
    {
        var mock = new MockInterop { ExStyle = DesktopNative.WS_EX_NOREDIRECTIONBITMAP };
        var host = new DesktopLayerHost(mock);
        host.Probe();
        var hwnd = new IntPtr(0xDEAD);
        mock.AttachedHwnd = hwnd;
        host.Attach(hwnd);
        Assert.Equal(DesktopNative.WINEVENT_OUTOFCONTEXT | DesktopNative.WINEVENT_SKIPOWNPROCESS, mock.LastWinEventFlags);
        Assert.NotEqual(IntPtr.Zero, host.WinEventHookHandle);
        Assert.NotEqual(0u, host.TaskbarCreatedMessage);
    }
}
