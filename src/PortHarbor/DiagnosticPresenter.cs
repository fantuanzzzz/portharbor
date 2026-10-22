namespace PortHarbor;

public static class DiagnosticPresenter
{
    public static string Status(ConnectionDiagnostic diagnostic)
    {
        var stage = diagnostic.Stage switch
        {
            ConnectionStage.Authenticating => "正在验证身份",
            ConnectionStage.HostKeyVerification => "正在验证主机密钥",
            ConnectionStage.Reading => "正在读取",
            ConnectionStage.Writing => "正在写入",
            ConnectionStage.Closing => "正在关闭",
            _ => diagnostic.State switch
            {
                ConnectionState.Connecting => "正在连接",
                ConnectionState.Connected => "已连接",
                ConnectionState.Closed => "已关闭",
                _ => "连接异常"
            }
        };
        if (diagnostic.ErrorCode == ConnectionErrorCode.None) return stage;
        return $"{stage}（{ErrorCode(diagnostic.ErrorCode)}）";
    }

    public static string Error(ConnectionDiagnostic diagnostic) =>
        $"{Status(diagnostic)}。操作编号：{diagnostic.OperationId.ToString("N")[..8]}";

    static string ErrorCode(ConnectionErrorCode code) => code switch
    {
        ConnectionErrorCode.Cancelled => "已取消",
        ConnectionErrorCode.InvalidConfiguration => "配置无效",
        ConnectionErrorCode.AuthenticationFailed => "身份验证失败",
        ConnectionErrorCode.HostKeyRejected => "主机密钥未接受",
        ConnectionErrorCode.NetworkUnavailable => "网络不可用",
        ConnectionErrorCode.Timeout => "连接超时",
        ConnectionErrorCode.PermissionDenied => "权限不足",
        ConnectionErrorCode.PlatformUnavailable => "平台资源不可用",
        ConnectionErrorCode.ProtocolError => "协议操作失败",
        _ => "未知错误"
    };
}