namespace OsageLagtrain.App.Cycles;

/// <summary>LRU decode cache for 2 scenes. Stub decode: reads file bytes; mock decoder in tests can be injected.</summary>
public sealed class PreloadCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, IReadOnlyList<byte[]>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly Func<string, byte[]>? _decoder;

    public PreloadCache(int capacity = 2, Func<string, byte[]>? decoder = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _decoder = decoder;
    }

    public int Count => _cache.Count;
    public int Capacity => _capacity;
    public IReadOnlyCollection<string> Keys => _cache.Keys;

    public async Task PreloadAsync(CycleInfo scene, CancellationToken ct = default)
    {
        if (_cache.ContainsKey(scene.Id))
        {
            Touch(scene.Id);
            return;
        }

        // Decode all frames (stub: read bytes)
        var decoded = new List<byte[]>();
        foreach (var f in scene.Frames)
        {
            ct.ThrowIfCancellationRequested();
            byte[] data;
            if (_decoder != null)
                data = _decoder(f);
            else
                data = await File.ReadAllBytesAsync(f, ct);
            decoded.Add(data);
        }

        _cache[scene.Id] = decoded;
        _lru.AddLast(scene.Id);

        // Evict LRU if over capacity
        while (_cache.Count > _capacity)
        {
            var oldest = _lru.First!.Value;
            _lru.RemoveFirst();
            _cache.Remove(oldest);
        }
    }

    public bool TryGet(string sceneId, out IReadOnlyList<byte[]>? frames)
    {
        if (_cache.TryGetValue(sceneId, out var v))
        {
            Touch(sceneId);
            frames = v;
            return true;
        }
        frames = null;
        return false;
    }

    private void Touch(string id)
    {
        var node = _lru.Find(id);
        if (node != null)
        {
            _lru.Remove(node);
            _lru.AddLast(node);
        }
    }
}

public sealed class CycleScheduler
{
    private readonly CycleStore _store;
    private readonly HistoryStore _history;
    private readonly ISelectionPolicy _policy;
    private readonly PreloadCache _preload;
    private readonly IReadOnlyDictionary<string, string[]>? _appMap;
    private readonly int _windowN;

    public event Action<CycleInfo>? SceneEnqueued;

    public CycleScheduler(CycleStore store, HistoryStore history, ISelectionPolicy policy, PreloadCache preload, IReadOnlyDictionary<string, string[]>? appMap = null, int windowN = 3)
    {
        _store = store;
        _history = history;
        _policy = policy;
        _preload = preload;
        _appMap = appMap;
        _windowN = windowN;
    }

    /// <summary>Scheduler OnAdvance(monitor, exe) → policy.Pick → Load → Preload → RenderQueue.Enqueue</summary>
    public async Task<CycleInfo?> OnAdvance(string monitor, string exeName, CancellationToken ct = default)
    {
        var cycles = _store.LoadAll();
        if (cycles.Count == 0) return null;
        var history = _history.Load();
        var exeLower = exeName.ToLowerInvariant();
        var pickId = _policy.Pick(cycles, history, exeLower, _appMap);
        if (pickId == null) return null;
        var scene = cycles.FirstOrDefault(c => c.Id == pickId) ?? _store.Load(pickId);
        await _preload.PreloadAsync(scene, ct);
        // Update history sliding window truncated to N on write + 1KB cap handled in store
        var recent = history.Recent.ToList();
        recent.Add(scene.Id);
        var updated = new History { Recent = recent, MtimeCursor = scene.Id };
        _history.Save(updated, _windowN);
        SceneEnqueued?.Invoke(scene);
        return scene;
    }
}
