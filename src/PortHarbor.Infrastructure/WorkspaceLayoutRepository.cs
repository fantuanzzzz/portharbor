using System.Text.Json;

namespace PortHarbor;

public sealed class WorkspaceLayoutRepository(IAppDataPaths paths) : IWorkspaceLayoutRepository
{
    readonly string path = Path.Combine(paths.ConfigurationDirectory, "workspace-layout.json");
    sealed record Document(int SchemaVersion, WorkspaceLayout Layout);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public WorkspaceLayout Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<Document>(stream, Json);
            if (document is not { SchemaVersion: 1, Layout: not null })
                throw new InvalidDataException("工作台布局版本或结构不受支持，原文件已保留。");
            document.Layout.Validate(); return document.Layout;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException e) { throw new InvalidDataException("工作台布局文件损坏，原文件已保留。", e); }
    }
    public void Save(WorkspaceLayout value)
    {
        value.Validate(); _ = Load();
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
