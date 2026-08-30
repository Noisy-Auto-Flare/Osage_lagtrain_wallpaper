using System.Runtime.InteropServices;

namespace OsageLagtrain.App.Desktop;

public sealed partial class DesktopLayerHost
{
    private void SetupHealing()
    {
        try
        {
            _taskbarCreatedMsg = _interop.RegisterWindowMessage("TaskbarCreated");
            Log($"Healing: RegisterWindowMessage TaskbarCreated => 0x{_taskbarCreatedMsg:X}");
            var progman = _interop.FindWindow("Progman", null);
            _progmanForHook = progman;
            uint pid = 0;
            if (progman != IntPtr.Zero)
                _interop.GetWindowThreadProcessId(progman, out pid);
            _workerWProcessId = pid;
            _winEventDelegate = OnWinEvent;
            _winEventGCHandle = GCHandle.Alloc(_winEventDelegate);
            uint flags = DesktopNative.WINEVENT_OUTOFCONTEXT | DesktopNative.WINEVENT_SKIPOWNPROCESS;
            _winEventHook = _interop.SetWinEventHook(DesktopNative.EVENT_OBJECT_DESTROY, DesktopNative.EVENT_OBJECT_DESTROY, IntPtr.Zero, _winEventDelegate, pid, 0, flags);
            Log($"Healing: SetWinEventHook EVENT_OBJECT_DESTROY pid={pid} flags=0x{flags:X} hook=0x{_winEventHook.ToInt64():X}");
        }
        catch (Exception ex)
        {
            Log($"Healing setup failed: {ex.Message}");
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == DesktopNative.EVENT_OBJECT_DESTROY)
        {
            Log($"Healing: EVENT_OBJECT_DESTROY hwnd=0x{hwnd.ToInt64():X}");
            HandleHealingTrigger("EVENT_OBJECT_DESTROY");
        }
    }

    public void HandleHealingTrigger(string reason)
    {
        Log($"Healing trigger: {reason} -> re-probe");
        for (int i = 0; i < RetryCount; i++)
        {
            var t = Probe();
            bool layerOk = EnsureLayer();
            if (layerOk)
            {
                Log($"Healing: re-probe success on attempt {i + 1} topology={t}");
                if (_attachedHwnd != IntPtr.Zero)
                {
                    if (t == DesktopTopology.RaisedDesktop)
                        AttachRaised(_attachedHwnd);
                    else
                        AttachClassic(_attachedHwnd);
                }
                break;
            }
            _interop.Sleep(RetryDelayMs);
        }
    }

    public void OnTaskbarCreated() => HandleHealingTrigger("WM_TASKBARCREATED");
    public void OnDisplayChanged() => HandleHealingTrigger("WM_DISPLAYCHANGE");
    public void OnDpiChanged() => HandleHealingTrigger("WM_DPICHANGED");
    public void OnSessionUnlock() => HandleHealingTrigger("WTS_SESSION_UNLOCK");

    public uint TaskbarCreatedMessage => _taskbarCreatedMsg;
    public IntPtr WinEventHookHandle => _winEventHook;

    public void Hide()
    {
        try
        {
            if (_attachedHwnd != IntPtr.Zero)
            {
                _interop.ShowWindow(_attachedHwnd, DesktopNative.SW_HIDE);
                Log($"Hide: ShowWindow SW_HIDE hwnd=0x{_attachedHwnd.ToInt64():X}");
            }
            else
            {
                var workerW = FindWorkerW();
                if (workerW != IntPtr.Zero)
                {
                    _interop.ShowWindow(workerW, DesktopNative.SW_HIDE);
                    Log($"Hide: ShowWindow SW_HIDE WorkerW=0x{workerW.ToInt64():X}");
                }
            }
        }
        catch (Exception ex) { Log($"Hide failed: {ex.Message}"); }
    }

    public void Show()
    {
        try
        {
            if (_attachedHwnd != IntPtr.Zero)
            {
                _interop.ShowWindow(_attachedHwnd, DesktopNative.SW_SHOW);
                Log($"Show: ShowWindow SW_SHOW hwnd=0x{_attachedHwnd.ToInt64():X}");
            }
        }
        catch (Exception ex) { Log($"Show failed: {ex.Message}"); }
    }

    public void RestoreDesktop()
    {
        try
        {
            Log("RestoreDesktop: calling IDesktopWallpaper.SetWallpaper per-monitor");
            bool ok = _snapshot.Restore();
            Log($"RestoreDesktop: snapshot restore ok={ok}");
        }
        catch (Exception ex) { Log($"RestoreDesktop failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_winEventHook != IntPtr.Zero)
            {
                _interop.UnhookWinEvent(_winEventHook);
                Log($"Dispose: UnhookWinEvent 0x{_winEventHook.ToInt64():X}");
                _winEventHook = IntPtr.Zero;
            }
            if (_winEventGCHandle.IsAllocated) _winEventGCHandle.Free();
        }
        catch { }
        RestoreDesktop();
        try
        {
            Log("Dispose: fallback SPI_SETDESKWALLPAPER");
            _interop.SystemParametersInfo(DesktopNative.SPI_SETDESKWALLPAPER, 0, null, DesktopNative.SPIF_UPDATEINIFILE | DesktopNative.SPIF_SENDCHANGE);
        }
        catch (Exception ex) { Log($"Dispose SPI fallback failed: {ex.Message}"); }
        LastProgman = IntPtr.Zero;
        LastWorkerW = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private static void Log(string msg)
    {
        try { Console.WriteLine($"[DesktopLayerHost] {msg}"); } catch { }
        try { System.Diagnostics.Debug.WriteLine($"[DesktopLayerHost] {msg}"); } catch { }
    }
}
