using System.Text.Json;
using OsageLagtrain.App.Cycles;

namespace OsageLagtrain.App.Ui;

public interface ISettingsStore
{
    string FilePath { get; }
    SettingsConfig Load();
    void Save(SettingsConfig config);
}

public sealed class SettingsStore : ISettingsStore
{
    private readonly string _filePath;

    public string FilePath => _filePath;

    public SettingsStore(string? filePath = null, string? exeDirOverride = null)
    {
        _filePath = filePath ?? ResolveSettingsPath(exeDirOverride);
    }

    public static string ResolveSettingsPath(string? exeDirOverride = null)
    {
        string exeDir = exeDirOverride
            ?? Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
            ?? AppContext.BaseDirectory;
        string exeCandidate = Path.Combine(exeDir, "settings.json");
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OsageLagtrain", "settings.json");
        try
        {
            var probe = Path.Combine(exeDir, ".writetest");
            File.Create(probe).Close();
            File.Delete(probe);
            return exeCandidate;
        }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (IOException) { return fallback; }
    }

    public SettingsConfig Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new SettingsConfig();
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new SettingsConfig();
            return SettingsConfig.Parse(json, _filePath);
        }
        catch (SchemaValidationException)
        {
            Log($"Settings corrupted at {_filePath}, resetting to defaults");
            return new SettingsConfig();
        }
        catch (Exception ex)
        {
            Log($"Settings load failed {_filePath}: {ex.Message}, resetting");
            return new SettingsConfig();
        }
    }

    public void Save(SettingsConfig config)
    {
        config.Validate(_filePath);
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Build json via serialization respecting schema field names camelCase
        var opts = new JsonSerializerOptions { WriteIndented = true };
        // Manual object to keep snake? SettingsConfig uses camelCase properties via JSON?
        // We serialize via anonymous to control names exactly as schema expects
        var payload = new Dictionary<string, object?>();
        payload["cyclesRoot"] = config.CyclesRoot;
        payload["postEventDelayMs"] = config.PostEventDelayMs;
        payload["selectionPolicy"] = config.SelectionPolicy;
        payload["noRepeatWindow"] = config.NoRepeatWindow;
        payload["idleColor"] = config.IdleColor;
        payload["autostart"] = config.Autostart;
        if (config.AppMap != null) payload["appMap"] = config.AppMap;
        var json = JsonSerializer.Serialize(payload, opts);

        // atomic write tmp -> Replace/Move
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
            try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
            try { File.Move(tmp, _filePath); } catch { }
        }
    }

    private static void Log(string msg)
    {
        try { Console.WriteLine($"[SettingsStore] {msg}"); } catch { }
        try { System.Diagnostics.Debug.WriteLine($"[SettingsStore] {msg}"); } catch { }
    }
}
