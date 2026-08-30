namespace OsageLagtrain.App.Cycles;

public interface ISelectionPolicy
{
    string? Pick(IReadOnlyList<CycleInfo> cycles, History history, string? exeNameLower = null, IReadOnlyDictionary<string, string[]>? appMap = null);
    string PolicyName { get; }
}

public sealed class RandomPurePolicy : ISelectionPolicy
{
    private readonly Random _rng;
    public string PolicyName => "randomPure";
    public RandomPurePolicy(Random? rng = null) => _rng = rng ?? Random.Shared;

    public string? Pick(IReadOnlyList<CycleInfo> cycles, History history, string? exeNameLower = null, IReadOnlyDictionary<string, string[]>? appMap = null)
    {
        var pool = FilterByAppMap(cycles, exeNameLower, appMap);
        if (pool.Count == 0) return null;
        return pool[_rng.Next(pool.Count)].Id;
    }

    internal static IReadOnlyList<CycleInfo> FilterByAppMap(IReadOnlyList<CycleInfo> cycles, string? exeNameLower, IReadOnlyDictionary<string, string[]>? appMap)
    {
        if (appMap == null || string.IsNullOrWhiteSpace(exeNameLower)) return cycles;
        var key = exeNameLower.ToLowerInvariant();
        if (!appMap.TryGetValue(key, out var allowedIds)) return cycles;
        if (allowedIds == null || allowedIds.Length == 0) return cycles;
        var set = new HashSet<string>(allowedIds, StringComparer.OrdinalIgnoreCase);
        var filtered = cycles.Where(c => set.Contains(c.Id)).ToList();
        return filtered.Count > 0 ? filtered : cycles;
    }
}

public sealed class RandomNoRepeatPolicy : ISelectionPolicy
{
    private readonly int _windowN;
    private readonly Random _rng;
    public string PolicyName => "randomNoRepeat";
    public int WindowN => _windowN;

    public RandomNoRepeatPolicy(int windowN, Random? rng = null)
    {
        if (windowN < 0 || windowN > 20) throw new ArgumentOutOfRangeException(nameof(windowN));
        _windowN = windowN;
        _rng = rng ?? Random.Shared;
    }

    public string? Pick(IReadOnlyList<CycleInfo> cycles, History history, string? exeNameLower = null, IReadOnlyDictionary<string, string[]>? appMap = null)
    {
        var pool = RandomPurePolicy.FilterByAppMap(cycles, exeNameLower, appMap);
        if (pool.Count == 0) return null;
        if (_windowN == 0) return pool[_rng.Next(pool.Count)].Id;

        var recentSet = new HashSet<string>(history.Recent.TakeLast(_windowN), StringComparer.OrdinalIgnoreCase);
        var eligible = pool.Where(c => !recentSet.Contains(c.Id)).ToList();
        var pickPool = eligible.Count > 0 ? eligible : pool.ToList();
        return pickPool[_rng.Next(pickPool.Count)].Id;
    }
}

public sealed class SequentialByNamePolicy : ISelectionPolicy
{
    public string PolicyName => "sequentialByName";

    public string? Pick(IReadOnlyList<CycleInfo> cycles, History history, string? exeNameLower = null, IReadOnlyDictionary<string, string[]>? appMap = null)
    {
        var pool = RandomPurePolicy.FilterByAppMap(cycles, exeNameLower, appMap);
        if (pool.Count == 0) return null;
        var sorted = pool.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        if (history.Recent.Count == 0) return sorted[0].Id;
        var lastId = history.Recent[^1];
        int idx = sorted.FindIndex(c => string.Equals(c.Id, lastId, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return sorted[0].Id;
        return sorted[(idx + 1) % sorted.Count].Id;
    }
}

public sealed class SequentialByMtimePolicy : ISelectionPolicy
{
    public string PolicyName => "sequentialByMtime";

    public string? Pick(IReadOnlyList<CycleInfo> cycles, History history, string? exeNameLower = null, IReadOnlyDictionary<string, string[]>? appMap = null)
    {
        var pool = RandomPurePolicy.FilterByAppMap(cycles, exeNameLower, appMap);
        if (pool.Count == 0) return null;
        // Order by File.GetLastWriteTimeUtc(Directory) ascending (oldest first), then by Id for stability.
        var sorted = pool.OrderBy(c => c.Mtime).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        if (history.Recent.Count == 0) return sorted[0].Id;
        // If mtimeCursor present, use it as cursor
        string? cursor = history.MtimeCursor ?? history.Recent[^1];
        int idx = sorted.FindIndex(c => string.Equals(c.Id, cursor, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return sorted[0].Id;
        return sorted[(idx + 1) % sorted.Count].Id;
    }
}

public static class SelectionPolicyFactory
{
    public static ISelectionPolicy Create(string policyName, int noRepeatWindow = 3, Random? rng = null)
    {
        return policyName switch
        {
            "randomNoRepeat" => new RandomNoRepeatPolicy(noRepeatWindow, rng),
            "randomPure" => new RandomPurePolicy(rng),
            "sequentialByName" => new SequentialByNamePolicy(),
            "sequentialByMtime" => new SequentialByMtimePolicy(),
            _ => throw new ArgumentException($"Unknown selectionPolicy '{policyName}'", nameof(policyName))
        };
    }

    public static readonly string DefaultPolicy = "randomNoRepeat";
}
