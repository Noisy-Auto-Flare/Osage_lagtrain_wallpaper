using OsageLagtrain.App.Desktop;
using OsageLagtrain.App.Shell;
using OsageLagtrain.App.Cycles;
using Xunit;

namespace OsageLagtrain.Tests;

public class ConfigStoreTests
{
    // Helpers
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "osage_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private sealed class MockWallpaper : IDesktopWallpaper
    {
        public List<string> MonitorIds { get; set; } = new() { "\\\\.\\DISPLAY1", "\\\\.\\DISPLAY2" };
        public Dictionary<string, string> Paths { get; } = new();
        public int GetWallpaperCalls = 0;
        public int SetWallpaperCalls = 0;
        public List<(string id, string path)> SetLog = new();

        public MockWallpaper()
        {
            Paths["\\\\.\\DISPLAY1"] = @"C:\Wallpapers\wall1.jpg";
            Paths["\\\\.\\DISPLAY2"] = @"C:\Wallpapers\wall2.jpg";
        }

        public IReadOnlyList<string> GetMonitorIds() => MonitorIds;
        public string GetWallpaper(string monitorId)
        {
            GetWallpaperCalls++;
            if (Paths.TryGetValue(monitorId, out var p)) return p;
            if (monitorId == string.Empty && Paths.Count > 0) return Paths.Values.First();
            return @"C:\Wallpapers\default.jpg";
        }
        public void SetWallpaper(string monitorId, string path)
        {
            SetWallpaperCalls++;
            SetLog.Add((monitorId, path));
        }
    }

    private sealed class MockInterop : IDesktopInterop
    {
        public int FindWindowCalls = 0;
        public int SystemParametersInfoCalls = 0;
        public IntPtr Progman = new(0x1111);
        public uint ExStyle = 0;
        public IntPtr FindWindow(string? className, string? windowName) { FindWindowCalls++; if (className == "Progman") return Progman; return IntPtr.Zero; }
        public IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName) => IntPtr.Zero;
        public nint GetWindowLongPtr(IntPtr hWnd, int nIndex) => (nint)ExStyle;
        public nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong) => dwNewLong;
        public IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result) { result = new(1); return new(1); }
        public bool SetParent(IntPtr child, IntPtr newParent) => true;
        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags) => true;
        public bool EnumWindows(EnumWindowsProc proc, IntPtr lParam) => true;
        public uint RegisterWindowMessage(string lpString) => 0xC123;
        public IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags) => new(0x9999);
        public bool UnhookWinEvent(IntPtr hWinEventHook) => true;
        public uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid) { pid = 1; return 1; }
        public bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags) => true;
        public bool GetWindowRect(IntPtr hWnd, out RECT rect) { rect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }; return true; }
        public int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT rect, uint cPoints) => 1;
        public int GetDpiForWindow(IntPtr hwnd) => 96;
        public int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY) { dpiX = 96; dpiY = 96; return 0; }
        public bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni) { SystemParametersInfoCalls++; return true; }
        public int GetSystemMetrics(int nIndex) => nIndex == DesktopNative.SM_CXVIRTUALSCREEN ? 1920 : nIndex == DesktopNative.SM_CYVIRTUALSCREEN ? 1080 : 1920;
        public void Sleep(int millisecondsTimeout) { }
        public IntPtr GetShellDefView() => IntPtr.Zero;
        public uint GetDpiForSystem() => 96;
        public IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags) => IntPtr.Zero;
        public bool ShowWindow(IntPtr hWnd, int nCmdShow) => true;
    }

    [Fact]
    public void Config_PortableProbe_WritableVsFallback_NoProgramFilesString()
    {
        // Check source files do NOT perform string check for Program Files (probe only)
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var cfgFile = Path.Combine(repoRoot, "src", "App", "Shell", "ConfigStore.cs");
        if (File.Exists(cfgFile))
        {
            var src = File.ReadAllText(cfgFile);
            Assert.DoesNotContain("\"Program Files\"", src);
            Assert.DoesNotContain("Contains(\"Program", src);
        }
        var histFile = Path.Combine(repoRoot, "src", "App", "Cycles", "HistoryStore.cs");
        if (File.Exists(histFile))
        {
            var src2 = File.ReadAllText(histFile);
            Assert.DoesNotContain("\"Program Files\"", src2);
            Assert.DoesNotContain("Contains(\"Program", src2);
        }
        var cycleFile = Path.Combine(repoRoot, "src", "App", "Cycles", "CycleStore.cs");
        if (File.Exists(cycleFile))
        {
            var src3 = File.ReadAllText(cycleFile);
            Assert.DoesNotContain("\"Program Files\"", src3);
        }

        // Writable probe returns exeDir
        var writable = TempDir();
        try
        {
            var dir = ConfigStore.GetStorageDir(writable);
            Assert.Equal(writable, dir);
            Assert.Equal(Path.Combine(writable, "settings.json"), ConfigStore.ResolveSettingsPath(writable));
            Assert.Equal(Path.Combine(writable, "history.json"), ConfigStore.ResolveHistoryPath(writable));
        }
        finally { try { Directory.Delete(writable, true); } catch { } }

        // Fallback when unwritable (non-existent nested path -> IOException -> fallback to AppData)
        var bogus = Path.Combine(Path.GetTempPath(), "osage_bogus_" + Guid.NewGuid().ToString("N"), "nonexistent_child");
        // Do NOT create directory -> probe File.Create will throw DirectoryNotFoundException (IOException) -> fallback
        var fallback = ConfigStore.GetStorageDir(bogus);
        var expectedFallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OsageLagtrain");
        Assert.Equal(expectedFallback, fallback);
        Assert.DoesNotContain("\"Program Files\"", fallback);
    }

    [Fact]
    public void Config_AtomicCrashMidWrite_ValidJson()
    {
        var tmpRoot = TempDir();
        try
        {
            var store = new ConfigStore(storageDirOverride: tmpRoot);
            // Write valid settings
            var cfg = new SettingsConfig { CyclesRoot = "./cycles", PostEventDelayMs = 500, SelectionPolicy = "randomNoRepeat", NoRepeatWindow = 3, IdleColor = "#b2b2b2" };
            store.SaveSettings(cfg);
            Assert.True(File.Exists(store.SettingsPath));
            var before = File.ReadAllText(store.SettingsPath);
            Assert.Contains("randomNoRepeat", before);

            // Simulate crash mid-write: WriteAllText to tmp then kill before Move/Replace
            var tmp = store.SettingsPath + ".tmp";
            File.WriteAllText(tmp, "{ incomplete json crash mid-write...");
            // At this point dest should still be valid previous json
            Assert.True(File.Exists(store.SettingsPath));
            var afterCrash = File.ReadAllText(store.SettingsPath);
            Assert.Equal(before, afterCrash);
            // Load should still succeed and ignore tmp
            var loaded = store.LoadSettings();
            Assert.Equal("randomNoRepeat", loaded.SelectionPolicy);
            // Corrupted tmp should not affect next Save — next Save should overwrite and cleanup
            File.Delete(tmp);
            var cfg2 = new SettingsConfig { CyclesRoot = "./cycles", PostEventDelayMs = 250, SelectionPolicy = "randomPure", NoRepeatWindow = 2, IdleColor = "#ffffff" };
            store.SaveSettings(cfg2);
            Assert.False(File.Exists(tmp));
            var loaded2 = store.LoadSettings();
            Assert.Equal("randomPure", loaded2.SelectionPolicy);

            // History crash scenario
            var hist = new History { Recent = new[] { "a", "b" }, MtimeCursor = "b" };
            store.SaveHistory(hist, 3);
            var histBefore = File.ReadAllText(store.HistoryPath);
            var histTmp = store.HistoryPath + ".tmp";
            File.WriteAllText(histTmp, "{ corrupted...");
            // dest still valid
            var histLoaded = store.LoadHistory();
            Assert.Equal(new[] { "a", "b" }, histLoaded.Recent);
            File.Delete(histTmp);

            // Corrupted history reset test
            File.WriteAllText(store.HistoryPath, "NOT JSON {{{");
            var corrupted = store.LoadHistory();
            Assert.Empty(corrupted.Recent);
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void Config_History_1KB_Trunc()
    {
        var tmpRoot = TempDir();
        try
        {
            var store = new ConfigStore(storageDirOverride: tmpRoot, historyMaxBytes: 1024);
            var large = new List<string>();
            for (int i = 0; i < 100; i++) large.Add("scene_" + i.ToString("D2") + "_very_long_name_to_exhaust_1kb_limit");
            var h = new History { Recent = large, MtimeCursor = null };
            store.SaveHistory(h, 20);
            var fi = new FileInfo(store.HistoryPath);
            Assert.True(fi.Exists);
            Assert.True(fi.Length <= 1024, $"history {fi.Length} >1024");
            var content = File.ReadAllText(store.HistoryPath);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(content) <= 1024);
            // Also verify atomic tmp not left
            Assert.False(File.Exists(store.HistoryPath + ".tmp"));
            // Verify truncated history still loads
            var loaded = store.LoadHistory();
            Assert.True(loaded.Recent.Count <= 20);
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void Config_RestoreDesktop_Snapshot_PerMonitor()
    {
        var tmpStatic = Path.Combine(Path.GetTempPath(), "osage_static_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpStatic);
        try
        {
            var mockWp = new MockWallpaper();
            var mockInterop = new MockInterop();
            var snap = new OriginalWallpaperSnapshot(mockWp, mockInterop, tmpStatic);

            // Capture on first Attach via snapshot
            snap.CaptureIfNeeded();
            Assert.True(File.Exists(snap.SnapshotTsvPath));
            Assert.True(File.Exists(snap.SnapshotTxtPath));
            var tsv = File.ReadAllText(snap.SnapshotTsvPath);
            Assert.Contains("DISPLAY1", tsv);
            Assert.Contains("wall1.jpg", tsv);
            Assert.Contains("DISPLAY2", tsv);
            Assert.Equal(2, mockWp.GetWallpaperCalls);

            // Second capture must be no-op (not overwrite)
            var beforeTsv = tsv;
            mockWp.Paths["\\\\.\\DISPLAY1"] = @"C:\Wallpapers\changed.jpg";
            snap.CaptureIfNeeded();
            Assert.Equal(beforeTsv, File.ReadAllText(snap.SnapshotTsvPath));

            // Restore should call SetWallpaper per-monitor
            mockWp.SetWallpaperCalls = 0;
            mockWp.SetLog.Clear();
            bool ok = snap.Restore();
            Assert.True(ok);
            Assert.Equal(2, mockWp.SetWallpaperCalls);
            Assert.Contains(mockWp.SetLog, x => x.path.Contains("wall1.jpg"));
            Assert.Contains(mockWp.SetLog, x => x.path.Contains("wall2.jpg"));

            // Verify DesktopLayerHost integration: Attach captures, RestoreDesktop uses snapshot, Dispose calls SPI
            var tmpStatic2 = Path.Combine(Path.GetTempPath(), "osage_static2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpStatic2);
            var mockWp2 = new MockWallpaper();
            var mockInterop2 = new MockInterop();
            var host = new DesktopLayerHost(mockInterop2, mockWp2, tmpStatic2);
            var hwnd = new IntPtr(0xDEAD);
            // Need to set style expectations for Attach
            mockInterop2.ExStyle = 0;
            // Mock that FindWindow returns Progman and WorkerW path will be used
            // For Attach classic, we need WorkerW enumeration — use default MockInterop's FindWindowEx behavior via host's interop is MockInterop which returns zero for WorkerW,
            // but OriginalWallpaperSnapshot still captures regardless of topology
            // Let's just call Capture via host
            host.Probe();
            // Ensure no snapshot yet
            Assert.False(File.Exists(Path.Combine(tmpStatic2, "original-wallpaper.tsv")));
            // Attach will trigger CaptureIfNeeded
            try { host.Attach(hwnd); } catch { }
            Assert.True(File.Exists(Path.Combine(tmpStatic2, "original-wallpaper.tsv")) || File.Exists(Path.Combine(tmpStatic2, "original-wallpaper.txt")));

            // RestoreDesktop should invoke SetWallpaper per-monitor
            mockWp2.SetLog.Clear();
            host.RestoreDesktop();
            Assert.True(mockWp2.SetWallpaperCalls >= 1);

            // Dispose should call SPI fallback
            int beforeSpi = mockInterop2.SystemParametersInfoCalls;
            host.Dispose();
            Assert.True(mockInterop2.SystemParametersInfoCalls > beforeSpi);
            try { Directory.Delete(tmpStatic2, true); } catch { }
        }
        finally { try { Directory.Delete(tmpStatic, true); } catch { } }
    }

    [Fact]
    public void Config_ReplaceExistsCheck_FirstCreateNoCrash()
    {
        var tmpRoot = TempDir();
        try
        {
            var store = new ConfigStore(storageDirOverride: tmpRoot);
            // Ensure file does NOT exist
            if (File.Exists(store.SettingsPath)) File.Delete(store.SettingsPath);
            Assert.False(File.Exists(store.SettingsPath));

            // First create — must use File.Move, not Replace (which would throw)
            var cfg = new SettingsConfig { CyclesRoot = "./cycles", SelectionPolicy = "randomNoRepeat", NoRepeatWindow = 3, IdleColor = "#b2b2b2" };
            var ex = Record.Exception(() => store.SaveSettings(cfg));
            Assert.Null(ex);
            Assert.True(File.Exists(store.SettingsPath));
            Assert.False(File.Exists(store.SettingsPath + ".tmp"));

            // Second save — now file Exists, should use Replace path and still succeed
            var cfg2 = new SettingsConfig { CyclesRoot = "./cycles2", SelectionPolicy = "randomPure", NoRepeatWindow = 1, IdleColor = "#ffffff" };
            ex = Record.Exception(() => store.SaveSettings(cfg2));
            Assert.Null(ex);
            Assert.True(File.Exists(store.SettingsPath));
            var loaded = store.LoadSettings();
            Assert.Equal("./cycles2", loaded.CyclesRoot);

            // History first-create similarly
            if (File.Exists(store.HistoryPath)) File.Delete(store.HistoryPath);
            ex = Record.Exception(() => store.SaveHistory(new History { Recent = new[] { "x" } }, 3));
            Assert.Null(ex);
            Assert.True(File.Exists(store.HistoryPath));
            ex = Record.Exception(() => store.SaveHistory(new History { Recent = new[] { "x", "y" } }, 3));
            Assert.Null(ex);

            // AppMap first-create
            if (File.Exists(store.AppMapPath)) File.Delete(store.AppMapPath);
            ex = Record.Exception(() => store.SaveAppMap(new Dictionary<string, string[]> { ["code.exe"] = new[] { "scene1" } }));
            Assert.Null(ex);
            Assert.True(File.Exists(store.AppMapPath));
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void Config_NoProgmanCache_FreshFindWindowEachProbe()
    {
        var mock = new MockInterop();
        var host = new DesktopLayerHost(mock, new MockWallpaper(), Path.Combine(Path.GetTempPath(), "osage_noCache_" + Guid.NewGuid().ToString("N")));
        host.Probe();
        int first = mock.FindWindowCalls;
        Assert.True(first >= 1);
        host.Probe();
        Assert.True(mock.FindWindowCalls > first);
        host.Probe();
        Assert.True(mock.FindWindowCalls > first + 1);

        // Verify source does not cache HWND without fresh FindWindow
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var hostFile = Path.Combine(repoRoot, "src", "App", "Desktop", "DesktopLayerHost.cs");
        if (File.Exists(hostFile))
        {
            var src = File.ReadAllText(hostFile);
            // Must contain fresh FindWindow calls in Probe and Attach
            Assert.Contains("FindWindow(\"Progman\"", src);
            Assert.DoesNotContain("\"Program Files\"", src);
        }
        var snapFile = Path.Combine(repoRoot, "src", "App", "Shell", "OriginalWallpaperSnapshot.cs");
        if (File.Exists(snapFile))
            Assert.DoesNotContain("\"Program Files\"", File.ReadAllText(snapFile));
    }
}
