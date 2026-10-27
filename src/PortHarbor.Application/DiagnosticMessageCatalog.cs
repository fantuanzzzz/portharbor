using System.Globalization;
using System.Resources;

namespace PortHarbor;

/// <summary>Trusted local text only. Unknown numeric values use stable fallbacks;
/// missing entries for defined values fail visibly and are covered by catalog checks.</summary>
public static class DiagnosticMessageCatalog
{
    static readonly ResourceManager Resources = new("PortHarbor.Resources.DiagnosticMessages", typeof(DiagnosticMessageCatalog).Assembly);

    public static string Error(ConnectionErrorCode code)
        => Text("Error_" + (Enum.IsDefined(code) ? code : ConnectionErrorCode.Unknown));
    public static string Stage(ConnectionStage stage)
        => Text("Stage_" + (Enum.IsDefined(stage) ? stage : ConnectionStage.None));
    public static string State(ConnectionState state)
        => Text("State_" + (Enum.IsDefined(state) ? state : ConnectionState.Failed));
    public static string ReadyToConnect => Text(nameof(ReadyToConnect));
    public static string ExternalDesktopStarted => Text(nameof(ExternalDesktopStarted));

    internal static string WithError(string stage, string error)
        => string.Format(CultureInfo.CurrentCulture, Text("StatusErrorFormat"), stage, error);
    internal static string WithOperation(string status, string operation)
        => string.Format(CultureInfo.CurrentCulture, Text("OperationFormat"), status, operation);

    static string Text(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException("Missing diagnostic message: " + key);
}
