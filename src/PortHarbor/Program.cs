namespace PortHarbor;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        args = args.Where(arg => !string.Equals(arg, "--smoke", StringComparison.Ordinal)).ToArray();
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try
        {
            bool portableDefault = System.Reflection.CustomAttributeExtensions
                .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(typeof(Program).Assembly)
                .Any(a => a.Key == "PortHarbor.PortableDefault" && a.Value == "true");
            var paths = WindowsAppDataPaths.Resolve(args, defaultPortable: portableDefault);
            using var store = new Store(new WindowsSecretProtector(), paths);
            if (smoke)
            {
                using var form = new MainForm(store, rememberLayout: false);
                form.Shown += (_, _) => form.Close();
                Application.Run(form);
            }
            else Application.Run(new MainForm(store));
            return 0;
        }
        catch (Exception e)
        {
            if (!smoke) MessageBox.Show("启动失败，原配置文件已保留。\n" + e.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

