using System.Text.Json;

namespace OsageLagtrain.App.Cycles;

public sealed class HistoryStore
{
    private readonly string _filePath;
    private readonly int _maxBytes;

    public string FilePath => _filePath;

    public HistoryStore(string filePath, int maxBytes = 1024)
    {
        _filePath = filePath;
        _maxBytes = maxBytes;
    }

    public static string ResolveHistoryPath(string? exeDirOverride = null)
    {
        string exeDir = exeDirOverride ?? Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        string exeHistoryCandidate = Path.Combine(exeDir, "history.json");
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OsageLagtrain", "history.json");
        // Use same writability probe as cycles: if exeDir writable, history lives beside exe else AppData
        try
        {
            var probe = Path.Combine(exeDir, ".writetest");
            File.Create(probe).Close();
            File.Delete(probe);
            return exeHistoryCandidate;
        }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (IOException) { return fallback; }
    }

    public History Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
            return History.Parse(json, _filePath);
        }
        catch (SchemaValidationException)
        {
            Log($"History corrupted at {_filePath}, resetting to []");
            return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
        }
        catch (Exception ex)
        {
            Log($"History load failed {_filePath}: {ex.Message}, resetting");
            return new History { Recent = Array.Empty<string>(), MtimeCursor = null };
        }
    }

    public void Save(History history, int windowN)
    {
        // sliding window truncated to N on write
        var recent = history.Recent ?? Array.Empty<string>();
        List<string> truncated = recent.TakeLast(Math.Max(0, windowN)).ToList();
        if (windowN == 0) truncated.Clear();

        // also enforce maxBytes 1KB: progressively truncate oldest until json fits
        // Build json and check UTF8 byte length
        string json = BuildJson(truncated, history.MtimeCursor);
        while (System.Text.Encoding.UTF8.GetByteCount(json) > _maxBytes && truncated.Count > 0)
        {
            truncated.RemoveAt(0);
            json = BuildJson(truncated, history.MtimeCursor);
        }

        // atomic write via temp+Replace/Move
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, json);
        try
        {
            if (!File.Exists(_filePath))
                File.Move(tmp, _filePath);
            else
#pragma warning disable CA1416
                File.Replace(tmp, _filePath, null);
#pragma warning restore CA1416
        }
        catch
        {
            // Fallback if Replace fails (e.g., cross-volume): Move after delete
            try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
            try { File.Move(tmp, _filePath); } catch { }
        }
        // Ensure file size <=maxBytes (1KB) even after move
        // If still oversized due to mtimeCursor, we already truncated recent to empty; if still >1KB, keep as-is (will be large due to cursor)
    }

    public void Append(string sceneId, int windowN)
    {
        var current = Load();
        var list = current.Recent.ToList();
        list.Add(sceneId);
        var updated = new History { Recent = list, MtimeCursor = current.MtimeCursor };
        Save(updated, windowN);
    }

    private static string BuildJson(IReadOnlyList<string> recent, string? mtimeCursor)
    {
        var obj = new { recent = recent, mtimeCursor = mtimeCursor };
        return JsonSerializer.Serialize(obj);
    }

    private static void Log(string msg)
    {
        try { Console.WriteLine($"[HistoryStore] {msg}"); } catch { }
        try { System.Diagnostics.Debug.WriteLine($"[HistoryStore] {msg}"); } catch { }
    }
}
