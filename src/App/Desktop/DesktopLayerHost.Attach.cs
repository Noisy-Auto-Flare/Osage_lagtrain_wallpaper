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
            // Avoid 6-sec synchronous EnsureLayer on UI thread. Background healing (EnsureLayerAsync) handles retries.
            // Do a single quick probe without sleep; if still missing, fail fast and let caller/healing retry asynchronously.
            Log("AttachClassic: WorkerW not found — skipping synchronous EnsureLayer (use EnsureLayerAsync on background)");
            // Fallback visible but NOT covering taskbar/icons: HWND_BOTTOM + parent to Progman so taskbar Shell_TrayWnd stays on top.
            // Remove rounded corners via DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND.
            try { TryDisableRoundedCorners(hwnd); } catch { }
            // Parent to Progman even in fallback so icons (SHELLDLL_DefView child of Progman) stay above wallpaper.
            try
            {
                var progmanFb = _interop.FindWindow("Progman", null);
                if (progmanFb != IntPtr.Zero)
                {
                    _interop.SetParent(hwnd, progmanFb);
                    LastProgman = progmanFb;
                    Log($"AttachClassic fallback: SetParent to Progman=0x{progmanFb.ToInt64():X} so fallback stays behind desktop icons");
                }
            }
            catch { }
            // Restore WS_POPUP, remove WS_CHILD, add WS_EX_TOOLWINDOW, remove WS_EX_APPWINDOW, position covering virtual screen with DpiScale + MapWindowPoints, SW_SHOWNA.
            try
            {
                var fStyle = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_STYLE);
                uint fNewStyle = fStyle;
                // Remove caption/border/thickframe/sysmenu/min/max/child and ensure borderless POPUP
                fNewStyle &= ~(DesktopNative.WS_CHILD | DesktopNative.WS_CAPTION | DesktopNative.WS_THICKFRAME | DesktopNative.WS_SYSMENU | DesktopNative.WS_MINIMIZEBOX | DesktopNative.WS_MAXIMIZEBOX | DesktopNative.WS_BORDER | DesktopNative.WS_DLGFRAME);
                fNewStyle |= DesktopNative.WS_POPUP;
                // Ensure visible flag not cleared accidentally — keep WS_POPUP even if signed
                if ((fNewStyle & DesktopNative.WS_POPUP) == 0)
                    fNewStyle |= DesktopNative.WS_POPUP;
                if (fNewStyle != fStyle)
                {
                    _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_STYLE, (nint)fNewStyle);
                    Log($"AttachClassic fallback: style 0x{fStyle:X} -> 0x{fNewStyle:X} (WS_POPUP restored, caption/thickframe/sysmenu removed, borderless)");
                }
                // Also via AppWindow (reflection to avoid Tests hard dep on Microsoft.UI)
                try
                {
                    var win32InteropType = Type.GetType("Microsoft.UI.Win32Interop, Microsoft.UI");
                    var appWindowType = Type.GetType("Microsoft.UI.Windowing.AppWindow, Microsoft.UI");
                    if (win32InteropType != null && appWindowType != null)
                    {
                        var getId = win32InteropType.GetMethod("GetWindowIdFromWindow");
                        var getFromId = appWindowType.GetMethod("GetFromWindowId");
                        if (getId != null && getFromId != null)
                        {
                            var id = getId.Invoke(null, new object[] { hwnd });
                            var aw = getFromId.Invoke(null, new object[] { id! });
                            if (aw != null)
                            {
                                var isShownProp = appWindowType.GetProperty("IsShownInSwitchers");
                                isShownProp?.SetValue(aw, false);
                                try
                                {
                                    var tbProp = appWindowType.GetProperty("TitleBar");
                                    var tb = tbProp?.GetValue(aw);
                                    var extProp = tb?.GetType().GetProperty("ExtendsContentIntoTitleBar");
                                    extProp?.SetValue(tb, true);
                                }
                                catch { }
                                try
                                {
                                    var presenterProp = appWindowType.GetProperty("Presenter");
                                    var presenter = presenterProp?.GetValue(aw);
                                    if (presenter != null)
                                    {
                                        var t = presenter.GetType();
                                        t.GetProperty("IsMaximizable")?.SetValue(presenter, false);
                                        t.GetProperty("IsMinimizable")?.SetValue(presenter, false);
                                        t.GetProperty("IsResizable")?.SetValue(presenter, false);
                                        t.GetProperty("IsAlwaysOnTop")?.SetValue(presenter, false);
                                        var setBorder = t.GetMethod("SetBorderAndTitleBar");
                                        setBorder?.Invoke(presenter, new object[] { false, false });
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
            try
            {
                var fExStyle = (uint)_interop.GetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE);
                uint fNewEx = fExStyle;
                fNewEx |= DesktopNative.WS_EX_TOOLWINDOW;
                fNewEx &= ~DesktopNative.WS_EX_APPWINDOW;
                if (fNewEx != fExStyle)
                {
                    _interop.SetWindowLongPtr(hwnd, DesktopNative.GWL_EXSTYLE, (nint)fNewEx);
                    Log($"AttachClassic fallback: exStyle 0x{fExStyle:X} -> 0x{fNewEx:X} (TOOLWINDOW added, APPWINDOW removed)");
                }
            }
            catch { }
            try
            {
                var fbBounds = _display.VirtualScreenBounds;
                double fbScale = _display.GetScaleForWindow(hwnd);
                if (fbScale <= 0) fbScale = 1.0;
                // Map virtual-screen rect into Progman client coords when parented to Progman, so wallpaper sits behind icons covering entire virtual screen.
                var progmanForMap = LastProgman != IntPtr.Zero ? LastProgman : _interop.FindWindow("Progman", null);
                var fbRc = new RECT { Left = fbBounds.Left, Top = fbBounds.Top, Right = fbBounds.Right, Bottom = fbBounds.Bottom };
                if (progmanForMap != IntPtr.Zero)
                    _interop.MapWindowPoints(IntPtr.Zero, progmanForMap, ref fbRc, 2);
                else
                    _interop.MapWindowPoints(IntPtr.Zero, IntPtr.Zero, ref fbRc, 2);
                int fbW = (int)(fbBounds.Width * fbScale);
                int fbH = (int)(fbBounds.Height * fbScale);
                bool fbPosOk = _interop.SetWindowPos(hwnd, DesktopNative.HWND_BOTTOM, fbRc.Left, fbRc.Top, fbW, fbH, DesktopNative.SWP_NOACTIVATE);
                Log($"AttachClassic fallback: SetWindowPos HWND_BOTTOM to {fbRc.Left},{fbRc.Top} {fbW}x{fbH} scale {fbScale} => {fbPosOk} (MapWindowPoints rc {fbRc.Left},{fbRc.Top} virtual screen fallback visible behind icons/taskbar)");
            }
            catch (Exception ex) { Log($"AttachClassic fallback SetWindowPos failed: {ex.Message}"); }
            try { _interop.ShowWindow(hwnd, DesktopNative.SW_SHOWNA); } catch { }
            // Re-assert bottom after ShowWindow — ShowWindow can bring to top, so push back to bottom behind taskbar/icons and disable rounding again.
            try { _interop.SetWindowPos(hwnd, DesktopNative.HWND_BOTTOM, 0, 0, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE); } catch { }
            try { TryDisableRoundedCorners(hwnd); } catch { }
            // Ensure icons stay above wallpaper: wallpaper is HWND_BOTTOM child of Progman, so bring SHELLDLL_DefView (and its SysListView32) to HWND_TOP.
            try
            {
                IntPtr defView = IntPtr.Zero;
                try
                {
                    var progmanForDefView = LastProgman != IntPtr.Zero ? LastProgman : _interop.FindWindow("Progman", null);
                    if (progmanForDefView != IntPtr.Zero)
                        defView = _interop.FindWindowEx(progmanForDefView, IntPtr.Zero, "SHELLDLL_DefView", null);
                }
                catch { }
                if (defView == IntPtr.Zero)
                {
                    try { defView = _interop.GetShellDefView(); } catch { }
                }
                if (defView == IntPtr.Zero)
                {
                    _interop.EnumWindows((hWnd, lParam) =>
                    {
                        IntPtr dv = IntPtr.Zero;
                        try { dv = _interop.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null); } catch { }
                        if (dv != IntPtr.Zero)
                        {
                            defView = dv;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                if (defView != IntPtr.Zero)
                {
                    bool topOk = false;
                    try { topOk = _interop.SetWindowPos(defView, DesktopNative.HWND_TOP, 0, 0, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE); } catch { }
                    Log($"AttachClassic fallback: SetWindowPos DefView=0x{defView.ToInt64():X} HWND_TOP => {topOk} (icons on top of wallpaper)");
                    try
                    {
                        var listView = _interop.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                        if (listView != IntPtr.Zero)
                        {
                            bool lvOk = false;
                            try { lvOk = _interop.SetWindowPos(listView, DesktopNative.HWND_TOP, 0, 0, 0, 0, DesktopNative.SWP_NOMOVE | DesktopNative.SWP_NOSIZE | DesktopNative.SWP_NOACTIVATE); } catch { }
                            Log($"AttachClassic fallback: SetWindowPos SysListView32=0x{listView.ToInt64():X} HWND_TOP => {lvOk}");
                        }
                    }
                    catch { }
                }
                else
                {
                    Log("AttachClassic fallback: SHELLDLL_DefView not found — icons may still be behind wallpaper");
                }
            }
            catch (Exception ex) { Log($"AttachClassic fallback bring DefView on top failed: {ex.Message}"); }
            Log($"AttachClassic: fallback SW_SHOWNA HWND_BOTTOM hwnd=0x{hwnd.ToInt64():X} WorkerW not found — fallback visible behind icons/taskbar (not covering)");
            _attachedHwnd = hwnd;
            SetupHealing();
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
        IntPtr foundWorkerW = IntPtr.Zero;
        _interop.EnumWindows((hWnd, lParam) =>
        {
            var defView = _interop.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                var workerW = _interop.FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                if (workerW != IntPtr.Zero) foundWorkerW = workerW;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (foundWorkerW == IntPtr.Zero)
        {
            var progman = _interop.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                var workerW = _interop.FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);
                if (workerW != IntPtr.Zero) foundWorkerW = workerW;
            }
        }
        return foundWorkerW;
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
