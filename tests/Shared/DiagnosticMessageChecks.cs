using System.Collections;
using System.Globalization;
using System.Resources;
using PortHarbor;

namespace PortHarbor.Checks;

internal static class DiagnosticMessageChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(DiagnosticMessageCatalog).Assembly;
        check(assembly == typeof(SessionService).Assembly && assembly == typeof(DiagnosticPresenter).Assembly
            && assembly.GetReferencedAssemblies().All(a => a.Name is not ("System.Windows.Forms" or "PortHarbor.Infrastructure" or "PortHarbor.Platform.Windows")),
            "diagnostic messages and safe presentation run in Application without UI or platform dependencies");
        var resources = new ResourceManager("PortHarbor.Resources.DiagnosticMessages", assembly);
        var expectedKeys = Enum.GetNames<ConnectionErrorCode>().Select(n => "Error_" + n)
            .Concat(Enum.GetNames<ConnectionStage>().Select(n => "Stage_" + n))
            .Concat(Enum.GetNames<ConnectionState>().Select(n => "State_" + n))
            .Concat(["ReadyToConnect", "ExternalDesktopStarted", "StatusErrorFormat", "OperationFormat"]).Order().ToArray();
        var resourceSet = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        check(resourceSet.Cast<DictionaryEntry>().Select(e => (string)e.Key).Order().SequenceEqual(expectedKeys),
            "embedded diagnostic resource keys exactly cover every state, stage and error plus the four presentation messages");
        foreach (var error in Enum.GetValues<ConnectionErrorCode>())
            check(error == ConnectionErrorCode.None ? DiagnosticMessageCatalog.Error(error) == ""
                : !string.IsNullOrWhiteSpace(DiagnosticMessageCatalog.Error(error)),
                "diagnostic catalog explicitly defines error " + error);
        foreach (var stage in Enum.GetValues<ConnectionStage>())
            check(stage == ConnectionStage.None ? DiagnosticMessageCatalog.Stage(stage) == ""
                : !string.IsNullOrWhiteSpace(DiagnosticMessageCatalog.Stage(stage)),
                "diagnostic catalog explicitly defines stage " + stage);
        check(Enum.GetValues<ConnectionState>().All(state => !string.IsNullOrWhiteSpace(DiagnosticMessageCatalog.State(state))),
            "diagnostic catalog explicitly defines text for all lifecycle states");
        check(DiagnosticMessageCatalog.ReadyToConnect == "准备连接"
            && DiagnosticMessageCatalog.ExternalDesktopStarted == "已启动 Windows 远程桌面客户端",
            "catalog preserves the ready prompt and external-client-only RDP success wording");

        var diagnostic = new ConnectionDiagnostic(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), Protocol.SSH, ConnectionState.Failed,
            ConnectionStage.Authenticating, ConnectionErrorCode.AuthenticationFailed, false, "private-host/password/私钥\r\n\x1b[31m");
        check(DiagnosticPresenter.Status(diagnostic) == "正在验证身份（身份验证失败）"
            && DiagnosticPresenter.Error(diagnostic) == "正在验证身份（身份验证失败）。操作编号：01234567",
            "resource-based diagnostics preserve the authentication failure text and eight-character operation reference");
        var closed = diagnostic with { State = ConnectionState.Closed, Stage = ConnectionStage.Closing, ErrorCode = ConnectionErrorCode.None };
        check(DiagnosticPresenter.Status(closed) == "已关闭"
            && DiagnosticPresenter.Status(closed with { ErrorCode = ConnectionErrorCode.AuthenticationFailed }) == "正在关闭（身份验证失败）",
            "normal closed status wins over closing stage while failed cleanup diagnostics retain their error");
        check(DiagnosticPresenter.Status(diagnostic with { Protocol = Protocol.RDP, State = ConnectionState.Connected, ErrorCode = ConnectionErrorCode.None })
                == "已启动 Windows 远程桌面客户端"
            && DiagnosticPresenter.Status(diagnostic with { State = ConnectionState.Connected, ErrorCode = ConnectionErrorCode.None }) == "已连接",
            "RDP client startup remains distinct from a connected network session");
        var unknown = diagnostic with { State = (ConnectionState)999, Stage = (ConnectionStage)999, ErrorCode = (ConnectionErrorCode)999, Protocol = (Protocol)999 };
        check(DiagnosticPresenter.Error(unknown) == "连接异常（未知错误）。操作编号：01234567"
            && DiagnosticMessageCatalog.Error((ConnectionErrorCode)(-1)) == "未知错误"
            && DiagnosticMessageCatalog.Stage((ConnectionStage)(-1)) == ""
            && DiagnosticMessageCatalog.State((ConnectionState)(-1)) == "连接异常",
            "undefined enum values have stable safe fallbacks without exposing numeric values or raw messages");
        check(DiagnosticPresenter.Status(unknown with { State = ConnectionState.Connecting, ErrorCode = ConnectionErrorCode.None }) == "正在连接"
            && DiagnosticPresenter.Status(unknown with { State = ConnectionState.Closed, ErrorCode = ConnectionErrorCode.None }) == "已关闭"
            && DiagnosticPresenter.Status(unknown with { State = ConnectionState.Failed, ErrorCode = ConnectionErrorCode.None }) == "连接异常",
            "an absent or unknown stage falls back to the lifecycle state");

        var oldCulture = CultureInfo.CurrentCulture; var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string culture in new[] { "zh-CN", "en-US", "fr-FR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                check(DiagnosticPresenter.Error(diagnostic) == "正在验证身份（身份验证失败）。操作编号：01234567"
                    && DiagnosticMessageCatalog.ReadyToConnect == "准备连接",
                    "missing language resources fall back to the bundled Chinese catalog under " + culture);
            }
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUiCulture; }
        foreach (var protocol in Enum.GetValues<Protocol>())
        {
            bool safe = true;
            foreach (var state in Enum.GetValues<ConnectionState>())
            foreach (var stage in Enum.GetValues<ConnectionStage>())
            foreach (var error in Enum.GetValues<ConnectionErrorCode>())
            {
                var value = diagnostic with { Protocol = protocol, State = state, Stage = stage, ErrorCode = error };
                string message = DiagnosticPresenter.Error(value);
                safe &= message == DiagnosticPresenter.Error(value with { Message = "different private endpoint/secret", Retryable = !value.Retryable })
                    && message.EndsWith("。操作编号：01234567", StringComparison.Ordinal)
                    && !message.Any(char.IsControl) && !message.Contains("private") && !message.Contains("私钥");
            }
            check(safe, protocol + " presentation across every state, stage and error never incorporates raw messages or control characters");
        }
        check(resourceSet.Cast<DictionaryEntry>().All(entry => entry.Value is string text && !text.Any(char.IsControl)),
            "bundled diagnostic messages contain only plain text without terminal control characters");
    }
}
