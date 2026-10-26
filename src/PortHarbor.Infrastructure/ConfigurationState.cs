namespace PortHarbor;

// All adapters share one state and one lock because their data lives in one file.
internal sealed class ConfigurationState
{
    internal object Gate { get; } = new();
    internal ConfigurationFileStorage Storage { get; }
    internal AppData Data { get; }
    internal bool IsPortable => Storage.IsPortable;

    internal ConfigurationState(ConfigurationFileStorage storage)
    {
        Storage = storage;
        Data = storage.Load();
    }

    internal void Save()
    {
        lock (Gate) Storage.Save(Data);
    }
}
