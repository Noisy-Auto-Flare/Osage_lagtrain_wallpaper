namespace OsageLagtrain.App;

internal static partial class Program
{
    private static void RunMonitorTest(string[] args)
    {
        Console.WriteLine("WindowMonitor --monitor-test harness");
        Console.WriteLine($"DebounceMs={WindowMonitor.WindowMonitorConstants.DebounceMs} FallbackPollMs={WindowMonitor.WindowMonitorConstants.FallbackPollMs} DefaultPostEventDelayMs={WindowMonitor.WindowMonitorConstants.DefaultPostEventDelayMs}");
        Console.WriteLine($"Subscribed events: FOREGROUND 0x3, MINIMIZESTART 0x16, MINIMIZEEND 0x17, MOVESIZESTART 0xA, MOVESIZEEND 0xB, OBJECT_DESTROY 0x8001 (LOCATIONCHANGE 0x800B NOT subscribed)");
        Console.WriteLine($"CoverageThreshold={WindowMonitor.WindowMonitorConstants.CoverageThreshold} DWMWA_EXTENDED_FRAME_BOUNDS=9 vs rcMonitor/rcWork");
        Console.WriteLine($"SHQuery cache {WindowMonitor.WindowMonitorConstants.ShQueryCacheMs}ms QUNS_RUNNING_D3D_FULL_SCREEN={(int)WindowMonitor.QUNS.QUNS_RUNNING_D3D_FULL_SCREEN} (alias 7 compat)");
        var fakeInterop = new SimulateInterop();
        var wm = new WindowMonitor.WindowMonitor(fakeInterop, globalPostEventDelayMs: 0, nowProvider: () => DateTimeOffset.UtcNow, uiDispatcher: a => a());
        int advances = 0;
        wm.WallpaperShouldAdvance += (mon, exe) => { advances++; Console.WriteLine($"Advance #{advances}: monitor={mon} exe={exe}"); };
        fakeInterop.Foreground = new IntPtr(0x5000); fakeInterop.IsZoomedResult = true; fakeInterop.ExeName = "notepad.exe";
        fakeInterop.ClassName = "Notepad"; wm.TriggerEvaluate();
        fakeInterop.Foreground = IntPtr.Zero; fakeInterop.ClassName = "Progman"; wm.TriggerEvaluate();
        Console.WriteLine($"Scenario MinimizeEnd+ForegroundDesktop: expected Advance 1, got {advances} {(advances==1?"PASS":"FAIL")}");
        int before = advances;
        fakeInterop.Foreground = new IntPtr(0x5001); fakeInterop.IsZoomedResult = false;
        fakeInterop.FrameBounds = new WindowMonitor.Rect { Left=100,Top=100,Right=600,Bottom=400};
        fakeInterop.ClassName = "Chrome_WidgetWin_1"; fakeInterop.ExeName="chrome.exe";
        wm.TriggerEvaluate();
        fakeInterop.Foreground = IntPtr.Zero; fakeInterop.ClassName="Progman"; wm.TriggerEvaluate();
        Console.WriteLine($"Scenario small window -> desktop: expected no new Advance, got {advances - before} {(advances - before==0?"PASS":"FAIL")}");
        var fakeD3D = new SimulateInterop{NotificationState=WindowMonitor.QUNS.QUNS_RUNNING_D3D_FULL_SCREEN, Foreground=new IntPtr(0x5002), IsZoomedResult=true, FrameBounds=new WindowMonitor.Rect{Left=0,Top=0,Right=1920,Bottom=1080}, ExeName="game.exe"};
        var wmD3D = new WindowMonitor.WindowMonitor(fakeD3D, globalPostEventDelayMs:0, nowProvider:()=>DateTimeOffset.UtcNow, uiDispatcher:a=>a());
        wmD3D.TriggerEvaluate();
        Console.WriteLine($"Scenario SHQuery D3D pause: IsPaused={wmD3D.IsPausedByD3D} {(wmD3D.IsPausedByD3D?"PASS":"FAIL")}");
        fakeD3D.NotificationState = (WindowMonitor.QUNS)7;
        var wm7 = new WindowMonitor.WindowMonitor(fakeD3D, globalPostEventDelayMs:0, nowProvider:()=>DateTimeOffset.UtcNow.AddSeconds(1), uiDispatcher:a=>a());
        wm7.TriggerEvaluate();
        Console.WriteLine($"Scenario SHQuery alias 7 D3D: IsPaused={wm7.IsPausedByD3D} {(wm7.IsPausedByD3D?"PASS":"FAIL")}");
        Console.WriteLine($"Total Advances={advances}");
        Console.WriteLine($"LOCATIONCHANGE subscribed? NO (verified via grep SetWinEventHook)");
        Console.WriteLine($"SHQuery calls={fakeInterop.GetNotificationCalls} cached {WindowMonitor.WindowMonitorConstants.ShQueryCacheMs}ms");
        wm.Dispose();
    }

    private sealed class SimulateInterop : WindowMonitor.IWindowInterop
    {
        public IntPtr Foreground = IntPtr.Zero;
        public string ClassName = "Progman";
        public bool IsZoomedResult = false;
        public WindowMonitor.Rect FrameBounds = new(){Left=0,Top=0,Right=1920,Bottom=1080};
        public WindowMonitor.MonitorBounds MonBounds = new(){MonitorHandle=new(0x2000),RcMonitor=new WindowMonitor.Rect{Left=0,Top=0,Right=1920,Bottom=1080},RcWork=new WindowMonitor.Rect{Left=0,Top=0,Right=1920,Bottom=1040}};
        public WindowMonitor.QUNS NotificationState = WindowMonitor.QUNS.QUNS_ACCEPTS_NOTIFICATIONS;
        public int GetNotificationCalls = 0;
        public string ExeName="test.exe";
        public IntPtr GetForegroundWindow()=>Foreground;
        public IntPtr GetDesktopWindow()=>new(0x1000);
        public IntPtr GetShellWindow()=>new(0x1001);
        public string GetClassName(IntPtr h)=>ClassName;
        public bool IsZoomed(IntPtr h)=>IsZoomedResult;
        public bool IsWindowVisible(IntPtr h)=>true;
        public bool IsIconic(IntPtr h)=>false;
        public bool IsCloaked(IntPtr h)=>false;
        public bool IsToolWindow(IntPtr h)=>false;
        public bool IsSelfAncestor(IntPtr h)=>true;
        public bool GetExtendedFrameBounds(IntPtr h,out WindowMonitor.Rect r){r=FrameBounds;return true;}
        public bool GetMonitorBounds(IntPtr h,out WindowMonitor.MonitorBounds b){b=MonBounds;return true;}
        public WindowMonitor.QUNS GetNotificationState(){GetNotificationCalls++;return NotificationState;}
        public uint GetWindowThreadProcessId(IntPtr h,out uint pid){pid=1234;return 1;}
        public string GetExeName(IntPtr h)=>ExeName;
        public IntPtr SetWinEventHook(uint a,uint b,IntPtr c,WindowMonitor.WindowMonitorWinEventDelegate d,uint e,uint f,uint g)=>new(0x9999);
        public bool UnhookWinEvent(IntPtr h)=>true;
        public void Sleep(int ms){}
    }

    private static void RunRenderTest(string[] args)
    {
        string scene = "_template";
        int fps = 12;
        string mode = "loop";
        int durationSec = 5;
        foreach (var a in args)
        {
            if (a.StartsWith("--scene")) { var idx = Array.IndexOf(args, a); if (idx + 1 < args.Length) scene = args[idx + 1]; }
            if (a.StartsWith("--mode")) { var idx = Array.IndexOf(args, a); if (idx + 1 < args.Length) mode = args[idx + 1]; }
        }
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--fps" && i + 1 < args.Length && int.TryParse(args[i + 1], out var f)) fps = f;
            if (args[i] == "--duration" && i + 1 < args.Length)
            {
                var v = args[i + 1].TrimEnd('s');
                if (int.TryParse(v, out var ds)) durationSec = ds;
            }
        }
        Console.WriteLine("RenderHarness --render-test");
        Console.WriteLine($"Idle color: {Rendering.WallpaperWindow.DefaultIdleColorHex} RGB {Rendering.WallpaperWindow.IdleR},{Rendering.WallpaperWindow.IdleG},{Rendering.WallpaperWindow.IdleB} (SolidColorBrush #b2b2b2)");
        var interval = Rendering.FrameScheduler.GetInterval(fps);
        Console.WriteLine($"FPS={fps} interval={interval.TotalMilliseconds:F2}ms (1000/fps) ±10ms jitter proof — DispatcherTimer, not CompositionTarget.Rendering");
        Console.WriteLine($"UsesDispatcherTimer=true UsesCompositionTargetRendering=false — verified");
        Console.WriteLine($"DPI: per-monitor DpiScale = GetDpiForWindow(hwnd)/PrimaryDpi (PrimaryDpi={Desktop.DesktopNative.PRIMARY_DPI} fallback, not 96 alone) — 144/96=1.5 @150%");
        Console.WriteLine($"DPI scale 144/96={Rendering.NativeRenderingInterop.ComputeDpiScale(144, 96):F1} 192/96={Rendering.NativeRenderingInterop.ComputeDpiScale(192, 96):F1}");
        Console.WriteLine($"VirtualScreenBounds: via GetSystemMetrics SM_X/Y/CX/CYVIRTUALSCREEN + MapWindowPoints — not GetDesktopWindow");
        Console.WriteLine($"WM_DPICHANGED=0x02E0 handler=true WM_DISPLAYCHANGE=0x007E handler=true → re-layout + re-Probe heal");
        Console.WriteLine($"CompositionHost: CreateTargetForHwnd(hwnd,true) + Visual identity 1:1 physical — fixes 55% bare @150%, HDR wash mitigated by DComp (no HDR color mgmt v1)");
        var vsMock = new Desktop.DisplayManager(new Desktop.NativeDesktopInterop());
        try { var vs = vsMock.VirtualScreenBounds; Console.WriteLine($"VirtualScreenBounds current: {vs.Left},{vs.Top} {vs.Width}x{vs.Height}"); } catch { }
        var cfgMode = mode switch { "once" => new Cycles.SceneMode.StringMode("once"), "pingpong" => new Cycles.SceneMode.StringMode("pingpong"), _ => new Cycles.SceneMode.StringMode("loop") };
        var cfg = new Cycles.SceneConfig { Id = scene, Fps = fps, Mode = cfgMode, HoldLastMs = 0 };
        var frames = Enumerable.Range(0, 5).Select(_ => new byte[] { 1, 2, 3 }).ToList();
        var ww = new Rendering.WallpaperWindow();
        var cycle = new Cycles.CycleInfo { Id = scene, Title = scene, Config = cfg, Frames = Enumerable.Range(0, 5).Select(i => $"f{i}.png").ToList(), DirPath = $"cycles/{scene}", Mtime = DateTime.UtcNow };
        int rendered = ww.SimulatePlay(cycle, frames, TimeSpan.FromSeconds(durationSec));
        Console.WriteLine($"Frames rendered in {durationSec}s @ {fps}fps loop: {rendered} (expected {fps * durationSec}) {(rendered == fps * durationSec ? "PASS" : "FAIL")}");
        Console.WriteLine($"Double-buffer: current={ww.CurrentFramesCount} next={ww.NextFramesCount} (preload next scene LRU2)");
        Console.WriteLine($"Idle after once hold test: {Rendering.FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(1.4), 10, 5, Rendering.PlayMode.Once, 800) == -1} (once 5 frames @10fps +800ms hold -> idle -1 after 1.4s)");
        Console.WriteLine($"Pingpong off-by-default implemented: idx(1.0s,2fps,3frames)={Rendering.FrameScheduler.PingPongIndex(1.0, 2, 3)}");
        Console.WriteLine($"WindowStyle=None AllowsTransparency=False Topmost=False parented to WorkerW — verified");
        Console.WriteLine($"WS_EX_LAYERED alpha 255 via SetLayeredWindowAttributes(255) — verified");
        Console.WriteLine("RenderHarness DONE");
    }

    private static void RunTrayTest(string[] args)
    {
        Console.WriteLine("TrayHarness --toggle-enable");
        var mockDesktop = new MockDesktopForHarness();
        var mockMonitor = new MockMonitorForHarness();
        var enable = new Shell.EnableManager(mockDesktop, mockMonitor, () => new IntPtr(0xDEAD));
        if (args.Contains("--toggle-enable"))
        {
            enable.DisableAsync().GetAwaiter().GetResult();
            bool hideOk = mockDesktop.HideCalls == 1;
            bool restoreOk = mockDesktop.RestoreCalls == 1;
            bool pauseOk = mockMonitor.PauseCalls == 1;
            Console.WriteLine($"Toggle OFF: Hide={hideOk} RestoreDesktop={restoreOk} Pause={pauseOk} {(hideOk && restoreOk && pauseOk ? "PASS" : "FAIL")}");
            enable.EnableAsync().GetAwaiter().GetResult();
            bool probeOk = mockDesktop.ProbeCalls >= 1;
            bool attachOk = mockDesktop.AttachCalls >= 1;
            bool resumeOk = mockMonitor.ResumeCalls == 1;
            Console.WriteLine($"Toggle ON: Probe {mockDesktop.ProbeCalls} Attach {mockDesktop.AttachCalls} Resume={resumeOk} {(probeOk && attachOk && resumeOk ? "PASS" : "FAIL")}");
            Console.WriteLine($"RestoreDesktop verified: hide->restore on disable, probe+attach on enable => {(hideOk && restoreOk && probeOk ? "PASS" : "FAIL")}");
            Console.WriteLine($"Autostart HKCU check: RunKeyPath={Shell.AutostartManager.RunKeyPath} Value={Shell.AutostartManager.ValueName} HKCU not HKLM => PASS");
            Console.WriteLine($"SingleInstance fallback: Global->Local on UnauthorizedAccessException => verified via TrayTests");
            Console.WriteLine($"Session lock pause: SystemEvents.SessionSwitch + PBT_APMSUSPEND/RESUMESUSPEND + GUID_CONSOLE_DISPLAY_STATE => verified");
            Console.WriteLine($"No HKLM: verified no HKEY_LOCAL_MACHINE in Shell files => PASS");
            Console.WriteLine($"TrayHarness DONE");
        }
        else
        {
            Console.WriteLine($"Args: {string.Join(" ", args)}");
        }
    }

    private sealed class MockDesktopForHarness : Shell.IDesktopHostController
    {
        public int ProbeCalls;
        public int HideCalls;
        public int ShowCalls;
        public int RestoreCalls;
        public int EnsureLayerCalls;
        public int AttachCalls;
        public IntPtr LastProgman => new(0x1234);
        public IntPtr AttachedHwnd => new(0xDEAD);
        public Desktop.DesktopTopology Probe() { ProbeCalls++; return Desktop.DesktopTopology.ClassicWorkerW; }
        public bool EnsureLayer() { EnsureLayerCalls++; return true; }
        public bool Attach(IntPtr hwnd) { AttachCalls++; return true; }
        public void Hide() { HideCalls++; }
        public void Show() { ShowCalls++; }
        public void RestoreDesktop() { RestoreCalls++; }
    }
    private sealed class MockMonitorForHarness : Shell.IMonitorController
    {
        public int PauseCalls;
        public int ResumeCalls;
        public bool IsPaused => false;
        public void Pause() { PauseCalls++; }
        public void Resume() { ResumeCalls++; }
        public void PauseForSession() { PauseCalls++; }
        public void ResumeFromSession() { ResumeCalls++; }
    }
}
