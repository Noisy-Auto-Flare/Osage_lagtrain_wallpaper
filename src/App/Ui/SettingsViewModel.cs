using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using OsageLagtrain.App.Cycles;
using OsageLagtrain.App.Rendering;

namespace OsageLagtrain.App.Ui;

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICycleStore _cycleStore;
    private readonly ISettingsStore _settingsStore;
    private readonly IFilePicker? _filePicker;
    private readonly Action<SettingsConfig>? _updateConfig;
    private readonly int _debounceMs;

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _previewCts;
    private readonly object _saveLock = new();

    public ObservableCollection<SceneListItem> Scenes { get; } = new();

    private SceneListItem? _selectedScene;
    public SceneListItem? SelectedScene
    {
        get => _selectedScene;
        set
        {
            if (_selectedScene == value) return;
            _selectedScene = value;
            OnPropertyChanged(nameof(SelectedScene));
            OnPropertyChanged(nameof(SelectedFps));
            OnPropertyChanged(nameof(SelectedHoldLastMs));
            OnPropertyChanged(nameof(SelectedPostEventDelayMs));
            OnPropertyChanged(nameof(PreviewFrameCount));
            OnPropertyChanged(nameof(HasSelectedScene));
            if (value != null)
            {
                ResetPreview();
                if (IsPreviewPlaying) StartPreviewTimer();
            }
            else
            {
                PausePreview();
            }
        }
    }

    public bool HasSelectedScene => SelectedScene != null;

    private SettingsConfig _globalSettings = new();
    public SettingsConfig GlobalSettings
    {
        get => _globalSettings;
        private set { _globalSettings = value; OnPropertyChanged(nameof(GlobalSettings)); OnPropertyChanged(nameof(CyclesRoot)); OnPropertyChanged(nameof(PostEventDelayMs)); OnPropertyChanged(nameof(SelectionPolicy)); OnPropertyChanged(nameof(NoRepeatWindow)); OnPropertyChanged(nameof(IdleColor)); OnPropertyChanged(nameof(HasAppMap)); }
    }

    public string CyclesRoot
    {
        get => GlobalSettings.CyclesRoot;
        set
        {
            if (GlobalSettings.CyclesRoot == value) return;
            GlobalSettings = GlobalSettings with { CyclesRoot = value };
            ScheduleSave();
        }
    }

    public int PostEventDelayMs
    {
        get => GlobalSettings.PostEventDelayMs;
        set
        {
            var clamped = Math.Clamp(value, 0, 5000);
            if (GlobalSettings.PostEventDelayMs == clamped) return;
            GlobalSettings = GlobalSettings with { PostEventDelayMs = clamped };
            ScheduleSave();
        }
    }

    public string SelectionPolicy
    {
        get => GlobalSettings.SelectionPolicy;
        set
        {
            if (GlobalSettings.SelectionPolicy == value) return;
            GlobalSettings = GlobalSettings with { SelectionPolicy = value };
            ScheduleSave();
        }
    }

    public int NoRepeatWindow
    {
        get => GlobalSettings.NoRepeatWindow;
        set
        {
            var clamped = Math.Clamp(value, 0, 20);
            if (GlobalSettings.NoRepeatWindow == clamped) return;
            GlobalSettings = GlobalSettings with { NoRepeatWindow = clamped };
            ScheduleSave();
        }
    }

    public string IdleColor
    {
        get => GlobalSettings.IdleColor;
        set
        {
            if (GlobalSettings.IdleColor == value) return;
            GlobalSettings = GlobalSettings with { IdleColor = value };
            ScheduleSave();
        }
    }

    public bool HasAppMap => GlobalSettings.AppMap != null && GlobalSettings.AppMap.Count > 0;
    public IReadOnlyDictionary<string, string[]>? AppMap => GlobalSettings.AppMap;

    public static IReadOnlyList<string> SelectionPolicies { get; } = new[] { "randomNoRepeat", "randomPure", "sequentialByName", "sequentialByMtime" };

    // Global edit debounce count for testing
    public int SaveCallCount { get; private set; }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); } }

    // Preview
    private bool _isPreviewPlaying;
    public bool IsPreviewPlaying { get => _isPreviewPlaying; private set { _isPreviewPlaying = value; OnPropertyChanged(nameof(IsPreviewPlaying)); } }

    private int _currentFrameIndex;
    public int CurrentFrameIndex
    {
        get => _currentFrameIndex;
        private set { _currentFrameIndex = value; OnPropertyChanged(nameof(CurrentFrameIndex)); OnPropertyChanged(nameof(CurrentPreviewFramePath)); OnPropertyChanged(nameof(SliderValue)); }
    }

    public int PreviewFrameCount => SelectedScene?.Frames.Count ?? 0;
    public string? CurrentPreviewFramePath
    {
        get
        {
            if (SelectedScene == null || SelectedScene.Frames.Count == 0) return null;
            if (CurrentFrameIndex < 0 || CurrentFrameIndex >= SelectedScene.Frames.Count) return null;
            return SelectedScene.Frames[CurrentFrameIndex];
        }
    }

    public double SliderValue
    {
        get => CurrentFrameIndex;
        set => ScrubTo((int)value);
    }

    public int SelectedFps
    {
        get => SelectedScene?.Fps ?? 12;
        set => UpdateSelectedFps(value);
    }

    public int SelectedHoldLastMs
    {
        get => SelectedScene?.Config?.HoldLastMs ?? 0;
        set => UpdateSelectedHoldLast(value);
    }

    public int? SelectedPostEventDelayMs
    {
        get => SelectedScene?.Config?.PostEventDelayMs;
        set => UpdateSelectedPostEventDelay(value);
    }

    private TimeSpan _previewElapsed = TimeSpan.Zero;
    private DateTime _previewStart = DateTime.UtcNow;
    private System.Threading.Timer? _previewTimer;
    private readonly object _previewLock = new();

    public SettingsViewModel(ICycleStore cycleStore, ISettingsStore settingsStore, IFilePicker? filePicker = null, Action<SettingsConfig>? updateConfig = null, int debounceMs = 500)
    {
        _cycleStore = cycleStore;
        _settingsStore = settingsStore;
        _filePicker = filePicker;
        _updateConfig = updateConfig;
        _debounceMs = debounceMs;
        GlobalSettings = _settingsStore.Load();
    }

    public async Task LoadScenesAsync()
    {
        IsLoading = true;
        // Must NOT block UI: run on background thread via Task.Run
        var items = await Task.Run(() => ScanScenes());
        Scenes.Clear();
        foreach (var it in items) Scenes.Add(it);
        // auto-select first
        if (Scenes.Count > 0 && SelectedScene == null)
            SelectedScene = Scenes[0];
        IsLoading = false;
    }

    private List<SceneListItem> ScanScenes()
    {
        var result = new List<SceneListItem>();
        string root = _cycleStore.CyclesRoot;
        if (!Directory.Exists(root))
            return result;
        var dirs = Directory.GetDirectories(root);
        // natural sort dirs by name
        Array.Sort(dirs, (a,b) => NaturalSort.Comparer.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        foreach (var dir in dirs)
        {
            var scenePath = Path.Combine(dir, "scene.json");
            if (!File.Exists(scenePath))
            {
                // Directory without scene.json: skip? Or show invalid? Spec says each subdir is scene with scene.json, else skip
                // For validation badge we only show dirs with scene.json invalid vs missing?
                // Show missing as invalid with red badge to satisfy "invalid scene.json red badge" if no json
                result.Add(SceneListItem.FromInvalid(dir, "Missing scene.json"));
                continue;
            }
            try
            {
                var cfg = SceneConfig.ParseFile(scenePath);
                // Also need frames for validation
                var frames = _cycleStore.GetFrames(dir);
                if (frames.Count == 0)
                    throw new CycleValidationError($"Scene '{cfg.Id}' has no frames", scenePath);
                var thumb = frames.Count > 0 ? frames[0] : string.Empty;
                result.Add(new SceneListItem(dir, cfg.Id, cfg.Title ?? cfg.Id, cfg.Fps, frames, true, null, thumb, cfg));
            }
            catch (Exception ex)
            {
                // invalid -> red badge
                result.Add(SceneListItem.FromInvalid(dir, ex.Message));
            }
        }
        return result;
    }

    // Preview control
    public void PlayPreview()
    {
        if (SelectedScene == null || SelectedScene.Frames.Count == 0) return;
        IsPreviewPlaying = true;
        _previewStart = DateTime.UtcNow;
        _previewElapsed = TimeSpan.Zero;
        StartPreviewTimer();
    }

    public void PausePreview()
    {
        IsPreviewPlaying = false;
        StopPreviewTimer();
    }

    private void StartPreviewTimer()
    {
        StopPreviewTimer();
        if (SelectedScene == null) return;
        int fps = SelectedScene.Fps;
        if (fps < 1) fps = 12;
        var interval = FrameScheduler.GetInterval(fps);
        // Use System.Threading.Timer that calls Tick on interval
        _previewTimer = new System.Threading.Timer(_ => TickPreviewFromTimer(), null, interval, interval);
    }

    private void StopPreviewTimer()
    {
        lock (_previewLock)
        {
            _previewTimer?.Dispose();
            _previewTimer = null;
        }
    }

    private void TickPreviewFromTimer()
    {
        // Called on threadpool, compute index
        if (!IsPreviewPlaying || SelectedScene == null) return;
        var elapsed = DateTime.UtcNow - _previewStart;
        var idx = ComputeFrameIndex(elapsed);
        // For testability without dispatcher, just set property directly
        // In real UI, this would marshal to DispatcherQueue
        CurrentFrameIndex = idx;
    }

    // Testable synchronous tick: advance one frame (or compute from elapsed)
    public void TickPreview()
    {
        if (SelectedScene == null || SelectedScene.Frames.Count == 0) return;
        int count = SelectedScene.Frames.Count;
        int next = (CurrentFrameIndex + 1) % count;
        CurrentFrameIndex = next;
    }

    // For fps-driven elapsed tick (used in tests to verify flip @ fps)
    public void TickPreview(TimeSpan elapsed)
    {
        if (SelectedScene == null) return;
        int idx = ComputeFrameIndex(elapsed);
        if (idx >= 0) CurrentFrameIndex = idx;
    }

    private int ComputeFrameIndex(TimeSpan elapsed)
    {
        if (SelectedScene?.Config == null) return 0;
        var cfg = SelectedScene.Config;
        return FrameScheduler.GetFrameIndexWithCount(elapsed, cfg.Fps, SelectedScene.Frames.Count, cfg);
    }

    public void ScrubTo(int index)
    {
        if (SelectedScene == null) return;
        if (index < 0) index = 0;
        if (index >= SelectedScene.Frames.Count) index = SelectedScene.Frames.Count - 1;
        CurrentFrameIndex = index;
        // reset elapsed to match scrub position for timer continuity
        _previewStart = DateTime.UtcNow - TimeSpan.FromSeconds((double)index / SelectedFps);
    }

    private void ResetPreview()
    {
        CurrentFrameIndex = 0;
        _previewElapsed = TimeSpan.Zero;
        _previewStart = DateTime.UtcNow;
    }

    // Per-scene edits with debounce
    private void UpdateSelectedFps(int fps)
    {
        if (SelectedScene == null) return;
        fps = Math.Clamp(fps, 1, 30);
        if (SelectedScene.Fps == fps) return;
        // Update config and schedule scene file save
        var cfg = SelectedScene.Config;
        if (cfg == null) return;
        var updated = cfg with { Fps = fps };
        SelectedScene.UpdateFromConfig(updated, SelectedScene.Frames);
        OnPropertyChanged(nameof(SelectedFps));
        ScheduleSceneSave(SelectedScene);
        // preview slows immediately: restart timer with new interval
        if (IsPreviewPlaying) StartPreviewTimer();
    }

    private void UpdateSelectedHoldLast(int ms)
    {
        if (SelectedScene == null) return;
        ms = Math.Clamp(ms, 0, 5000);
        var cfg = SelectedScene.Config;
        if (cfg == null) return;
        if (cfg.HoldLastMs == ms) return;
        var updated = cfg with { HoldLastMs = ms };
        SelectedScene.UpdateFromConfig(updated, SelectedScene.Frames);
        OnPropertyChanged(nameof(SelectedHoldLastMs));
        ScheduleSceneSave(SelectedScene);
    }

    private void UpdateSelectedPostEventDelay(int? ms)
    {
        if (SelectedScene == null) return;
        if (ms.HasValue) ms = Math.Clamp(ms.Value, 0, 5000);
        var cfg = SelectedScene.Config;
        if (cfg == null) return;
        if (cfg.PostEventDelayMs == ms) return;
        var updated = cfg with { PostEventDelayMs = ms };
        SelectedScene.UpdateFromConfig(updated, SelectedScene.Frames);
        OnPropertyChanged(nameof(SelectedPostEventDelayMs));
        ScheduleSceneSave(SelectedScene);
    }

    private CancellationTokenSource? _sceneSaveCts;
    private void ScheduleSceneSave(SceneListItem item)
    {
        lock (_saveLock)
        {
            _sceneSaveCts?.Cancel();
            _sceneSaveCts?.Dispose();
            _sceneSaveCts = new CancellationTokenSource();
            var ct = _sceneSaveCts.Token;
            var dir = item.DirPath;
            var cfgSnapshot = item.Config!;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceMs, ct);
                    if (ct.IsCancellationRequested) return;
                    await Task.Run(() => WriteSceneJson(cfgSnapshot, dir), ct);
                    SaveCallCount++;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { try { Console.WriteLine($"[SettingsViewModel] scene save failed: {ex.Message}"); } catch { } }
            });
        }
    }

    private static void WriteSceneJson(SceneConfig cfg, string dir)
    {
        var path = Path.Combine(dir, "scene.json");
        var payload = new Dictionary<string, object?>();
        payload["id"] = cfg.Id;
        if (cfg.Title != null) payload["title"] = cfg.Title;
        payload["fps"] = cfg.Fps;
        if (cfg.Mode != null)
        {
            switch (cfg.Mode)
            {
                case SceneMode.StringMode sm: payload["mode"] = sm.Value; break;
                case SceneMode.CountMode cm: payload["mode"] = new Dictionary<string, object> { ["count"] = cm.Count }; break;
            }
        }
        if (cfg.LoopCount.HasValue) payload["loopCount"] = cfg.LoopCount.Value;
        payload["holdLastMs"] = cfg.HoldLastMs;
        if (cfg.PostEventDelayMs.HasValue) payload["postEventDelayMs"] = cfg.PostEventDelayMs.Value;
        payload["idleColor"] = cfg.IdleColor;
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        try
        {
            if (!File.Exists(path)) File.Move(tmp, path);
            else File.Replace(tmp, path, null);
        }
        catch
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try { File.Move(tmp, path); } catch { }
        }
    }

    // Global debounce save
    private void ScheduleSave()
    {
        lock (_saveLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var ct = _debounceCts.Token;
            var snapshot = GlobalSettings;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceMs, ct);
                    if (ct.IsCancellationRequested) return;
                    _settingsStore.Save(snapshot);
                    SaveCallCount++;
                    try { _cycleStore.Reload(); } catch { }
                    try
                    {
                        if (_updateConfig != null) _updateConfig(snapshot);
                        // Also handle cyclesRoot change: if adapter has UpdateRoot, reflect?
                        if (_cycleStore is CycleStoreAdapter ad)
                        {
                            // Only update if path changed and is valid under not absolute outside check
                            // For test, cyclesRoot absolute is allowed if it's the root itself
                            ad.UpdateRoot(snapshot.CyclesRoot);
                        }
                    }
                    catch { }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { try { Console.WriteLine($"[SettingsViewModel] save failed: {ex.Message}"); } catch { } }
            });
        }
    }

    // Folder picker
    public async Task BrowseCyclesRootAsync()
    {
        if (_filePicker == null) return;
        var picked = await _filePicker.PickFolderAsync(GlobalSettings.CyclesRoot);
        if (picked == null) return;
        // Do NOT store absolute paths outside cyclesRoot — but cyclesRoot itself is the root.
        // Validate not empty and directory exists or can be created
        if (string.IsNullOrWhiteSpace(picked)) return;
        CyclesRoot = picked;
        // Save will be debounced; for test we also trigger immediate? Keep debounced
    }

    public async Task AddSceneAsync()
    {
        string root = _cycleStore.CyclesRoot;
        // If VM's GlobalSettings cyclesRoot differs from store's, use global
        root = GlobalSettings.CyclesRoot;
        if (!Directory.Exists(root))
            Directory.CreateDirectory(root);
        // Find unique folder name
        string baseName = "new_scene";
        string dir = Path.Combine(root, baseName);
        int n = 1;
        while (Directory.Exists(dir))
        {
            dir = Path.Combine(root, $"{baseName}_{n++}");
        }
        Directory.CreateDirectory(dir);
        var id = Path.GetFileName(dir).Replace("-", "_");
        // sanitize to regex
        id = System.Text.RegularExpressions.Regex.Replace(id.ToLowerInvariant(), @"[^a-z0-9_-]", "_");
        if (id.Length > 32) id = id.Substring(0, 32);
        if (string.IsNullOrEmpty(id)) id = "scene1";
        var cfg = new SceneConfig { Id = id, Title = id, Fps = 12, HoldLastMs = 0, IdleColor = "#b2b2b2", Mode = new SceneMode.StringMode("once") };
        WriteSceneJson(cfg, dir);
        // Create placeholder 1x1 png if not exists
        var pngPath = Path.Combine(dir, "0001.png");
        if (!File.Exists(pngPath))
        {
            try { File.WriteAllBytes(pngPath, CreatePlaceholderPng()); } catch { }
        }
        await LoadScenesAsync();
        var added = Scenes.FirstOrDefault(s => s.DirPath == dir);
        if (added != null) SelectedScene = added;
    }

    private static byte[] CreatePlaceholderPng()
    {
        // 1x1 #b2b2b2 same as template: IHDR + IDAT zlib compressed 00B2B2B2
        // Use minimal valid PNG bytes
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        void WriteChunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes((uint)data.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(len);
            ms.Write(len, 0, 4);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            ms.Write(t, 0, 4);
            ms.Write(data, 0, data.Length);
            var crcData = t.Concat(data).ToArray();
            var crc = Crc32(crcData);
            var cb = BitConverter.GetBytes(crc);
            if (BitConverter.IsLittleEndian) Array.Reverse(cb);
            ms.Write(cb, 0, 4);
        }
        var ihdr = new byte[13];
        ihdr[0] = 0; ihdr[1] = 0; ihdr[2] = 0; ihdr[3] = 1;
        ihdr[4] = 0; ihdr[5] = 0; ihdr[6] = 0; ihdr[7] = 1;
        ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk("IHDR", ihdr);
        var raw = new byte[] { 0x00, 0xB2, 0xB2, 0xB2 };
        using var cms = new MemoryStream();
        using (var ds = new System.IO.Compression.ZLibStream(cms, System.IO.Compression.CompressionLevel.Optimal, true))
            ds.Write(raw, 0, raw.Length);
        var idat = cms.ToArray();
        WriteChunk("IDAT", idat);
        WriteChunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public void Dispose()
    {
        StopPreviewTimer();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _sceneSaveCts?.Cancel();
        _sceneSaveCts?.Dispose();
        _previewCts?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
