namespace OsageLagtrain.App.Ui;

/// <summary>Abstraction over Windows.Storage.Pickers.FolderPicker for testability.</summary>
public interface IFilePicker
{
    /// <summary>Show folder picker from initial path, return chosen folder or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string initialPath);
}
