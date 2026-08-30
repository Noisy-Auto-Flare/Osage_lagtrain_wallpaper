using OsageLagtrain.App.Cycles;

namespace OsageLagtrain.App.Ui;

/// <summary>Adapter that wraps CycleStore as ICycleStore with Reload support.</summary>
public sealed class CycleStoreAdapter : ICycleStore
{
    private CycleStore _inner;

    public string CyclesRoot => _inner.CyclesRoot;

    public CycleStoreAdapter(string? cyclesRoot = null, string? exeDirOverride = null, Func<string,bool>? webpProbe=null, Action<string>? toast=null)
    {
        _inner = new CycleStore(cyclesRoot, exeDirOverride, webpProbe, toast);
    }

    public CycleStoreAdapter(CycleStore inner) => _inner = inner;

    public IReadOnlyList<CycleInfo> LoadAll() => _inner.LoadAll();
    public IReadOnlyList<string> GetFrames(string sceneDirOrId) => _inner.GetFrames(sceneDirOrId);
    public CycleInfo Load(string sceneId) => _inner.Load(sceneId);

    public void Reload()
    {
        // Recreate inner with same root (re-scan on next LoadAll does fresh scan anyway)
        // If CyclesRoot changed via settings, caller should construct new adapter with new root.
        // Here we just force a no-op reload that validates the root is still readable.
        try { _ = _inner.LoadAll(); } catch { }
    }

    public void UpdateRoot(string newCyclesRoot)
    {
        _inner = new CycleStore(newCyclesRoot);
        Console.WriteLine($"[CycleStoreAdapter] UpdateRoot -> {_inner.CyclesRoot}");
    }
}
