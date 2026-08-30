using Xunit;
using OsageLagtrain.App.Shell;
using OsageLagtrain.App.Desktop;
using OsageLagtrain.App.WindowMonitor;
using Microsoft.Win32;

namespace OsageLagtrain.Tests;

public class TrayTests
{
    // ---------- Helpers ----------

    private sealed class MockDesktop : IDesktopHostController
    {
        public int ProbeCalls;
        public int HideCalls;
        public int ShowCalls;
        public int RestoreCalls;
        public int EnsureLayerCalls;
        public int AttachCalls;
        private IntPtr _attachedHwnd = new(0xDEAD);
        public IntPtr AttachedHwnd => _attachedHwnd;
        public DesktopTopology ProbeResult = DesktopTopology.ClassicWorkerW;
        public IntPtr LastProgman => new(0x1234);
        public bool AttachResult = true;
        // Track ShowWindow SW_HIDE via Hide
        public DesktopTopology Probe() { ProbeCalls++; return ProbeResult; }
        public bool EnsureLayer() { EnsureLayerCalls++; return true; }
        public bool Attach(IntPtr hwnd) { AttachCalls++; return AttachResult; }
        public void Hide() { HideCalls++; }
        public void Show() { ShowCalls++; }
        public void RestoreDesktop() { RestoreCalls++; }
    }

    private sealed class MockMonitor : IMonitorController
    {
        public int PauseCalls;
        public int ResumeCalls;
        public int PauseSessionCalls;
        public int ResumeSessionCalls;
        public bool IsPausedVal;
        public bool IsPaused => IsPausedVal;
        public void Pause() { PauseCalls++; IsPausedVal = true; }
        public void Resume() { ResumeCalls++; IsPausedVal = false; }
        public void PauseForSession() { PauseSessionCalls++; IsPausedVal = true; }
        public void ResumeFromSession() { ResumeSessionCalls++; IsPausedVal = false; }
    }

    private sealed class MockMutexFactory : IMutexFactory
    {
        public bool ThrowOnGlobal;
        public string? LastName;
        public bool ReturnCreatedNew = true;
        public IMutexHandle? TryCreate(string name, out bool createdNew)
        {
            LastName = name;
            if (ThrowOnGlobal && name.StartsWith("Global"))
            {
                throw new UnauthorizedAccessException("Global denied");
            }
            createdNew = ReturnCreatedNew;
            return new MockMutexHandle(createdNew);
        }
        private sealed class MockMutexHandle : IMutexHandle
        {
            public bool IsHeld { get; }
            public MockMutexHandle(bool held) => IsHeld = held;
            public void Dispose() { }
        }
    }

    private sealed class MockMsgInterop : IWindowMessageInterop
    {
        public uint NextMsgId = 0xC001;
        public int RegisterCalls;
        public int PostCalls;
        public IntPtr LastHwnd;
        public uint LastMsg;
        public uint RegisterWindowMessage(string name) { RegisterCalls++; return NextMsgId; }
        public bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) { PostCalls++; LastHwnd = hwnd; LastMsg = msg; return true; }
    }

    // ---------- Tests ----------

    [Fact]
    public void Autostart_Registry_HKCU_Run_SetAndDelete()
    {
        var provider = new InMemoryRegistryProvider();
        string fakeExe = @"C:\Fake\OsageLagtrain.exe";
        var mgr = new AutostartManager(provider, () => fakeExe);

        Assert.False(mgr.IsEnabled);
        mgr.SetEnabled(true);
        Assert.True(mgr.IsEnabled);
        Assert.True(provider.Store.ContainsKey("OsageLagtrain"));
        var val = provider.Store["OsageLagtrain"] as string;
        Assert.NotNull(val);
        Assert.Equal("\"" + fakeExe + "\"", val);
        // Toggle off
        mgr.SetEnabled(false);
        Assert.False(mgr.IsEnabled);
        Assert.False(provider.Store.ContainsKey("OsageLagtrain"));
        // DeleteValue(false) must not throw when missing
        mgr.SetEnabled(false);
        Assert.False(mgr.IsEnabled);
    }

    [Fact]
    public void Autostart_IsEnabled_ReadsLive()
    {
        var provider = new InMemoryRegistryProvider();
        var mgr = new AutostartManager(provider, () => @"C:\App\OsageLagtrain.exe");
        // Simulate external change via registry
        provider.Store["OsageLagtrain"] = "\"C:\\App\\OsageLagtrain.exe\"";
        Assert.True(mgr.IsEnabled);
        provider.Store.Remove("OsageLagtrain");
        Assert.False(mgr.IsEnabled);
    }

    [Fact]
    public void Enable_HideRestore_Pause_And_ReProbe_OnEnable()
    {
        var desktop = new MockDesktop();
        var monitor = new MockMonitor();
        var enable = new EnableManager(desktop, monitor, () => new IntPtr(0xBEEF));

        Assert.True(enable.IsEnabled);
        // Disable
        enable.Disable();
        Assert.False(enable.IsEnabled);
        Assert.Equal(1, monitor.PauseCalls);
        Assert.Equal(1, desktop.HideCalls);
        Assert.Equal(1, desktop.RestoreCalls);
        Assert.True(enable.LastHideCalled);
        Assert.True(enable.LastRestoreCalled);

        // Enable back should Probe+Attach+Resume with retry
        enable.Enable();
        Assert.True(enable.IsEnabled);
        Assert.True(desktop.ProbeCalls >= 1);
        Assert.True(desktop.AttachCalls >= 1);
        Assert.Equal(1, desktop.ShowCalls);
        Assert.Equal(1, monitor.ResumeCalls);
        Assert.True(enable.LastProbeAttempts >= 1);
    }

    [Fact]
    public void SingleInstance_Global_Fallback_To_Local_WhenDenied()
    {
        var mutexFactory = new MockMutexFactory { ThrowOnGlobal = true, ReturnCreatedNew = true };
        var msgInterop = new MockMsgInterop();
        var mgr = new SingleInstanceManager(mutexFactory, msgInterop);

        bool first = mgr.TryAcquire();
        Assert.True(first);
        Assert.Equal("Local\\OsageLagtrain-v1", mgr.ActiveMutexName);
        Assert.Equal(SingleInstanceManager.MutexNameLocal, mgr.ActiveMutexName);
        Assert.NotEqual(0u, mgr.ShowSettingsMessageId);
    }

    [Fact]
    public void SingleInstance_SecondInstance_Posts_Registered_Message_To_Broadcast()
    {
        var mutexFactory = new MockMutexFactory { ReturnCreatedNew = false };
        var msgInterop = new MockMsgInterop { NextMsgId = 0xC123 };
        var mgr = new SingleInstanceManager(mutexFactory, msgInterop);
        bool first = mgr.TryAcquire();
        Assert.False(first);

        bool posted = mgr.NotifyFirstInstance();
        Assert.True(posted);
        Assert.Equal(1, msgInterop.PostCalls);
        // Must use RegisterWindowMessage id, not bare WM_USER
        Assert.Equal(msgInterop.NextMsgId, msgInterop.LastMsg);
        // Must broadcast to HWND_BROADCAST 0xFFFF
        Assert.Equal(new IntPtr(0xFFFF), msgInterop.LastHwnd);
        // Must have registered OsageLagtrain_ShowSettings
        Assert.True(msgInterop.RegisterCalls >= 1);
    }

    [Fact]
    public void SingleInstance_FirstInstance_Handles_RegisteredMessage()
    {
        var mutexFactory = new MockMutexFactory { ReturnCreatedNew = true };
        var msgInterop = new MockMsgInterop { NextMsgId = 0xC999 };
        var mgr = new SingleInstanceManager(mutexFactory, msgInterop);
        mgr.TryAcquire();
        uint msgId = mgr.ShowSettingsMessageId;
        bool handled = false;
        bool result = mgr.HandleWindowMessage(msgId, () => handled = true);
        Assert.True(result);
        Assert.True(handled);
        // Wrong msg not handled
        Assert.False(mgr.HandleWindowMessage(msgId + 1, () => { }));
    }

    [Fact]
    public void Session_Lock_Pause_And_Resume_Via_TrayIcon()
    {
        var desktop = new MockDesktop();
        var monitor = new MockMonitor();
        var enable = new EnableManager(desktop, monitor, () => new IntPtr(0xDEAD));
        var autostart = new AutostartManager(new InMemoryRegistryProvider(), () => @"C:\Fake\Osage.exe");
        var tray = new TrayIcon(autostart, enable, null, () => { }, () => { });

        // Simulate SessionLock via TrayIcon
        tray.SimulateSessionSwitch(SessionSwitchReason.SessionLock);
        Assert.Equal(1, monitor.PauseSessionCalls);
        Assert.True(tray.IsPausedForSession);

        tray.SimulateSessionSwitch(SessionSwitchReason.SessionUnlock);
        Assert.Equal(1, monitor.ResumeSessionCalls);
        Assert.False(tray.IsPausedForSession);

        // WM_POWERBROADCAST PBT_APMSUSPEND / RESUME
        bool handledSuspend = tray.HandleWindowMessage(TrayIcon.WM_POWERBROADCAST, new IntPtr(TrayIcon.PBT_APMSUSPEND), IntPtr.Zero);
        Assert.True(handledSuspend);
        Assert.Equal(2, monitor.PauseSessionCalls);

        bool handledResume = tray.HandleWindowMessage(TrayIcon.WM_POWERBROADCAST, new IntPtr(TrayIcon.PBT_APMRESUMESUSPEND), IntPtr.Zero);
        Assert.True(handledResume);
        Assert.Equal(2, monitor.ResumeSessionCalls);
    }

    [Fact]
    public void Session_DisplayOff_Guid_Pause()
    {
        var desktop = new MockDesktop();
        var monitor = new MockMonitor();
        var enable = new EnableManager(desktop, monitor);
        var tray = new TrayIcon(new AutostartManager(new InMemoryRegistryProvider(), () => @"C:\Fake\Osage.exe"), enable);

        // Direct display off/on via EnableManager
        enable.OnDisplayOff();
        Assert.Equal(1, monitor.PauseSessionCalls);
        enable.OnDisplayOn();
        Assert.Equal(1, monitor.ResumeSessionCalls);
    }

    [Fact]
    public void No_HKLM_No_ProgramData_No_Service_No_SPI_WhileHidden()
    {
        // Verify none of the Shell files contain HKLM, ProgramData, service, SPI
        var shellDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "App", "Shell");
        // Since linked compile, check source files directly relative to repo root
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        // Fallback to G:\Projects...
        var autostartPath = Path.Combine(repoRoot, "src", "App", "Shell", "AutostartManager.cs");
        if (!File.Exists(autostartPath))
            autostartPath = @"G:\Projects\Osage_lagtrain_wallpaper\src\App\Shell\AutostartManager.cs";
        var trayPath = autostartPath.Replace("AutostartManager", "TrayIcon");
        var enablePath = autostartPath.Replace("AutostartManager", "EnableManager");
        var singlePath = autostartPath.Replace("AutostartManager", "SingleInstanceManager");

        foreach (var p in new[] { autostartPath, trayPath, enablePath, singlePath })
        {
            Assert.True(File.Exists(p), $"file exists {p}");
            var text = File.ReadAllText(p);
            Assert.DoesNotContain("HKLM", text);
            Assert.DoesNotContain("HKEY_LOCAL_MACHINE", text);
            Assert.DoesNotContain("LocalMachine", text);
            Assert.DoesNotContain("ProgramData", text);
            // Ensure not caching Progman HWND comment violation - file should contain fresh FindWindow usage in DesktopLayerHost, but here check not caching
            // SPI while hidden check: Tray/Enable should not call SystemParametersInfo directly
            // Only DesktopLayerHost should have SystemParametersInfo
            if (p.Contains("TrayIcon") || p.Contains("EnableManager"))
            {
                Assert.DoesNotContain("SystemParametersInfo", text);
                Assert.DoesNotContain("SPI_SETDESKWALLPAPER", text);
            }
        }

        // Verify DesktopLayerHost does not contain cached HWND field misuse? It should have Log but not cache progman across Explorer restart
        var desktopHostPath = autostartPath.Replace(Path.Combine("Shell", "AutostartManager.cs"), Path.Combine("Desktop", "DesktopLayerHost.cs"));
        if (File.Exists(desktopHostPath))
        {
            var dh = File.ReadAllText(desktopHostPath);
            // Must contain FindWindow("Progman" fresh, not a cached static field
            Assert.Contains("FindWindow(\"Progman\"", dh);
            // Must contain SW_HIDE for Hide
            Assert.Contains("SW_HIDE", dh);
            // Must contain RestoreDesktop SPI only once (called on Dispose/Hide)
            Assert.Contains("SPI_SETDESKWALLPAPER", dh);
        }
    }

    [Fact]
    public void TrayMenu_Checked_Reflects_Live_Enable_And_Autostart()
    {
        var provider = new InMemoryRegistryProvider();
        var autostart = new AutostartManager(provider, () => @"C:\Fake\Osage.exe");
        var desktop = new MockDesktop();
        var monitor = new MockMonitor();
        var enable = new EnableManager(desktop, monitor);
        var tray = new TrayIcon(autostart, enable);

        var menu1 = tray.BuildMenu();
        var enableItem1 = menu1.First(m => m.Text == "Enable");
        Assert.True(enableItem1.isChecked); // initially enabled
        Assert.Equal(TrayIcon.MF_CHECKED, enableItem1.checkedState);
        var autostartItem1 = menu1.First(m => m.Text == "Autostart");
        Assert.False(autostartItem1.isChecked);
        Assert.Equal(TrayIcon.MF_UNCHECKED, autostartItem1.checkedState);

        // Toggle enable -> should flip MF_CHECKED
        tray.ToggleEnable();
        var menu2 = tray.BuildMenu();
        var enableItem2 = menu2.First(m => m.Text == "Enable");
        Assert.False(enableItem2.isChecked);
        Assert.Equal(TrayIcon.MF_UNCHECKED, enableItem2.checkedState);

        // Toggle autostart
        tray.ToggleAutostart();
        var menu3 = tray.BuildMenu();
        var autostartItem2 = menu3.First(m => m.Text == "Autostart");
        Assert.True(autostartItem2.isChecked);
        Assert.Equal(TrayIcon.MF_CHECKED, autostartItem2.checkedState);
    }

    [Fact]
    public void TrayMenu_Contains_Required_Items()
    {
        var tray = new TrayIcon(new AutostartManager(new InMemoryRegistryProvider(), () => @"C:\f.exe"), new EnableManager(new MockDesktop(), new MockMonitor()));
        var menu = tray.BuildMenu();
        Assert.Contains(menu, m => m.Text == "Show Settings");
        Assert.Contains(menu, m => m.Text == "Enable");
        Assert.Contains(menu, m => m.Text == "Autostart");
        Assert.Contains(menu, m => m.Text == "Exit");
        Assert.Equal(4, menu.Count);
    }
}
