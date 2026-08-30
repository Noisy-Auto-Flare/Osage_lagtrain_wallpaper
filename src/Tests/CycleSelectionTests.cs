using OsageLagtrain.App.Cycles;
using Xunit;

namespace OsageLagtrain.Tests;

public class CycleSelectionTests
{
    private static string CreateTempCyclesRoot(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "osage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateScene(string cyclesRoot, string id, int fps, string[] frameNames, DateTime? mtimeUtc = null)
    {
        var dir = Path.Combine(cyclesRoot, id);
        Directory.CreateDirectory(dir);
        var sceneJson = $$"""{"id":"{{id}}","fps":{{fps}},"mode":"once","holdLastMs":0}""";
        File.WriteAllText(Path.Combine(dir, "scene.json"), sceneJson);
        foreach (var fn in frameNames)
        {
            File.WriteAllBytes(Path.Combine(dir, fn), new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // fake png header
        }
        if (mtimeUtc.HasValue)
            Directory.SetLastWriteTimeUtc(dir, mtimeUtc.Value);
    }

    private static List<CycleInfo> FakeCycles(params string[] ids)
    {
        var list = new List<CycleInfo>();
        foreach (var id in ids)
        {
            list.Add(new CycleInfo
            {
                Id = id,
                Title = id,
                Config = new SceneConfig { Id = id, Fps = 12, HoldLastMs = 0, IdleColor = "#b2b2b2" },
                Frames = new[] { id + "/0001.png" },
                DirPath = "/tmp/" + id,
                Mtime = DateTime.UtcNow
            });
        }
        return list;
    }

    [Fact]
    public void RandomNoRepeat_Window3_NeverReturnsRecent3_100Picks()
    {
        var cycles = FakeCycles("a", "b", "c", "d", "e");
        var rng = new Random(42);
        var policy = new RandomNoRepeatPolicy(3, rng);
        var history = new History { Recent = new[] { "a", "b", "c" }, MtimeCursor = null };

        for (int i = 0; i < 100; i++)
        {
            var pick = policy.Pick(cycles, history, null, null)!;
            Assert.DoesNotContain(pick, history.Recent.TakeLast(3));
            // simulate history update sliding window truncated to N on write
            var lst = history.Recent.ToList();
            lst.Add(pick);
            lst = lst.TakeLast(3).ToList();
            history = new History { Recent = lst, MtimeCursor = pick };
        }
    }

    [Fact]
    public void SequentialByMtime_OldestFirst()
    {
        var now = DateTime.UtcNow;
        var cycles = new List<CycleInfo>
        {
            new() { Id="newest", Title="newest", Config=new SceneConfig{Id="newest",Fps=12,IdleColor="#b2b2b2"}, Frames=new[]{"a"}, DirPath="/tmp/newest", Mtime=now },
            new() { Id="oldest", Title="oldest", Config=new SceneConfig{Id="oldest",Fps=12,IdleColor="#b2b2b2"}, Frames=new[]{"a"}, DirPath="/tmp/oldest", Mtime=now.AddDays(-2) },
            new() { Id="middle", Title="middle", Config=new SceneConfig{Id="middle",Fps=12,IdleColor="#b2b2b2"}, Frames=new[]{"a"}, DirPath="/tmp/middle", Mtime=now.AddDays(-1) },
        };
        var policy = new SequentialByMtimePolicy();
        var history = new History { Recent = Array.Empty<string>(), MtimeCursor=null };
        var first = policy.Pick(cycles, history);
        Assert.Equal("oldest", first);
        // next should be middle after oldest
        history = new History { Recent = new[] { "oldest" }, MtimeCursor="oldest" };
        var second = policy.Pick(cycles, history);
        Assert.Equal("middle", second);
        history = new History { Recent = new[] { "oldest","middle" }, MtimeCursor="middle" };
        var third = policy.Pick(cycles, history);
        Assert.Equal("newest", third);
    }

    [Fact]
    public void NaturalSort_0001_0002_0010_Order()
    {
        var root = CreateTempCyclesRoot(out var tmpRoot);
        try
        {
            CreateScene(tmpRoot, "scene_a", 12, new[] { "0001.png", "0010.png", "0002.png", "0003.png" });
            var store = new CycleStore(tmpRoot);
            var all = store.LoadAll();
            var scene = Assert.Single(all);
            var names = scene.Frames.Select(f => Path.GetFileName(f)).ToArray();
            Assert.Equal(new[] { "0001.png", "0002.png", "0003.png", "0010.png" }, names);
            // also test NaturalSort.Comparer directly
            var sorted = new[] { "0001.png", "0010.png", "0002.png" }.OrderBy(x => x, NaturalSort.Comparer).ToArray();
            Assert.Equal(new[] { "0001.png", "0002.png", "0010.png" }, sorted);
            // lexical would be 0001,0010,0002 -> ensure natural differs
            var lexical = new[] { "0001.png", "0010.png", "0002.png" }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            // lexical with leading zeros also happens to be natural here; prove non-padded case diff
            // (keep assertion for non-padded below)
            var noPad = new[] { "1.png", "10.png", "2.png" }.OrderBy(x => x, NaturalSort.Comparer).ToArray();
            Assert.Equal(new[] { "1.png", "2.png", "10.png" }, noPad);
            var noPadLexical = new[] { "1.png", "10.png", "2.png" }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "1.png", "10.png", "2.png" }, noPadLexical);
            Assert.NotEqual(noPadLexical, noPad);
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void AppMap_Filter_ReturnsOnlyMappedScenes()
    {
        var cycles = FakeCycles("scene1", "scene2", "scene3");
        var appMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["code.exe"] = new[] { "scene1" }
        };
        var policy = new RandomPurePolicy(new Random(1));
        for (int i = 0; i < 20; i++)
        {
            var pick = policy.Pick(cycles, new History { Recent = Array.Empty<string>() }, "code.exe", appMap);
            Assert.Equal("scene1", pick);
        }
    }

    [Fact]
    public void AppMap_Fallback_GlobalWhenNoMatch()
    {
        var cycles = FakeCycles("scene1", "scene2");
        var appMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["code.exe"] = new[] { "scene1" }
        };
        var policy = new RandomPurePolicy(new Random(2));
        // exe not in map -> global pool
        var picks = new HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            var pick = policy.Pick(cycles, new History { Recent = Array.Empty<string>() }, "notepad.exe", appMap);
            picks.Add(pick!);
        }
        Assert.Contains("scene1", picks);
        Assert.Contains("scene2", picks);
    }

    [Fact]
    public void Fps_Validation_Error_ThrowsCycleValidationError()
    {
        var root = CreateTempCyclesRoot(out var tmpRoot);
        try
        {
            CreateScene(tmpRoot, "bad_fps", 99, new[] { "0001.png" }); // will write fps 99 json manually
            var dir = Path.Combine(tmpRoot, "bad_fps");
            File.WriteAllText(Path.Combine(dir, "scene.json"), """{"id":"bad_fps","fps":99}""");
            var store = new CycleStore(tmpRoot);
            var ex = Assert.Throws<CycleValidationError>(() => store.LoadAll());
            Assert.Contains("fps", ex.Message);
            Assert.Contains("99", ex.Message);
            Assert.Contains("bad_fps", ex.JsonPath);
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void History_Corrupted_ResetsToEmpty()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), "osage_hist_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tmpFile, "THIS IS NOT JSON {{{");
            var hs = new HistoryStore(tmpFile);
            var h = hs.Load();
            Assert.Empty(h.Recent);
        }
        finally { try { File.Delete(tmpFile); } catch { } }
    }

    [Fact]
    public void History_Atomicity_WriteAndRead()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), "osage_hist_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var hs = new HistoryStore(tmpFile);
            var h = new History { Recent = new[] { "a", "b" }, MtimeCursor = "b" };
            hs.Save(h, 3);
            Assert.True(File.Exists(tmpFile));
            var loaded = hs.Load();
            Assert.Equal(new[] { "a", "b" }, loaded.Recent);
            // check atomicity: tmp file should not remain
            Assert.False(File.Exists(tmpFile + ".tmp"));
            // corrupted overwrite should reset but Save after should work
            File.WriteAllText(tmpFile, "corrupted");
            var h2 = hs.Load();
            Assert.Empty(h2.Recent);
        }
        finally { try { File.Delete(tmpFile); } catch { } try { File.Delete(tmpFile + ".tmp"); } catch { } }
    }

    [Fact]
    public void History_SlidingWindow_TruncatedToN()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), "osage_hist_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var hs = new HistoryStore(tmpFile);
            var h = new History { Recent = new[] { "a", "b", "c", "d", "e" }, MtimeCursor = null };
            hs.Save(h, 3);
            var loaded = hs.Load();
            Assert.Equal(3, loaded.Recent.Count);
            Assert.Equal(new[] { "c", "d", "e" }, loaded.Recent);
        }
        finally { try { File.Delete(tmpFile); } catch { } }
    }

    [Fact]
    public void History_Max1KB_Cap_Truncates()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), "osage_hist_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var hs = new HistoryStore(tmpFile, 1024);
            // 100 advances with N=3 but we test with N=20 and many entries to overflow 1KB
            var large = new List<string>();
            for (int i = 0; i < 100; i++) large.Add("scene_" + i.ToString("D2") + "_longname");
            var h = new History { Recent = large, MtimeCursor = null };
            hs.Save(h, 20);
            var bytes = new FileInfo(tmpFile).Length;
            Assert.True(bytes <= 1024, $"history file {bytes} bytes >1024");
            var content = File.ReadAllText(tmpFile);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(content) <= 1024);
        }
        finally { try { File.Delete(tmpFile); } catch { } }
    }

    [Fact]
    public void Webp_Skip_WhenNotSupported()
    {
        var root = CreateTempCyclesRoot(out var tmpRoot);
        try
        {
            var dir = Path.Combine(tmpRoot, "webp_scene");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scene.json"), """{"id":"webp_scene","fps":12}""");
            File.WriteAllBytes(Path.Combine(dir, "0001.png"), new byte[] { 0x89, 0x50 });
            File.WriteAllBytes(Path.Combine(dir, "0002.webp"), new byte[] { 0x52, 0x49, 0x46, 0x46 });
            bool toastCalled = false;
            var store = new CycleStore(tmpRoot, webpProbe: _ => false, toast: _ => toastCalled = true);
            var all = store.LoadAll();
            var scene = Assert.Single(all);
            Assert.Single(scene.Frames);
            Assert.EndsWith("0001.png", scene.Frames[0]);
            Assert.True(toastCalled);
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void Offline_Hydrate_Skip()
    {
        var root = CreateTempCyclesRoot(out var tmpRoot);
        try
        {
            var dir = Path.Combine(tmpRoot, "offline_scene");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scene.json"), """{"id":"offline_scene","fps":12}""");
            var onlineFile = Path.Combine(dir, "0001.png");
            var offlineFile = Path.Combine(dir, "0002.png");
            File.WriteAllBytes(onlineFile, new byte[] { 0x89 });
            File.WriteAllBytes(offlineFile, new byte[] { 0x89 });
            // Mark offline attribute
            File.SetAttributes(offlineFile, File.GetAttributes(offlineFile) | FileAttributes.Offline);
            var store = new CycleStore(tmpRoot);
            var all = store.LoadAll();
            var scene = Assert.Single(all);
            // offline file should be skipped, only 1 frame
            Assert.Single(scene.Frames);
            Assert.EndsWith("0001.png", scene.Frames[0]);
            // cleanup: remove offline flag for delete
            try { File.SetAttributes(offlineFile, FileAttributes.Normal); } catch { }
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    [Fact]
    public void SequentialByName_Order()
    {
        var cycles = FakeCycles("c", "a", "b");
        var policy = new SequentialByNamePolicy();
        var h0 = new History { Recent = Array.Empty<string>() };
        Assert.Equal("a", policy.Pick(cycles, h0));
        var h1 = new History { Recent = new[] { "a" } };
        Assert.Equal("b", policy.Pick(cycles, h1));
        var h2 = new History { Recent = new[] { "a", "b" } };
        Assert.Equal("c", policy.Pick(cycles, h2));
        var h3 = new History { Recent = new[] { "a", "b", "c" } };
        Assert.Equal("a", policy.Pick(cycles, h3));
    }

    [Fact]
    public async Task Preload_LRU_Eviction_Only2Scenes()
    {
        var cache = new PreloadCache(2, decoder: _ => new byte[] { 1, 2, 3 });
        var c1 = new CycleInfo { Id = "s1", Title = "s1", Config = new SceneConfig { Id = "s1", Fps = 12, IdleColor = "#b2b2b2" }, Frames = new[] { "f1" }, DirPath = "/tmp/s1", Mtime = DateTime.UtcNow };
        var c2 = new CycleInfo { Id = "s2", Title = "s2", Config = new SceneConfig { Id = "s2", Fps = 12, IdleColor = "#b2b2b2" }, Frames = new[] { "f2" }, DirPath = "/tmp/s2", Mtime = DateTime.UtcNow };
        var c3 = new CycleInfo { Id = "s3", Title = "s3", Config = new SceneConfig { Id = "s3", Fps = 12, IdleColor = "#b2b2b2" }, Frames = new[] { "f3" }, DirPath = "/tmp/s3", Mtime = DateTime.UtcNow };
        await cache.PreloadAsync(c1);
        await cache.PreloadAsync(c2);
        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("s1", out _));
        Assert.True(cache.TryGet("s2", out _));
        await cache.PreloadAsync(c3);
        Assert.Equal(2, cache.Count);
        // s1 should be evicted (LRU), s2 and s3 remain
        Assert.False(cache.TryGet("s1", out _));
        Assert.True(cache.TryGet("s2", out _));
        Assert.True(cache.TryGet("s3", out _));
    }

    [Fact]
    public void PortableProbe_UsesExeDirWhenWritable()
    {
        var writable = Path.Combine(Path.GetTempPath(), "osage_probe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(writable);
        try
        {
            var resolved = CycleStore.ResolveCyclesRoot(writable);
            Assert.Equal(Path.Combine(writable, "cycles"), resolved);
            // Do NOT check Program Files string
            Assert.DoesNotContain("Program Files", resolved);
        }
        finally { try { Directory.Delete(writable, true); } catch { } }
    }

    // Additional sub-check: history atomicity via corrupted reset after valid save
    [Fact]
    public void History_TruncatedToN_OnWrite_WithMtimeCursor()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), "osage_hist_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var hs = new HistoryStore(tmpFile);
            hs.Save(new History { Recent = new[] { "a","b","c","d" }, MtimeCursor="d" }, 2);
            var loaded = hs.Load();
            Assert.Equal(new[] { "c","d" }, loaded.Recent);
            Assert.Equal("d", loaded.MtimeCursor);
        }
        finally { try { File.Delete(tmpFile); } catch { } }
    }
}
