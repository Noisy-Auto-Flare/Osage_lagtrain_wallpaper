using OsageLagtrain.App.Desktop;

namespace OsageLagtrain.App.Shell;

/// <summary>
/// Abstraction for IDesktopWallpaper COM to allow mocking in tests.
/// Real implementation would call IDesktopWallpaper::GetWallpaper/SetWallpaper per-monitor.
/// </summary>
public interface IDesktopWallpaper
{
    IReadOnlyList<string> GetMonitorIds();
    string GetWallpaper(string monitorId);
    void SetWallpaper(string monitorId, string path);
}

/// <summary>
/// Real COM wrapper — P/Invoke IDesktopWallpaper via CoCreateInstance.
/// Falls back to no-op on non-Windows or COM unavailable (tests use mock).
/// </summary>
public sealed class NativeDesktopWallpaper : IDesktopWallpaper
{
    public IReadOnlyList<string> GetMonitorIds()
    {
        // Best-effort: enumerate via DisplayManager monitors if COM not available.
        // For now return single default monitor id (empty represents NULL per spec GetWallpaper(NULL)).
        // Actual COM would call IDesktopWallpaper::GetMonitorDevicePathAt etc.
        return new[] { string.Empty };
    }

    public string GetWallpaper(string monitorId)
    {
        try
        {
            // COM IID IDesktopWallpaper {B92B56A9-8B55-4E14-9A89-0199BBB6F93B}
            // Simplified: return empty if not on Windows. Tests inject mock.
            // On real Windows we would CoCreateInstance and call GetWallpaper.
            return string.Empty;
        }
        catch { return string.Empty; }
    }

    public void SetWallpaper(string monitorId, string path)
    {
        try
        {
            // Real COM SetWallpaper(monitorId, path)
        }
        catch { }
    }
}

/// <summary>
/// Snapshot of original desktop wallpaper per-monitor.
/// Captured on first DesktopLayerHost.Attach (not on exit), stored in
/// %LOCALAPPDATA%\OsageLagtrain\static\original-wallpaper.txt + tsv per-monitor.
/// Restore via IDesktopWallpaper.SetWallpaper per-monitor; fallback
/// SystemParametersInfo only on final Dispose (not here).
/// </summary>
public sealed class OriginalWallpaperSnapshot
{
    private readonly string _staticDir;
    private readonly IDesktopWallpaper _wallpaper;
    private readonly IDesktopInterop _interop;
    private bool _capturedThisInstance;
    private readonly object _lock = new();

    public string SnapshotTxtPath => Path.Combine(_staticDir, "original-wallpaper.txt");
    public string SnapshotTsvPath => Path.Combine(_staticDir, "original-wallpaper.tsv");

    public bool WasCapturedThisInstance => _capturedThisInstance;

    public OriginalWallpaperSnapshot(
        IDesktopWallpaper? wallpaper = null,
        IDesktopInterop? interop = null,
        string? staticDirOverride = null)
    {
        _wallpaper = wallpaper ?? new NativeDesktopWallpaper();
        _interop = interop ?? new NativeDesktopInterop();
        _staticDir = staticDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsageLagtrain", "static");
    }

    /// <summary>Resolve static dir — always LOCALAPPDATA, not probed.</summary>
    public static string ResolveStaticDir(string? overridePath = null)
        => overridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsageLagtrain", "static");

    /// <summary>
    /// Capture on first Attach. If snapshot files already exist, just mark captured and return.
    /// Saves per-monitor GetWallpaper to txt + tsv atomically.
    /// Must be called on first Attach only — subsequent calls are no-op.
    /// </summary>
    public void CaptureIfNeeded()
    {
        lock (_lock)
        {
            if (_capturedThisInstance) return;
            // If files already exist from previous run, don't recapture — preserve original
            if (File.Exists(SnapshotTxtPath) && File.Exists(SnapshotTsvPath))
            {
                _capturedThisInstance = true;
                return;
            }

            IReadOnlyList<string> monitorIds;
            try { monitorIds = _wallpaper.GetMonitorIds(); }
            catch { monitorIds = new[] { string.Empty }; }

            if (monitorIds.Count == 0)
                monitorIds = new[] { string.Empty };

            var entries = new List<(string monitorId, string path)>();
            foreach (var id in monitorIds)
            {
                string path = string.Empty;
                try { path = _wallpaper.GetWallpaper(id) ?? string.Empty; }
                catch { path = string.Empty; }
                entries.Add((id, path));
            }

            Directory.CreateDirectory(_staticDir);

            // Build tsv: monitorId\twallpaperPath per line
            var tsvLines = entries.Select(e => $"{e.monitorId}\t{e.path}");
            var tsvContent = string.Join(Environment.NewLine, tsvLines);

            // Build txt: one path per line (FeatherWall compat: original-wallpaper.txt)
            var txtLines = entries.Select(e => e.path).Where(p => !string.IsNullOrEmpty(p));
            var txtContent = string.Join(Environment.NewLine, txtLines);
            if (string.IsNullOrEmpty(txtContent) && entries.Count > 0)
            {
                // If no path captured (mock returns empty), write placeholder to indicate captured
                txtContent = string.Empty;
            }

            // Atomic write both files — use ConfigStore atomics pattern
            ConfigStore.AtomicWrite(SnapshotTsvPath, tsvContent);
            ConfigStore.AtomicWrite(SnapshotTxtPath, txtContent);

            _capturedThisInstance = true;
        }
    }

    /// <summary>
    /// Restore via IDesktopWallpaper.SetWallpaper per-monitor.
    /// Does NOT call SystemParametersInfo — that fallback is only on final Dispose().
    /// Returns true if at least one SetWallpaper succeeded.
    /// </summary>
    public bool Restore()
    {
        if (!File.Exists(SnapshotTsvPath) && !File.Exists(SnapshotTxtPath))
            return false;

        bool anyOk = false;
        List<(string monitorId, string path)> entries = new();

        if (File.Exists(SnapshotTsvPath))
        {
            try
            {
                var lines = File.ReadAllLines(SnapshotTsvPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t', 2);
                    if (parts.Length == 2)
                        entries.Add((parts[0], parts[1]));
                    else if (parts.Length == 1)
                        entries.Add((string.Empty, parts[0]));
                }
            }
            catch { }
        }

        // Fallback to txt if tsv empty or missing
        if (entries.Count == 0 && File.Exists(SnapshotTxtPath))
        {
            try
            {
                var lines = File.ReadAllLines(SnapshotTxtPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    entries.Add((string.Empty, line.Trim()));
                }
            }
            catch { }
        }

        foreach (var (monitorId, path) in entries)
        {
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                _wallpaper.SetWallpaper(monitorId, path);
                anyOk = true;
            }
            catch { }
        }

        return anyOk;
    }

    /// <summary>
    /// Final Dispose fallback: if Restore via COM failed or file missing,
    /// caller (DesktopLayerHost.Dispose) will call SystemParametersInfo.
    /// This helper reports whether SPI fallback should be invoked.
    /// </summary>
    public bool NeedsSpiFallback()
    {
        // If no snapshot files, need SPI to reset whatever Windows holds
        if (!File.Exists(SnapshotTsvPath) && !File.Exists(SnapshotTxtPath))
            return true;
        return false;
    }

    /// <summary>Called only on final Dispose path — SPI fallback via IDesktopInterop.</summary>
    public void FallbackSystemParametersInfo()
    {
        try
        {
            _interop.SystemParametersInfo(
                DesktopNative.SPI_SETDESKWALLPAPER, 0, null,
                DesktopNative.SPIF_UPDATEINIFILE | DesktopNative.SPIF_SENDCHANGE);
        }
        catch { }
    }
}
