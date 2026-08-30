using System.Text.Json;
using System.Text.RegularExpressions;

namespace OsageLagtrain.App.Cycles;

public sealed record SettingsConfig
{
    private static readonly Regex ColorRegex = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex IdRegex = new(@"^[a-z0-9_-]{1,32}$", RegexOptions.Compiled);

    public string CyclesRoot { get; init; } = "./cycles";
    public int PostEventDelayMs { get; init; } = 500;
    public string SelectionPolicy { get; init; } = "randomNoRepeat";
    public int NoRepeatWindow { get; init; } = 3;
    public string IdleColor { get; init; } = "#b2b2b2";
    public bool Autostart { get; init; } = false;
    public Dictionary<string, string[]>? AppMap { get; init; }

    public static readonly HashSet<string> AllowedPolicies = new(StringComparer.Ordinal)
        { "randomNoRepeat", "randomPure", "sequentialByName", "sequentialByMtime" };

    public void Validate(string jsonPath = "settings.json")
    {
        if (string.IsNullOrWhiteSpace(CyclesRoot))
            throw new SchemaValidationException("cyclesRoot must be non-empty string", $"{jsonPath}#/cyclesRoot");
        if (PostEventDelayMs < 0 || PostEventDelayMs > 5000)
            throw new SchemaValidationException($"postEventDelayMs must be 0..5000, got {PostEventDelayMs}", $"{jsonPath}#/postEventDelayMs");
        if (!AllowedPolicies.Contains(SelectionPolicy))
            throw new SchemaValidationException($"selectionPolicy must be one of {string.Join("|", AllowedPolicies)}, got '{SelectionPolicy}'", $"{jsonPath}#/selectionPolicy");
        if (NoRepeatWindow < 0 || NoRepeatWindow > 20)
            throw new SchemaValidationException($"noRepeatWindow must be 0..20, got {NoRepeatWindow}", $"{jsonPath}#/noRepeatWindow");
        if (!ColorRegex.IsMatch(IdleColor))
            throw new SchemaValidationException($"idleColor must match ^#[0-9a-fA-F]{{6}}$, got '{IdleColor}'", $"{jsonPath}#/idleColor");
        if (AppMap != null)
        {
            foreach (var kv in AppMap)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    throw new SchemaValidationException("appMap key must be non-empty exe name", $"{jsonPath}#/appMap");
                foreach (var sid in kv.Value)
                    if (!IdRegex.IsMatch(sid))
                        throw new SchemaValidationException($"appMap scene id must match ^[a-z0-9_-]{{1,32}}$, got '{sid}'", $"{jsonPath}#/appMap/{kv.Key}");
            }
        }
    }

    public static SettingsConfig Parse(string json, string jsonPath = "settings.json")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException ex) { throw new SchemaValidationException($"Invalid JSON: {ex.Message}", jsonPath, ex); }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new SchemaValidationException("settings.json root must be object", jsonPath);

        string cyclesRoot = "./cycles";
        if (root.TryGetProperty("cyclesRoot", out var cr))
        {
            if (cr.ValueKind != JsonValueKind.String) throw new SchemaValidationException("cyclesRoot must be string", $"{jsonPath}#/cyclesRoot");
            cyclesRoot = cr.GetString()!;
        }
        int postEventDelayMs = 500;
        if (root.TryGetProperty("postEventDelayMs", out var ped))
        {
            if (ped.ValueKind != JsonValueKind.Number || !ped.TryGetInt32(out postEventDelayMs))
                throw new SchemaValidationException($"postEventDelayMs must be integer 0..5000, got {ped}", $"{jsonPath}#/postEventDelayMs");
        }
        string selectionPolicy = "randomNoRepeat";
        if (root.TryGetProperty("selectionPolicy", out var sp))
        {
            if (sp.ValueKind != JsonValueKind.String) throw new SchemaValidationException("selectionPolicy must be string", $"{jsonPath}#/selectionPolicy");
            selectionPolicy = sp.GetString()!;
        }
        int noRepeatWindow = 3;
        if (root.TryGetProperty("noRepeatWindow", out var nrw))
        {
            if (nrw.ValueKind != JsonValueKind.Number || !nrw.TryGetInt32(out noRepeatWindow))
                throw new SchemaValidationException($"noRepeatWindow must be integer 0..20, got {nrw}", $"{jsonPath}#/noRepeatWindow");
        }
        string idleColor = "#b2b2b2";
        if (root.TryGetProperty("idleColor", out var ic))
        {
            if (ic.ValueKind != JsonValueKind.String) throw new SchemaValidationException("idleColor must be string", $"{jsonPath}#/idleColor");
            idleColor = ic.GetString()!;
        }
        bool autostart = false;
        if (root.TryGetProperty("autostart", out var ast))
        {
            if (ast.ValueKind != JsonValueKind.True && ast.ValueKind != JsonValueKind.False)
                throw new SchemaValidationException("autostart must be boolean", $"{jsonPath}#/autostart");
            autostart = ast.GetBoolean();
        }
        Dictionary<string, string[]>? appMap = null;
        if (root.TryGetProperty("appMap", out var am))
        {
            if (am.ValueKind != JsonValueKind.Object) throw new SchemaValidationException("appMap must be object", $"{jsonPath}#/appMap");
            appMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in am.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) throw new SchemaValidationException($"appMap[{prop.Name}] must be array", $"{jsonPath}#/appMap/{prop.Name}");
                var list = new List<string>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.String) throw new SchemaValidationException($"appMap[{prop.Name}] items must be strings", $"{jsonPath}#/appMap/{prop.Name}");
                    list.Add(el.GetString()!);
                }
                appMap[prop.Name] = list.ToArray();
            }
        }

        var allowed = new HashSet<string> { "cyclesRoot", "postEventDelayMs", "selectionPolicy", "noRepeatWindow", "idleColor", "autostart", "appMap" };
        foreach (var p in root.EnumerateObject())
            if (!allowed.Contains(p.Name))
                throw new SchemaValidationException($"Unknown property '{p.Name}'", $"{jsonPath}#/{p.Name}");

        var cfg = new SettingsConfig
        {
            CyclesRoot = cyclesRoot,
            PostEventDelayMs = postEventDelayMs,
            SelectionPolicy = selectionPolicy,
            NoRepeatWindow = noRepeatWindow,
            IdleColor = idleColor,
            Autostart = autostart,
            AppMap = appMap
        };
        cfg.Validate(jsonPath);
        return cfg;
    }

    public static SettingsConfig ParseFile(string path)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) { throw new SchemaValidationException($"Cannot read {path}: {ex.Message}", path, ex); }
        return Parse(json, path);
    }
}
