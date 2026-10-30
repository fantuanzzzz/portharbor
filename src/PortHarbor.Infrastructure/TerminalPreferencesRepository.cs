using System.Text.Json;

namespace PortHarbor;

public sealed class TerminalPreferencesRepository(IAppDataPaths paths) : ITerminalPreferencesRepository
{
    readonly string path = Path.Combine(paths.ConfigurationDirectory, "terminal-preferences.json");
    sealed record Document(int SchemaVersion, TerminalPreferences Terminal);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public TerminalPreferences Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<Document>(stream, Json);
            if (document is not { SchemaVersion: 1, Terminal: not null }) throw new InvalidDataException("终端偏好版本或结构不受支持，原文件已保留。");
            document.Terminal.Validate(); return document.Terminal;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException e) { throw new InvalidDataException("终端偏好文件损坏，原文件已保留。", e); }
    }
    public void Save(TerminalPreferences value)
    {
        value.Validate();
        // Refuse to silently overwrite unreadable, corrupt or future-version data.
        _ = Load();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new Document(1, value), Json); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
