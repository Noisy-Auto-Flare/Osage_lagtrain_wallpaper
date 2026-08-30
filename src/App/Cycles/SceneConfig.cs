using System.Text.Json;
using System.Text.RegularExpressions;

namespace OsageLagtrain.App.Cycles;

/// <summary>Scene config matching docs/scene.json.schema.json</summary>
public sealed record SceneConfig
{
    private static readonly Regex IdRegex = new(@"^[a-z0-9_-]{1,32}$", RegexOptions.Compiled);
    private static readonly Regex ColorRegex = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public required string Id { get; init; }
    public string? Title { get; init; }
    public int Fps { get; init; } = 12;
    public SceneMode? Mode { get; init; }
    public int? LoopCount { get; init; }
    public int HoldLastMs { get; init; } = 0;
    public int? PostEventDelayMs { get; init; }
    public string IdleColor { get; init; } = "#b2b2b2";

    public void Validate(string jsonPath = "scene.json")
    {
        if (string.IsNullOrEmpty(Id) || !IdRegex.IsMatch(Id))
            throw new SchemaValidationException($"id must match ^[a-z0-9_-]{{1,32}}$, got '{Id}'", $"{jsonPath}#/id");
        if (Title != null && Title.Length == 0)
            throw new SchemaValidationException("title must be non-empty string", $"{jsonPath}#/title");
        if (Fps < 1 || Fps > 30)
            throw new SchemaValidationException($"fps must be integer 1..30, got {Fps}", $"{jsonPath}#/fps");
        if (Mode is SceneMode.StringMode sm && !SceneMode.StringMode.Allowed.Contains(sm.Value))
            throw new SchemaValidationException($"mode must be once|loop|pingpong, got '{sm.Value}'", $"{jsonPath}#/mode");
        if (Mode is SceneMode.CountMode cm && (cm.Count < 1 || cm.Count > 100))
            throw new SchemaValidationException($"mode.count must be 1..100, got {cm.Count}", $"{jsonPath}#/mode/count");
        if (LoopCount.HasValue && (LoopCount < 1 || LoopCount > 100))
            throw new SchemaValidationException($"loopCount must be 1..100, got {LoopCount}", $"{jsonPath}#/loopCount");
        if (HoldLastMs < 0 || HoldLastMs > 5000)
            throw new SchemaValidationException($"holdLastMs must be 0..5000, got {HoldLastMs}", $"{jsonPath}#/holdLastMs");
        if (PostEventDelayMs.HasValue && (PostEventDelayMs < 0 || PostEventDelayMs > 5000))
            throw new SchemaValidationException($"postEventDelayMs must be 0..5000, got {PostEventDelayMs}", $"{jsonPath}#/postEventDelayMs");
        if (!ColorRegex.IsMatch(IdleColor))
            throw new SchemaValidationException($"idleColor must match ^#[0-9a-fA-F]{{6}}$, got '{IdleColor}'", $"{jsonPath}#/idleColor");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new SceneModeJsonConverter() }
    };

    public static SceneConfig Parse(string json, string jsonPath = "scene.json")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException ex) { throw new SchemaValidationException($"Invalid JSON: {ex.Message}", jsonPath, ex); }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new SchemaValidationException("scene.json root must be object", jsonPath);

        if (!root.TryGetProperty("id", out var idProp))
            throw new SchemaValidationException("Missing required property 'id'", $"{jsonPath}#/id");
        if (idProp.ValueKind != JsonValueKind.String)
            throw new SchemaValidationException("id must be string", $"{jsonPath}#/id");
        var id = idProp.GetString()!;

        string? title = null;
        if (root.TryGetProperty("title", out var t))
        {
            if (t.ValueKind != JsonValueKind.String) throw new SchemaValidationException("title must be string", $"{jsonPath}#/title");
            title = t.GetString();
        }

        int fps = 12;
        if (root.TryGetProperty("fps", out var fpsProp))
        {
            if (fpsProp.ValueKind != JsonValueKind.Number || !fpsProp.TryGetInt32(out fps))
                throw new SchemaValidationException($"fps must be integer 1..30, got {fpsProp}", $"{jsonPath}#/fps");
        }

        SceneMode? mode = null;
        if (root.TryGetProperty("mode", out var modeProp))
        {
            var raw = modeProp.GetRawText();
            try
            {
                mode = JsonSerializer.Deserialize<SceneMode>(raw, JsonOpts);
            }
            catch (JsonException ex) { throw new SchemaValidationException(ex.Message, $"{jsonPath}#/mode", ex); }
        }

        int? loopCount = null;
        if (root.TryGetProperty("loopCount", out var lc))
        {
            if (lc.ValueKind != JsonValueKind.Number || !lc.TryGetInt32(out var v))
                throw new SchemaValidationException($"loopCount must be integer 1..100, got {lc}", $"{jsonPath}#/loopCount");
            loopCount = v;
        }

        int holdLastMs = 0;
        if (root.TryGetProperty("holdLastMs", out var hlm))
        {
            if (hlm.ValueKind != JsonValueKind.Number || !hlm.TryGetInt32(out holdLastMs))
                throw new SchemaValidationException($"holdLastMs must be integer 0..5000, got {hlm}", $"{jsonPath}#/holdLastMs");
        }

        int? postEventDelayMs = null;
        if (root.TryGetProperty("postEventDelayMs", out var ped))
        {
            if (ped.ValueKind != JsonValueKind.Number || !ped.TryGetInt32(out var v))
                throw new SchemaValidationException($"postEventDelayMs must be integer 0..5000, got {ped}", $"{jsonPath}#/postEventDelayMs");
            postEventDelayMs = v;
        }

        string idleColor = "#b2b2b2";
        if (root.TryGetProperty("idleColor", out var ic))
        {
            if (ic.ValueKind != JsonValueKind.String) throw new SchemaValidationException($"idleColor must be string ^#[0-9a-fA-F]{{6}}$, got {ic}", $"{jsonPath}#/idleColor");
            idleColor = ic.GetString()!;
        }

        var allowed = new HashSet<string> { "id", "title", "fps", "mode", "loopCount", "holdLastMs", "postEventDelayMs", "idleColor" };
        foreach (var p in root.EnumerateObject())
            if (!allowed.Contains(p.Name))
                throw new SchemaValidationException($"Unknown property '{p.Name}'", $"{jsonPath}#/{p.Name}");

        var cfg = new SceneConfig
        {
            Id = id,
            Title = title,
            Fps = fps,
            Mode = mode,
            LoopCount = loopCount,
            HoldLastMs = holdLastMs,
            PostEventDelayMs = postEventDelayMs,
            IdleColor = idleColor
        };
        cfg.Validate(jsonPath);
        return cfg;
    }

    public static SceneConfig ParseFile(string path)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) { throw new SchemaValidationException($"Cannot read {path}: {ex.Message}", path, ex); }
        return Parse(json, path);
    }
}
