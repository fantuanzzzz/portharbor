using PortHarbor;
using System.Text;

namespace PortHarbor.Checks;

internal static class UiShell
{
    public static void Run(string root,string result)
    {
        Program.InitializeUi();
        using var form=new Form { Width=1000,Height=650 };
        var terminal=new TerminalView { Dock=DockStyle.Fill }; form.Controls.Add(terminal);
        var session=new Session { Protocol=Protocol.Shell,Executable="cmd.exe",Arguments="/Q",WorkingDirectory=root };
        using var connection=TerminalConnectionFactory.Create(session,new Store(new WindowsSecretProtector(), new WindowsAppDataPaths(Path.Combine(root,"ui-shell"))),"",_=>false);
        var output=new StringBuilder();
        connection.Output+=text=> { lock(output) output.Append(text); terminal.Append(text); };
        terminal.Input+=connection.Send;
        var timer=new System.Windows.Forms.Timer { Interval=1000 }; int ticks=0;
        timer.Tick+=(_,_)=> { ticks++; if(ticks==2) connection.Send(Encoding.UTF8.GetBytes("echo CONPTY_OK\r")); if(ticks==5) { lock(output) File.WriteAllText(result,output.ToString()); connection.Send(Encoding.UTF8.GetBytes("exit\r")); } if(ticks==7) form.Close(); };
        form.Shown+=(_,_)=> { _=connection.Run(100,30); timer.Start(); };
        Application.Run(form); timer.Dispose();
    }
}
