using OsageLagtrain.App.WindowMonitor;
using Xunit;

namespace OsageLagtrain.Tests;

public class WindowMonitorTests
{
    private sealed class MockInterop : IWindowInterop
    {
        public IntPtr Foreground = IntPtr.Zero;
        public IntPtr DesktopWindow = new(0x1000);
        public IntPtr ShellWindow = new(0x1001);
        public string ForegroundClass = "Chrome_WidgetWin_1";
        public bool IsZoomedResult = false;
        public bool IsVisibleResult = true;
        public bool IsIconicResult = false;
        public bool IsCloakedResult = false;
        public bool IsToolWindowResult = false;
        public bool IsSelfAncestorResult = true;
        public Rect FrameBounds = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        public bool HasFrameBounds = true;
        public MonitorBounds MonBounds = new()
        {
            MonitorHandle = new(0x2000),
            RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
        };
        public bool HasMonitorBounds = true;
        public QUNS NotificationState = QUNS.QUNS_ACCEPTS_NOTIFICATIONS;
        public int GetNotificationStateCalls = 0;
        public string ExeName = "chrome.exe";
        public List<(uint min, uint max, uint flags)> HookCalls = new();
        public int HookReturnHandle = 0x9999;
        public List<IntPtr> Unhooked = new();
        public string? LastMonitorId;

        public IntPtr GetForegroundWindow() => Foreground;
        public IntPtr GetDesktopWindow() => DesktopWindow;
        public IntPtr GetShellWindow() => ShellWindow;
        public string GetClassName(IntPtr hwnd) => ForegroundClass;
        public bool IsZoomed(IntPtr hwnd) => IsZoomedResult;
        public bool IsWindowVisible(IntPtr hwnd) => IsVisibleResult;
        public bool IsIconic(IntPtr hwnd) => IsIconicResult;
        public bool IsCloaked(IntPtr hwnd) => IsCloakedResult;
        public bool IsToolWindow(IntPtr hwnd) => IsToolWindowResult;
        public bool IsSelfAncestor(IntPtr hwnd) => IsSelfAncestorResult;
        public bool GetExtendedFrameBounds(IntPtr hwnd, out Rect rect) { rect = FrameBounds; return HasFrameBounds; }
        public bool GetMonitorBounds(IntPtr hwnd, out MonitorBounds bounds) { bounds = MonBounds; return HasMonitorBounds; }
        public QUNS GetNotificationState() { GetNotificationStateCalls++; return NotificationState; }
        public uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid) { pid = 1234; return 1; }
        public string GetExeName(IntPtr hwnd) => ExeName;
        public IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WindowMonitorWinEventDelegate del, uint idProcess, uint idThread, uint dwFlags)
        {
            HookCalls.Add((eventMin, eventMax, dwFlags));
            return new IntPtr(HookReturnHandle++);
        }
        public bool UnhookWinEvent(IntPtr h) { Unhooked.Add(h); return true; }
        public void Sleep(int ms) { }
    }

    [Fact]
    public void IsCovered_True_For_IsZoomed_Maximized()
    {
        var mock = new MockInterop { IsZoomedResult = true, Foreground = new(0x5000) };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        Assert.True(wm.IsCovering(new IntPtr(0x5000)));
        Assert.True(wm.CoversMonitor(new IntPtr(0x5000)));
    }

    [Fact]
    public void CoversMonitor_95_Borderless_F11_True()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = false,
            FrameBounds = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            MonBounds = new MonitorBounds
            {
                MonitorHandle = new(0x2000),
                RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 }
            }
        };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        Assert.True(wm.CoversMonitor(new IntPtr(0x5000)));

        // 95% width/height borderless should still pass
        mock.FrameBounds = new Rect { Left = 0, Top = 0, Right = 1824, Bottom = 1026 }; // 95% of 1920x1080
        Assert.True(wm.CoversMonitor(new IntPtr(0x5000)));
    }

    [Fact]
    public void Small_Window_False()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = false,
            FrameBounds = new Rect { Left = 100, Top = 100, Right = 900, Bottom = 700 }, // 800x600
            MonBounds = new MonitorBounds
            {
                MonitorHandle = new(0x2000),
                RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 }
            }
        };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        Assert.False(wm.CoversMonitor(new IntPtr(0x5000)));
        Assert.False(wm.IsCovering(new IntPtr(0x5000)));
    }

    [Fact]
    public void Desktop_Gate_True()
    {
        var mock = new MockInterop();
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        // Progman
        mock.ForegroundClass = "Progman";
        Assert.True(wm.IsDesktopForeground(new IntPtr(0x6000)));
        mock.ForegroundClass = "WorkerW";
        Assert.True(wm.IsDesktopForeground(new IntPtr(0x6000)));
        mock.ForegroundClass = "SHELLDLL_DefView";
        Assert.True(wm.IsDesktopForeground(new IntPtr(0x6000)));
        mock.ForegroundClass = "Shell_TrayWnd";
        Assert.True(wm.IsDesktopForeground(new IntPtr(0x6000)));
        mock.ForegroundClass = "SysListView32";
        Assert.True(wm.IsDesktopForeground(new IntPtr(0x6000)));
        // hwnd == desktop
        mock.ForegroundClass = "Anything";
        Assert.True(wm.IsDesktopForeground(mock.DesktopWindow));
        Assert.True(wm.IsDesktopForeground(mock.ShellWindow));
        // IntPtr.Zero also desktop per spec (null foreground)
        Assert.True(wm.IsDesktopForeground(IntPtr.Zero));
        // Chrome not desktop
        mock.ForegroundClass = "Chrome_WidgetWin_1";
        Assert.False(wm.IsDesktopForeground(new IntPtr(0x5000)));
    }

    [Fact]
    public void SHQuery_D3D_True_Pauses_And_NoAdvance()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            NotificationState = QUNS.QUNS_RUNNING_D3D_FULL_SCREEN
        };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int advances = 0;
        wm.WallpaperShouldAdvance += (_, _) => advances++;
        // Set previous covering
        wm.TriggerEvaluate(); // foreground covering, caches state but D3D pauses -> no fire yet
        Assert.True(wm.IsPausedByD3D);
        // Now foreground is desktop but still D3D -> should NOT fire
        mock.Foreground = IntPtr.Zero;
        mock.ForegroundClass = "Progman";
        wm.TriggerEvaluate();
        Assert.Equal(0, advances);
    }

    [Fact]
    public void SHQuery_Cached_500ms()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var mock = new MockInterop { NotificationState = QUNS.QUNS_ACCEPTS_NOTIFICATIONS };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => now, uiDispatcher: a => a());
        wm.TriggerEvaluate();
        Assert.Equal(1, mock.GetNotificationStateCalls);
        // Within 100ms -> cached, no new call
        now = now.AddMilliseconds(100);
        wm.TriggerEvaluate();
        Assert.Equal(1, mock.GetNotificationStateCalls);
        // After 600ms total -> new call
        now = now.AddMilliseconds(500);
        wm.TriggerEvaluate();
        Assert.Equal(2, mock.GetNotificationStateCalls);
    }

    [Fact]
    public void Debounce_SingleTrigger_ForegroundDesktop_FiresAdvance_Once()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            ExeName = "notepad.exe",
            MonBounds = new MonitorBounds
            {
                MonitorHandle = new IntPtr(0xABCD),
                RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
            }
        };
        // Use 0 delay for immediate fire
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int advances = 0;
        string? lastMonitor = null;
        string? lastExe = null;
        wm.WallpaperShouldAdvance += (m, e) => { advances++; lastMonitor = m; lastExe = e; };

        // Covering window in foreground
        wm.TriggerEvaluate();
        Assert.Equal(0, advances); // covering does not fire, just records

        // Now desktop foreground -> should fire 1 advance
        mock.Foreground = IntPtr.Zero; // null foreground treated as desktop
        wm.TriggerEvaluate();
        Assert.Equal(1, advances);
        Assert.Equal("0xABCD", lastMonitor);
        Assert.Equal("notepad.exe", lastExe);

        // Second desktop evaluate without intermediate covering -> no second fire
        wm.TriggerEvaluate();
        Assert.Equal(1, advances);
    }

    [Fact]
    public void MinimizeEnd_ForegroundDesktop_FiresAdvance()
    {
        // Simulate: minimized covering app then desktop
        var mock = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            ExeName = "game.exe",
            MonBounds = new MonitorBounds
            {
                MonitorHandle = new IntPtr(0x100),
                RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
            }
        };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int advances = 0;
        wm.WallpaperShouldAdvance += (_, _) => advances++;

        // Covering
        wm.TriggerEvaluate();
        Assert.Equal(0, advances);
        // Desktop after minimize+foreground change
        mock.Foreground = mock.DesktopWindow; // class progman
        mock.ForegroundClass = "Progman";
        // Even if we had minimize start/end events, EvaluateCovering is what fires
        wm.TriggerEvaluate();
        Assert.Equal(1, advances);
    }

    [Fact]
    public void AltTab_Maximized_NoFire_UntilDesktop()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            ExeName = "app1.exe",
            MonBounds = new MonitorBounds
            {
                MonitorHandle = new IntPtr(0x3000),
                RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
            }
        };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int advances = 0;
        wm.WallpaperShouldAdvance += (_, _) => advances++;

        wm.TriggerEvaluate(); // app1 covering
        Assert.Equal(0, advances);

        // Alt+Tab to another maximized window (also covering) -> no desktop, no fire
        mock.ExeName = "app2.exe";
        mock.Foreground = new IntPtr(0x5001);
        wm.TriggerEvaluate();
        Assert.Equal(0, advances);

        // Alt+Tab to small window -> previousWasCovering becomes false, so next desktop should NOT fire? Spec says single trigger only when previousForeground was IsZoomed/CoversMonitor.
        // But our second evaluate set previousWasCovering to true (since app2 also covering). Then small window should clear it.
        // Let's set small window: override to non-covering via IsZoomed false + small bounds
        mock.IsZoomedResult = false;
        mock.FrameBounds = new Rect { Left = 100, Top = 100, Right = 500, Bottom = 400 };
        mock.Foreground = new IntPtr(0x5002);
        mock.ExeName = "small.exe";
        mock.ForegroundClass = "Chrome_WidgetWin_1";
        wm.TriggerEvaluate();
        Assert.Equal(0, advances); // small window clears flag

        // Now desktop after small window -> should NOT fire (since previous was small, not covering)
        mock.Foreground = IntPtr.Zero;
        mock.ForegroundClass = "Progman";
        wm.TriggerEvaluate();
        Assert.Equal(0, advances);
    }

    [Fact]
    public void LocationChange_Not_Subscribed()
    {
        var mock = new MockInterop();
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        wm.Start();
        // 0x800B is LOCATIONCHANGE — must NOT be subscribed
        foreach (var (min, max, _) in mock.HookCalls)
        {
            Assert.NotEqual(0x800Bu, min);
            Assert.NotEqual(0x800Bu, max);
        }
        // Must be subscribed to correct events
        var subscribed = mock.HookCalls.Select(c => c.min).ToHashSet();
        Assert.Contains(0x3u, subscribed);
        Assert.Contains(0x16u, subscribed);
        Assert.Contains(0x17u, subscribed);
        Assert.Contains(0xAu, subscribed);
        Assert.Contains(0xBu, subscribed);
        Assert.Contains(0x8001u, subscribed);
        Assert.Equal(6, mock.HookCalls.Count);
        // Flags must be OUTOFCONTEXT|SKIPOWNPROCESS (0x2)
        foreach (var (_, _, flags) in mock.HookCalls)
        {
            Assert.Equal(WindowMonitorConstants.WINEVENT_OUTOFCONTEXT | WindowMonitorConstants.WINEVENT_SKIPOWNPROCESS, flags);
        }
        wm.Dispose();
    }

    [Fact]
    public void Filters_Blocked_When_CloakedOrToolOrNotVisible()
    {
        var mock = new MockInterop { IsZoomedResult = true, Foreground = new IntPtr(0x5000) };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());

        mock.IsVisibleResult = false;
        Assert.False(wm.IsCovering(new IntPtr(0x5000)));
        mock.IsVisibleResult = true;

        mock.IsIconicResult = true;
        Assert.False(wm.IsCovering(new IntPtr(0x5000)));
        mock.IsIconicResult = false;

        mock.IsCloakedResult = true;
        Assert.False(wm.IsCovering(new IntPtr(0x5000)));
        mock.IsCloakedResult = false;

        mock.IsToolWindowResult = true;
        Assert.False(wm.IsCovering(new IntPtr(0x5000)));
        mock.IsToolWindowResult = false;

        mock.IsSelfAncestorResult = false;
        Assert.False(wm.IsCovering(new IntPtr(0x5000)));
        mock.IsSelfAncestorResult = true;

        // Now all filters pass, should be covering via IsZoomed
        Assert.True(wm.IsCovering(new IntPtr(0x5000)));
    }

    [Fact]
    public void PostEventDelayMs_PerScene_Override()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            ExeName = "a.exe",
            MonBounds = new MonitorBounds
            {
                MonitorHandle = new IntPtr(0x4000),
                RcMonitor = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                RcWork = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
            }
        };
        // global 500, per-scene 0 -> should fire immediately
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 500, perScenePostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int advances = 0;
        wm.WallpaperShouldAdvance += (_, _) => advances++;
        wm.TriggerEvaluate(); // covering
        mock.Foreground = IntPtr.Zero;
        wm.TriggerEvaluate(); // desktop
        Assert.Equal(1, advances);

        // Now test per-scene override 500 when global 0 -> delayed; we use immediate check but with delay the advance will be async via timer
        // For deterministic check, we test Effective delay via second monitor with delay 200 -> not immediate, so advances still 0 before sleep
        var mock2 = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            ExeName = "b.exe",
            MonBounds = mock.MonBounds
        };
        var wm2 = new WindowMonitor(mock2, globalPostEventDelayMs: 0, perScenePostEventDelayMs: 200, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int adv2 = 0;
        wm2.WallpaperShouldAdvance += (_, _) => adv2++;
        wm2.TriggerEvaluate();
        mock2.Foreground = IntPtr.Zero;
        wm2.TriggerEvaluate();
        // Delayed, so not yet
        Assert.Equal(0, adv2);
        // Wait 300ms
        Thread.Sleep(350);
        Assert.Equal(1, adv2);
    }

    [Fact]
    public void FallbackPoll_Subscribed_Dispose_Unhooks()
    {
        var mock = new MockInterop();
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        wm.Start();
        Assert.Equal(6, mock.HookCalls.Count);
        wm.Dispose();
        Assert.Equal(6, mock.Unhooked.Count);
        // Second dispose no throw
        wm.Dispose();
    }

    [Fact]
    public void SHQuery_Alias7_TreatedAsD3D()
    {
        var mock = new MockInterop
        {
            IsZoomedResult = true,
            Foreground = new IntPtr(0x5000),
            NotificationState = (QUNS)7
        };
        var wm = new WindowMonitor(mock, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        wm.TriggerEvaluate();
        Assert.True(wm.IsPausedByD3D);
    }
}
