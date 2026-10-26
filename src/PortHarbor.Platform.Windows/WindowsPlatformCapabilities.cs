using System.Runtime.InteropServices;

namespace PortHarbor;

/// <summary>Checks prerequisites without opening devices, starting clients or handling secrets.
/// Results are refreshed on every request; runtime operations remain authoritative.</summary>
public sealed class WindowsPlatformCapabilities : IPlatformCapabilities
{
    readonly Func<bool> isWindows;
    readonly Func<string, string[], bool> hasExports;
    readonly Func<bool> hasDesktopClient;

    public WindowsPlatformCapabilities() : this(OperatingSystem.IsWindows, HasExports,
        () => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe"))) { }

    internal WindowsPlatformCapabilities(Func<bool> isWindows, Func<string, string[], bool> hasExports, Func<bool> hasDesktopClient)
    {
        this.isWindows = isWindows; this.hasExports = hasExports; this.hasDesktopClient = hasDesktopClient;
    }

    public PlatformCapabilityStatus Get(PlatformCapability capability)
    {
        if (!Enum.IsDefined(capability)) throw new ArgumentOutOfRangeException(nameof(capability));
        try
        {
            if (!isWindows()) return new(false, PlatformUnavailableReason.UnsupportedOperatingSystem);
            bool available = capability switch
            {
                PlatformCapability.SecretProtection => hasExports("crypt32.dll", ["CryptProtectData", "CryptUnprotectData"]),
                PlatformCapability.LocalShell => hasExports("kernel32.dll", ["CreatePseudoConsole", "ResizePseudoConsole", "ClosePseudoConsole"]),
                // Device enumeration and opening are separate operations. An empty list is not lack of platform support.
                PlatformCapability.SerialPort => true,
                PlatformCapability.RemoteDesktop => hasDesktopClient(),
                _ => false
            };
            return available ? PlatformCapabilityStatus.Available : new(false, PlatformUnavailableReason.MissingSystemComponent);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or System.ComponentModel.Win32Exception or DllNotFoundException or BadImageFormatException or PlatformNotSupportedException)
        {
            return new(false, PlatformUnavailableReason.InspectionFailed);
        }
    }

    static bool HasExports(string library, string[] exports)
    {
        // Resolve only OS libraries in the system directory, not DLLs from the working directory.
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), library);
        if (!NativeLibrary.TryLoad(path, out var handle)) return false;
        try { return exports.All(name => NativeLibrary.TryGetExport(handle, name, out _)); }
        finally { NativeLibrary.Free(handle); }
    }
}
