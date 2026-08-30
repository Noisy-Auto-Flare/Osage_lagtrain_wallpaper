using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OsageLagtrain.App.Cycles;

/// <summary>Thrown when scene.json / settings.json / history.json fails schema validation. Never silently ignored.</summary>
public sealed class SchemaValidationException : Exception
{
    public string JsonPath { get; }
    public SchemaValidationException(string message, string jsonPath = "") : base(message)
    {
        JsonPath = jsonPath;
    }
    public SchemaValidationException(string message, string jsonPath, Exception inner) : base(message, inner)
    {
        JsonPath = jsonPath;
    }
}

public abstract record SceneMode
{
    public sealed record StringMode(string Value) : SceneMode
    {
        public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "once", "loop", "pingpong" };
    }
    public sealed record CountMode(int Count) : SceneMode;
}

public sealed class SceneModeJsonConverter : JsonConverter<SceneMode>
{
    public override SceneMode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString()!;
            if (!SceneMode.StringMode.Allowed.Contains(s))
                throw new JsonException($"mode string must be one of once|loop|pingpong, got '{s}'");
            return new SceneMode.StringMode(s);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (!doc.RootElement.TryGetProperty("count", out var c))
                throw new JsonException("mode object must have required property 'count'");
            if (c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out var count))
                throw new JsonException("mode.count must be integer 1..100");
            if (count < 1 || count > 100)
                throw new JsonException($"mode.count must be 1..100, got {count}");
            if (doc.RootElement.EnumerateObject().Count() != 1)
                throw new JsonException("mode object must only have 'count' property");
            return new SceneMode.CountMode(count);
        }
        throw new JsonException($"mode must be string (once|loop|pingpong) or object {{count:int}}, got {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, SceneMode value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case SceneMode.StringMode sm: writer.WriteStringValue(sm.Value); break;
            case SceneMode.CountMode cm: writer.WriteStartObject(); writer.WriteNumber("count", cm.Count); writer.WriteEndObject(); break;
            default: writer.WriteNullValue(); break;
        }
    }
}

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

    /// <summary>Validate fields, throwing SchemaValidationException on first error. Defaults already applied.</summary>
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

    /// <summary>Parse JSON string, apply defaults, validate. Throws SchemaValidationException on invalid JSON or schema violation.</summary>
    public static SceneConfig Parse(string json, string jsonPath = "scene.json")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException ex) { throw new SchemaValidationException($"Invalid JSON: {ex.Message}", jsonPath, ex); }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new SchemaValidationException("scene.json root must be object", jsonPath);

        // required id
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
            // use converter logic via raw json
            var raw = modeProp.GetRawText();
            try
            {
                var r = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(raw));
                // need to deserialize via options
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

        // check unknown properties
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

public sealed record History
{
    private static readonly Regex IdRegex = new(@"^[a-z0-9_-]{1,32}$", RegexOptions.Compiled);
    public required IReadOnlyList<string> Recent { get; init; }
    public string? MtimeCursor { get; init; }

    public void Validate(string jsonPath = "history.json", int maxWindow = 20)
    {
        if (Recent.Count > 20)
            throw new SchemaValidationException($"recent maxItems 20, got {Recent.Count}", $"{jsonPath}#/recent");
        if (Recent.Count > maxWindow)
            throw new SchemaValidationException($"recent length {Recent.Count} exceeds noRepeatWindow {maxWindow}", $"{jsonPath}#/recent");
        foreach (var id in Recent)
            if (!IdRegex.IsMatch(id))
                throw new SchemaValidationException($"recent item must match ^[a-z0-9_-]{{1,32}}$, got '{id}'", $"{jsonPath}#/recent");
    }

    public static History Parse(string json, string jsonPath = "history.json")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException ex) { throw new SchemaValidationException($"Invalid JSON: {ex.Message}", jsonPath, ex); }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new SchemaValidationException("history.json root must be object", jsonPath);
        if (!root.TryGetProperty("recent", out var recentProp))
            throw new SchemaValidationException("Missing required property 'recent'", $"{jsonPath}#/recent");
        if (recentProp.ValueKind != JsonValueKind.Array)
            throw new SchemaValidationException("recent must be array", $"{jsonPath}#/recent");

        var recent = new List<string>();
        foreach (var el in recentProp.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String) throw new SchemaValidationException("recent items must be strings", $"{jsonPath}#/recent");
            recent.Add(el.GetString()!);
        }

        string? cursor = null;
        if (root.TryGetProperty("mtimeCursor", out var mc))
        {
            if (mc.ValueKind == JsonValueKind.Null) cursor = null;
            else if (mc.ValueKind == JsonValueKind.String) cursor = mc.GetString();
            else throw new SchemaValidationException("mtimeCursor must be string or null", $"{jsonPath}#/mtimeCursor");
        }

        var allowed = new HashSet<string> { "recent", "mtimeCursor" };
        foreach (var p in root.EnumerateObject())
            if (!allowed.Contains(p.Name))
                throw new SchemaValidationException($"Unknown property '{p.Name}'", $"{jsonPath}#/{p.Name}");

        var h = new History { Recent = recent, MtimeCursor = cursor };
        h.Validate(jsonPath);
        return h;
    }
}
