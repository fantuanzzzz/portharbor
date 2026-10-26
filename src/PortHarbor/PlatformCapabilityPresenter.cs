namespace PortHarbor;

internal static class PlatformCapabilityPresenter
{
    internal static string Name(PlatformCapability capability) => capability switch
    {
        PlatformCapability.LocalShell => "本地 Shell",
        PlatformCapability.SerialPort => "串口",
        PlatformCapability.RemoteDesktop => "RDP 远程桌面",
        PlatformCapability.SecretProtection => "Windows 凭据保护",
        _ => "平台功能"
    };

    internal static string Describe(PlatformCapability capability, PlatformCapabilityStatus state)
    {
        string name = Name(capability);
        if (state.IsAvailable) return name + "：运行支持可用";
        return state.Reason switch
        {
            PlatformUnavailableReason.UnsupportedOperatingSystem => name + "不可用：当前实现需要 Windows。",
            PlatformUnavailableReason.InspectionFailed => name + "不可用：无法检查系统支持，请检查系统权限后重新检查。",
            _ => capability switch
            {
                PlatformCapability.LocalShell => "本地 Shell 不可用：系统缺少所需的伪控制台支持，请更新 Windows 后重试。",
                PlatformCapability.RemoteDesktop => "RDP 远程桌面不可用：未找到系统远程桌面客户端，请安装或修复 Windows 远程桌面组件。",
                PlatformCapability.SerialPort => "串口不可用：系统缺少串口运行支持。",
                _ => "Windows 凭据保护不可用：系统缺少用户加密支持，请修复 Windows 加密组件。"
            }
        };
    }

    internal static string ForProtocol(Protocol protocol, PlatformCapabilityStatus state) => Describe(protocol switch
    {
        Protocol.Shell => PlatformCapability.LocalShell,
        Protocol.Serial => PlatformCapability.SerialPort,
        _ => PlatformCapability.RemoteDesktop
    }, state);
}
