namespace PortHarbor;

/// <summary>One server-provided keyboard-interactive prompt. Response text is never persisted.</summary>
public sealed record SshAuthenticationPrompt(string Request, bool Echo);

/// <summary>A single keyboard-interactive round, including its non-secret instruction text.</summary>
public sealed record SshAuthenticationChallenge(string Instruction, IReadOnlyList<SshAuthenticationPrompt> Prompts);

/// <summary>Raised when the user cancels an interactive authentication round.</summary>
public sealed class SshAuthenticationCancelledException : OperationCanceledException
{
    public SshAuthenticationCancelledException() : base("SSH interactive authentication was cancelled.") { }
}

/// <summary>Synchronously answers one SSH challenge on the owning UI thread, or returns null to cancel.</summary>
public delegate IReadOnlyList<string>? SshAuthenticationPromptHandler(SshAuthenticationChallenge challenge);
