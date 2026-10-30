namespace PortHarbor;

public enum TransferJobState { Pending, Running, Succeeded, Failed, Cancelled }
public sealed record TransferJob(Guid Id, TransferDirection Direction, string Source, string Destination, TransferJobState State, string Result = "");

/// <summary>Single owner/context queue. Executor owns protocol I/O and safe presentation. No automatic retry.</summary>
public sealed class TransferQueue
{
    public const int Capacity = 500;
    readonly List<TransferJob> jobs = [];
    CancellationTokenSource? current;
    bool stopAfterCurrent;
    public bool IsRunning { get; private set; }
    public IReadOnlyList<TransferJob> Jobs => jobs.ToArray();
    public event Action? Changed;
    public void Add(IEnumerable<(TransferDirection Direction, string Source, string Destination)> batch)
    {
        if (IsRunning) throw new InvalidOperationException("队列正在执行。");
        var incoming = batch.ToArray();
        if (jobs.Count + incoming.Length > Capacity) throw new ArgumentException("队列最多保留 500 项，请先清理已结束任务。");
        if (incoming.Any(j => !Enum.IsDefined(j.Direction) || string.IsNullOrWhiteSpace(j.Source) || string.IsNullOrWhiteSpace(j.Destination)))
            throw new ArgumentException("任务路径或方向无效。");
        jobs.AddRange(incoming.Select(j => new TransferJob(Guid.NewGuid(), j.Direction, j.Source, j.Destination, TransferJobState.Pending)));
        Changed?.Invoke();
    }
    public void CancelPending(Guid? id = null)
    {
        for (int i = 0; i < jobs.Count; i++)
            if (jobs[i].State == TransferJobState.Pending && (id == null || jobs[i].Id == id)) jobs[i] = jobs[i] with { State = TransferJobState.Cancelled, Result = "尚未开始，已取消" };
        Changed?.Invoke();
    }
    public void CancelCurrent() => current?.Cancel();
    public void CancelAll() { stopAfterCurrent = true; CancelPending(); current?.Cancel(); }
    public void Retry(Guid id, bool resultsVerified)
    {
        if (IsRunning) throw new InvalidOperationException("请等待队列停止。");
        if (!resultsVerified) throw new InvalidOperationException("请先核对原目标、暂存及备份结果。");
        int i = jobs.FindIndex(j => j.Id == id);
        if (i < 0 || jobs[i].State is not (TransferJobState.Failed or TransferJobState.Cancelled)) return;
        jobs[i] = jobs[i] with { State = TransferJobState.Pending, Result = "已核对，等待重新开始" }; Changed?.Invoke();
    }
    public void ClearFinished()
    {
        if (IsRunning) return;
        jobs.RemoveAll(j => j.State is TransferJobState.Succeeded or TransferJobState.Failed or TransferJobState.Cancelled); Changed?.Invoke();
    }
    public async Task RunAsync(Func<TransferJob, CancellationToken, Task<string>> execute, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (IsRunning) return;
        IsRunning = true; stopAfterCurrent = false; Changed?.Invoke();
        using var stopRegistration = token.Register(CancelAll);
        try
        {
            while (!stopAfterCurrent && !token.IsCancellationRequested)
            {
                int i = jobs.FindIndex(j => j.State == TransferJobState.Pending); if (i < 0) break;
                using var itemStop = CancellationTokenSource.CreateLinkedTokenSource(token); current = itemStop;
                jobs[i] = jobs[i] with { State = TransferJobState.Running, Result = "" }; Changed?.Invoke();
                try
                {
                    itemStop.Token.ThrowIfCancellationRequested();
                    string result = await execute(jobs[i], itemStop.Token);
                    // The executor returns only after protocol/target confirmation. A late cancel cannot undo a confirmed commit.
                    jobs[i] = jobs[i] with { State = TransferJobState.Succeeded, Result = result };
                }
                catch (OperationCanceledException) when (itemStop.IsCancellationRequested)
                { jobs[i] = jobs[i] with { State = TransferJobState.Cancelled, Result = "已取消；请核对目标及暂存/备份" }; }
                catch (Exception)
                { jobs[i] = jobs[i] with { State = TransferJobState.Failed, Result = "失败；已暂停等待任务，请核对结果后显式继续" }; stopAfterCurrent = true; }
                finally { current = null; Changed?.Invoke(); }
            }
        }
        finally { current = null; IsRunning = false; Changed?.Invoke(); }
    }
}
