namespace PortHarbor;

public sealed class HostKeyRejectedException(string message) : InvalidOperationException(message);
