using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortHarbor;

// Consumes resolved paths; owns format compatibility, legacy copy and atomic replacement.
internal sealed class ConfigurationFileStorage
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    internal string DirectoryPath { get; }
    internal bool IsPortable { get; }
    internal string FilePath { get; }

    internal ConfigurationFileStorage(IAppDataPaths paths)
    {
        IsPortable = paths.IsPortable;
        DirectoryPath = paths.ConfigurationDirectory;
        FilePath = paths.ConfigurationFile;
        Directory.CreateDirectory(DirectoryPath);
        var legacySettings = paths.LegacyConfigurationFile;
        if (!IsPortable && legacySettings != null && !File.Exists(FilePath) && File.Exists(legacySettings)) File.Copy(legacySettings, FilePath);
    }

    internal AppData Load()
    {
        if (File.Exists(FilePath)) return Read(FilePath);
        var data = new AppData();
        if (IsPortable) Save(data);
        return data;
    }

    internal static AppData Read(string path)
    {
        var data = (Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase) ? IniConfiguration.Read(File.ReadAllText(path)) : JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Json)) ?? throw new InvalidDataException("配置为空。");
        if (data.SchemaVersion is not (1 or 2)) throw new InvalidDataException("不支持此配置版本。原文件已保留。");
        if (data.Sessions == null || data.Credentials == null || data.HostKeys == null || data.Groups == null) throw new InvalidDataException("配置结构损坏。原文件已保留。");
        if (data.SchemaVersion == 2 && data.Credentials.Count > 0 && data.Vault == null) throw new InvalidDataException("密码库元数据缺失。原文件已保留。");
        if (data.Groups.Any(g => string.IsNullOrWhiteSpace(g) || g.Any(char.IsControl))) throw new InvalidDataException("分类数据损坏。原文件已保留。");
        foreach (var s in data.Sessions) s.Validate();
        if (data.Sessions.Select(s => s.Id).Distinct().Count() != data.Sessions.Count) throw new InvalidDataException("会话 ID 重复。");
        return data;
    }

    internal void Save(AppData data)
    {
        var temp = FilePath + ".tmp";
        using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (IsPortable) f.Write(System.Text.Encoding.UTF8.GetBytes(IniConfiguration.Write(data)));
            else JsonSerializer.Serialize(f, data, Json);
            f.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }

    internal void Export(string path, AppData data) => File.WriteAllText(path, JsonSerializer.Serialize(new AppData
    {
        Groups = data.Groups.ToList(),
        Sessions = data.Sessions.Select(s => s with { SavePassword = false }).ToList()
    }, Json));
}
