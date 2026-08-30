using System.Text.Json;
using System.Text.RegularExpressions;

namespace OsageLagtrain.App.Cycles;

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
