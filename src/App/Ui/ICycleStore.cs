using OsageLagtrain.App.Cycles;

namespace OsageLagtrain.App.Ui;

public interface ICycleStore
{
    string CyclesRoot { get; }
    IReadOnlyList<CycleInfo> LoadAll();
    IReadOnlyList<string> GetFrames(string sceneDirOrId);
    CycleInfo Load(string sceneId);
    void Reload();
}
