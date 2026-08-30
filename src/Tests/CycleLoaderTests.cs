using OsageLagtrain.App.Cycles;
using Xunit;

namespace OsageLagtrain.Tests;

public class CycleLoaderTests
{
    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "osage_loader_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void Loader_SortsNatural_AndValidatesAtLeastOneFrame()
    {
        var root = CreateTempRoot();
        try
        {
            var dir = Path.Combine(root, "scene_x");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scene.json"), """{"id":"scene_x","fps":12}""");
            // No frames -> should throw validation error on LoadAll
            var store = new CycleStore(root);
            var ex = Assert.Throws<CycleValidationError>(() => store.LoadAll());
            Assert.Contains("no frames", ex.Message.ToLowerInvariant());

            // Add frames in wrong order with webp/png mix natural
            File.WriteAllBytes(Path.Combine(dir, "10.png"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(dir, "2.png"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(dir, "1.png"), new byte[] { 0 });
            var all = store.LoadAll();
            var scene = Assert.Single(all);
            var names = scene.Frames.Select(f => Path.GetFileName(f)).ToArray();
            Assert.Equal(new[] { "1.png", "2.png", "10.png" }, names);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Loader_ThrowsOnInvalidFps()
    {
        var root = CreateTempRoot();
        try
        {
            var dir = Path.Combine(root, "bad");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scene.json"), """{"id":"bad","fps":0}""");
            File.WriteAllBytes(Path.Combine(dir, "0001.png"), new byte[] { 0 });
            var store = new CycleStore(root);
            var ex = Assert.Throws<CycleValidationError>(() => store.LoadAll());
            Assert.Contains("fps", ex.Message);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Loader_IgnoresDirWithoutSceneJson()
    {
        var root = CreateTempRoot();
        try
        {
            var dirNoJson = Path.Combine(root, "empty");
            Directory.CreateDirectory(dirNoJson);
            File.WriteAllBytes(Path.Combine(dirNoJson, "0001.png"), new byte[] { 0 });
            var store = new CycleStore(root);
            var all = store.LoadAll();
            Assert.Empty(all);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void GetFrames_ReturnsNaturalOrder()
    {
        var root = CreateTempRoot();
        try
        {
            var dir = Path.Combine(root, "sceneA");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scene.json"), """{"id":"sceneA","fps":12}""");
            foreach (var n in new[] { "0001.png", "0010.png", "0002.png" })
                File.WriteAllBytes(Path.Combine(dir, n), new byte[] { 0 });
            var store = new CycleStore(root);
            var frames = store.GetFrames(Path.Combine(root, "sceneA"));
            var names = frames.Select(f => Path.GetFileName(f)).ToArray();
            Assert.Equal(new[] { "0001.png", "0002.png", "0010.png" }, names);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task Scheduler_OnAdvance_PickPreloadEnqueue()
    {
        var root = CreateTempRoot();
        var histPath = Path.Combine(Path.GetTempPath(), "osage_sched_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            foreach (var id in new[] { "alpha", "beta" })
            {
                var d = Path.Combine(root, id);
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "scene.json"), $$"""{"id":"{{id}}","fps":12}""");
                File.WriteAllBytes(Path.Combine(d, "0001.png"), new byte[] { 1 });
            }
            var store = new CycleStore(root);
            var hs = new HistoryStore(histPath);
            var policy = new RandomPurePolicy(new Random(0));
            var cache = new PreloadCache(2, decoder: _ => new byte[] { 9 });
            var sched = new CycleScheduler(store, hs, policy, cache, null, 3);
            CycleInfo? enqueued = null;
            sched.SceneEnqueued += c => enqueued = c;
            var result = await sched.OnAdvance("monitor1", "notepad.exe");
            Assert.NotNull(result);
            Assert.NotNull(enqueued);
            Assert.Equal(result!.Id, enqueued!.Id);
            Assert.Equal(1, cache.Count);
            var hist = hs.Load();
            Assert.Single(hist.Recent);
        }
        finally { try { Directory.Delete(root, true); } catch { } try { File.Delete(histPath); } catch { } }
    }
}
