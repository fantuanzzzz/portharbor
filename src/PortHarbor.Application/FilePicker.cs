namespace PortHarbor;

/// <summary>Single-file selection. Null means cancellation; selecting a path performs no file I/O.</summary>
public interface IFilePicker
{
    string? OpenFile(FilePickerOptions options);
    string? SaveFile(FilePickerOptions options);
}

public sealed record FilePickerOptions(string Filter = "", string FileName = "");
