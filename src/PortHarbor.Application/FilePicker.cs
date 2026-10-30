namespace PortHarbor;

/// <summary>Single-file selection. Null means cancellation; selecting a path performs no file I/O.</summary>
public interface IFilePicker
{
    string? OpenFile(FilePickerOptions options);
    string? SaveFile(FilePickerOptions options);
}

public sealed record FilePickerOptions(string Filter = "", string FileName = "");

/// <summary>Multiple existing files. Empty means cancellation; selection performs no file I/O.</summary>
public interface IMultiFilePicker
{
    IReadOnlyList<string> OpenFiles(FilePickerOptions options);
}

/// <summary>Directory selection only. Null means cancellation; no directory is created.</summary>
public interface IFolderPicker
{
    string? SelectFolder(string selectedPath = "");
}
