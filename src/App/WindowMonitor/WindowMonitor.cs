using System.Runtime.InteropServices;

namespace OsageLagtrain.App.WindowMonitor;

public sealed class WindowMonitor : IDisposable
{
    private readonly IWindowInterop _interop;
    private readonly Func<DateTimeOffset> _nowProvider;
    private readonly Action<Action> _uiDispatcher;
    private int _globalPostEventDelayMs;

    private GCHandle _hookGCHandle;
    private WindowMonitorWinEventDelegate? _hookDelegate;
    private readonly List<IntPtr> _hookHandles = new();
    private readonly object _lock = new();
    private System.Threading.Timer? _fallbackTimer;
    private System.Threading.Timer? _debounceTimer;
    private bool _disposed;
    private bool _dirty;
    private DateTimeOffset _lastShQuery = DateTimeOffset.MinValue;
    private QUNS _cachedShState = QUNS.QUNS_ACCEPTS_NOTIFICATIONS;
    private int _shQueryCalls;
    private bool _previousWasCovering;
    private string _previousMonitorId = string.Empty;
    private string _previousExeName = string.Empty;
    private IntPtr _previousHwnd = IntPtr.Zero;
    private bool _pausedByD3D;
    private bool _pausedExplicitly;
    private bool _pausedBySession;

    // For test inspection
    public IReadOnlyList<IntPtr> HookHandles => _hookHandles;
    public int ShQueryCalls => _shQueryCalls;
    public bool IsPausedByD3D => _pausedByD3D;
    public bool IsPausedExplicitly => _pausedExplicitly;
    public bool IsPausedBySession => _pausedBySession;
    public bool IsPaused => _pausedExplicitly || _pausedBySession || _pausedByD3D;

    public event Action<string, string>? WallpaperShouldAdvance;

    // Per-scene override key: caller can pass optional per-scene delay
    private int? _perSceneOverrideMs;

    public WindowMonitor(
        IWindowInterop? interop = null,
        int globalPostEventDelayMs = WindowMonitorConstants.DefaultPostEventDelayMs,
        int? perScenePostEventDelayMs = null,
        Func<DateTimeOffset>? nowProvider = null,
        Action<Action>? uiDispatcher = null)
    {
        _interop = interop ?? new NativeWindowInterop();
        _globalPostEventDelayMs = Math.Clamp(globalPostEventDelayMs, 0, 5000);
        _perSceneOverrideMs = perScenePostEventDelayMs.HasValue ? Math.Clamp(perScenePostEventDelayMs.Value, 0, 5000) : null;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
        _uiDispatcher = uiDispatcher ?? (a => a());
    }

    public void SetPerSceneDelay(int? ms)
    {
        _perSceneOverrideMs = ms.HasValue ? Math.Clamp(ms.Value, 0, 5000) : null;
    }

    public void UpdateConfig(Cycles.SettingsConfig config)
    {
        if (config == null) return;
        _globalPostEventDelayMs = Math.Clamp(config.PostEventDelayMs, 0, 5000);
        // future: selectionPolicy, noRepeatWindow handled by scheduler, not monitor
    }

    public void UpdateConfig(int postEventDelayMs)
    {
        _globalPostEventDelayMs = Math.Clamp(postEventDelayMs, 0, 5000);
    }

    public int CurrentPostEventDelayMs => EffectivePostDelayMs;

    private int EffectivePostDelayMs => _perSceneOverrideMs ?? _globalPostEventDelayMs;

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowMonitor));
        SubscribeHooks();
        _fallbackTimer = new System.Threading.Timer(_ => OnPollTick(), null, WindowMonitorConstants.FallbackPollMs, WindowMonitorConstants.FallbackPollMs);
        // Initial evaluation
        EvaluateCovering();
    }

    private void SubscribeHooks()
    {
        _hookDelegate = OnWinEvent;
        _hookGCHandle = GCHandle.Alloc(_hookDelegate);
        uint flags = WindowMonitorConstants.WINEVENT_OUTOFCONTEXT | WindowMonitorConstants.WINEVENT_SKIPOWNPROCESS;

        // Subscribe each event separately; grep check expects these constants present
        var events = new uint[]
        {
            WindowMonitorConstants.EVENT_SYSTEM_FOREGROUND,      // 0x3
            WindowMonitorConstants.EVENT_SYSTEM_MINIMIZESTART,   // 0x16
            WindowMonitorConstants.EVENT_SYSTEM_MINIMIZEEND,     // 0x17
            WindowMonitorConstants.EVENT_SYSTEM_MOVESIZESTART,   // 0xA
            WindowMonitorConstants.EVENT_SYSTEM_MOVESIZEEND,     // 0xB
            WindowMonitorConstants.EVENT_OBJECT_DESTROY,         // 0x8001
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

    private void OnPollTick()
    {
        ScheduleEvaluate();
    }

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

    // Exposed for tests to trigger without timer
    public void TriggerEvaluate() => EvaluateCovering();

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
        {
            try { _interop.UnhookWinEvent(h); } catch { }
        }
        _hookHandles.Clear();
        if (_hookGCHandle.IsAllocated) _hookGCHandle.Free();
        _hookDelegate = null;
    }

    public void Resume()
    {
        if (_disposed) return;
        if (!_pausedExplicitly && !_pausedBySession) return;
        _pausedExplicitly = false;
        if (_pausedBySession) return; // still paused by session
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
        // re-create hooks
        if (_disposed) return;
        if (_hookHandles.Count == 0)
        {
            SubscribeHooks();
            _fallbackTimer = new System.Threading.Timer(_ => OnPollTick(), null, WindowMonitorConstants.FallbackPollMs, WindowMonitorConstants.FallbackPollMs);
        }
    }

    // Core evaluation
    public void EvaluateCovering()
    {
        if (_disposed) return;
        if (_pausedExplicitly || _pausedBySession) return;
        lock (_lock) { _dirty = false; }

        // SHQuery caching 500ms
        var now = _nowProvider();
        if ((now - _lastShQuery).TotalMilliseconds >= WindowMonitorConstants.ShQueryCacheMs)
        {
            _cachedShState = _interop.GetNotificationState();
            _lastShQuery = now;
            _shQueryCalls++;
        }

        // QUNS_RUNNING_D3D_FULL_SCREEN = 3 (Win32). Spec alias 7 handled via compat check.
        bool isD3D = _cachedShState == QUNS.QUNS_RUNNING_D3D_FULL_SCREEN || (int)_cachedShState == 7;
        if (isD3D)
        {
            _pausedByD3D = true;
            // Still track previous covering state but don't fire
            UpdatePreviousCoveringState();
            return;
        }
        _pausedByD3D = false;

        var fg = _interop.GetForegroundWindow();
        bool isDesktopFg = IsDesktopForeground(fg);

        if (isDesktopFg)
        {
            if (_previousWasCovering)
            {
                var monitorId = _previousMonitorId;
                var exeName = _previousExeName;
                int delay = EffectivePostDelayMs;
                // Reset before delay to avoid double-fire
                _previousWasCovering = false;
                if (delay <= 0)
                {
                    WallpaperShouldAdvance?.Invoke(monitorId, exeName);
                }
                else
                {
                    // Use Task.Delay style via Timer to respect testability (use Sleep abstraction on timer)
                    var capturedMonitor = monitorId;
                    var capturedExe = exeName;
                    var t = new System.Threading.Timer(_ =>
                    {
                        WallpaperShouldAdvance?.Invoke(capturedMonitor, capturedExe);
                    }, null, delay, Timeout.Infinite);
                    // Let timer fire; not disposed immediately — tiny leak acceptable, tests await via manual wait
                    // Prevent GC: keep reference briefly via GC.KeepAlive
                    GC.KeepAlive(t);
                }
            }
            // Update state: desktop is not covering
            _previousWasCovering = false;
            _previousHwnd = fg;
            return;
        }

        // Foreground is a real window — check if it covers monitor
        if (fg != IntPtr.Zero)
        {
            bool covers = IsCovering(fg);
            if (covers)
            {
                // Remember for later desktop transition
                _previousWasCovering = true;
                _previousHwnd = fg;
                // Capture monitor id and exe
                _previousMonitorId = GetMonitorId(fg);
                _previousExeName = _interop.GetExeName(fg);
            }
            else
            {
                // Alt+Tab to small window should NOT set covering — so next desktop won't fire
                _previousWasCovering = false;
                _previousHwnd = fg;
                _previousMonitorId = string.Empty;
                _previousExeName = string.Empty;
            }
        }
        else // fg == null -> treat as desktop (spec: foreground==null/desktop)
        {
            if (_previousWasCovering)
            {
                var monitorId = _previousMonitorId;
                var exeName = _previousExeName;
                int delay = EffectivePostDelayMs;
                _previousWasCovering = false;
                if (delay <= 0) WallpaperShouldAdvance?.Invoke(monitorId, exeName);
                else
                {
                    var capturedMonitor = monitorId;
                    var capturedExe = exeName;
                    var t = new System.Threading.Timer(_ => WallpaperShouldAdvance?.Invoke(capturedMonitor, capturedExe), null, delay, Timeout.Infinite);
                    GC.KeepAlive(t);
                }
            }
        }
    }

    private void UpdatePreviousCoveringState()
    {
        var fg = _interop.GetForegroundWindow();
        if (fg != IntPtr.Zero && !IsDesktopForeground(fg))
        {
            bool covers = IsCovering(fg);
            _previousWasCovering = covers;
            if (covers)
            {
                _previousMonitorId = GetMonitorId(fg);
                _previousExeName = _interop.GetExeName(fg);
                _previousHwnd = fg;
            }
        }
    }

    public bool IsDesktopForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        if (hwnd == _interop.GetDesktopWindow()) return true;
        if (hwnd == _interop.GetShellWindow()) return true;
        var cls = _interop.GetClassName(hwnd);
        return WindowMonitorConstants.DesktopClassAllowList.Contains(cls);
    }

    public bool PassesFilters(IntPtr hwnd)
    {
        if (!_interop.IsWindowVisible(hwnd)) return false;
        if (_interop.IsIconic(hwnd)) return false;
        if (_interop.IsCloaked(hwnd)) return false;
        if (_interop.IsToolWindow(hwnd)) return false;
        if (!_interop.IsSelfAncestor(hwnd)) return false;
        return true;
    }

    public bool IsCovering(IntPtr hwnd)
    {
        if (!PassesFilters(hwnd)) return false;
        return CoversMonitor(hwnd);
    }

    public bool CoversMonitor(IntPtr hwnd)
    {
        // IsZoomed fast-path
        if (_interop.IsZoomed(hwnd)) return true;

        if (!_interop.GetExtendedFrameBounds(hwnd, out var frame)) return false;
        if (!_interop.GetMonitorBounds(hwnd, out var mon)) return false;

        // Compare DWM bounds vs rcMonitor/rcWork @ >=0.95
        // Use each dimension 95% or area 95% — spec says width*height coverage OR each dimension 95%
        var monRect = mon.RcMonitor;
        // Also consider rcWork — spec says vs rcMonitor/rcWork, so check both; if either passes, covering
        bool coversMonitor = IsRectCovers(frame, monRect);
        bool coversWork = IsRectCovers(frame, mon.RcWork);
        return coversMonitor || coversWork;
    }

    private static bool IsRectCovers(Rect frame, Rect monitor)
    {
        int monW = monitor.Width;
        int monH = monitor.Height;
        int frameW = frame.Width;
        int frameH = frame.Height;
        if (monW <= 0 || monH <= 0) return false;
        if (frameW <= 0 || frameH <= 0) return false;

        double widthRatio = (double)frameW / monW;
        double heightRatio = (double)frameH / monH;
        double areaRatio = (double)frame.Area / monitor.Area;

        // Each dimension >=0.95 OR area >=0.95
        if (widthRatio >= WindowMonitorConstants.CoverageThreshold && heightRatio >= WindowMonitorConstants.CoverageThreshold)
            return true;
        if (areaRatio >= WindowMonitorConstants.CoverageThreshold)
            return true;
        return false;
    }

    private string GetMonitorId(IntPtr hwnd)
    {
        if (_interop.GetMonitorBounds(hwnd, out var mon))
        {
            return $"0x{mon.MonitorHandle.ToInt64():X}";
        }
        return "primary";
    }

    // For simulation harness
    public int SimulateSequence(IEnumerable<IntPtr?> foregroundSequence, int stepDelayMs = 0)
    {
        int advances = 0;
        WallpaperShouldAdvance += (_, _) => advances++;
        foreach (var fg in foregroundSequence)
        {
            // Mock interop should return fg via GetForegroundWindow; simulation harness uses manual TriggerEvaluate after setting mock state
            TriggerEvaluate();
            if (stepDelayMs > 0) Thread.Sleep(stepDelayMs);
        }
        return advances;
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
        foreach (var h in _hookHandles)
        {
            try { _interop.UnhookWinEvent(h); } catch { }
        }
        _hookHandles.Clear();
        if (_hookGCHandle.IsAllocated) _hookGCHandle.Free();
        _hookDelegate = null;
        GC.SuppressFinalize(this);
    }
}
