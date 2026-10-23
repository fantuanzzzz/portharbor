using System.Runtime.InteropServices;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendTabMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    static void TabMouseUi()
    {
        using var form = new Form { Width = 600, Height = 250 };
        var tabs = new ConnectionTabs(); form.Controls.Add(tabs);
        var first = new TabPage("first"); var second = new TabPage("second");
        tabs.TabPages.Add(first); tabs.TabPages.Add(second);
        var requests = new List<TabPage>(); tabs.CloseRequested += requests.Add;
        form.Show(); Application.DoEvents();
        var rect = tabs.GetTabRect(1);
        var position = new IntPtr(((rect.Top + rect.Height / 2) << 16) | (rect.Left + 12));
        SendTabMessage(tabs.Handle, 0x201, new IntPtr(1), position);
        SendTabMessage(tabs.Handle, 0x202, IntPtr.Zero, position);
        Check(requests.Count == 0, "single click on tab title selects without requesting close");
        SendTabMessage(tabs.Handle, 0x203, new IntPtr(1), position);
        SendTabMessage(tabs.Handle, 0x202, IntPtr.Zero, position);
        Check(requests.Count == 1 && requests[0] == second, "native double click requests closure of clicked tab");
        requests.Clear();
        var close = new IntPtr(((rect.Top + rect.Height / 2) << 16) | (rect.Right - 10));
        SendTabMessage(tabs.Handle, 0x201, new IntPtr(1), close);
        SendTabMessage(tabs.Handle, 0x202, IntPtr.Zero, close);
        Check(requests.Count == 1 && requests[0] == second, "native close-button click requests closure of clicked tab");
        requests.Clear();
        SendTabMessage(tabs.Handle, 0x207, new IntPtr(16), position);
        SendTabMessage(tabs.Handle, 0x208, IntPtr.Zero, position);
        Check(requests.Count == 1 && requests[0] == second, "native middle click requests closure of clicked tab");
        form.Close();
    }
}
