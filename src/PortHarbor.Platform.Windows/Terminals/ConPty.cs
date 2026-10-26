using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PortHarbor;

public sealed class ConPty : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Coord { public short X, Y; public Coord(int x, int y) { X = (short)x; Y = (short)y; } }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo { public int cb; public string? reserved, desktop, title; public int x, y, xSize, ySize, xCount, yCount, fill, flags; public short show, reserved2; public IntPtr reservedPtr, input, output, error; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr attrs, int size);
    [DllImport("kernel32.dll")] static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] static extern int ResizePseudoConsole(IntPtr console, Coord size);
    [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] static extern bool CreateProcess(string? application, StringBuilder command, IntPtr processAttrs, IntPtr threadAttrs, bool inherit, uint flags, IntPtr environment, string? directory, [In] ref StartupInfoEx startup, out ProcessInfo info);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint exitCode);
    public FileStream Input { get; private set; } = null!;
    public FileStream Output { get; private set; } = null!;
    IntPtr console, process;
    readonly object consoleGate = new();
    int disposed;
    public ConPty(Session session, int columns, int rows)
    {
        IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero, attributes = IntPtr.Zero;
        bool initialized = false;
        try
        {
            if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(new(columns, rows), inputRead, outputWrite, 0, out console));
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
            initialized = true;
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x00020016, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            // Explicit empty standard handles prevent redirected parent handles from
            // overriding the pseudo console (Windows Terminal issue #15814).
            var startup = new StartupInfoEx { Info = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>(), flags = 0x00000100 }, Attributes = attributes };
            string executable = session.Executable.Trim().Trim('"');
            if (executable.Contains('"')) throw new ArgumentException("Shell 程序路径不能包含引号。");
            var command = new StringBuilder($"\"{executable}\" {session.Arguments}");
            if (!CreateProcess(null, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000 | 0x00000400, IntPtr.Zero, string.IsNullOrWhiteSpace(session.WorkingDirectory) ? null : session.WorkingDirectory, ref startup, out var info)) throw new Win32Exception();
            process = info.Process; CloseHandle(info.Thread);
            CloseHandle(inputRead); inputRead = IntPtr.Zero; CloseHandle(outputWrite); outputWrite = IntPtr.Zero;
            Input = new FileStream(new SafeFileHandle(inputWrite, true), FileAccess.Write, 4096, false); inputWrite = IntPtr.Zero;
            Output = new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read, 4096, false); outputRead = IntPtr.Zero;
            var child = System.Diagnostics.Process.GetProcessById((int)info.ProcessId);
            _ = Task.Run(async () => { using (child) { await child.WaitForExitAsync(); CloseConsole(); } });
        }
        catch { Dispose(); throw; }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            foreach (var h in new[] { inputRead, inputWrite, outputRead, outputWrite }) if (h != IntPtr.Zero) CloseHandle(h);
        }
    }
    public void Resize(int columns, int rows) { lock (consoleGate) { if (disposed == 0 && console != IntPtr.Zero) ResizePseudoConsole(console, new(columns, rows)); } }
    void CloseConsole() { lock (consoleGate) { if (console != IntPtr.Zero) { ClosePseudoConsole(console); console = IntPtr.Zero; } } }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (process != IntPtr.Zero) { TerminateProcess(process, 0); CloseHandle(process); process = IntPtr.Zero; }
        Input?.Dispose();
        CloseConsole();
        Output?.Dispose();
    }
}
