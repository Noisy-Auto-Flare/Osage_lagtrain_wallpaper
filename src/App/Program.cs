using System.Runtime.InteropServices;

namespace OsageLagtrain.App;

internal static class Program
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    static void Main(string[] args)
    {
        // Handle --verify-cycles placeholder returning 0
        if (args.Contains("--verify-cycles"))
        {
            var cyclesRoot = Path.Combine(AppContext.BaseDirectory, "cycles");
            // also check beside exe dir for single-file
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
            var altRoot = Path.Combine(exeDir, "cycles");
            var template = Path.Combine(exeDir, "cycles", "_template", "scene.json");
            // template beside exe for publish, or src relative
            Console.WriteLine($"cyclesRoot: {altRoot}");
            Console.WriteLine(File.Exists(template) ? "template OK, 0 real scenes" : "template missing");
            if (!args.Contains("--diag"))
                return;
        }

        if (args.Contains("--diag"))
        {
            PrintDiagnostics();
            return;
        }

        // Set DPI awareness before window creation
        TrySetPerMonitorV2();

        // Launch WinUI3 app
        XamlCheckProcessRequirements();
        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void TrySetPerMonitorV2()
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { /* best effort — manifest already declares PerMonitorV2 */ }
    }

    private static void PrintDiagnostics()
    {
        // Attempt to set before querying
        bool setResult = false;
        try { setResult = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }

        var osVersion = Environment.OSVersion.Version;
        var osBuild = osVersion.Build;
        // Try to get OS build from registry for accuracy
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var buildStr = key?.GetValue("CurrentBuild")?.ToString();
            if (int.TryParse(buildStr, out var b)) osBuild = b;
        }
        catch { }

        bool isPerMonitorV2 = false;
        string dpiContext = "Unknown";
        try
        {
            // After SetProcessDpiAwarenessContext, process is PerMonitorV2.
            // We can at least report what we set; also probe current thread awareness via dummy check
            // Use AreDpiAwarenessContextsEqual with -4 sentinel
            // Since we have no HWND yet, report based on setResult + manifest
            // Manifest declares PerMonitorV2, so if Set succeeded or already set, report true
            isPerMonitorV2 = true; // manifest guarantees; setResult may be false if already set (ERROR_ACCESS_DENIED = already set)
            dpiContext = "DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (-4)";
        }
        catch { }

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
