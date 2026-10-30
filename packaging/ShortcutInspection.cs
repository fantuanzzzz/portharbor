using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PortHarbor.BuildTools
{
    public sealed class ShortcutDetails
    {
        public string TargetPath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
    }

    public static class ShortcutInspection
    {
        // Only used by regression fixtures; do not rely on the legacy WScript shortcut setter for Unicode paths.
        public static void CreateFixture(string path, string target, string arguments, string directory)
        {
            var shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046"), true));
            try
            {
                var link = (IShellLinkW)shellLink;
                link.SetPath(Path.GetFullPath(target));
                link.SetArguments(arguments);
                link.SetWorkingDirectory(Path.GetFullPath(directory));
                ((IPersistFile)shellLink).Save(Path.GetFullPath(path), true);
            }
            finally { if (shellLink != null) Marshal.FinalReleaseComObject(shellLink); }
        }

        // Read via the Unicode Shell interface. Do not resolve or launch the shortcut.
        public static ShortcutDetails Read(string path)
        {
            var shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046"), true));
            try
            {
                ((IPersistFile)shellLink).Load(Path.GetFullPath(path), 0);
                var link = (IShellLinkW)shellLink;
                var target = new StringBuilder(32768);
                var arguments = new StringBuilder(32768);
                var directory = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH
                link.GetArguments(arguments, arguments.Capacity);
                link.GetWorkingDirectory(directory, directory.Capacity);
                return new ShortcutDetails
                {
                    TargetPath = target.ToString(), Arguments = arguments.ToString(),
                    WorkingDirectory = directory.ToString()
                };
            }
            finally { if (shellLink != null) Marshal.FinalReleaseComObject(shellLink); }
        }

        public static bool TargetsFile(string target, string expected)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(expected)) return false;
            // File identity handles long/short paths and directory aliases; identical bytes alone would not.
            using (var actualFile = Open(target))
            using (var expectedFile = Open(expected))
            {
                FileInformation actual, wanted;
                if (!GetFileInformationByHandle(actualFile.SafeFileHandle, out actual) ||
                    !GetFileInformationByHandle(expectedFile.SafeFileHandle, out wanted))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return actual.VolumeSerialNumber == wanted.VolumeSerialNumber &&
                    actual.FileIndexHigh == wanted.FileIndexHigh && actual.FileIndexLow == wanted.FileIndexLow;
            }
        }

        static FileStream Open(string path) => new FileStream(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        [StructLayout(LayoutKind.Sequential)]
        struct FileInformation
        {
            public uint Attributes;
            public FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int command);
            void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
    }
}
