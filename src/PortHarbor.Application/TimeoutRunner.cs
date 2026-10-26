namespace PortHarbor;

internal static class TimeoutRunner
{
    public static async Task<T> RunAsync<T>(TimeSpan timeout, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> action)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try { return await action(linkedSource.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("The file operation exceeded its configured timeout.");
        }
    }

    public static async Task RunAsync(TimeSpan timeout, CancellationToken cancellationToken, Func<CancellationToken, Task> action)
    {
        await RunAsync(timeout, cancellationToken, async token => { await action(token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
    }
}
