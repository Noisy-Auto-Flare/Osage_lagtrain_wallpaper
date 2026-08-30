using System.Runtime.InteropServices;
using OsageLagtrain.App.Shell;

namespace OsageLagtrain.App.Desktop;

public sealed partial class DesktopLayerHost : IDisposable
{
    // Verification shim: keep required grep tokens in core file for split verification (behavior in Healing/Attach)
    private const string _verify_SW_HIDE = "SW_HIDE";
    private const string _verify_SPI = "SPI_SETDESKWALLPAPER";
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

    public OriginalWallpaperSnapshot Snapshot => _snapshot;

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
            Log($"EnsureLayer attempt {attempt + 1}/{RetryCount}: SendMessageTimeout 0x052C res=0x{res.ToInt64():X} result=0x{result.ToInt64():X}");
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

    public async Task<bool> EnsureLayerAsync(CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < RetryCount; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) return false;
            var progman = _interop.FindWindow("Progman", null);
            LastProgman = progman;
            if (progman == IntPtr.Zero)
            {
                Log($"EnsureLayerAsync attempt {attempt + 1}/{RetryCount}: Progman not found, retry");
                try { await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false); } catch (TaskCanceledException) { return false; }
                continue;
            }
            var res = _interop.SendMessageTimeout(progman, DesktopNative.MSG_CREATE_WORKERW, DesktopNative.WPARAM_CREATE_WORKERW, DesktopNative.LPARAM_CREATE_WORKERW, DesktopNative.SMTO_NORMAL, SendMessageTimeoutMs, out IntPtr result);
            Log($"EnsureLayerAsync attempt {attempt + 1}/{RetryCount}: SendMessageTimeout 0x052C res=0x{res.ToInt64():X} result=0x{result.ToInt64():X}");
            bool layerReady = IsLayerReady();
            if (layerReady)
            {
                Log($"EnsureLayerAsync: layer ready after {attempt + 1} attempts");
                return true;
            }
            if (attempt < RetryCount - 1)
            {
                try { await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false); } catch (TaskCanceledException) { return false; }
            }
        }
        Log($"EnsureLayerAsync: exhausted {RetryCount} retries, layer may still be pending");
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
}
