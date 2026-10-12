#nullable disable
// C4b U7: only the packaging smoke process and its descendants belong to this local build-tool job.
// Create suspended -> assign to kill-on-close job -> resume. No process outside this job is enumerated or terminated.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Nth.Build
{
    public sealed class SmokeProcess : IDisposable
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateNoWindow = 0x08000000;
        private const uint KillOnJobClose = 0x00002000;
        private readonly SafeFileHandle job;
        private bool disposed;
        public Process Process { get; }

        private SmokeProcess(Process process, SafeFileHandle jobHandle)
        {
            Process = process;
            job = jobHandle;
        }

        public static SmokeProcess Start(string filePath, string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A smoke executable path is required.", nameof(filePath));
            var path = Path.GetFullPath(filePath);
            var command = new StringBuilder(BuildCommandLine(path, arguments ?? Array.Empty<string>()));
            var jobHandle = CreateJobObject(IntPtr.Zero, null);
            var child = new ProcessInformation();
            Process process = null;
            var created = false;
            var transferred = false;
            try
            {
                if (jobHandle.IsInvalid) throw LastError("Could not create the smoke process job");
                var limits = new ExtendedLimits();
                limits.Basic.LimitFlags = KillOnJobClose;
                if (!SetInformationJobObject(jobHandle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
                    throw LastError("Could not set kill-on-close for the smoke process job");
                var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
                if (!CreateProcess(path, command, IntPtr.Zero, IntPtr.Zero, false, CreateSuspended | CreateNoWindow,
                    IntPtr.Zero, null, ref startup, out child)) throw LastError("Could not start the smoke process");
                created = true;
                if (!AssignProcessToJobObject(jobHandle, child.Process)) throw LastError("Could not contain the smoke process");
                process = System.Diagnostics.Process.GetProcessById((int)child.ProcessId);
                // Open the managed process handle while the owned child is suspended, before a fast exit can race it.
                var handle = process.SafeHandle;
                if (ResumeThread(child.Thread) == uint.MaxValue) throw LastError("Could not resume the smoke process");
                var owned = new SmokeProcess(process, jobHandle);
                transferred = true;
                return owned;
            }
            finally
            {
                try
                {
                    if (!transferred)
                    {
                        try
                        {
                            // Assignment or resume failure must not leave a suspended child. Terminate uses our creation handle.
                            if (created && child.Process != IntPtr.Zero)
                            {
                                if (!TerminateProcess(child.Process, 1)) throw LastError("Could not stop the failed smoke start");
                                if (WaitForSingleObject(child.Process, 5000) != 0) throw new TimeoutException("The failed smoke start did not stop within 5000 ms.");
                            }
                        }
                        finally { jobHandle.Dispose(); process?.Dispose(); }
                    }
                }
                finally
                {
                    if (created && child.Thread != IntPtr.Zero) CloseHandle(child.Thread);
                    if (created && child.Process != IntPtr.Zero) CloseHandle(child.Process);
                }
            }
        }

        public bool WaitForExit(int milliseconds) => Process.WaitForExit(milliseconds);
        public int ExitCode => Process.ExitCode;

        public void Stop(int milliseconds)
        {
            if (milliseconds < 1) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            if (disposed) return;
            // Terminate the JOB even when the original process has exited: its living descendants remain members.
            if (!TerminateJobObject(job, 1)) throw LastError("Could not stop the smoke process job");
            var clock = Stopwatch.StartNew();
            while (true)
            {
                if (!QueryInformationJobObject(job, 1, out Accounting info, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                    throw LastError("Could not check the smoke process job cleanup");
                if (info.ActiveProcesses == 0) return;
                if (clock.ElapsedMilliseconds >= milliseconds) throw new TimeoutException("The smoke process tree did not stop within " + milliseconds + " ms.");
                Thread.Sleep((int)Math.Max(1, Math.Min(10, milliseconds - clock.ElapsedMilliseconds)));
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Kill-on-close is the final backstop even if bounded Stop or an earlier start/wait check failed.
            try { job.Dispose(); } finally { Process.Dispose(); }
        }

        // Windows argv quoting: every argument is quoted, quotes escaped, and backslashes doubled before a quote or closing quote.
        public static string BuildCommandLine(string filePath, string[] arguments)
        {
            var result = new StringBuilder(Quote(filePath));
            foreach (var argument in arguments) result.Append(' ').Append(Quote(argument ?? ""));
            if (result.Length >= 32767) throw new ArgumentException("The smoke command line is too long.", nameof(arguments));
            return result.ToString();
        }

        private static string Quote(string value)
        {
            if (value.IndexOf('\0') >= 0) throw new ArgumentException("The smoke command line contains a null character.");
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static Win32Exception LastError(string message) => new Win32Exception(Marshal.GetLastWin32Error(), message);

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public uint Size;
            public IntPtr Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public ushort ShowWindow, ReservedSize;
            public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Accounting
        {
            public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint size);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation child);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting info, uint size, IntPtr returnedSize);
    }
}
