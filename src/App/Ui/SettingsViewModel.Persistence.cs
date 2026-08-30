using System.Text.Json;
using OsageLagtrain.App.Cycles;

namespace OsageLagtrain.App.Ui;

public sealed partial class SettingsViewModel
{
    private void UpdateSelectedFps(int fps)
    {
        if (SelectedScene == null) return;
        fps = Math.Clamp(fps, 1, 30);
        if (SelectedScene.Fps == fps) return;
        var cfg = SelectedScene.Config;
        if (cfg == null) return;
        var updated = cfg with { Fps = fps };
        SelectedScene.UpdateFromConfig(updated, SelectedScene.Frames);
        OnPropertyChanged(nameof(SelectedFps));
        OnPropertyChanged(nameof(SelectedScene));
        ScheduleSceneSave(SelectedScene);
    }

    internal void UpdateSelectedMode(bool isLoop)
    {
        if (SelectedScene == null) return;
        var cfg = SelectedScene.Config;
        if (cfg == null) return;
        SceneMode newMode = isLoop ? new SceneMode.StringMode("loop") : new SceneMode.StringMode("once");
        string cur = (cfg.Mode as SceneMode.StringMode)?.Value ?? "once";
        string desired = isLoop ? "loop" : "once";
        if (cur == desired) return;
        var updated = cfg with { Mode = newMode };
        SelectedScene.UpdateFromConfig(updated, SelectedScene.Frames);
        OnPropertyChanged(nameof(SelectedScene));
        ScheduleSceneSave(SelectedScene);
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
                        if (_cycleStore is CycleStoreAdapter ad) ad.UpdateRoot(snapshot.CyclesRoot);
                    }
                    catch { }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { try { Console.WriteLine($"[SettingsViewModel] save failed: {ex.Message}"); } catch { } }
            });
        }
    }

    public async Task BrowseCyclesRootAsync()
    {
        if (_filePicker == null) return;
        var picked = await _filePicker.PickFolderAsync(GlobalSettings.CyclesRoot);
        if (picked == null) return;
        if (string.IsNullOrWhiteSpace(picked)) return;
        CyclesRoot = picked;
    }

    public async Task AddSceneAsync()
    {
        string root = GlobalSettings.CyclesRoot;
        if (!Directory.Exists(root))
            Directory.CreateDirectory(root);
        string baseName = "new_scene";
        string dir = Path.Combine(root, baseName);
        int n = 1;
        while (Directory.Exists(dir))
            dir = Path.Combine(root, $"{baseName}_{n++}");
        Directory.CreateDirectory(dir);
        var id = Path.GetFileName(dir).Replace("-", "_");
        id = System.Text.RegularExpressions.Regex.Replace(id.ToLowerInvariant(), @"[^a-z0-9_-]", "_");
        if (id.Length > 32) id = id.Substring(0, 32);
        if (string.IsNullOrEmpty(id)) id = "scene1";
        var cfg = new SceneConfig { Id = id, Title = id, Fps = 12, HoldLastMs = 0, IdleColor = "#b2b2b2", Mode = new SceneMode.StringMode("once") };
        WriteSceneJson(cfg, dir);
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
}
