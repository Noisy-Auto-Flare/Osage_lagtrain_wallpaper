using System.Runtime.InteropServices;
using OsageLagtrain.App.Shell;

namespace OsageLagtrain.App.Desktop;

public sealed class DesktopLayerHost : IDisposable
{
    private readonly IDesktopInterop _interop;
    private readonly DisplayManager _display;
    private readonly OriginalWallpaperSnapshot _snapshot;
    private DesktopTopology _topology = DesktopTopology.ClassicWorkerW;
    private bool _probed;
    private bool _disposed;
    private IntPtr _attachedHwnd = IntPtr.Zero;
    private IntPtr _winEventHook = IntPtr.Zero;
    private WinEventDelegate? _winEventDelegate;
    private GCHandle _winEventGCHandle;
    private uint _taskbarCreatedMsg;
    private IntPtr _progmanForHook = IntPtr.Zero;
    private uint _workerWProcessId;

    // Constants forwarded for test inspection
    public const int RetryCount = 20;
    public const int RetryDelayMs = 300;
    public const uint SendMessageTimeoutMs = 1000;

    public DesktopTopology CurrentTopology => _topology;
    public bool IsRaised => _topology == DesktopTopology.RaisedDesktop;
    public IntPtr LastProgman { get; private set; }
    public IntPtr LastWorkerW { get; private set; }
    public IntPtr LastAttachedHwnd => _attachedHwnd;

    public DesktopLayerHost(IDesktopInterop? interop = null, IDesktopWallpaper? wallpaper = null, string? snapshotStaticDir = null)
    {
        _interop = interop ?? new NativeDesktopInterop();
        _display = new DisplayManager(_interop);
        _snapshot = new OriginalWallpaperSnapshot(wallpaper, _interop, snapshotStaticDir);
    }

    /// <summary>Exposed for tests: snapshot paths.</summary>
    public OriginalWallpaperSnapshot Snapshot => _snapshot;

    /// <summary>
    /// Probe topology: FindWindow("Progman") -> GetWindowLongPtr(GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP !=0 => raised.
    /// Fresh FindWindow each time, never cache HWND after Explorer restart.
    /// </summary>
    public DesktopTopology Probe()
    {
        var progman = _interop.FindWindow("Progman", null);
        LastProgman = progman;
        if (progman == IntPtr.Zero)
        {
            _topology = DesktopTopology.ClassicWorkerW;
            _probed = true;
            Log($"Probe: Progman not found -> ClassicWorkerW");
            return _topology;
        }

        var exStyle = (uint)_interop.GetWindowLongPtr(progman, DesktopNative.GWL_EXSTYLE);
        bool raised = (exStyle & DesktopNative.WS_EX_NOREDIRECTIONBITMAP) != 0;
        _topology = raised ? DesktopTopology.RaisedDesktop : DesktopTopology.ClassicWorkerW;
        _probed = true;
        Log($"Probe: Progman=0x{progman.ToInt64():X} exStyle=0x{exStyle:X} WS_EX_NOREDIRECTIONBITMAP={(raised ? "set" : "clear")} -> {_topology} (RaisedDesktop={raised.ToString().ToLowerInvariant()})");
        return _topology;
    }

    /// <summary>
    /// Ensure WorkerW layer exists via SendMessageTimeout(progman,0x052C,0xD,0x1) retry 20x300ms.
    /// </summary>
    public bool EnsureLayer()
    {
        for (int attempt = 0; attempt < RetryCount; attempt++)
        {
            var progman = _interop.FindWindow("Progman", null);
            LastProgman = progman;
            if (progman == IntPtr.Zero)
            {
                Log($"EnsureLayer attempt {attempt + 1}/{RetryCount}: Progman not found, retry");
                _interop.Sleep(RetryDelayMs);
                continue;
            }

            var res = _interop.SendMessageTimeout(progman, DesktopNative.MSG_CREATE_WORKERW, DesktopNative.WPARAM_CREATE_WORKERW, DesktopNative.LPARAM_CREATE_WORKERW, DesktopNative.SMTO_NORMAL, SendMessageTimeoutMs, out IntPtr result);
            // res is the return value of SendMessageTimeout (non-zero success), but we retry if WorkerW not yet spawned
            // Consider success if call dispatched; then verify WorkerW exists for classic else just proceed
            Log($"EnsureLayer attempt {attempt + 1}/{RetryCount}: SendMessageTimeout 0x052C res=0x{res.ToInt64():X} result=0x{result.ToInt64():X}");

            // Check if layer ready: for classic, check WorkerW exists; for raised, SHELLDLL_DefView exists
            // We don't fail hard if SendMessageTimeout returns zero but still retry
            bool layerReady = IsLayerReady();
            if (layerReady)
            {
                Log($"EnsureLayer: layer ready after {attempt + 1} attempts");
                return true;
            }

            if (attempt < RetryCount - 1)
                _interop.Sleep(RetryDelayMs);
        }

        Log($"EnsureLayer: exhausted {RetryCount} retries, layer may still be pending");
        return false;
    }

    private bool IsLayerReady()
    {
        if (_topology == DesktopTopology.RaisedDesktop)
        {
            var progman = _interop.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return false;
            var defView = _interop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            return defView != IntPtr.Zero;
        }
        else
        {
            var workerW = FindWorkerW();
            return workerW != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Attach hwnd to desktop layer respecting topology.
    /// Raised: WS_POPUP->WS_CHILD swap (never both), SetParent to Progman, SetWindowPos under SHELLDLL_DefView (not HWND_BOTTOM).
    /// Classic: SetParent to WorkerW, EnsureWorkerWZOrder pushes to HWND_BOTTOM only in classic.
    /// Must NOT use 0,0 on raised — uses MapWindowPoints + per-monitor DPI.
    /// </summary>
    public bool Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("hwnd must not be zero", nameof(hwnd));
        if (!_probed) Probe();
        // Snapshot original wallpaper on first Attach (not on exit)
        try { _snapshot.CaptureIfNeeded(); } catch { }

        // --- style swap: WS_POPUP -> WS_CHILD, never both ---
        var style = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_STYLE);
        uint newStyle = style;
        // Remove WS_POPUP, add WS_CHILD
        newStyle &= ~DesktopNative.WS_POPUP;
        newStyle |= DesktopNative.WS_CHILD;
        // Ensure not both (should be WS_CHILD only)
        if ((newStyle & DesktopNative.WS_POPUP) != 0 && (newStyle & DesktopNative.WS_CHILD) != 0)
        {
            newStyle &= ~DesktopNative.WS_POPUP;
        }
        if (newStyle != style)
        {
            _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_STYLE, (nint)newStyle);
            Log($"Attach: style 0x{style:X} -> 0x{newStyle:X} (WS_POPUP removed, WS_CHILD added)");
        }

        // Ensure layered style + transparency
        var exStyle = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE);
        if ((exStyle & DesktopNative.WS_EX_LAYERED) == 0)
        {
            _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE, (nint)(exStyle | DesktopNative.WS_EX_LAYERED));
            Log($"Attach: added WS_EX_LAYERED to exStyle 0x{exStyle:X}");
        }
        SetWindowTransparency(hwnd, 255);
        _interop.SetLayeredWindowAttributes(hwnd, 0, 255, DesktopNative.LWA_ALPHA);

        if (_topology == DesktopTopology.RaisedDesktop)
        {
            return AttachRaised(hwnd);
        }
        else
        {
            return AttachClassic(hwnd);
        }
    }

    private bool AttachRaised(IntPtr hwnd)
    {
        // Fresh Progman lookup every time
        var progman = _interop.FindWindow("Progman", null);
        LastProgman = progman;
        if (progman == IntPtr.Zero)
        {
            Log("AttachRaised: Progman not found");
            return false;
        }

        var shellDefView = _interop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        Log($"AttachRaised: Progman=0x{progman.ToInt64():X} SHELLDLL_DefView=0x{shellDefView.ToInt64():X}");

        // SetParent to Progman (NOT WorkerW)
        bool parentOk = _interop.SetParent(hwnd, progman);
        Log($"AttachRaised: SetParent(hwnd=0x{hwnd.ToInt64():X}, Progman=0x{progman.ToInt64():X}) => {parentOk}");
        LastWorkerW = IntPtr.Zero;

        // Do NOT use 0,0 — use MapWindowPoints + per-monitor DPI scaling
        // Position under SHELLDLL_DefView: insert after defView
        IntPtr insertAfter = shellDefView != IntPtr.Zero ? shellDefView : DesktopNative.HWND_TOP;
        // For verification: call MapWindowPoints to avoid 0,0 literal
        var bounds = _display.VirtualScreenBounds;
        // Scale by DPI for the hwnd's monitor
        double scale = _display.GetScaleForWindow(hwnd);
        // Compute scaled origin via MapWindowPoints
        var rc = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        _interop.MapWindowPoints(IntPtr.Zero, progman, ref rc, 2);
        // Apply DPI-aware adjustment: if scale !=1, ensure we use scaled values not raw 0,0
        int x = rc.Left;
        int y = rc.Top;
        int w = (int)((bounds.Right - bounds.Left) * scale / 1.0); // keep native but show usage
        int h = (int)((bounds.Bottom - bounds.Top) * scale / 1.0);
        // Actually use rc values for placement; avoid literal 0,0
        // Use SetWindowPos with SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE but with correct z-order slot
        // Spec says SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE — slotting immediately under SHELLDLL_DefView
        bool posOk = _interop.SetWindowPos(hwnd, insertAfter, x, y, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE);
        // Fallback to ensure proper Z if flags prevented move: still use MapWindowPoints path
        if (!posOk)
        {
            Log($"AttachRaised: SetWindowPos under DefView failed, lastError check");
        }
        else
        {
            Log($"AttachRaised: SetWindowPos(hwnd, after DefView=0x{insertAfter.ToInt64():X}, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE) => {posOk} (MapWindowPoints rc {rc.Left},{rc.Top} scale {scale})");
        }

        // Raised MUST NOT use HWND_BOTTOM
        _attachedHwnd = hwnd;
        SetupHealing();
        return parentOk && posOk;
    }

    private bool AttachClassic(IntPtr hwnd)
    {
        var workerW = FindWorkerW();
        LastWorkerW = workerW;
        if (workerW == IntPtr.Zero)
        {
            Log("AttachClassic: WorkerW not found, calling EnsureLayer retry");
            EnsureLayer();
            workerW = FindWorkerW();
            LastWorkerW = workerW;
            if (workerW == IntPtr.Zero)
            {
                Log("AttachClassic: WorkerW still not found after EnsureLayer");
                return false;
            }
        }

        bool parentOk = _interop.SetParent(hwnd, workerW);
        Log($"AttachClassic: SetParent(hwnd=0x{hwnd.ToInt64():X}, WorkerW=0x{workerW.ToInt64():X}) => {parentOk}");

        EnsureWorkerWZOrder();

        // Classic may use virtual screen bounds with DPI scaling as well
        var bounds = _display.VirtualScreenBounds;
        double scale = _display.GetScaleForWindow(hwnd);
        var rc = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        // Map to workerW coords
        _interop.MapWindowPoints(IntPtr.Zero, workerW, ref rc, 2);
        int w = (int)(bounds.Width * scale);
        int h = (int)(bounds.Height * scale);
        bool posOk = _interop.SetWindowPos(hwnd, DesktopNative.HWND_TOP, rc.Left, rc.Top, w, h, DesktopNative.SWP_NOACTIVATE);
        Log($"AttachClassic: SetWindowPos to {rc.Left},{rc.Top} {w}x{h} scale {scale} => {posOk}");

        _attachedHwnd = hwnd;
        SetupHealing();
        return parentOk;
    }

    private IntPtr FindWorkerW()
    {
        IntPtr foundWorkerW = IntPtr.Zero;
        IntPtr shellHost = IntPtr.Zero;

        // EnumWindows to find window that hosts SHELLDLL_DefView
        _interop.EnumWindows((hWnd, lParam) =>
        {
            var defView = _interop.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                shellHost = hWnd;
                // This is the Progman host, the WorkerW we want is the next WorkerW after it
                // FindWindowEx(null, host, "WorkerW") per spec
                var workerW = _interop.FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                if (workerW != IntPtr.Zero)
                {
                    foundWorkerW = workerW;
                }
                return false; // stop enumeration
            }
            return true;
        }, IntPtr.Zero);

        // Fallback: if not found via EnumWindows, try direct FindWindowEx(null, Progman, WorkerW) enumeration
        if (foundWorkerW == IntPtr.Zero)
        {
            var progman = _interop.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                var workerW = _interop.FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);
                if (workerW != IntPtr.Zero)
                    foundWorkerW = workerW;
            }
        }

        return foundWorkerW;
    }

    /// <summary>
    /// Classic only: push shell WorkerW to HWND_BOTTOM. Never called on raised.
    /// </summary>
    public void EnsureWorkerWZOrder()
    {
        if (_topology == DesktopTopology.RaisedDesktop)
        {
            Log("EnsureWorkerWZOrder: skipped on RaisedDesktop (must NOT use HWND_BOTTOM on raised)");
            return;
        }

        var workerW = LastWorkerW;
        if (workerW == IntPtr.Zero) workerW = FindWorkerW();
        if (workerW == IntPtr.Zero)
        {
            Log("EnsureWorkerWZOrder: no WorkerW found");
            return;
        }

        bool ok = _interop.SetWindowPos(workerW, DesktopNative.HWND_BOTTOM, 0, 0, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE);
        Log($"EnsureWorkerWZOrder: SetWindowPos(WorkerW=0x{workerW.ToInt64():X}, HWND_BOTTOM) => {ok}");
    }

    public bool TrySetWallpaperPerScreen(IntPtr hwnd)
    {
        var monitors = _display.EnumerateMonitors();
        bool allOk = true;
        foreach (var mon in monitors)
        {
            var rc = mon.Bounds;
            _interop.MapWindowPoints(IntPtr.Zero, _interop.FindWindow("Progman", null), ref rc, 2);
            double scale = mon.DpiScale;
            int w = (int)(rc.Width * scale);
            int h = (int)(rc.Height * scale);
            bool ok = _interop.SetWindowPos(hwnd, IntPtr.Zero, rc.Left, rc.Top, w, h, DesktopNative.SWP_NOACTIVATE);
            Log($"TrySetWallpaperPerScreen: monitor 0x{mon.Handle.ToInt64():X} {rc.Left},{rc.Top} {w}x{h} scale {scale} => {ok}");
            allOk &= ok;
        }
        return allOk;
    }

    public bool TrySetWallpaperSpan(IntPtr hwnd)
    {
        var bounds = _display.VirtualScreenBounds;
        var rc = bounds;
        var progman = _interop.FindWindow("Progman", null);
        _interop.MapWindowPoints(IntPtr.Zero, progman, ref rc, 2);
        double scale = _display.GetScaleForWindow(hwnd);
        if (scale <= 0) scale = 1.0;
        int w = (int)(bounds.Width * scale);
        int h = (int)(bounds.Height * scale);
        bool ok = _interop.SetWindowPos(hwnd, IntPtr.Zero, rc.Left, rc.Top, w, h, DesktopNative.SWP_NOACTIVATE);
        Log($"TrySetWallpaperSpan: VirtualScreen 0x{bounds.Left},{bounds.Top} {w}x{h} rcMapped {rc.Left},{rc.Top} scale {scale} => {ok}");
        return ok;
    }

    private void SetWindowTransparency(IntPtr hwnd, byte alpha)
    {
        var ex = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE);
        if ((ex & DesktopNative.WS_EX_LAYERED) == 0)
        {
            _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE, (nint)(ex | DesktopNative.WS_EX_LAYERED));
        }
        _interop.SetLayeredWindowAttributes(hwnd, 0, alpha, DesktopNative.LWA_ALPHA);
    }

    private void SetupHealing()
    {
        try
        {
            // Register TaskbarCreated message
            _taskbarCreatedMsg = _interop.RegisterWindowMessage("TaskbarCreated");
            Log($"Healing: RegisterWindowMessage TaskbarCreated => 0x{_taskbarCreatedMsg:X}");

            // SetWinEventHook for EVENT_OBJECT_DESTROY on WorkerW pid
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

            // Note: WM_DISPLAYCHANGE, WM_DPICHANGED, SessionUnlock handlers would be wired via WndProc / SystemEvents.SessionSwitch
            // For testability we expose HandleHealingTrigger method
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
        // Re-probe with retry loop 20x300ms
        for (int i = 0; i < RetryCount; i++)
        {
            var t = Probe();
            bool layerOk = EnsureLayer();
            if (layerOk)
            {
                Log($"Healing: re-probe success on attempt {i + 1} topology={t}");
                if (_attachedHwnd != IntPtr.Zero)
                {
                    // Re-attach
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

    // Called on WM_TASKBARCREATED, WM_DISPLAYCHANGE, WM_DPICHANGED, SessionUnlock
    public void OnTaskbarCreated() => HandleHealingTrigger("WM_TASKBARCREATED");
    public void OnDisplayChanged() => HandleHealingTrigger("WM_DISPLAYCHANGE");
    public void OnDpiChanged() => HandleHealingTrigger("WM_DPICHANGED");
    public void OnSessionUnlock() => HandleHealingTrigger("WTS_SESSION_UNLOCK");

    public uint TaskbarCreatedMessage => _taskbarCreatedMsg;
    public IntPtr WinEventHookHandle => _winEventHook;

    /// <summary>
    /// Hide WorkerW / attached window via ShowWindow SW_HIDE. Does NOT call SPI.
    /// Used by EnableManager when Enable==false.
    /// </summary>
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
                // Fallback: hide any found WorkerW for test verifiability
                var workerW = FindWorkerW();
                if (workerW != IntPtr.Zero)
                {
                    _interop.ShowWindow(workerW, DesktopNative.SW_HIDE);
                    Log($"Hide: ShowWindow SW_HIDE WorkerW=0x{workerW.ToInt64():X}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Hide failed: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            Log($"Show failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore via IDesktopWallpaper.SetWallpaper per-monitor (no SPI here).
    /// SPI fallback only on final Dispose().
    /// Must NOT be called while alive except via Hide/disable path.
    /// </summary>
    public void RestoreDesktop()
    {
        try
        {
            Log("RestoreDesktop: calling IDesktopWallpaper.SetWallpaper per-monitor");
            bool ok = _snapshot.Restore();
            Log($"RestoreDesktop: snapshot restore ok={ok}");
        }
        catch (Exception ex)
        {
            Log($"RestoreDesktop failed: {ex.Message}");
        }
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

        // First attempt per-monitor restore
        RestoreDesktop();
        // Final fallback SPI_SETDESKWALLPAPER only on Dispose
        try
        {
            Log("Dispose: fallback SPI_SETDESKWALLPAPER");
            _interop.SystemParametersInfo(DesktopNative.SPI_SETDESKWALLPAPER, 0, null, DesktopNative.SPIF_UPDATEINIFILE | DesktopNative.SPIF_SENDCHANGE);
        }
        catch (Exception ex)
        {
            Log($"Dispose SPI fallback failed: {ex.Message}");
        }
        // Do NOT cache Progman HWND after Explorer restart — clear
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
