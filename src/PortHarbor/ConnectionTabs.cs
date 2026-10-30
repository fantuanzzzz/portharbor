namespace PortHarbor;

internal sealed class ConnectionTabs : TabControl
{
    internal event Action<TabPage>? CloseRequested;
    internal event Action<TabPage>? CloseOthersRequested;
    internal event Action<TabPage>? RenameRequested;
    internal event Action<TabPage>? CopyTitleRequested;
    internal ConnectionTabs()
    {
        Dock = DockStyle.Fill;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        ShowToolTips = true;
        Padding = new Point(24, 7);
        var menu = new ContextMenuStrip();
        menu.Items.Add("重命名标签…", null, (_, _) => { if (SelectedTab is { } page) RenameRequested?.Invoke(page); });
        menu.Items.Add("复制标签标题", null, (_, _) => { if (SelectedTab is { } page) CopyTitleRequested?.Invoke(page); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关闭其他标签…", null, (_, _) => { if (SelectedTab is { } page) CloseOthersRequested?.Invoke(page); });
        menu.Items.Add("关闭当前标签…", null, (_, _) => { if (SelectedTab is { } page) CloseRequested?.Invoke(page); });
        ContextMenuStrip = menu;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = UiTheme.Palette(this);
        e.Graphics.Clear(palette.Subtle);
        var content = DisplayRectangle; content.Inflate(1, 1);
        using var fill = new SolidBrush(palette.Surface); e.Graphics.FillRectangle(fill, content);
        using var edge = new Pen(palette.Border); e.Graphics.DrawRectangle(edge, content);
        for (int i = 0; i < TabCount; i++)
        {
            TabPages[i].ToolTipText = TabPages[i].Text.Length > 16 ? TabPages[i].Text : "";
            var bounds = GetTabRect(i);
            if (bounds.IntersectsWith(e.ClipRectangle))
                OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, bounds, i, i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None));
        }
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
        var palette = UiTheme.Palette(this);
        using var brush = new SolidBrush(e.Index == SelectedIndex ? palette.Surface : palette.Subtle);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var close = CloseBounds(e.Index);
        var text = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, Math.Max(0, close.Left - e.Bounds.Left - 12), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, tab.Text, Font, text, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (e.Index == SelectedIndex)
        {
            using var accent = new Pen(palette.HighContrast ? SystemColors.Highlight : palette.Accent, 2);
            e.Graphics.DrawLine(accent, e.Bounds.Left, e.Bounds.Bottom - 2, e.Bounds.Right, e.Bounds.Bottom - 2);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(text, -2, -3), palette.Text, palette.Surface);
        }
        using var pen = new Pen(palette.Muted, 1.5f);
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
