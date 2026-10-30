namespace PortHarbor;

public sealed record TerminalPreferences
{
    public string FontFamily { get; init; } = "";
    public float FontSize { get; init; } = 11;
    public int FontStyle { get; init; }
    public int ForegroundRgb { get; init; } = 0xDCE3EE;
    public int BackgroundRgb { get; init; } = 0x111827;
    public bool CopyOnSelection { get; init; }
    public bool PasteOnRightClick { get; init; }
    public void Validate()
    {
        if (FontFamily == null || FontFamily.Length > 128 || FontFamily.Any(char.IsControl)
            || !float.IsFinite(FontSize) || FontSize is < 6 or > 48 || FontStyle is < 0 or > 15
            || ForegroundRgb is < 0 or > 0xFFFFFF || BackgroundRgb is < 0 or > 0xFFFFFF)
            throw new ArgumentException("终端偏好无效，请检查字体、字号和颜色。");
    }
}

public interface ITerminalPreferencesRepository
{
    TerminalPreferences Load();
    void Save(TerminalPreferences value);
}

public sealed class TerminalPreferencesService(ITerminalPreferencesRepository repository)
{
    public TerminalPreferences Current { get; private set; } = new();
    public bool LoadFailed { get; private set; }
    public void Load()
    {
        try { var value = repository.Load(); value.Validate(); Current = value; LoadFailed = false; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { Current = new(); LoadFailed = true; }
    }
    public void Save(TerminalPreferences value)
    {
        value.Validate(); repository.Save(value); Current = value; LoadFailed = false;
    }
}
