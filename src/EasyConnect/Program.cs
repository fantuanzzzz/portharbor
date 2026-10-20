namespace EasyConnect;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try
        {
            string? data = null;
            if (args.Length == 2 && args[0] == "--data-dir") data = Path.GetFullPath(args[1]);
            Application.Run(new MainForm(new Store(data)));
        }
        catch (Exception e) { MessageBox.Show("启动失败，原配置文件已保留。\n" + e.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
