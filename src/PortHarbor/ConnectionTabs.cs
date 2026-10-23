namespace PortHarbor;

internal sealed class ConnectionTabs : TabControl
{
    internal event Action<TabPage>? CloseRequested;
    internal ConnectionTabs()
    {
        Dock = DockStyle.Fill;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Padding = new Point(24, 7);
    }
    Rectangle CloseBounds(int index)
    {
        var tab = GetTabRect(index);
        int size = Math.Max(12, (int)(14 * DeviceDpi / 96f));
        return new Rectangle(tab.Right - size - 5, tab.Top + (tab.Height - size) / 2, size, size);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var tab = TabPages[e.Index];
        using var brush = new SolidBrush(e.Index == SelectedIndex ? Color.White : SystemColors.Control);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var close = CloseBounds(e.Index);
        var text = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, Math.Max(0, close.Left - e.Bounds.Left - 12), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, tab.Text, Font, text, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var pen = new Pen(Color.DimGray, 1.5f);
        var cross = Rectangle.Inflate(close, -3, -3);
        e.Graphics.DrawLine(pen, cross.Left, cross.Top, cross.Right, cross.Bottom);
        e.Graphics.DrawLine(pen, cross.Right, cross.Top, cross.Left, cross.Bottom);
    }
    void RequestMouseClose(MouseEventArgs e)
    {
        for (int i = 0; i < TabCount; i++)
            if (GetTabRect(i).Contains(e.Location) && (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left && CloseBounds(i).Contains(e.Location)))
            { CloseRequested?.Invoke(TabPages[i]); return; }
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        for (int i = 0; i < TabCount; i++)
            if (GetTabRect(i).Contains(e.Location) && !CloseBounds(i).Contains(e.Location))
            { CloseRequested?.Invoke(TabPages[i]); return; }
    }
    protected override void WndProc(ref Message m)
    {
        // Native TabControl does not reliably surface these as managed mouse events.
        if (m.Msg == 0x203)
        {
            long point = m.LParam.ToInt64();
            OnMouseDoubleClick(new MouseEventArgs(MouseButtons.Left, 2, unchecked((short)point), unchecked((short)(point >> 16)), 0));
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg is 0x202 or 0x208)
        {
            long point = m.LParam.ToInt64();
            var button = m.Msg == 0x202 ? MouseButtons.Left : MouseButtons.Middle;
            base.WndProc(ref m);
            RequestMouseClose(new MouseEventArgs(button, 1, unchecked((short)point), unchecked((short)(point >> 16)), 0));
            return;
        }
        base.WndProc(ref m);
    }
}
