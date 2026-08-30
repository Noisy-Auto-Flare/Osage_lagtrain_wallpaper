using OsageLagtrain.App.Cycles;
using Xunit;

namespace OsageLagtrain.Tests;

public class SceneSchemaTests
{
    // valid cases
    [Fact]
    public void Valid_Once_WithHold()
    {
        var json = """{"id":"jump_hand","fps":12,"mode":"once","holdLastMs":800}""";
        var cfg = SceneConfig.Parse(json);
        Assert.Equal("jump_hand", cfg.Id);
        Assert.Equal(12, cfg.Fps);
        Assert.Equal(800, cfg.HoldLastMs);
        Assert.IsType<SceneMode.StringMode>(cfg.Mode);
        Assert.Equal("once", ((SceneMode.StringMode)cfg.Mode!).Value);
    }

    [Fact]
    public void Valid_Loop()
    {
        var json = """{"id":"loop_run","title":"Loop Run","fps":12,"mode":"loop","holdLastMs":0}""";
        var cfg = SceneConfig.Parse(json);
        Assert.Equal("loop_run", cfg.Id);
        Assert.Equal("Loop Run", cfg.Title);
        Assert.Equal("loop", ((SceneMode.StringMode)cfg.Mode!).Value);
    }

    [Fact]
    public void Valid_PingPong()
    {
        var json = """{"id":"ping_pong","title":"Ping Pong Demo","fps":8,"mode":"pingpong","holdLastMs":200,"idleColor":"#b2b2b2"}""";
        var cfg = SceneConfig.Parse(json);
        Assert.Equal(8, cfg.Fps);
        Assert.Equal("pingpong", ((SceneMode.StringMode)cfg.Mode!).Value);
        Assert.Equal("#b2b2b2", cfg.IdleColor);
    }

    [Fact]
    public void Valid_OverrideDelay_WithCountMode()
    {
        var json = """{"id":"override_delay","title":"Override Delay","fps":15,"mode":{"count":3},"holdLastMs":500,"postEventDelayMs":1200}""";
        var cfg = SceneConfig.Parse(json);
        Assert.Equal(15, cfg.Fps);
        var cm = Assert.IsType<SceneMode.CountMode>(cfg.Mode);
        Assert.Equal(3, cm.Count);
        Assert.Equal(1200, cfg.PostEventDelayMs);
    }

    [Fact]
    public void Valid_Defaults_Applied()
    {
        var json = """{"id":"minimal"}""";
        var cfg = SceneConfig.Parse(json);
        Assert.Equal(12, cfg.Fps);
        Assert.Equal(0, cfg.HoldLastMs);
        Assert.Equal("#b2b2b2", cfg.IdleColor);
        Assert.Null(cfg.Mode);
        Assert.Null(cfg.PostEventDelayMs);
    }

    private static string RepoPath(string rel)
    {
        var baseDir = AppContext.BaseDirectory;
        // baseDir is src/Tests/bin/Debug/net8.0 -> go up 5 to repo root
        var repo = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        // if not found, try 4 levels (CI)
        if (!Directory.Exists(Path.Combine(repo, "cycles")))
            repo = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        return Path.Combine(repo, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Valid_Fixture_OnceScene_File()
    {
        var cfg = SceneConfig.ParseFile(RepoPath("cycles/examples/once_scene/scene.json"));
        Assert.Equal("jump_hand", cfg.Id);
    }

    [Fact]
    public void Valid_Fixture_LoopScene_File()
    {
        var cfg = SceneConfig.ParseFile(RepoPath("cycles/examples/loop_scene/scene.json"));
        Assert.Equal("loop_run", cfg.Id);
    }

    [Fact]
    public void Valid_Fixture_PingPong_File()
    {
        var cfg = SceneConfig.ParseFile(RepoPath("cycles/examples/pingpong_scene/scene.json"));
        Assert.Equal("ping_pong", cfg.Id);
    }

    [Fact]
    public void Valid_Fixture_OverrideDelay_File()
    {
        var cfg = SceneConfig.ParseFile(RepoPath("cycles/examples/override_delay/scene.json"));
        Assert.Equal("override_delay", cfg.Id);
        var cm = Assert.IsType<SceneMode.CountMode>(cfg.Mode);
        Assert.Equal(3, cm.Count);
    }

    // invalid cases
    [Fact]
    public void Invalid_Fps_99_Throws_WithMessage()
    {
        var json = """{"id":"bad_fps","fps":99,"mode":"once"}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("fps", ex.Message);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Invalid_Fps_0_Throws()
    {
        var json = """{"id":"bad_zero","fps":0}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("fps", ex.Message);
    }

    [Fact]
    public void Invalid_MissingId_Throws()
    {
        var json = """{"fps":12,"mode":"once"}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Invalid_Mode_Throws()
    {
        var json = """{"id":"bad_mode","mode":"random"}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("mode", ex.Message);
    }

    [Fact]
    public void Invalid_IdleColor_Throws()
    {
        var json = """{"id":"bad_color","idleColor":"b2b2b2"}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("idleColor", ex.Message);
    }

    [Fact]
    public void Invalid_Id_Regex_Throws()
    {
        var json = """{"id":"BadUpper"}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Invalid_Json_Syntax_Throws()
    {
        var json = """{"id":"bad",}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("Invalid JSON", ex.Message);
    }

    [Fact]
    public void Invalid_UnknownProperty_Throws()
    {
        var json = """{"id":"ok","foo":123}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SceneConfig.Parse(json));
        Assert.Contains("Unknown property", ex.Message);
    }

    // Settings tests
    [Fact]
    public void Valid_Settings_Defaults()
    {
        var json = """{"cyclesRoot":"./cycles"}""";
        var s = SettingsConfig.Parse(json);
        Assert.Equal(500, s.PostEventDelayMs);
        Assert.Equal(3, s.NoRepeatWindow);
        Assert.Equal("#b2b2b2", s.IdleColor);
    }

    [Fact]
    public void Invalid_Settings_SelectionPolicy_Throws()
    {
        var json = """{"selectionPolicy":"bad"}""";
        var ex = Assert.Throws<SchemaValidationException>(() => SettingsConfig.Parse(json));
        Assert.Contains("selectionPolicy", ex.Message);
    }

    [Fact]
    public void Invalid_History_Recent_TooLong_Throws()
    {
        var arr = string.Join(",", Enumerable.Repeat("\"a\"", 21));
        var json = $"{{\"recent\":[{arr}]}}";
        var ex = Assert.Throws<SchemaValidationException>(() => History.Parse(json));
        Assert.Contains("recent", ex.Message);
    }
}
