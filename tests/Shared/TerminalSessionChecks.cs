using System.Text;
using PortHarbor;

namespace PortHarbor.Checks;

internal static class TerminalSessionChecks
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    static Session Options => new() { Protocol = Protocol.SSH, Host = "fixture.invalid" };
    internal static async Task RunAsync(Action<bool, string> check)
    {
        check(typeof(TerminalConnection).Assembly == typeof(SessionService).Assembly
            && typeof(TerminalConnection).Assembly.GetReferencedAssemblies().All(a => a.Name is not ("System.Windows.Forms" or "Renci.SshNet" or "PortHarbor.Infrastructure")),
            "terminal lifecycle lives in Application without UI, platform or vendor dependencies");

        var ordered = new ScriptedTerminalTransport();
        var orderedOptions = Options;
        await using (var terminal = new TerminalConnection(orderedOptions, ordered))
        {
            var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>();
            terminal.Diagnostic += diagnostics.Enqueue;
            byte[] input = [65]; terminal.Send(input); input[0] = 90;
            var text = new StringBuilder();
            var decoded = ScriptedTerminalTransport.NewSignal();
            terminal.Output += value => { text.Append(value); if (text.ToString().Contains("中文")) decoded.TrySetResult(); };
            Task run = terminal.Run(80, 24);
            await ordered.ReadStarted.Task.WaitAsync(Deadline);
            terminal.Resize(100, 30); terminal.Send([66]);
            byte[] payload = Encoding.UTF8.GetBytes("中文");
            ordered.Incoming.Writer.TryWrite(payload[..1]); ordered.Incoming.Writer.TryWrite(payload[1..]);
            await decoded.Task.WaitAsync(Deadline);
            // A barrier in the writer ensures all previous inputs have run before assertions.
            terminal.Send([67]);
            await WaitForAsync(() => ordered.Written.Count == 3);
            check(ordered.Written.SelectMany(b => b).SequenceEqual(new byte[] { 65, 66, 67 }), "terminal input is copied on enqueue and written in order");
            check(ordered.Actions.ToArray().SequenceEqual(new[] { "write:41", "resize:100x30", "write:42", "write:43" }), "terminal resize is serialized with input writes");
            check(text.ToString().Contains("中文") && !text.ToString().Contains('\ufffd'), "terminal UTF-8 decoding preserves characters split across reads");
            bool rejected = false; try { _ = terminal.Run(80, 24); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && ordered.ConnectCalls == 1, "a terminal session rejects overlapping or repeated runs");
            ordered.Incoming.Writer.TryComplete();
            await run.WaitAsync(Deadline);
            await terminal.CloseAsync(); await terminal.CloseAsync();
            check(diagnostics.Last() is { BytesSent: 3, BytesReceived: 6, CharactersDecoded: 2, DurationMs: >= 0 }
                && diagnostics.Last().SessionId == orderedOptions.Id,
                "terminal diagnostics report actual sent/received byte and decoded character counts without output contents");
            terminal.Send([68]);
            check(terminal.State == ConnectionState.Closed && ordered.DisposeCalls == 1 && ordered.Written.Count == 3,
                "peer EOF completes cleanup once and closed terminals reject further input");
            check(!diagnostics.Any(e => e.State == ConnectionState.Failed) && diagnostics.All(e => e.OperationId == terminal.OperationId && e.Protocol == Protocol.SSH)
                && diagnostics.Last().State == ConnectionState.Closed, "normal terminal disconnect preserves diagnostic identity without reporting failure");
        }

        var cancelled = new ScriptedTerminalTransport();
        var timedConnect = new ScriptedTerminalTransport { BlockConnect = true };
        await using (var terminal = new TerminalConnection(Options, timedConnect, connectTimeout: TimeSpan.FromMilliseconds(150)))
        {
            var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>(); terminal.Diagnostic += diagnostics.Enqueue;
            bool timedOut = false;
            try { await terminal.Run(80, 24).WaitAsync(Deadline); } catch (TimeoutException) { timedOut = true; }
            check(timedOut && terminal.State == ConnectionState.Closed && timedConnect.DisposeCalls == 1
                && diagnostics.Any(d => d.ErrorCode == ConnectionErrorCode.Timeout && d.Stage == ConnectionStage.Authenticating),
                "terminal startup deadline reports timeout at the active stage and awaits transport cleanup");
        }
        var timedWrite = new ScriptedTerminalTransport { BlockWrite = true };
        await using (var terminal = new TerminalConnection(Options, timedWrite, writeTimeout: TimeSpan.FromMilliseconds(150)))
        {
            var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>(); terminal.Diagnostic += diagnostics.Enqueue;
            Task run = terminal.Run(80, 24);
            await timedWrite.ReadStarted.Task.WaitAsync(Deadline); terminal.Send([65]);
            await timedWrite.WriteStarted.Task.WaitAsync(Deadline);
            await timedWrite.Aborted.Task.WaitAsync(Deadline);
            bool waited = !run.IsCompleted && timedWrite.DisposeCalls == 0;
            timedWrite.WriteRelease.TrySetResult();
            bool timedOut = false;
            try { await run.WaitAsync(Deadline); } catch (TimeoutException) { timedOut = true; }
            check(waited && timedOut && !timedWrite.DisposedDuringIo && terminal.State == ConnectionState.Closed
                && diagnostics.Any(d => d.ErrorCode == ConnectionErrorCode.Timeout && d.Stage == ConnectionStage.Writing),
                "terminal write deadline aborts blocking I/O and waits for the worker before releasing resources");
        }
        await using (var terminal = new TerminalConnection(Options, cancelled))
        {
            using var token = new CancellationTokenSource(); token.Cancel();
            await terminal.Run(80, 24, token.Token).WaitAsync(Deadline);
            check(cancelled.ConnectCalls == 0 && cancelled.DisposeCalls == 1 && terminal.State == ConnectionState.Closed,
                "pre-cancelled terminal startup never opens its adapter and still releases it");
        }

        var unopened = new ScriptedTerminalTransport();
        await using (var terminal = new TerminalConnection(Options, unopened))
        {
            await terminal.CloseAsync();
            bool rejected = false; try { _ = terminal.Run(80, 24); } catch (ObjectDisposedException) { rejected = true; }
            check(rejected && unopened.ConnectCalls == 0 && unopened.DisposeCalls == 1, "closing an unstarted terminal prevents later startup");
        }

        var late = new ScriptedTerminalTransport { BlockConnect = true, IgnoreConnectCancellation = true };
        await using (var terminal = new TerminalConnection(Options, late))
        {
            var events = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>(); terminal.Diagnostic += events.Enqueue;
            Task run = terminal.Run(80, 24);
            await late.ConnectStarted.Task.WaitAsync(Deadline);
            Task close = terminal.CloseAsync();
            await late.ConnectCancelled.Task.WaitAsync(Deadline);
            check(!close.IsCompleted && late.DisposeCalls == 0, "terminal close waits for a startup that has not yet returned");
            late.ConnectRelease.TrySetResult();
            await Task.WhenAll(run, close).WaitAsync(Deadline);
            check(late.DisposeCalls == 1 && !events.Any(e => e.State is ConnectionState.Connected or ConnectionState.Failed),
                "late startup after cancellation never publishes connected and is cleaned up once");
        }

        var blocked = new ScriptedTerminalTransport { BlockWrite = true, BlockDispose = true };
        await using (var terminal = new TerminalConnection(Options, blocked))
        {
            Task run = terminal.Run(80, 24);
            await blocked.ReadStarted.Task.WaitAsync(Deadline); terminal.Send([65]);
            await blocked.WriteStarted.Task.WaitAsync(Deadline);
            Task close = terminal.CloseAsync();
            await blocked.Aborted.Task.WaitAsync(Deadline);
            check(!close.IsCompleted && blocked.DisposeCalls == 0, "terminal close waits for an in-flight write after interrupting the transport");
            blocked.WriteRelease.TrySetResult();
            await blocked.DisposeStarted.Task.WaitAsync(Deadline);
            check(!close.IsCompleted && !blocked.DisposedDuringIo, "terminal cleanup begins after I/O exits and close waits for asynchronous disposal");
            blocked.DisposeRelease.TrySetResult();
            await Task.WhenAll(run, close).WaitAsync(Deadline);
            check(terminal.State == ConnectionState.Closed && blocked.DisposeCalls == 1 && blocked.Written.IsEmpty,
                "terminal cancellation does not flush queued input after shutdown");
        }

        foreach (var stage in new[] { ConnectionStage.Authenticating, ConnectionStage.Reading, ConnectionStage.Writing })
        {
            var error = new IOException("private-host/password/path");
            var backend = new ScriptedTerminalTransport();
            if (stage == ConnectionStage.Authenticating) backend.ConnectError = error;
            else if (stage == ConnectionStage.Reading) backend.ReadError = error;
            else backend.WriteError = error;
            await using var terminal = new TerminalConnection(Options, backend, (_, _) => ConnectionErrorCode.PermissionDenied);
            var events = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>(); terminal.Diagnostic += events.Enqueue;
            if (stage == ConnectionStage.Writing) terminal.Send([65]);
            Exception? actual = null;
            try { await terminal.Run(80, 24).WaitAsync(Deadline); } catch (IOException observed) { actual = observed; }
            check(ReferenceEquals(error, actual) && backend.DisposeCalls == 1 && terminal.State == ConnectionState.Closed,
                stage + " failure propagates its original error after terminal cleanup");
            check(events.Any(e => e.State == ConnectionState.Failed && e.Stage == stage && e.ErrorCode == ConnectionErrorCode.PermissionDenied)
                && events.All(e => !e.Message.Contains("private") && !e.Message.Contains("password")),
                stage + " failure uses the injected error classifier and safe diagnostic messages");
        }

        var invalid = new ScriptedTerminalTransport();
        await using (var terminal = new TerminalConnection(Options with { Host = "" }, invalid))
        {
            bool rejected = false;
            try { await terminal.Run(80, 24).WaitAsync(Deadline); } catch (ArgumentException) { rejected = true; }
            check(rejected && invalid.ConnectCalls == 0 && invalid.DisposeCalls == 1, "terminal validation failure releases the adapter without protocol startup");
        }
    }

    static async Task WaitForAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        while (!ready()) await Task.Delay(10, timeout.Token);
    }
}
