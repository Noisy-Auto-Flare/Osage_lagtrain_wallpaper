using System.Runtime.InteropServices;

namespace OsageLagtrain.App.Rendering;

/// <summary>
/// DComp interop helpers. Stubbed for CI — real window not required in tests.
/// Raised path uses CreateTargetForHwnd(hwnd, true) + identity Visual (1:1 physical).
/// </summary>
internal static class NativeRenderingInterop
{
    // DComp via dcomp.dll (legacy) — stub signatures for testability
    // Actual raised fix uses Windows.UI.Composition / Microsoft.UI.Composition via WindowsAppSDK.
    // We expose P/Invokes that can be mocked.

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref Desktop.RECT lpPoints, uint cPoints);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetrics(int nIndex);

    // DComp creation — optional, may fail on non-Win11 or in test harness; we handle gracefully
    [DllImport("dcomp.dll", SetLastError = true)]
    public static extern int DCompositionCreateDevice(IntPtr dxgiDevice, ref Guid iid, out IntPtr dcompDevice);

    public const int PRIMARY_DPI = 96;

    /// <summary>
    /// Per-monitor scale = GetDpiForWindow(hwnd) / PrimaryDpi
    /// Spec: NOT 96 divisor alone — PrimaryDpi = GetDpiForWindow(primary) or GetDpiForSystem() or 96 fallback.
    /// Caller should pass primaryDpi from GetDpiForWindow(primaryHwnd) for correctness at 150%.
    /// </summary>
    public static double ComputeDpiScale(int hwndDpi, int primaryDpi)
    {
        if (primaryDpi <= 0) primaryDpi = PRIMARY_DPI;
        if (hwndDpi <= 0) hwndDpi = primaryDpi;
        return (double)hwndDpi / primaryDpi;
    }

    public static double ComputeDpiScaleForWindow(IntPtr hwnd, Func<IntPtr, int> getDpiForWindow, int primaryDpi)
    {
        int dpi = 96;
        try { dpi = getDpiForWindow(hwnd); } catch { }
        return ComputeDpiScale(dpi, primaryDpi);
    }
}
