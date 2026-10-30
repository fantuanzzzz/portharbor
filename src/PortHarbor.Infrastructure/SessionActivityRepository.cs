using System.Text.Json;

namespace PortHarbor;

public sealed class SessionActivityRepository(IAppDataPaths paths) : ISessionActivityRepository
{
    readonly string path = Path.Combine(paths.ConfigurationDirectory, "session-activity.json");
    sealed record Document(int SchemaVersion, SessionActivityState Activity);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public SessionActivityState Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("会话库偏好文件过大，原文件已保留。");
            var document = JsonSerializer.Deserialize<Document>(stream, Json);
            if (document is not { SchemaVersion: 1, Activity: not null }) throw new InvalidDataException("会话库偏好版本或结构不受支持，原文件已保留。");
            document.Activity.Validate(); return document.Activity;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException error) { throw new InvalidDataException("会话库偏好文件损坏，原文件已保留。", error); }
    }
    public void Save(SessionActivityState state)
    {
        state.Validate(); _ = Load();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new Document(1, state), Json); stream.Flush(true); }
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
