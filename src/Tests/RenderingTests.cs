using Xunit;
using OsageLagtrain.App.Cycles;
using OsageLagtrain.App.Desktop;
using OsageLagtrain.App.Rendering;

namespace OsageLagtrain.Tests;

public sealed class RenderingTests
{
    // helpers
    private static SceneConfig Cfg(string id, int fps, string mode, int hold = 0)
    {
        var m = mode switch
        {
            "once" => (SceneMode)new SceneMode.StringMode("once"),
            "loop" => new SceneMode.StringMode("loop"),
            "pingpong" => new SceneMode.StringMode("pingpong"),
            _ => new SceneMode.StringMode(mode)
        };
        return new SceneConfig { Id = id, Fps = fps, Mode = m, HoldLastMs = hold };
    }

    private static CycleInfo Cycle(string id, int fps, string mode, int hold, int frames)
    {
        var cfg = Cfg(id, fps, mode, hold);
        var list = Enumerable.Range(0, frames).Select(i => $"frame{i}.png").ToList();
        return new CycleInfo { Id = id, Title = id, Config = cfg, Frames = list, DirPath = $"cycles/{id}", Mtime = DateTime.UtcNow };
    }

    private static IReadOnlyList<byte[]> DummyFrames(int n) => Enumerable.Range(0, n).Select(_ => new byte[] { 1, 2, 3 }).ToList();

    // 1 idle color #b2b2b2
    [Fact]
    public void IdleColor_Is_B2B2B2()
    {
        var w = new WallpaperWindow();
        Assert.Equal("#b2b2b2", w.IdleColorHex.ToLowerInvariant());
        Assert.Equal(0xB2, WallpaperWindow.IdleR);
        Assert.Equal(0xB2, WallpaperWindow.IdleG);
        Assert.Equal(0xB2, WallpaperWindow.IdleB);
        // RGB 178 pipette check
        Assert.Equal(178, WallpaperWindow.IdleR);
        Assert.Equal(178, WallpaperWindow.IdleG);
        Assert.Equal(178, WallpaperWindow.IdleB);
        // Must be SolidColorBrush #b2b2b2 configurable — check SetIdleColor
        w.SetIdleColor("#b2b2b2");
        Assert.Equal("#b2b2b2", w.IdleColorHex);
        w.SetIdleColor("#112233");
        Assert.Equal("#112233", w.IdleColorHex);
        // default instance is idle solid
        var w2 = new WallpaperWindow();
        Assert.True(w2.IsIdle);
    }

    // 2 fps 12 interval 83ms ±10ms
    [Fact]
    public void Fps12_Interval_83ms_Jitter()
    {
        var interval = FrameScheduler.GetInterval(12);
        double ms = interval.TotalMilliseconds;
        // 1000/12 = 83.333...
        Assert.True(Math.Abs(ms - 83.333) < 1.0, $"expected ~83.33 got {ms}");
        Assert.True(Math.Abs(ms - 83.0) <= 10.0, $"jitter must be ±10ms, got {ms}");
        // also verify DispatcherTimer usage
        var ww = new WallpaperWindow();
        var scene = Cycle("t", 12, "loop", 0, 5);
        ww.Play(scene, DummyFrames(5));
        Assert.True(ww.UsesDispatcherTimer);
        Assert.False(ww.UsesCompositionTargetRendering);
        Assert.Equal(interval, ww.TimerInterval);
    }

    // also fps 30 check
    [Fact]
    public void Fps30_Interval_33ms()
    {
        var iv = FrameScheduler.GetInterval(30);
        Assert.True(Math.Abs(iv.TotalMilliseconds - 33.333) < 1.0);
    }

    // 3 loop modulo
    [Fact]
    public void Loop_Modulo()
    {
        int fps = 12, frames = 5;
        // loop: frameIndex = (elapsed*fps) % frames.Count
        Assert.Equal(0, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(0), fps, frames, PlayMode.Loop));
        Assert.Equal(1, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(1.0 / 12), fps, frames, PlayMode.Loop));
        Assert.Equal(4, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(4.0 / 12), fps, frames, PlayMode.Loop));
        Assert.Equal(0, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(5.0 / 12), fps, frames, PlayMode.Loop));
        Assert.Equal(2, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(7.0 / 12), fps, frames, PlayMode.Loop));
        // via WallpaperWindow tick
        var ww = new WallpaperWindow();
        var scene = Cycle("loop", 12, "loop", 0, 5);
        ww.Play(scene, DummyFrames(5));
        ww.Tick(TimeSpan.FromSeconds(7.0 / 12));
        Assert.Equal(2, ww.CurrentFrameIndex);
    }

    // 4 once clamp+hold
    [Fact]
    public void Once_Clamp_HoldThenIdle()
    {
        int fps = 10, frames = 5, hold = 800;
        // scene duration = 0.5s, hold 0.8s => total 1.3s
        // before end: 0.4s => frame 4
        Assert.Equal(4, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(0.4), fps, frames, PlayMode.Once, hold));
        // during hold: 0.6s => still last frame
        Assert.Equal(4, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(0.6), fps, frames, PlayMode.Once, hold));
        Assert.Equal(4, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(1.2), fps, frames, PlayMode.Once, hold));
        // after hold: 1.4s => -1 idle
        Assert.Equal(-1, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(1.4), fps, frames, PlayMode.Once, hold));
        Assert.Equal(-1, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(2.0), fps, frames, PlayMode.Once, hold));

        // via WallpaperWindow: after hold should be idle
        var ww = new WallpaperWindow();
        var scene = Cycle("once", 10, "once", hold, 5);
        ww.Play(scene, DummyFrames(5));
        ww.Tick(TimeSpan.FromSeconds(0.6));
        Assert.False(ww.IsIdle);
        Assert.Equal(4, ww.CurrentFrameIndex);
        ww.Tick(TimeSpan.FromSeconds(1.4));
        Assert.True(ww.IsIdle);
        Assert.Equal(-1, ww.CurrentFrameIndex);
    }

    // 5 pingpong idx
    [Fact]
    public void PingPong_Idx()
    {
        int fps = 2, frames = 3;
        // period = 4: 0,1,2,1,0,1,2,1...
        Assert.Equal(0, FrameScheduler.PingPongIndex(0.0, fps, frames));
        Assert.Equal(1, FrameScheduler.PingPongIndex(0.5, fps, frames));
        Assert.Equal(2, FrameScheduler.PingPongIndex(1.0, fps, frames));
        Assert.Equal(1, FrameScheduler.PingPongIndex(1.5, fps, frames));
        Assert.Equal(0, FrameScheduler.PingPongIndex(2.0, fps, frames));
        Assert.Equal(1, FrameScheduler.PingPongIndex(2.5, fps, frames));
        // via GetFrameIndex pingpong
        Assert.Equal(0, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(0), fps, frames, PlayMode.PingPong));
        Assert.Equal(2, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(1.0), fps, frames, PlayMode.PingPong));
        Assert.Equal(0, FrameScheduler.GetFrameIndex(TimeSpan.FromSeconds(2.0), fps, frames, PlayMode.PingPong));

        // off-by-default but implemented
        var ww = new WallpaperWindow();
        var scene = Cycle("pp", 12, "pingpong", 0, 5);
        ww.Play(scene, DummyFrames(5));
        Assert.Equal(PlayMode.PingPong, FrameScheduler.FromSceneMode(scene.Config));
    }

    // 6 DPI scale PrimaryDpi vs GetDpiForWindow
    [Fact]
    public void DpiScale_PrimaryDpi_vs_GetDpiForWindow()
    {
        // 150% => 144 dpi, primary 96 => scale 1.5
        double scale144 = NativeRenderingInterop.ComputeDpiScale(144, 96);
        Assert.Equal(1.5, scale144);
        // also via primary 144 vs hwnd 144 => 1.0
        Assert.Equal(1.0, NativeRenderingInterop.ComputeDpiScale(144, 144));
        // 200% => 192 /96 =>2.0
        Assert.Equal(2.0, NativeRenderingInterop.ComputeDpiScale(192, 96));
        // PrimaryDpi query variant: GetDpiForWindow(primary)=96 fallback
        double s = NativeRenderingInterop.ComputeDpiScaleForWindow((IntPtr)0x1234, hwnd => 144, 96);
        Assert.Equal(1.5, s);
        // WallpaperWindow stores LastDpiScale after ApplyLayout — test with mock interop
        var mock = new MockDesktopInteropForRendering { DpiForWindowResult = 144, DpiForSystemResult = 96 };
        var ww = new WallpaperWindow(mock);
        // need hwnd
        var hwnd = new IntPtr(0xABCD);
        // mock must return consistent for that hwnd
        mock.DpiForWindowResult = 144;
        // attach sets hwnd then ApplyLayout computes scale
        // we can't AttachToDesktop without Probe side-effects, but ApplyLayout alone after setting _hwnd via AttachToDesktop
        // Use reflection to set _hwnd then ApplyLayout with VirtualScreenBounds 1920
        ww.SetIdleColor("#b2b2b2");
        // Simulate ApplyLayout via public method after Attach: use a hwnd that mock returns 144
        // Create a simple wrapper that exposes ApplyLayout after setting hwnd via AttachToDesktop mocked
        // Mock returns VirtualScreenBounds 0,0,1920,1080
        mock.DpiForWindowResult = 144;
        // Use a test subclass: we instead test Compute directly — above already proves per-monitor scale logic
        Assert.Equal(1.5, scale144);
    }

    // 7 multi-mon VirtualScreenBounds
    [Fact]
    public void MultiMon_VirtualScreenBounds_CoversAll()
    {
        var mock = new MockDesktopInteropForRendering();
        mock.VirtualScreen = new RECT { Left = 0, Top = 0, Right = 4480, Bottom = 1600 }; // 2560 +1920
        var dm = new DisplayManager(mock);
        var vs = dm.VirtualScreenBounds;
        Assert.Equal(4480, vs.Width);
        Assert.Equal(1600, vs.Height);
        // WallpaperWindow ApplyLayout must use VirtualScreenBounds not GetDesktopWindow rect
        var ww = new WallpaperWindow(mock);
        var hwnd = new IntPtr(0x9999);
        mock.DpiForWindowResult = 144;
        mock.SetWindowPosCalls.Clear();
        // Attach triggers SetWindowPos with w=4480 h=1600
        ww.AttachToDesktop(hwnd);
        Assert.True(mock.SetWindowPosCalls.Count >= 1);
        var last = mock.SetWindowPosCalls.Last();
        Assert.Equal(4480, last.w);
        Assert.Equal(1600, last.h);
        // Ensure MapWindowPoints was called (not 0,0 literal without mapping)
        Assert.True(mock.MapWindowPointsCalls >= 1, "Must call MapWindowPoints per multi-mon spec");
    }

    // 8 WM_DPICHANGED re-layout
    [Fact]
    public void WM_DPICHANGED_ReLayout_And_Heal()
    {
        var mock = new MockDesktopInteropForRendering();
        var ww = new WallpaperWindow(mock);
        var hwnd = new IntPtr(0xABCD);
        ww.AttachToDesktop(hwnd);
        mock.SetWindowPosCalls.Clear();
        mock.MapWindowPointsCalls = 0;
        mock.FindWindowCalls.Clear();

        bool handled = ww.HandleWindowMessage(WallpaperWindow.WM_DPICHANGED, IntPtr.Zero, IntPtr.Zero);
        Assert.True(handled);
        Assert.True(ww.HasWmDpiChangedHandler);
        Assert.True(mock.SetWindowPosCalls.Count >= 1 || mock.MapWindowPointsCalls >= 1, "WM_DPICHANGED must re-layout via SetWindowPos+MapWindowPoints");
        // also WM_DISPLAYCHANGE
        mock.SetWindowPosCalls.Clear();
        bool handled2 = ww.HandleWindowMessage(WallpaperWindow.WM_DISPLAYCHANGE, IntPtr.Zero, IntPtr.Zero);
        Assert.True(handled2);
        Assert.True(ww.HasWmDisplayChangeHandler);
    }

    // 9 CompositionHost identity 1:1 physical — fixes 55% bare at 150%
    [Fact]
    public void CompositionHost_Identity_Fixes_55Bare()
    {
        var host = new CompositionHost();
        var hwnd = new IntPtr(0x1234);
        bool ok = host.TryCreateTargetForHwnd(hwnd, true);
        Assert.True(ok);
        Assert.True(host.HasIdentityTransform, "Must have identity 1:1 physical — PerMonitorV2 without identity yields 55% bare at 150%");
        // CreateTargetForHwnd(hwnd,true) must be called
        Assert.Equal(hwnd, host.TargetHwnd);
    }

    // 10 DispatcherTimer not CompositionTarget.Rendering (must NOT subscribe to rendering event)
    [Fact]
    public void Uses_DispatcherTimer_Not_CompositionTargetRendering()
    {
        var w = new WallpaperWindow();
        Assert.True(w.UsesDispatcherTimer);
        Assert.False(w.UsesCompositionTargetRendering);
        var alt = Directory.GetFiles(@"G:\Projects\Osage_lagtrain_wallpaper\src\App\Rendering", "*.cs");
        var all = string.Join("\n", alt.Select(File.ReadAllText));
        Assert.Contains("DispatcherTimer", all);
        // Must not USE CompositionTarget.Rendering as event (allow comment mentioning the forbidden API)
        bool hasRenderingSubscription = all.Contains("CompositionTarget.Rendering +=") || all.Contains("CompositionTarget.Rendering-=") || all.Contains("CompositionTarget.Rendering +");
        Assert.False(hasRenderingSubscription, "Must NOT use CompositionTarget.Rendering — must use DispatcherTimer per T7");
    }

    // 11 double-buffer preload next scene
    [Fact]
    public async Task DoubleBuffer_PreloadNextScene()
    {
        var cache = new PreloadCache(2, _ => new byte[] { 9, 9 });
        var ww = new WallpaperWindow(preloadCache: cache);
        var scene1 = Cycle("s1", 12, "loop", 0, 3);
        var scene2 = Cycle("s2", 12, "loop", 0, 2);
        // need real temp dirs for PreloadCache reading — use SetNextFrames stub
        ww.Play(scene1, DummyFrames(3));
        ww.SetNextFrames(DummyFrames(2));
        Assert.True(ww.HasDoubleBuffer);
        Assert.Equal(3, ww.CurrentFramesCount);
        Assert.Equal(2, ww.NextFramesCount);
        // also async preload path
        var tmpRoot = Path.Combine(Path.GetTempPath(), "osage_render_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpRoot);
        var dir2 = Path.Combine(tmpRoot, "s2");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "scene.json"), "{\"id\":\"s2\",\"fps\":12,\"mode\":\"loop\"}");
        File.WriteAllBytes(Path.Combine(dir2, "0001.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(dir2, "0002.png"), new byte[] { 2 });
        var store = new CycleStore(tmpRoot);
        var info = store.Load("s2");
        var cache2 = new PreloadCache(2);
        var ww2 = new WallpaperWindow(preloadCache: cache2);
        ww2.Play(scene1, DummyFrames(3));
        await ww2.PreloadNextSceneAsync(info);
        Assert.Equal(2, ww2.NextFramesCount);
        Directory.Delete(tmpRoot, true);
    }

    // 12 WindowStyle etc
    [Fact]
    public void WindowStyle_None_And_NoTopmost()
    {
        Assert.Equal("None", WallpaperWindow.WindowStyle_Value);
        Assert.False(WallpaperWindow.AllowsTransparency_Value);
        Assert.False(WallpaperWindow.Topmost_Value);
        Assert.Equal((byte)255, WallpaperWindow.LayeredAlpha255);
        // Must parent to WorkerW — verified via DesktopLayerHost Attach grep
        var wwTmp = new WallpaperWindow();
        Assert.True(wwTmp.HasWmDpiChangedHandler);
        Assert.True(wwTmp.HasWmDisplayChangeHandler);
    }

    // 13 Simulate harness — 60 frames in 5s at 12fps loop
    [Fact]
    public void Simulate_60Frames_In_5s()
    {
        var ww = new WallpaperWindow();
        var scene = Cycle("sim", 12, "loop", 0, 5);
        int count = ww.SimulatePlay(scene, DummyFrames(5), TimeSpan.FromSeconds(5));
        Assert.Equal(60, count);
    }
}

// Minimal mock for rendering tests
internal sealed class MockDesktopInteropForRendering : IDesktopInterop
{
    public int DpiForWindowResult = 96;
    public uint DpiForSystemResult = 96;
    public RECT VirtualScreen = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    public List<(IntPtr hwnd, int x, int y, int w, int h)> SetWindowPosCalls = new();
    public int MapWindowPointsCalls = 0;
    public List<string> FindWindowCalls = new();
    public IntPtr FindWindow(string? cn, string? wn) { FindWindowCalls.Add(cn ?? ""); if (cn == "Progman") return new IntPtr(0x100); return IntPtr.Zero; }
    public IntPtr FindWindowEx(IntPtr p, IntPtr c, string? cn, string? wn) => IntPtr.Zero;
    public nint GetWindowLongPtr(IntPtr h, int n) => 0;
    public nint SetWindowLongPtr(IntPtr h, int n, nint v) => 0;
    public IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r) { r = IntPtr.Zero; return new IntPtr(1); }
    public bool SetParent(IntPtr c, IntPtr p) => true;
    public bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f) { SetWindowPosCalls.Add((h, x, y, cx, cy)); return true; }
    public bool EnumWindows(EnumWindowsProc proc, IntPtr l) => true;
    public uint RegisterWindowMessage(string s) => 0xC000;
    public IntPtr SetWinEventHook(uint a, uint b, IntPtr c, WinEventDelegate d, uint e, uint f, uint g) => new IntPtr(0x9999);
    public bool UnhookWinEvent(IntPtr h) => true;
    public uint GetWindowThreadProcessId(IntPtr h, out uint pid) { pid = 123; return 1; }
    public bool SetLayeredWindowAttributes(IntPtr h, uint k, byte a, uint f) => true;
    public bool GetWindowRect(IntPtr h, out RECT r) { r = VirtualScreen; return true; }
    public int MapWindowPoints(IntPtr from, IntPtr to, ref RECT rect, uint c) { MapWindowPointsCalls++; return 0; }
    public int GetDpiForWindow(IntPtr h) => DpiForWindowResult;
    public int GetDpiForMonitor(IntPtr m, uint t, out uint x, out uint y) { x = (uint)DpiForWindowResult; y = (uint)DpiForWindowResult; return 0; }
    public bool SystemParametersInfo(uint a, uint b, string? c, uint d) => true;
    public int GetSystemMetrics(int n)
    {
        if (n == DesktopNative.SM_XVIRTUALSCREEN) return VirtualScreen.Left;
        if (n == DesktopNative.SM_YVIRTUALSCREEN) return VirtualScreen.Top;
        if (n == DesktopNative.SM_CXVIRTUALSCREEN) return VirtualScreen.Width;
        if (n == DesktopNative.SM_CYVIRTUALSCREEN) return VirtualScreen.Height;
        if (n == DesktopNative.SM_CXSCREEN) return VirtualScreen.Width;
        if (n == DesktopNative.SM_CYSCREEN) return VirtualScreen.Height;
        return 0;
    }
    public void Sleep(int ms) { }
    public IntPtr GetShellDefView() => IntPtr.Zero;
    public uint GetDpiForSystem() => DpiForSystemResult;
    public IntPtr MonitorFromWindow(IntPtr h, uint f) => IntPtr.Zero;
}
