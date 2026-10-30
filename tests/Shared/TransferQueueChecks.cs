using PortHarbor;

namespace PortHarbor.Checks;

internal static class TransferQueueChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        static void Add(TransferQueue q, int count) => q.Add(Enumerable.Range(0, count).Select(i => (TransferDirection.Upload, "source-" + i, "/target-" + i)));
        var queue = new TransferQueue(); Add(queue, 3);
        var ids = queue.Jobs.Select(j => j.Id).ToArray();
        check(ids.Distinct().Count() == 3 && queue.Jobs.All(j => j.State == TransferJobState.Pending), "queue allocates distinct pending task identities");
        var started = new List<Guid>(); int concurrent = 0, maxConcurrent = 0;
        await queue.RunAsync(async (job, token) => { concurrent++; maxConcurrent = Math.Max(maxConcurrent, concurrent); started.Add(job.Id); await Task.Yield(); concurrent--; return "confirmed"; });
        check(maxConcurrent == 1 && started.SequenceEqual(ids) && queue.Jobs.All(j => j.State == TransferJobState.Succeeded), "queue executes in insertion order with exactly one active transfer");
        await queue.RunAsync((_, _) => throw new Exception("must not replay"));
        check(started.Count == 3 && queue.Jobs.All(j => j.State == TransferJobState.Succeeded), "completed jobs are never replayed when starting the queue again");
        queue.ClearFinished(); Add(queue, 3);
        await queue.RunAsync((_, _) => throw new IOException("private path and password"));
        check(queue.Jobs[0].State == TransferJobState.Failed && queue.Jobs.Skip(1).All(j => j.State == TransferJobState.Pending) && !queue.IsRunning
            && !queue.Jobs[0].Result.Contains("private"), "queue failure pauses pending work and exposes no raw exception data");
        bool blocked = false; try { queue.Retry(queue.Jobs[0].Id, false); } catch (InvalidOperationException) { blocked = true; }
        check(blocked && queue.Jobs[0].State == TransferJobState.Failed, "retry requires an explicit verified-results decision");
        queue.Retry(queue.Jobs[0].Id, true);
        check(queue.Jobs.All(j => j.State == TransferJobState.Pending), "explicit verified retry returns only the selected job to pending");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = queue.RunAsync(async (_, token) => { await release.Task.WaitAsync(token); return "confirmed"; });
        check(queue.IsRunning && queue.Jobs[0].State == TransferJobState.Running, "queue publishes a running item before awaiting its transfer");
        queue.CancelPending(queue.Jobs[1].Id);
        check(queue.Jobs[1].State == TransferJobState.Cancelled && queue.Jobs[2].State == TransferJobState.Pending && queue.Jobs[0].State == TransferJobState.Running, "cancelling one waiting job cannot cancel the current or other waiting jobs");
        queue.CancelAll(); await running.WaitAsync(TimeSpan.FromSeconds(5));
        check(queue.Jobs.All(j => j.State == TransferJobState.Cancelled) && !queue.IsRunning, "cancel-all cancels the active transfer and all pending work before completion");
        queue.ClearFinished(); Add(queue, 2);
        int calls = 0;
        await queue.RunAsync(async (_, token) => { if (++calls == 1) { queue.CancelCurrent(); await Task.Delay(Timeout.Infinite, token); } return "confirmed"; });
        check(queue.Jobs[0].State == TransferJobState.Cancelled && queue.Jobs[1].State == TransferJobState.Succeeded, "cancel-current does not cancel the next queued task");
        queue.ClearFinished(); Add(queue, 1);
        await queue.RunAsync((_, _) => { queue.CancelCurrent(); return Task.FromResult("commit confirmed before cancellation"); });
        check(queue.Jobs.Single().State == TransferJobState.Succeeded, "late cancellation cannot reclassify a confirmed commit as cancelled");
        queue.ClearFinished(); Add(queue, 2); using var stop = new CancellationTokenSource(); stop.Cancel();
        await queue.RunAsync((_, _) => throw new Exception("pre-cancelled execution"), stop.Token);
        check(queue.Jobs.All(j => j.State == TransferJobState.Cancelled), "pre-cancelled lifetime starts no work and cancels waiting jobs");
        queue.ClearFinished();
        bool invalid = false; try { queue.Add([(TransferDirection.Upload, "", "target")]); } catch (ArgumentException) { invalid = true; }
        check(invalid && queue.Jobs.Count == 0, "invalid queue batches are rejected atomically before insertion");
        Add(queue, TransferQueue.Capacity); bool overflow = false; try { Add(queue, 1); } catch (ArgumentException) { overflow = true; }
        check(overflow && queue.Jobs.Count == TransferQueue.Capacity, "queue has a bounded task count without dropping existing work");
    }
}
