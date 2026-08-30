using System.Runtime.InteropServices;

namespace OsageLagtrain.App.WindowMonitor;

public sealed partial class WindowMonitor
{
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowMonitor));
        SubscribeHooks();
        _fallbackTimer = new System.Threading.Timer(_ => OnPollTick(), null, WindowMonitorConstants.FallbackPollMs, WindowMonitorConstants.FallbackPollMs);
        EvaluateCovering();
    }

    private void SubscribeHooks()
    {
        _hookDelegate = OnWinEvent;
        _hookGCHandle = GCHandle.Alloc(_hookDelegate);
        uint flags = WindowMonitorConstants.WINEVENT_OUTOFCONTEXT | WindowMonitorConstants.WINEVENT_SKIPOWNPROCESS;
        var events = new uint[]
        {
            WindowMonitorConstants.EVENT_SYSTEM_FOREGROUND,
            WindowMonitorConstants.EVENT_SYSTEM_MINIMIZESTART,
            WindowMonitorConstants.EVENT_SYSTEM_MINIMIZEEND,
            WindowMonitorConstants.EVENT_SYSTEM_MOVESIZESTART,
            WindowMonitorConstants.EVENT_SYSTEM_MOVESIZEEND,
            WindowMonitorConstants.EVENT_OBJECT_DESTROY,
        };
        foreach (var ev in events)
        {
            var h = _interop.SetWinEventHook(ev, ev, IntPtr.Zero, _hookDelegate, 0, 0, flags);
            if (h != IntPtr.Zero) _hookHandles.Add(h);
        }
        // Intentionally NOT subscribing to 0x800B LOCATIONCHANGE
    }

    private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint tid, uint time)
    {
        lock (_lock) { _dirty = true; }
        ScheduleEvaluate();
    }

    private void OnPollTick() => ScheduleEvaluate();

    private void ScheduleEvaluate()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                _uiDispatcher(() => EvaluateCovering());
            }, null, WindowMonitorConstants.DebounceMs, Timeout.Infinite);
        }
    }

    public void Pause()
    {
        if (_disposed) return;
        _pausedExplicitly = true;
        lock (_lock)
        {
            _fallbackTimer?.Dispose();
            _fallbackTimer = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        foreach (var h in _hookHandles)
            try { _interop.UnhookWinEvent(h); } catch { }
        _hookHandles.Clear();
        if (_hookGCHandle.IsAllocated) _hookGCHandle.Free();
        _hookDelegate = null;
    }

    public void Resume()
    {
        if (_disposed) return;
        if (!_pausedExplicitly && !_pausedBySession) return;
        _pausedExplicitly = false;
        if (_pausedBySession) return;
        SubscribeHooks();
        _fallbackTimer = new System.Threading.Timer(_ => OnPollTick(), null, WindowMonitorConstants.FallbackPollMs, WindowMonitorConstants.FallbackPollMs);
        EvaluateCovering();
    }

    public void PauseForSession()
    {
        _pausedBySession = true;
        Pause();
    }

    public void ResumeFromSession()
    {
        _pausedBySession = false;
        if (_pausedExplicitly) return;
        if (_disposed) return;
        if (_hookHandles.Count == 0)
        {
            SubscribeHooks();
            _fallbackTimer = new System.Threading.Timer(_ => OnPollTick(), null, WindowMonitorConstants.FallbackPollMs, WindowMonitorConstants.FallbackPollMs);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _fallbackTimer?.Dispose();
            _fallbackTimer = null;
        }
        lock (_advanceTimers) { foreach (var t in _advanceTimers) try { t.Dispose(); } catch { } _advanceTimers.Clear(); }
        foreach (var h in _hookHandles)
            try { _interop.UnhookWinEvent(h); } catch { }
        _hookHandles.Clear();
        if (_hookGCHandle.IsAllocated) _hookGCHandle.Free();
        _hookDelegate = null;
        GC.SuppressFinalize(this);
    }
}
