using System.Text;
using System.Text.Json;

namespace PortHarbor;

internal static class StorageLocation
{
    internal static (string? Directory, bool Portable) Resolve(string[] args, string baseDirectory)
    {
        bool portable = File.Exists(Path.Combine(baseDirectory, "PortHarbor.portable")) || File.Exists(Path.Combine(baseDirectory, "PortHarbor.ini"));
        string? directory = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--portable") portable = true;
            else if (args[i] == "--installed") portable = false;
            else if (args[i] == "--data-dir" && i + 1 < args.Length) directory = Path.GetFullPath(args[++i]);
            else throw new ArgumentException("Invalid startup argument: " + args[i]);
        }
        return (directory ?? (portable ? baseDirectory : null), portable);
    }
}

// Values use JSON scalar escaping to preserve Unicode, multiline values and INI delimiters.
internal static class IniConfiguration
{
    internal static string Write(AppData data)
    {
        var text = new StringBuilder("; PortHarbor portable configuration. UTF-8; values use JSON escaping.\n");
        void Section(string name, IEnumerable<KeyValuePair<string, JsonElement>> values)
        {
            text.Append('[').Append(name).AppendLine("]");
            foreach (var (key, value) in values) text.Append(key).Append('=').AppendLine(value.GetRawText());
            text.AppendLine();
        }
        Dictionary<string, JsonElement> Fields<T>(T value) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value, Store.Json))!;
        Section("General", Fields(new { data.SchemaVersion }));
        Section("Groups", data.Groups.Select((g, i) => new KeyValuePair<string, JsonElement>(i.ToString(), JsonSerializer.SerializeToElement(g))));
        if (data.Vault != null) Section("Vault", Fields(data.Vault));
        Section("Credentials", Fields(data.Credentials));
        Section("HostKeys", data.HostKeys.Select(pair => new KeyValuePair<string, JsonElement>(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)).TrimEnd('='), JsonSerializer.SerializeToElement(pair.Value))));
        foreach (var session in data.Sessions) Section("Session:" + session.Id, Fields(session));
        return text.ToString();
    }

    internal static AppData Read(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        Dictionary<string, JsonElement>? current = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new(StringComparer.Ordinal);
                if (!sections.TryAdd(line[1..^1], current)) throw new InvalidDataException("Duplicate INI section.");
                continue;
            }
            int separator = line.IndexOf('=');
            if (current == null || separator < 1) throw new InvalidDataException("Invalid INI line.");
            using var value = JsonDocument.Parse(line[(separator + 1)..]);
            if (!current.TryAdd(line[..separator].Trim(), value.RootElement.Clone())) throw new InvalidDataException("Duplicate INI key.");
        }
        T Decode<T>(Dictionary<string, JsonElement> fields) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(fields), Store.Json)!;
        if (!sections.TryGetValue("General", out var general) || !general.ContainsKey("SchemaVersion")) throw new InvalidDataException("Missing INI version.");
        var data = Decode<AppData>(general);
        if (sections.TryGetValue("Groups", out var groups)) data.Groups = groups.Values.Select(v => v.GetString() ?? throw new InvalidDataException("Invalid group.")).ToList();
        if (sections.TryGetValue("Vault", out var vault)) data.Vault = Decode<VaultSettings>(vault);
        if (sections.TryGetValue("Credentials", out var credentials)) data.Credentials = Decode<Dictionary<string, string>>(credentials);
        if (sections.TryGetValue("HostKeys", out var hosts)) data.HostKeys = hosts.ToDictionary(pair => Encoding.UTF8.GetString(Convert.FromBase64String(pair.Key.PadRight((pair.Key.Length + 3) / 4 * 4, '='))), pair => pair.Value.GetString() ?? throw new InvalidDataException("Invalid host key."));
        foreach (var (name, fields) in sections.Where(s => s.Key.StartsWith("Session:", StringComparison.Ordinal)))
        {
            var session = Decode<Session>(fields);
            if (name != "Session:" + session.Id) throw new InvalidDataException("Session ID does not match section.");
            data.Sessions.Add(session);
        }
        return data;
    }
}
