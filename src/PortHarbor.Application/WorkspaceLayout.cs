using System.Drawing;

namespace PortHarbor;

public enum InterfaceTheme { Light, Dark }

public sealed record WorkspaceLayout
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; } = 1250;
    public int Height { get; init; } = 820;
    public int Dpi { get; init; } = 96;
    public bool HasPosition { get; init; }
    public bool Maximized { get; init; }
    public int SidebarWidth { get; init; } = 250;
    public bool SidebarVisible { get; init; } = true;
    public InterfaceTheme Theme { get; init; }
    public void Validate()
    {
        if (X is < -1000000 or > 1000000 || Y is < -1000000 or > 1000000
            || Width is < 200 or > 20000 || Height is < 200 or > 20000 || Dpi is < 48 or > 768
            || SidebarWidth is < 120 or > 4000 || !Enum.IsDefined(Theme))
            throw new ArgumentException("工作台布局参数无效。");
    }
}

public interface IWorkspaceLayoutRepository
{
    WorkspaceLayout Load();
    void Save(WorkspaceLayout value);
}

public sealed class WorkspaceLayoutService(IWorkspaceLayoutRepository repository)
{
    public WorkspaceLayout Current { get; private set; } = new();
    public bool LoadFailed { get; private set; }
    public void Load()
    {
        try { var value = repository.Load(); value.Validate(); Current = value; LoadFailed = false; }
        catch (Exception e) when (IsStorageFailure(e)) { Current = new(); LoadFailed = true; }
    }
    public void Save(WorkspaceLayout value)
    {
        value.Validate(); repository.Save(value); Current = value; LoadFailed = false;
    }
    public static bool IsStorageFailure(Exception e) => e is IOException or InvalidDataException
        or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException;
}

public static class WorkspacePlacement
{
    public static Rectangle Restore(WorkspaceLayout layout, IReadOnlyList<Rectangle> workAreas, int currentDpi, Size minimum)
    {
        layout.Validate();
        if (workAreas.Count == 0 || workAreas.Any(r => r.Width <= 0 || r.Height <= 0) || currentDpi is < 48 or > 768)
            throw new ArgumentException("可用屏幕或 DPI 无效。");
        int width = (int)Math.Round(layout.Width * (double)currentDpi / layout.Dpi);
        int height = (int)Math.Round(layout.Height * (double)currentDpi / layout.Dpi);
        var saved = new Rectangle(layout.X, layout.Y, width, height);
        var area = layout.HasPosition
            ? workAreas.OrderByDescending(r => (long)Rectangle.Intersect(r, saved).Width * Rectangle.Intersect(r, saved).Height).First()
            : workAreas[0];
        width = Math.Clamp(width, Math.Min(minimum.Width, area.Width), area.Width);
        height = Math.Clamp(height, Math.Min(minimum.Height, area.Height), area.Height);
        // A removed monitor must not leave even the title bar outside the remaining desktop.
        int x = layout.HasPosition ? Math.Clamp(layout.X, area.Left, area.Right - width) : area.Left + (area.Width - width) / 2;
        int y = layout.HasPosition ? Math.Clamp(layout.Y, area.Top, area.Bottom - height) : area.Top + (area.Height - height) / 2;
        return new(x, y, width, height);
    }
}
