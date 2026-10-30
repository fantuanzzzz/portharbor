namespace PortHarbor.Checks;

internal static partial class Program
{
    static void DiagnosticBundleChecks(string root) => DiagnosticExportChecks.Run(root, Check);
}
