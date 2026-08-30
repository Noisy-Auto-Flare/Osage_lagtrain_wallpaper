using System.Runtime.InteropServices;

namespace OsageLagtrain.App.WindowMonitor;

// QUERY_USER_NOTIFICATION_STATE from shellapi.h
public enum QUNS : int
{
    QUNS_NOT_PRESENT = 1,
    QUNS_BUSY = 2,
    QUNS_RUNNING_D3D_FULL_SCREEN = 3,
    QUNS_PRESENTATION_MODE = 4,
    QUNS_ACCEPTS_NOTIFICATIONS = 5,
    QUNS_QUIET_TIME = 6,
    QUNS_APP = 7,
}

// Legacy alias kept for spec text referencing "7"; real value is 3.
// Expose correct constant for compatibility.
public static class QUNSCompat
{
    public const int QUNS_RUNNING_D3D_FULL_SCREEN_SpecAlias = 7;
}

[StructLayout(LayoutKind.Sequential)]
public struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public long Area => (long)Width * Height;
}

public struct MonitorBounds
{
    public Rect RcMonitor;
    public Rect RcWork;
    public nint MonitorHandle;
}

public static class WindowMonitorConstants
{
    public const double CoverageThreshold = 0.95;
    public const int FallbackPollMs = 500;
    public const int DebounceMs = 150;
    public const int DefaultPostEventDelayMs = 500;
    public const int ShQueryCacheMs = 500;

    // WinEvent constants this monitor subscribes to
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    // Explicitly NOT subscribed: EVENT_OBJECT_LOCATIONCHANGE = 0x800B

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWMWA_CLOAKED = 14;
    public const int GA_ROOT = 2;

    public static readonly HashSet<string> DesktopClassAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "SHELLDLL_DefView", "Shell_TrayWnd", "SysListView32"
    };
}
