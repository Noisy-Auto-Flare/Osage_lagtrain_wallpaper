using OsageLagtrain.App.Desktop;
using OsageLagtrain.App.WindowMonitor;

namespace OsageLagtrain.App.Shell;

public interface IDesktopHostController
{
    DesktopTopology Probe();
    bool EnsureLayer();
    bool Attach(IntPtr hwnd);
    void Hide();
    void Show();
    void RestoreDesktop();
    IntPtr AttachedHwnd { get; }
    IntPtr LastProgman { get; }
}

public interface IMonitorController
{
    void Pause();
    void Resume();
    void PauseForSession();
    void ResumeFromSession();
    bool IsPaused { get; }
}

internal sealed class DesktopHostAdapter : IDesktopHostController
{
    private readonly DesktopLayerHost _host;
    public DesktopHostAdapter(DesktopLayerHost host) => _host = host;
    public DesktopTopology Probe() => _host.Probe();
    public bool EnsureLayer() => _host.EnsureLayer();
    public bool Attach(IntPtr hwnd) => _host.Attach(hwnd);
    public void Hide() => _host.Hide();
    public void Show() => _host.Show();
    public void RestoreDesktop() => _host.RestoreDesktop();
    public IntPtr AttachedHwnd => _host.LastAttachedHwnd;
    public IntPtr LastProgman => _host.LastProgman;
}

internal sealed class MonitorAdapter : IMonitorController
{
    private readonly WindowMonitor.WindowMonitor _monitor;
    public MonitorAdapter(WindowMonitor.WindowMonitor m) => _monitor = m;
    public void Pause() => _monitor.Pause();
    public void Resume() => _monitor.Resume();
    public void PauseForSession() => _monitor.PauseForSession();
    public void ResumeFromSession() => _monitor.ResumeFromSession();
    public bool IsPaused => _monitor.IsPaused;
}

/// <summary>
/// EnableManager coordinates Enable toggle: false → Pause + Hide + RestoreDesktop; true → Probe+Attach+Resume with retry.
/// Checked state reflects live Enable.
/// </summary>
public sealed class EnableManager
{
    private readonly IDesktopHostController _desktop;
    private readonly IMonitorController _monitor;
    private readonly Func<IntPtr> _hwndProvider;
    private bool _isEnabled = true;

    public bool IsEnabled => _isEnabled;

    // For tests / manual harness verification
    public int LastProbeAttempts { get; private set; }
    public bool LastRestoreCalled { get; private set; }
    public bool LastHideCalled { get; private set; }

    public EnableManager(IDesktopHostController desktop, IMonitorController monitor, Func<IntPtr>? hwndProvider = null)
    {
        _desktop = desktop;
        _monitor = monitor;
        _hwndProvider = hwndProvider ?? (() => desktop.AttachedHwnd);
    }

    public EnableManager(DesktopLayerHost desktopHost, WindowMonitor.WindowMonitor monitor, Func<IntPtr>? hwndProvider = null)
        : this(new DesktopHostAdapter(desktopHost), new MonitorAdapter(monitor), hwndProvider)
    { }

    public void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled) return;
        _isEnabled = enabled;
        if (!enabled)
        {
            // Enable==false → Pause + Hide + RestoreDesktop to snapshot
            try { _monitor.Pause(); } catch { }
            try
            {
                _desktop.Hide();
                LastHideCalled = true;
            }
            catch { }
            try
            {
                _desktop.RestoreDesktop();
                LastRestoreCalled = true;
            }
            catch { }
        }
        else
        {
            // Enable==true → Probe()+Attach()+Resume with retry Probe
            LastRestoreCalled = false;
            LastHideCalled = false;
            int attempts = 0;
            try
            {
                // Retry Probe up to 20*300ms pattern is inside host EnsureLayer, but we do probe retry here for enable
                for (int i = 0; i < DesktopLayerHost.RetryCount; i++)
                {
                    attempts = i + 1;
                    var topo = _desktop.Probe();
                    // Try ensure layer then attach
                    try { _desktop.EnsureLayer(); } catch { }
                    var hwnd = _hwndProvider();
                    if (hwnd != IntPtr.Zero)
                    {
                        bool ok = _desktop.Attach(hwnd);
                        if (ok) break;
                    }
                    else
                    {
                        // No hwnd yet, just probe success enough to resume monitor
                        break;
                    }
                    // sleep between retries via interop sleep? Use Thread.Sleep directly for test verifiability
                    Thread.Sleep(DesktopLayerHost.RetryDelayMs);
                }
            }
            catch { }
            LastProbeAttempts = attempts;
            try
            {
                _desktop.Show();
            }
            catch { }
            try { _monitor.Resume(); } catch { }
        }
    }

    public void Enable() => SetEnabled(true);
    public void Disable() => SetEnabled(false);
    public void Toggle() => SetEnabled(!_isEnabled);

    // Session lock/display-off helpers
    public void OnSessionLock() => _monitor.PauseForSession();
    public void OnSessionUnlock() => _monitor.ResumeFromSession();
    public void OnDisplayOff() => _monitor.PauseForSession();
    public void OnDisplayOn() => _monitor.ResumeFromSession();
    public void OnSuspend() => _monitor.PauseForSession();
    public void OnResume() => _monitor.ResumeFromSession();
}
