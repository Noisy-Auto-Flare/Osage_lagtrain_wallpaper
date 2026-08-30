using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsageLagtrain.App.Cycles;

/// <summary>Thrown when scene.json / settings.json / history.json fails schema validation. Never silently ignored.</summary>
public class SchemaValidationException : Exception
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
