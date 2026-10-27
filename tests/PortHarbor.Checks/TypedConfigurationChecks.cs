using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void TypedConfigurationChecks()
    {
        foreach (var protocol in Enum.GetValues<Protocol>())
        {
            var session = new Session { Protocol = protocol, Name = "类型兼容", Group = "Work/中文", Host = "server.invalid", Port = 2222,
                Username = "user", PrivateKey = "private-key-path", RemotePath = "/中文", UseTls = true,
                SerialPort = "COM42", BaudRate = 9600, Parity = "Even", StopBits = "Two", Handshake = "XOnXOff",
                Executable = "cmd.exe", Arguments = "/Q", WorkingDirectory = "C:\\", RdpFullScreen = true, Clipboard = false };
            var expected = session.ProtocolConfiguration;
            string json = JsonSerializer.Serialize(session, Store.Json);
            var restored = JsonSerializer.Deserialize<Session>(json, Store.Json)!;
            var ini = IniConfiguration.Read(IniConfiguration.Write(new AppData { Sessions = [session] })).Sessions.Single();
            Check(restored == session && ini == session && restored.ProtocolConfiguration == expected && ini.ProtocolConfiguration == expected
                && !json.Contains("ProtocolConfiguration") && SessionProtocolConfiguration.Profile(restored).Metadata.Id == session.Id,
                protocol + " typed options round trip through legacy JSON/INI while preserving IDs and inactive fields");
        }
    }
}
