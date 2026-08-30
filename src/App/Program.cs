using System.Runtime.InteropServices;

namespace OsageLagtrain.App;

internal static partial class Program
{
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
        if (args.Contains("--probe"))
        {
            RunProbe();
            return;
        }
        if (args.Contains("--render-test"))
        {
            RunRenderTest(args);
            return;
        }
        if (args.Contains("--monitor-test") || args.Contains("--simulate"))
        {
            RunMonitorTest(args);
            return;
        }
        if (args.Contains("--verify-cycles"))
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
            var template = Path.Combine(exeDir, "cycles", "_template", "scene.json");
            Console.WriteLine($"cyclesRoot: {Path.Combine(exeDir, "cycles")}");
            Console.WriteLine(File.Exists(template) ? "template OK, 0 real scenes" : "template missing");
            if (!args.Contains("--diag"))
                return;
        }
        if (args.Contains("--toggle-enable") || args.Contains("--tray-test"))
        {
            RunTrayTest(args);
            return;
        }
        if (args.Contains("--diag"))
        {
            PrintDiagnostics();
            return;
        }
        TrySetPerMonitorV2();
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
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { }
    }
}
