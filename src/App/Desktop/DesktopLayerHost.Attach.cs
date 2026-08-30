using System.Runtime.InteropServices;

namespace OsageLagtrain.App.Desktop;

public sealed partial class DesktopLayerHost
{
    public bool Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("hwnd must not be zero", nameof(hwnd));
        if (!_probed) Probe();
        try { _snapshot.CaptureIfNeeded(); } catch { }

        var style = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_STYLE);
        uint newStyle = style;
        newStyle &= ~DesktopNative.WS_POPUP;
        newStyle |= DesktopNative.WS_CHILD;
        if ((newStyle & DesktopNative.WS_POPUP) != 0 && (newStyle & DesktopNative.WS_CHILD) != 0)
            newStyle &= ~DesktopNative.WS_POPUP;
        if (newStyle != style)
        {
            _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_STYLE, (nint)newStyle);
            Log($"Attach: style 0x{style:X} -> 0x{newStyle:X} (WS_POPUP removed, WS_CHILD added)");
        }

        var exStyle = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE);
        if ((exStyle & DesktopNative.WS_EX_LAYERED) == 0)
        {
            _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE, (nint)(exStyle | DesktopNative.WS_EX_LAYERED));
            Log($"Attach: added WS_EX_LAYERED to exStyle 0x{exStyle:X}");
        }
        SetWindowTransparency(hwnd, 255);
        _interop.SetLayeredWindowAttributes(hwnd, 0, 255, DesktopNative.LWA_ALPHA);

        if (_topology == DesktopTopology.RaisedDesktop)
            return AttachRaised(hwnd);
        else
            return AttachClassic(hwnd);
    }

    private bool AttachRaised(IntPtr hwnd)
    {
        var progman = _interop.FindWindow("Progman", null);
        LastProgman = progman;
        if (progman == IntPtr.Zero)
        {
            Log("AttachRaised: Progman not found");
            try { _interop.ShowWindow(hwnd, DesktopNative.SW_HIDE); } catch { }
            return false;
        }
        var shellDefView = _interop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        Log($"AttachRaised: Progman=0x{progman.ToInt64():X} SHELLDLL_DefView=0x{shellDefView.ToInt64():X}");
        bool parentOk = _interop.SetParent(hwnd, progman);
        Log($"AttachRaised: SetParent(hwnd=0x{hwnd.ToInt64():X}, Progman=0x{progman.ToInt64():X}) => {parentOk}");
        LastWorkerW = IntPtr.Zero;
        // Ensure layered opaque
        _interop.SetLayeredWindowAttributes(hwnd, 0, 255, DesktopNative.LWA_ALPHA);
        IntPtr insertAfter = shellDefView != IntPtr.Zero ? shellDefView : DesktopNative.HWND_TOP;
        var bounds = _display.VirtualScreenBounds;
        double scale = _display.GetScaleForWindow(hwnd);
        var rc = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        _interop.MapWindowPoints(IntPtr.Zero, progman, ref rc, 2);
        int x = rc.Left;
        int y = rc.Top;
        bool posOk = _interop.SetWindowPos(hwnd, insertAfter, x, y, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE);
        if (!posOk) Log($"AttachRaised: SetWindowPos under DefView failed, lastError check");
        else Log($"AttachRaised: SetWindowPos(hwnd, after DefView=0x{insertAfter.ToInt64():X}, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE) => {posOk} (MapWindowPoints rc {rc.Left},{rc.Top} scale {scale})");
        // Also ensure WorkerW (if present) stays after wallpaper — HWND_BOTTOM for WorkerW only, not wallpaper
        try
        {
            var workerW = FindWorkerW_Unified(progman, true);
            if (workerW != IntPtr.Zero)
            {
                LastWorkerW = workerW;
                bool wOk = _interop.SetWindowPos(workerW, hwnd, 0, 0, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE);
                Log($"AttachRaised: SetWindowPos(WorkerW=0x{workerW.ToInt64():X} after wallpaper=0x{hwnd.ToInt64():X}) => {wOk}");
            }
        }
        catch { }
        _attachedHwnd = hwnd;
        SetupHealing();
        return parentOk && posOk;
    }

    private bool AttachClassic(IntPtr hwnd)
    {
        var progman = _interop.FindWindow("Progman", null);
        var workerW = FindWorkerW_Unified(progman, false);
        LastWorkerW = workerW;
        if (workerW == IntPtr.Zero)
        {
            // WorkerW not found — DO NOT show visible covering window (covers icons, GDI dead on raised).
            // Keep hidden and let healing retry (EVENT_OBJECT_DESTROY / WM_TASKBARCREATED / SessionUnlock -> EnsureLayerAsync).
            Log("AttachClassic: WorkerW not found — hide window and schedule retry via healing (no visible covering)");
            try { _interop.ShowWindow(hwnd, DesktopNative.SW_HIDE); } catch { }
            LastProgman = progman;
            _attachedHwnd = hwnd;
            SetupHealing();
            try { HandleHealingTrigger("WorkerW_not_found_retry"); } catch { }
            return false;
        }
        bool parentOk = _interop.SetParent(hwnd, workerW);
        Log($"AttachClassic: SetParent(hwnd=0x{hwnd.ToInt64():X}, WorkerW=0x{workerW.ToInt64():X}) => {parentOk}");
        EnsureWorkerWZOrder();
        var bounds = _display.VirtualScreenBounds;
        double scale = _display.GetScaleForWindow(hwnd);
        var rc = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
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
        var progman = _interop.FindWindow("Progman", null);
        return FindWorkerW_Unified(progman, _topology == DesktopTopology.RaisedDesktop);
    }

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
            _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE, (nint)(ex | DesktopNative.WS_EX_LAYERED));
        _interop.SetLayeredWindowAttributes(hwnd, 0, alpha, DesktopNative.LWA_ALPHA);
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private static void TryDisableRoundedCorners(IntPtr hwnd)
    {
        try
        {
            int pref = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { }
    }
}
