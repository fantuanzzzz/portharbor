namespace PortHarbor;

/// <summary>Owns each native dialog for one modal selection on the owner's UI thread.</summary>
public sealed class WindowsFilePicker : IFilePicker, IFolderPicker, IMultiFilePicker
{
    readonly IWin32Window owner;
    readonly Func<FileDialog, IWin32Window, DialogResult> show;
    readonly Func<FolderBrowserDialog, IWin32Window, DialogResult> showFolder;

    public WindowsFilePicker(IWin32Window owner) : this(owner, (dialog, window) => dialog.ShowDialog(window)) { }

    internal WindowsFilePicker(IWin32Window owner, Func<FileDialog, IWin32Window, DialogResult> show,
        Func<FolderBrowserDialog, IWin32Window, DialogResult>? showFolder = null)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.show = show ?? throw new ArgumentNullException(nameof(show));
        this.showFolder = showFolder ?? ((dialog, window) => dialog.ShowDialog(window));
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

    public IReadOnlyList<string> OpenFiles(FilePickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var dialog = new OpenFileDialog { Filter = options.Filter, FileName = options.FileName, Multiselect = true };
        return show(dialog, owner) == DialogResult.OK ? dialog.FileNames.ToArray() : [];
    }

    public string? SelectFolder(string selectedPath = "")
    {
        ArgumentNullException.ThrowIfNull(selectedPath);
        using var dialog = new FolderBrowserDialog { SelectedPath = selectedPath, Description = "选择目录", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        return showFolder(dialog, owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
