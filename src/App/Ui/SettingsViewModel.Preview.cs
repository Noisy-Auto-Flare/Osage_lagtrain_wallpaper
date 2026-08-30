using OsageLagtrain.App.Rendering;

namespace OsageLagtrain.App.Ui;

public sealed partial class SettingsViewModel
{
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

    public void PlayPreview()
    {
        if (SelectedScene == null || SelectedScene.Frames.Count == 0) return;
        IsPreviewPlaying = true;
        _previewStart = DateTime.UtcNow;
        _previewElapsed = TimeSpan.Zero;
        // Preview timer is driven by SettingsWindow DispatcherTimer (83ms @12fps).
        // ViewModel no longer spawns its own ThreadPool timer to avoid double-tick / cross-thread PropertyChanged.
        StopPreviewTimer();
    }

    public void PausePreview()
    {
        IsPreviewPlaying = false;
        StopPreviewTimer();
    }

    private void StartPreviewTimer()
    {
        // Intentionally no-op: SettingsWindow owns the DispatcherTimer.
        // Kept for compatibility with tests that call UpdateSelectedFps which previously restarted VM timer.
        StopPreviewTimer();
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
        if (!IsPreviewPlaying || SelectedScene == null) return;
        var elapsed = DateTime.UtcNow - _previewStart;
        var idx = ComputeFrameIndex(elapsed);
        CurrentFrameIndex = idx;
    }

    public void TickPreview()
    {
        if (SelectedScene == null || SelectedScene.Frames.Count == 0) return;
        int count = SelectedScene.Frames.Count;
        int next = (CurrentFrameIndex + 1) % count;
        CurrentFrameIndex = next;
    }

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
        _previewStart = DateTime.UtcNow - TimeSpan.FromSeconds((double)index / SelectedFps);
    }

    private void ResetPreview()
    {
        CurrentFrameIndex = 0;
        _previewElapsed = TimeSpan.Zero;
        _previewStart = DateTime.UtcNow;
    }

    public void DisposePreview() => StopPreviewTimer();
}
