using System.Runtime.InteropServices;

namespace OsageLagtrain.App.WindowMonitor;

public sealed partial class WindowMonitor : IDisposable
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
    private readonly List<System.Threading.Timer> _advanceTimers = new();

    public IReadOnlyList<IntPtr> HookHandles => _hookHandles;
    public int ShQueryCalls => _shQueryCalls;
    public bool IsPausedByD3D => _pausedByD3D;
    public bool IsPausedExplicitly => _pausedExplicitly;
    public bool IsPausedBySession => _pausedBySession;
    public bool IsPaused => _pausedExplicitly || _pausedBySession || _pausedByD3D;

    public event Action<string, string>? WallpaperShouldAdvance;

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
    }

    public void UpdateConfig(int postEventDelayMs)
    {
        _globalPostEventDelayMs = Math.Clamp(postEventDelayMs, 0, 5000);
    }

    public int CurrentPostEventDelayMs => EffectivePostDelayMs;
    private int EffectivePostDelayMs => _perSceneOverrideMs ?? _globalPostEventDelayMs;

    public void TriggerEvaluate() => EvaluateCovering();

    public void EvaluateCovering()
    {
        if (_disposed) return;
        if (_pausedExplicitly || _pausedBySession) return;
        lock (_lock) { _dirty = false; }
        var now = _nowProvider();
        if ((now - _lastShQuery).TotalMilliseconds >= WindowMonitorConstants.ShQueryCacheMs)
        {
            _cachedShState = _interop.GetNotificationState();
            _lastShQuery = now;
            _shQueryCalls++;
        }
        bool isD3D = _cachedShState == QUNS.QUNS_RUNNING_D3D_FULL_SCREEN || (int)_cachedShState == 7;
        if (isD3D)
        {
            _pausedByD3D = true;
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
                _previousWasCovering = false;
                Log($"WallpaperShouldAdvance trigger desktopFg coverEnded monitor={monitorId} exe={exeName} delay={delay} fg=0x{fg.ToInt64():X}");
                if (delay <= 0)
                    WallpaperShouldAdvance?.Invoke(monitorId, exeName);
                else
                {
                    var capturedMonitor = monitorId;
                    var capturedExe = exeName;
                    System.Threading.Timer? t = null;
                    t = new System.Threading.Timer(_ =>
                    {
                        try { WallpaperShouldAdvance?.Invoke(capturedMonitor, capturedExe); } catch { }
                        try { if (t != null) { lock (_advanceTimers) _advanceTimers.Remove(t); t.Dispose(); } } catch { }
                    }, null, delay, Timeout.Infinite);
                    lock (_advanceTimers) _advanceTimers.Add(t);
                }
            }
            else
            {
                Log($"EvaluateCovering desktopFg no previousCovering fg=0x{fg.ToInt64():X} class={_interop.GetClassName(fg)}");
            }
            _previousWasCovering = false;
            _previousHwnd = fg;
            return;
        }
        if (fg != IntPtr.Zero)
        {
            bool covers = IsCovering(fg);
            string exe = _interop.GetExeName(fg);
            bool isSelf = string.Equals(exe, "OsageLagtrain.exe", StringComparison.OrdinalIgnoreCase);
            bool isIconic = _interop.IsIconic(fg);
            Log($"EvaluateCovering fg=0x{fg.ToInt64():X} class={_interop.GetClassName(fg)} exe={exe} covers={covers} isZoomed={_interop.IsZoomed(fg)} visible={_interop.IsWindowVisible(fg)} isIconic={isIconic} isSelf={isSelf}");
            if (covers)
            {
                _previousWasCovering = true;
                _previousHwnd = fg;
                _previousMonitorId = GetMonitorId(fg);
                _previousExeName = exe;
            }
            else
            {
                // If previous was covering and new fg is our own Settings window (small, non-covering),
                // still fire wallpaper advance — Settings shouldn't block desktop trigger.
                if (_previousWasCovering && isSelf)
                {
                    var monitorId = _previousMonitorId;
                    var exeName = _previousExeName;
                    int delay = EffectivePostDelayMs;
                    _previousWasCovering = false;
                    _previousHwnd = fg;
                    Log($"WallpaperShouldAdvance trigger selfFg after coverEnded monitor={monitorId} exe={exeName} delay={delay} fg=0x{fg.ToInt64():X} (Settings open fallback)");
                    if (delay <= 0)
                        WallpaperShouldAdvance?.Invoke(monitorId, exeName);
                    else
                    {
                        var capturedMonitor = monitorId;
                        var capturedExe = exeName;
                        System.Threading.Timer? t = null;
                        t = new System.Threading.Timer(_ =>
                        {
                            try { WallpaperShouldAdvance?.Invoke(capturedMonitor, capturedExe); } catch { }
                            try { if (t != null) { lock (_advanceTimers) _advanceTimers.Remove(t); t.Dispose(); } } catch { }
                        }, null, delay, Timeout.Infinite);
                        lock (_advanceTimers) _advanceTimers.Add(t);
                    }
                    _previousMonitorId = string.Empty;
                    _previousExeName = string.Empty;
                }
                else if (_previousWasCovering && isIconic)
                {
                    // Minimized covering window (MINIMIZESTART) — don't clear previousWasCovering.
                    // User minimized maximized Notepad: foreground may briefly stay on iconic hwnd before desktop.
                    // Keep flag true so next desktop fg fires WallpaperShouldAdvance.
                    Log($"keep previousWasCovering after minimize/iconic fg=0x{fg.ToInt64():X} exe={exe} — waiting for desktop IsDesktopFg");
                    _previousHwnd = fg;
                    // keep _previousMonitorId/_previousExeName for desktop trigger
                }
                else
                {
                    _previousWasCovering = false;
                    _previousHwnd = fg;
                    _previousMonitorId = string.Empty;
                    _previousExeName = string.Empty;
                }
            }
        }
        else
        {
            if (_previousWasCovering)
            {
                var monitorId = _previousMonitorId;
                var exeName = _previousExeName;
                int delay = EffectivePostDelayMs;
                _previousWasCovering = false;
                Log($"WallpaperShouldAdvance trigger nullFg coverEnded monitor={monitorId} exe={exeName} delay={delay}");
                if (delay <= 0) WallpaperShouldAdvance?.Invoke(monitorId, exeName);
                else
                {
                    var capturedMonitor = monitorId;
                    var capturedExe = exeName;
                    System.Threading.Timer? t = null;
                    t = new System.Threading.Timer(_ =>
                    {
                        try { WallpaperShouldAdvance?.Invoke(capturedMonitor, capturedExe); } catch { }
                        try { if (t != null) { lock (_advanceTimers) _advanceTimers.Remove(t); t.Dispose(); } } catch { }
                    }, null, delay, Timeout.Infinite);
                    lock (_advanceTimers) _advanceTimers.Add(t);
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
        if (_interop.IsZoomed(hwnd)) return true;
        if (!_interop.GetExtendedFrameBounds(hwnd, out var frame)) return false;
        if (!_interop.GetMonitorBounds(hwnd, out var mon)) return false;
        var monRect = mon.RcMonitor;
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
        if (widthRatio >= WindowMonitorConstants.CoverageThreshold && heightRatio >= WindowMonitorConstants.CoverageThreshold)
            return true;
        if (areaRatio >= WindowMonitorConstants.CoverageThreshold)
            return true;
        return false;
    }

    private string GetMonitorId(IntPtr hwnd)
    {
        if (_interop.GetMonitorBounds(hwnd, out var mon))
            return $"0x{mon.MonitorHandle.ToInt64():X}";
        return "primary";
    }

    public int SimulateSequence(IEnumerable<IntPtr?> foregroundSequence, int stepDelayMs = 0)
    {
        int advances = 0;
        WallpaperShouldAdvance += (_, _) => advances++;
        foreach (var fg in foregroundSequence)
        {
            TriggerEvaluate();
            if (stepDelayMs > 0) Thread.Sleep(stepDelayMs);
        }
        return advances;
    }

    private static void Log(string msg)
    {
        try { Console.WriteLine($"[WindowMonitor] {msg}"); } catch { }
        try { System.Diagnostics.Debug.WriteLine($"[WindowMonitor] {msg}"); } catch { }
    }
}
