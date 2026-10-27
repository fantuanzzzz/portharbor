namespace PortHarbor;

/// <summary>Owns each native dialog for one modal selection on the owner's UI thread.</summary>
public sealed class WindowsFilePicker : IFilePicker
{
    readonly IWin32Window owner;
    readonly Func<FileDialog, IWin32Window, DialogResult> show;

    public WindowsFilePicker(IWin32Window owner) : this(owner, (dialog, window) => dialog.ShowDialog(window)) { }

    internal WindowsFilePicker(IWin32Window owner, Func<FileDialog, IWin32Window, DialogResult> show)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.show = show ?? throw new ArgumentNullException(nameof(show));
    }

    public string? OpenFile(FilePickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var dialog = new OpenFileDialog { Filter = options.Filter, FileName = options.FileName, Multiselect = false };
        return show(dialog, owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? SaveFile(FilePickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var dialog = new SaveFileDialog { Filter = options.Filter, FileName = options.FileName, OverwritePrompt = true };
        return show(dialog, owner) == DialogResult.OK ? dialog.FileName : null;
    }
}
