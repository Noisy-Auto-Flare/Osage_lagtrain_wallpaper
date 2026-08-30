using System.Text.Json;
using OsageLagtrain.App.Cycles;

namespace OsageLagtrain.App.Shell;

/// <summary>
/// Unified persistence: settings.json, history.json, appMap.json via writability probe.
/// Atomic WriteTemp to Move/Replace with Exists check. 1KB cap for history.
/// Never checks install path via string — probe only.
/// Single-file publish compatible.
/// </summary>
public sealed class ConfigStore
{
    private readonly string _storageDir;
    private readonly int _historyMaxBytes;

    public string StorageDir => _storageDir;
    public string SettingsPath => Path.Combine(_storageDir, "settings.json");
    public string HistoryPath => Path.Combine(_storageDir, "history.json");
    public string AppMapPath => Path.Combine(_storageDir, "appMap.json");

    /// <summary>StaticDir is always LOCALAPPDATA — not probed, per spec for snapshots.</summary>
    public static string StaticDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OsageLagtrain", "static");

    public string WallpaperSnapshotPath => Path.Combine(StaticDir, "original-wallpaper.txt");
    public string WallpaperTsvPath => Path.Combine(StaticDir, "original-wallpaper.tsv");

    public ConfigStore(string? exeDirOverride = null, string? storageDirOverride = null, int historyMaxBytes = 1024)
    {
        _storageDir = storageDirOverride ?? GetStorageDir(exeDirOverride);
        _historyMaxBytes = historyMaxBytes;
    }

    /// <summary>
    /// Probe writability: try File.Create(exeDir/.writetest) else fallback %APPDATA%.
    /// No Program Files string check.
    /// </summary>
    public static string GetStorageDir(string? exeDirOverride = null)
    {
        string exeDir = exeDirOverride
            ?? Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
            ?? AppContext.BaseDirectory;
        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OsageLagtrain");

        try
        {
            var probe = Path.Combine(exeDir, ".writetest");
            File.Create(probe).Close();
            File.Delete(probe);
            return exeDir;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    public static string ResolveSettingsPath(string? exeDirOverride = null)
        => Path.Combine(GetStorageDir(exeDirOverride), "settings.json");

    public static string ResolveHistoryPath(string? exeDirOverride = null)
        => Path.Combine(GetStorageDir(exeDirOverride), "history.json");

    public static string ResolveAppMapPath(string? exeDirOverride = null)
        => Path.Combine(GetStorageDir(exeDirOverride), "appMap.json");

    // ---- Atomic write helper ----

    public static void AtomicWrite(string destPath, string content)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = destPath + ".tmp";
        File.WriteAllText(tmp, content);
        try
        {
            if (!File.Exists(destPath))
                File.Move(tmp, destPath);
            else
            {
#pragma warning disable CA1416
                File.Replace(tmp, destPath, null);
#pragma warning restore CA1416
            }
        }
        catch
        {
            try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
            try { File.Move(tmp, destPath); } catch { }
        }
        // cleanup orphan tmp if still exists and dest exists
        try { if (File.Exists(tmp) && File.Exists(destPath)) File.Delete(tmp); } catch { }
    }

    // ---- Settings ----

    public SettingsConfig LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new SettingsConfig();
            var json = File.ReadAllText(SettingsPath);
            if (string.IsNullOrWhiteSpace(json))
                return new SettingsConfig();
            return SettingsConfig.Parse(json, SettingsPath);
        }
        catch (SchemaValidationException)
        {
            Log($"Settings corrupted at {SettingsPath}, resetting");
            return new SettingsConfig();
        }
        catch (Exception ex)
        {
            Log($"Settings load failed {SettingsPath}: {ex.Message}, resetting");
            return new SettingsConfig();
        }
    }

    public void SaveSettings(SettingsConfig config)
    {
        config.Validate(SettingsPath);
        // normalize cyclesRoot: if absolute outside storage, store relative if possible — never lose but avoid absolute outside
        // Keep as-is if already relative or inside cyclesRoot; spec forbids storing absolute cycles outside cyclesRoot
        var payload = new Dictionary<string, object?>();
        payload["cyclesRoot"] = config.CyclesRoot;
        payload["postEventDelayMs"] = config.PostEventDelayMs;
        payload["selectionPolicy"] = config.SelectionPolicy;
        payload["noRepeatWindow"] = config.NoRepeatWindow;
        payload["idleColor"] = config.IdleColor;
        payload["autostart"] = config.Autostart;
        if (config.AppMap != null) payload["appMap"] = config.AppMap;
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(payload, opts);
        AtomicWrite(SettingsPath, json);
    }

    // ---- History ----

    public History LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
            var json = File.ReadAllText(HistoryPath);
            if (string.IsNullOrWhiteSpace(json))
                return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
            return History.Parse(json, HistoryPath);
        }
        catch (SchemaValidationException)
        {
            Log($"History corrupted at {HistoryPath}, resetting");
            return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
        }
        catch (Exception ex)
        {
            Log($"History load failed {HistoryPath}: {ex.Message}, resetting");
            return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
        }
    }

    public void SaveHistory(History history, int windowN)
    {
        var recent = history.Recent ?? Array.Empty<string>();
        List<string> truncated = recent.TakeLast(Math.Max(0, windowN)).ToList();
        if (windowN == 0) truncated.Clear();

        string json = BuildHistoryJson(truncated, history.MtimeCursor);
        while (System.Text.Encoding.UTF8.GetByteCount(json) > _historyMaxBytes && truncated.Count > 0)
        {
            truncated.RemoveAt(0);
            json = BuildHistoryJson(truncated, history.MtimeCursor);
        }

        AtomicWrite(HistoryPath, json);
    }

    /// <summary>Append + flush atomic after each Advance, truncate to noRepeatWindow, 1KB cap.</summary>
    public void AppendHistory(string sceneId, int windowN)
    {
        var current = LoadHistory();
        var list = current.Recent.ToList();
        list.Add(sceneId);
        var updated = new History { Recent = list, MtimeCursor = sceneId };
        SaveHistory(updated, windowN);
    }

    private static string BuildHistoryJson(IReadOnlyList<string> recent, string? mtimeCursor)
    {
        var obj = new { recent = recent, mtimeCursor = mtimeCursor };
        return JsonSerializer.Serialize(obj);
    }

    // ---- AppMap ----

    public Dictionary<string, string[]> LoadAppMap()
    {
        try
        {
            if (!File.Exists(AppMapPath))
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var json = File.ReadAllText(AppMapPath);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var arr = prop.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
                dict[prop.Name] = arr;
            }
            return dict;
        }
        catch (Exception ex)
        {
            Log($"AppMap load failed {AppMapPath}: {ex.Message}, resetting");
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveAppMap(Dictionary<string, string[]> map)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(map, opts);
        AtomicWrite(AppMapPath, json);
    }

    private static void Log(string msg)
    {
        try { Console.WriteLine($"[ConfigStore] {msg}"); } catch { }
        try { System.Diagnostics.Debug.WriteLine($"[ConfigStore] {msg}"); } catch { }
    }
}
