namespace OsageLagtrain.App.Desktop;

public sealed class DisplayManager
{
    private readonly IDesktopInterop _interop;

    public DisplayManager(IDesktopInterop interop)
    {
        _interop = interop;
    }

    public RECT VirtualScreenBounds
    {
        get
        {
            int x = _interop.GetSystemMetrics(DesktopNative.SM_XVIRTUALSCREEN);
            int y = _interop.GetSystemMetrics(DesktopNative.SM_YVIRTUALSCREEN);
            int w = _interop.GetSystemMetrics(DesktopNative.SM_CXVIRTUALSCREEN);
            int h = _interop.GetSystemMetrics(DesktopNative.SM_CYVIRTUALSCREEN);
            // Fallback if virtual metrics zero (e.g., mock)
            if (w == 0 || h == 0)
            {
                w = _interop.GetSystemMetrics(DesktopNative.SM_CXSCREEN);
                h = _interop.GetSystemMetrics(DesktopNative.SM_CYSCREEN);
                if (w == 0) w = 1920;
                if (h == 0) h = 1080;
            }
            return new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        }
    }

    public double GetScaleForWindow(IntPtr hwnd)
    {
        try
        {
            int dpi = _interop.GetDpiForWindow(hwnd);
            if (dpi <= 0) dpi = (int)_interop.GetDpiForSystem();
            if (dpi <= 0) dpi = DesktopNative.PRIMARY_DPI;
            return (double)dpi / DesktopNative.PRIMARY_DPI;
        }
        catch
        {
            return 1.0;
        }
    }

    public double GetScaleForMonitor(IntPtr hMonitor)
    {
        try
        {
            int hr = _interop.GetDpiForMonitor(hMonitor, DesktopNative.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            if (hr == 0 && dpiX > 0) return (double)dpiX / DesktopNative.PRIMARY_DPI;
            return 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    public IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var list = new List<MonitorInfo>();
        try
        {
            DesktopNative.MonitorEnumProc proc = (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var dpiScale = GetScaleForMonitor(hMon);
                list.Add(new MonitorInfo { Handle = hMon, Bounds = rc, DpiScale = dpiScale });
                return true;
            };
            DesktopNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        }
        catch { }
        if (list.Count == 0)
        {
            var vs = VirtualScreenBounds;
            list.Add(new MonitorInfo { Handle = IntPtr.Zero, Bounds = vs, DpiScale = 1.0 });
        }
        return list;
    }
}

public sealed class MonitorInfo
{
    public IntPtr Handle { get; init; }
    public RECT Bounds { get; init; }
    public double DpiScale { get; init; }
}
