using System.ComponentModel;
using OsageLagtrain.App.Cycles;

namespace OsageLagtrain.App.Ui;

public sealed class SceneListItem : INotifyPropertyChanged
{
    public string DirPath { get; }
    public string Id { get; private set; }
    public string Title { get; private set; }
    public int Fps { get; private set; }
    public IReadOnlyList<string> Frames { get; private set; } = Array.Empty<string>();
    public bool IsValid { get; private set; }
    public string? ValidationError { get; private set; }
    public string ThumbnailPath { get; private set; } = string.Empty;
    public SceneConfig? Config { get; private set; }

    public string FpsBadge => $"{Fps} fps";
    public string ValidationIcon => IsValid ? "\u2714" : "\u2716"; // check / cross
    public string ValidationColor => IsValid ? "#2ea043" : "#f85149"; // green / red

    public event PropertyChangedEventHandler? PropertyChanged;

    public SceneListItem(string dirPath, string id, string title, int fps, IReadOnlyList<string> frames, bool isValid, string? validationError, string thumbnailPath, SceneConfig? config)
    {
        DirPath = dirPath;
        Id = id;
        Title = title;
        Fps = fps;
        Frames = frames;
        IsValid = isValid;
        ValidationError = validationError;
        ThumbnailPath = thumbnailPath;
        Config = config;
    }

    public static SceneListItem FromInvalid(string dirPath, string error)
    {
        var name = Path.GetFileName(dirPath);
        return new SceneListItem(dirPath, name, name, 12, Array.Empty<string>(), false, error, string.Empty, null);
    }

    internal void UpdateFromConfig(SceneConfig cfg, IReadOnlyList<string> frames)
    {
        Id = cfg.Id;
        Title = cfg.Title ?? cfg.Id;
        Fps = cfg.Fps;
        Frames = frames;
        IsValid = true;
        ValidationError = null;
        Config = cfg;
        ThumbnailPath = frames.Count > 0 ? frames[0] : string.Empty;
        OnChanged(nameof(Id));
        OnChanged(nameof(Title));
        OnChanged(nameof(Fps));
        OnChanged(nameof(FpsBadge));
        OnChanged(nameof(IsValid));
        OnChanged(nameof(ValidationError));
        OnChanged(nameof(ValidationIcon));
        OnChanged(nameof(ValidationColor));
        OnChanged(nameof(ThumbnailPath));
        OnChanged(nameof(Config));
    }

    void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
