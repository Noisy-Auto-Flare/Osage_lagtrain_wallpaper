using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OsageLagtrain.App.Shell;

/// <summary>
/// TrayIcon with menu: Show Settings, Enable [checked], Autostart [checked], Exit.
/// Uses NotifyIcon/TaskbarIcon abstraction; menu checked reflects live Registry/Enable.
/// Handles SessionSwitch + WM_POWERBROADCAST + console display state.
/// SingleInstance broadcast handled via SingleInstanceManager.HandleWindowMessage.
/// Must NOT call SPI while hidden except on Exit; must NOT cache Progman HWND.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    // Win32 constants for tray checked handling (MF_CHECKED)
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_UNCHECKED = 0x00000000;

    // Power broadcast constants
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMSUSPEND = 0x0004;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new(0x6FE69556, 0x704A, 0x47A0, 0x8F, 0x24, 0xC2, 0x8D, 0x93, 0x6F, 0xDA, 0x47);
    public const int GUID_CONSOLE_DISPLAY_STATE_OFF = 0x0;
    public const int GUID_CONSOLE_DISPLAY_STATE_ON = 0x1;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;

    private readonly AutostartManager _autostart;
    private readonly EnableManager _enable;
    private readonly SingleInstanceManager? _singleInstance;
    private readonly Action? _showSettings;
    private readonly Action? _exitAction;
    private bool _disposed;

    // Menu state - for NotifyIcon ContextMenuStrip.Checked
    public bool IsAutostartChecked => _autostart.IsEnabled;
    public bool IsEnableChecked => _enable.IsEnabled;

    // For WndProc routing
    public uint ShowSettingsMessageId => _singleInstance?.ShowSettingsMessageId ?? 0;

    // Event for tests to verify pause/resume
    public bool IsPausedForSession { get; private set; }

    public TrayIcon(AutostartManager? autostart = null, EnableManager? enable = null, SingleInstanceManager? singleInstance = null, Action? showSettings = null, Action? exitAction = null)
    {
        _autostart = autostart ?? new AutostartManager();
        // enable must be provided or create dummy that does nothing
        if (enable != null)
        {
            _enable = enable;
        }
        else
        {
            // Create dummy desktop/monitor for standalone use
            var dummyDesktop = new DummyDesktopHost();
            var dummyMonitor = new DummyMonitor();
            _enable = new EnableManager(dummyDesktop, dummyMonitor);
        }
        _singleInstance = singleInstance;
        _showSettings = showSettings;
        _exitAction = exitAction;

        // Subscribe to session switch
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch { }
    }

    public IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        // Live values
        var autostartChecked = _autostart.IsEnabled;
        var enableChecked = _enable.IsEnabled;
        return new List<TrayMenuItem>
        {
            new("Show Settings", onClick: () => _showSettings?.Invoke(), isChecked: false),
            new("Enable", onClick: () => ToggleEnable(), isChecked: enableChecked, checkedState: enableChecked ? MF_CHECKED : MF_UNCHECKED),
            new("Autostart", onClick: () => ToggleAutostart(), isChecked: autostartChecked, checkedState: autostartChecked ? MF_CHECKED : MF_UNCHECKED),
            new("Exit", onClick: () => Exit(), isChecked: false),
        };
    }

    public void ToggleEnable()
    {
        _enable.Toggle();
        // MF_CHECKED updated via IsEnableChecked live read on next BuildMenu
    }

    public void ToggleAutostart()
    {
        bool cur = _autostart.IsEnabled;
        _autostart.SetEnabled(!cur);
        // MF_CHECKED updated via IsAutostartChecked live read
    }

    public void ShowSettings() => _showSettings?.Invoke();

    public void Exit()
    {
        try { _exitAction?.Invoke(); } catch { }
        Dispose();
    }

    // SessionSwitch handler
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                _enable.OnSessionLock();
                IsPausedForSession = true;
                break;
            case SessionSwitchReason.SessionUnlock:
                _enable.OnSessionUnlock();
                IsPausedForSession = false;
                break;
        }
    }

    // Test helper to simulate SessionSwitch without real SystemEvents
    public void SimulateSessionSwitch(SessionSwitchReason reason) => OnSessionSwitch(this, new SessionSwitchEventArgs(reason));

    /// <summary>
    /// WndProc handler for WM_POWERBROADCAST etc. Returns true if handled.
    /// Must handle PBT_APMSUSPEND / PBT_APMRESUMESUSPEND and GUID_CONSOLE_DISPLAY_STATE via PBT_POWERSETTINGCHANGE.
    /// </summary>
    public bool HandleWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        // SingleInstance first
        if (_singleInstance != null && _singleInstance.HandleWindowMessage(msg, _showSettings))
            return true;

        if (msg == WM_POWERBROADCAST)
        {
            int pbt = wParam.ToInt32();
            if (pbt == PBT_APMSUSPEND)
            {
                _enable.OnSuspend();
                IsPausedForSession = true;
                return true;
            }
            if (pbt == PBT_APMRESUMESUSPEND)
            {
                _enable.OnResume();
                IsPausedForSession = false;
                return true;
            }
            if (pbt == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                try
                {
                    var ps = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                    if (ps.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                    {
                        int state = Marshal.ReadInt32(ps.Data);
                        if (state == GUID_CONSOLE_DISPLAY_STATE_OFF)
                        {
                            _enable.OnDisplayOff();
                            IsPausedForSession = true;
                        }
                        else if (state == GUID_CONSOLE_DISPLAY_STATE_ON)
                        {
                            _enable.OnDisplayOn();
                            IsPausedForSession = false;
                        }
                        return true;
                    }
                }
                catch { }
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        // Only place where SPI may be called on Exit is via EnableManager/Desktop dispose; Hide path already handles.
        // Ensure we do not call SPI while hidden repeatedly.
    }

    // Dummy hosts for default ctor
    private sealed class DummyDesktopHost : IDesktopHostController
    {
        public Desktop.DesktopTopology Probe() => Desktop.DesktopTopology.ClassicWorkerW;
        public bool EnsureLayer() => true;
        public bool Attach(IntPtr hwnd) => true;
        public void Hide() { }
        public void Show() { }
        public void RestoreDesktop() { }
        public IntPtr AttachedHwnd => IntPtr.Zero;
        public IntPtr LastProgman => IntPtr.Zero;
    }
    private sealed class DummyMonitor : IMonitorController
    {
        public void Pause() { }
        public void Resume() { }
        public void PauseForSession() { }
        public void ResumeFromSession() { }
        public bool IsPaused => false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public IntPtr Data;
    }
}

public sealed record TrayMenuItem(string Text, Action? onClick, bool isChecked = false, uint checkedState = 0)
{
    public uint MfChecked => isChecked ? TrayIcon.MF_CHECKED : TrayIcon.MF_UNCHECKED;
    public void Click() => onClick?.Invoke();
}
