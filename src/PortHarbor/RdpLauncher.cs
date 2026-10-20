using System.Diagnostics;
using System.Text;

namespace PortHarbor;

public static class RdpLauncher
{
    public static string Configuration(Session s)
    {
        s.Validate();
        if (s.Username.Any(char.IsControl)) throw new ArgumentException("用户名不能包含控制字符。");
        string host = s.Host.Contains(':') ? $"[{s.Host}]" : s.Host;
        return $"full address:s:{host}:{s.Port}\r\nusername:s:{s.Username}\r\nscreen mode id:i:{(s.RdpFullScreen ? 2 : 1)}\r\ndesktopwidth:i:1280\r\ndesktopheight:i:800\r\nredirectclipboard:i:{(s.Clipboard ? 1 : 0)}\r\nprompt for credentials:i:1\r\nauthentication level:i:1\r\nenablecredsspsupport:i:1\r\n";
    }
    public static void Launch(Session s, string directory)
    {
        string path = Path.Combine(directory, "rdp-" + Guid.NewGuid().ToString("N") + ".rdp");
        File.WriteAllText(path, Configuration(s), Encoding.Unicode);
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe")) { UseShellExecute = false };
            info.ArgumentList.Add(path);
            var process = Process.Start(info) ?? throw new IOException("无法启动 Windows 远程桌面。");
            _ = Task.Run(async () => { try { await process.WaitForExitAsync(); } finally { process.Dispose(); try { File.Delete(path); } catch { } } });
        }
        catch { File.Delete(path); throw; }
    }
}
