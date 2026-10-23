namespace PortHarbor;

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
            var location = StorageLocation.Resolve(args, AppContext.BaseDirectory);
            using var store = new Store(location.Directory, location.Portable);
            Application.Run(new MainForm(store));
        }
        catch (Exception e) { MessageBox.Show("启动失败，原配置文件已保留。\n" + e.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

