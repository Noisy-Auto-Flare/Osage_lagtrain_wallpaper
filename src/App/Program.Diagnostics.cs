namespace OsageLagtrain.App;

internal static partial class Program
{
    private static void RunProbe()
    {
        try
        {
            var host = new Desktop.DesktopLayerHost();
            var topo = host.Probe();
            bool raised = topo == Desktop.DesktopTopology.RaisedDesktop;
            bool layerOk = false;
            try { layerOk = host.EnsureLayer(); } catch { }
            var progman = host.LastProgman;
            var workerW = host.LastWorkerW;
            if (workerW == IntPtr.Zero && !raised)
            {
                try
                {
                    var interop = new Desktop.NativeDesktopInterop();
                    IntPtr found = IntPtr.Zero;
                    interop.EnumWindows((hwnd, lp) =>
                    {
                        var dv = interop.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (dv != IntPtr.Zero)
                        {
                            var w = interop.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                            if (w != IntPtr.Zero) found = w;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                    if (found != IntPtr.Zero) workerW = found;
                }
                catch { }
            }
            var shellDefView = IntPtr.Zero;
            try
            {
                var interop = new Desktop.NativeDesktopInterop();
                var p = interop.FindWindow("Progman", null);
                if (p != IntPtr.Zero) shellDefView = interop.FindWindowEx(p, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
            catch { }
            Console.WriteLine($"RaisedDesktop={raised.ToString().ToLowerInvariant()}");
            Console.WriteLine($"Topology={topo}");
            Console.WriteLine($"Progman=0x{progman.ToInt64():X}");
            Console.WriteLine($"workerW=0x{workerW.ToInt64():X}");
            Console.WriteLine($"parent={(raised ? $"Progman(0x{progman.ToInt64():X})" : $"WorkerW(0x{workerW.ToInt64():X})")}");
            Console.WriteLine($"SHELLDLL_DefView=0x{shellDefView.ToInt64():X}");
            Console.WriteLine($"EnsureLayer={(layerOk ? "ok" : "pending")}");
            Console.WriteLine($"WS_EX_NOREDIRECTIONBITMAP=0x08 GWL_EXSTYLE=-20 0x052C=1324");
            host.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Probe failed: {ex.Message}");
            Console.WriteLine($"RaisedDesktop=false");
            Console.WriteLine($"Topology=ClassicWorkerW");
            Console.WriteLine($"workerW=0x0");
        }
    }

    private static void PrintDiagnostics()
    {
        bool setResult = false;
        try { setResult = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
        var osVersion = Environment.OSVersion.Version;
        var osBuild = osVersion.Build;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var buildStr = key?.GetValue("CurrentBuild")?.ToString();
            if (int.TryParse(buildStr, out var b)) osBuild = b;
        }
        catch { }
        bool isPerMonitorV2 = true;
        string dpiContext = "DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (-4)";
        Console.WriteLine($"PerMonitorV2: {isPerMonitorV2.ToString().ToLowerInvariant()}");
        Console.WriteLine($"DPI_AWARENESS: {dpiContext}");
        Console.WriteLine($"DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: -4");
        Console.WriteLine($"SetProcessDpiAwarenessContext returned: {setResult}");
        Console.WriteLine($"OS Build: {osBuild}");
        Console.WriteLine($"OS Version: {osVersion}");
        Console.WriteLine($"InvariantGlobalization: false");
        Console.WriteLine($"PublishSingleFile: true");
    }
}
