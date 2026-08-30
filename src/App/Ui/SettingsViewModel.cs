using System.Collections.ObjectModel;
using System.ComponentModel;
using OsageLagtrain.App.Cycles;
using OsageLagtrain.App.Rendering;

namespace OsageLagtrain.App.Ui;

public sealed partial class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICycleStore _cycleStore;
    private readonly ISettingsStore _settingsStore;
    private readonly IFilePicker? _filePicker;
    private readonly Action<SettingsConfig>? _updateConfig;
    private readonly int _debounceMs;

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _previewCts;
    internal readonly object _saveLock = new();

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
    public int SaveCallCount { get; private set; }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); } }

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
        var items = await Task.Run(() => ScanScenes());
        Scenes.Clear();
        foreach (var it in items) Scenes.Add(it);
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
        Array.Sort(dirs, (a,b) => NaturalSort.Comparer.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        foreach (var dir in dirs)
        {
            var scenePath = Path.Combine(dir, "scene.json");
            if (!File.Exists(scenePath))
            {
                result.Add(SceneListItem.FromInvalid(dir, "Missing scene.json"));
                continue;
            }
            try
            {
                var cfg = SceneConfig.ParseFile(scenePath);
                var frames = _cycleStore.GetFrames(dir);
                if (frames.Count == 0)
                    throw new CycleValidationError($"Scene '{cfg.Id}' has no frames", scenePath);
                var thumb = frames.Count > 0 ? frames[0] : string.Empty;
                result.Add(new SceneListItem(dir, cfg.Id, cfg.Title ?? cfg.Id, cfg.Fps, frames, true, null, thumb, cfg));
            }
            catch (Exception ex)
            {
                result.Add(SceneListItem.FromInvalid(dir, ex.Message));
            }
        }
        return result;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
