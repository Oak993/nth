// PORT-ONLY: RR-14 (ED-NEW-dl-8), ONDEMAND-PLAN.md 3.4. The production process seam of the installers: exactly the executables the catalogue's installers run, nothing else.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>
/// ENGINEERING DECISION (ED-NEW-dl-8): the allow-list is derived, per command, from the embedded catalogue and the trusted install records, never from a path a caller supplies:
/// (1) Windows Installer, only the system copy <c>%SystemRoot%\System32\msiexec.exe</c>, for an <c>msiAdmin</c> item (an administrative image needs no elevation and writes no registry or
/// Start menu entry); (2) a <c>sfx7z</c> item's self-extractor, only at the path its plan downloads it to and only while its bytes match the catalogue's size and SHA-256; (3) the embedded
/// Python of a <c>pythonEmbed</c> item, only at the selected pin's interpreter path and only while it matches the inventory its verified install recorded (ED-NEW-dl-9), for <c>pyset</c> installs.
/// The child runs in a working folder under <c>addons</c>, with the user's pip and Python variables removed and TEMP, TMP and HOME pointing into a staging folder of its own under
/// <c>addons\downloads</c>, inside a Job Object (kill on close) it joins through its attribute list at creation (R-PROC-1, ED-047): the install waits until every process the child started has ended (Git's self-extractor starts its post-install step and
/// returns), and a cancel ends the whole tree. No elevation is requested, no PATH or system setting is changed, and no shell or PATH search is used.
/// </summary>
public sealed class CatalogueInstallProcess(string dataRoot, SkillsCatalogue catalogue) : IInstallProcess
{
    private readonly string root = Path.GetFullPath(dataRoot);

    /// <summary>The one Windows Installer an administrative image is made with.</summary>
    public static string Msiexec => Path.Combine(Environment.SystemDirectory, "msiexec.exe");

    private static readonly string[] RemovedPrefixes = ["PIP_", "PYTHON", "VIRTUAL_ENV", "CONDA_", "UV_", "NODE_", "NPM_CONFIG_", "MSYS", "MINGW", "CHERE_INVOKING"];

    /// <summary>Whether the command's executable is one the catalogue's installers run, rechecked now (ED-NEW-dl-8 (1) to (3)); throws when it is not.</summary>
    public void Allow(InstallCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Path.IsPathFullyQualified(command.Executable) || !Path.IsPathFullyQualified(command.WorkingDirectory)) throw new InvalidDataException("Absolute process paths are required.");
        var executable = Path.GetFullPath(command.Executable);
        var addons = Path.Combine(root, "addons");
        if (!StorePaths.Under(Path.GetFullPath(command.WorkingDirectory), addons)) throw new InvalidDataException("An installer runs only inside the add-ons folder.");
        InstallPaths.NoReparseAncestors(command.WorkingDirectory);
        foreach (var item in catalogue.Items.Where(i => i.Install is not null))
        {
            switch (item.Install!.Kind)
            {
                case InstallKind.MsiAdmin when StorePaths.Comparer.Equals(executable, Msiexec):
                    if (!File.Exists(Msiexec)) throw new FileNotFoundException("Windows Installer is missing.", Msiexec);
                    InstallPaths.NoReparseAncestors(Msiexec);
                    return;
                case InstallKind.Sfx7z:
                    var artifact = item.Artifacts.Single();
                    var downloaded = Path.Combine(addons, "downloads", "items", item.Id, "artifacts", artifact.Name);
                    if (!StorePaths.Comparer.Equals(executable, Path.GetFullPath(downloaded))) continue;
                    InstallHash.Verify(executable, artifact.Size ?? throw new InvalidDataException("Unpinned artifact."), artifact.Sha256 ?? throw new InvalidDataException("Unpinned artifact."));
                    return;
                case InstallKind.PythonEmbed:
                    var stateFile = Path.Combine(addons, "state.json");
                    var route = PipelineRoutes.Read(stateFile).SingleOrDefault(r => r.Id == item.Id);
                    if (route is null) continue;
                    var shim = item.Install.Shims.FirstOrDefault(s => s.Python);
                    if (shim is null || !route.Shims.TryGetValue(shim.Name, out var definition) || !StorePaths.Comparer.Equals(executable, Path.GetFullPath(definition.Target))) continue;
                    var kept = PipelineManifests.ReadInstalled(stateFile, item.Id, route.Pin) ?? throw new InvalidDataException("The installed Python has no trusted inventory.");
                    var relative = Path.GetRelativePath(route.Folder, executable).Replace(Path.DirectorySeparatorChar, '/');
                    var pinned = kept.Manifest.Files.SingleOrDefault(f => f.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The interpreter is not in its trusted inventory.");
                    InstallHash.Verify(executable, pinned.Size, pinned.Sha256);
                    return;
            }
        }
        throw new InvalidOperationException("Installer executable is not allowed.");
    }

    public async Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installer executable execution is WINDOWS-ONLY.");
        cancellationToken.ThrowIfCancellationRequested();
        Allow(command);
        var staging = Path.Combine(root, "addons", "downloads", "tmp-" + Guid.NewGuid().ToString("N"));
        InstallPaths.NoReparseAncestors(staging);
        Directory.CreateDirectory(Path.Combine(staging, "home"));
        try
        {
            var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Select(e => (Key: (string)e.Key, Value: (string?)e.Value ?? ""))
                .Where(e => !RemovedPrefixes.Any(p => e.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
            environment["TEMP"] = environment["TMP"] = staging;
            environment["HOME"] = Path.Combine(staging, "home");
            foreach (var (key, value) in command.Environment) environment[key] = value;
            var line = new StringBuilder(WindowsInstallProcess.Quote(command.Executable));
            foreach (var argument in command.Arguments)
                line.Append(' ').Append(command.QuotePropertyValues ? WindowsInstallProcess.QuoteForWindowsInstaller(argument) : WindowsInstallProcess.Quote(argument));
            return await JobProcess.RunAsync(command.Executable, line.ToString(), command.WorkingDirectory, environment, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* collected with addons\downloads by the next reset */ }
        }
    }

    /// <summary>
    /// CreateProcessW with the kill-on-close job in the child's attribute list (PROC_THREAD_ATTRIBUTE_JOB_LIST, R-PROC-1 and ED-047, as ProcessLauncher does), so the child is in the
    /// job from its first instruction and is never added to it afterwards; the run ends when the job holds no process.
    /// </summary>
    private static class JobProcess
    {
        public static async Task<int> RunAsync(string executable, string commandLine, string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellation)
        {
            var job = CreateJobObjectW(0, 0);
            if (job == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var block = Marshal.StringToHGlobalUni(string.Concat(environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Key + "=" + e.Value + "\0")) + "\0");
            var info = default(ProcessInformation);
            nint attributes = 0, jobArray = 0;
            var attributesReady = false;
            try
            {
                var limits = new Shims.ShimJobExtendedLimits();
                limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Shims.ShimJobExtendedLimits>())) throw new Win32Exception(Marshal.GetLastWin32Error());
                nint size = 0;
                InitializeProcThreadAttributeList(0, 1, 0, ref size); // the sizing call fails by design
                attributes = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                attributesReady = true;
                jobArray = Marshal.AllocHGlobal(nint.Size);
                Marshal.WriteIntPtr(jobArray, job);
                if (!UpdateProcThreadAttribute(attributes, 0, JobList, jobArray, nint.Size, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var startup = new StartupInfoEx { Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, AttributeList = attributes };
                const uint flags = 0x00080000 | 0x400 | 0x08000000; // EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW
                if (!CreateProcessW(executable, new StringBuilder(commandLine), 0, 0, false, flags, block, workingDirectory, ref startup, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    while (WaitForSingleObject(info.Process, 0) != 0) await Task.Delay(100, cancellation).ConfigureAwait(false);
                    if (!GetExitCodeProcess(info.Process, out var exit)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    while (ActiveProcesses(job) > 0) await Task.Delay(100, cancellation).ConfigureAwait(false);
                    return unchecked((int)exit);
                }
                catch (OperationCanceledException)
                {
                    TerminateJobObject(job, 1);
                    for (var i = 0; i < 100 && ActiveProcesses(job) > 0; i++) await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                if (attributesReady) DeleteProcThreadAttributeList(attributes);
                if (attributes != 0) Marshal.FreeHGlobal(attributes);
                if (jobArray != 0) Marshal.FreeHGlobal(jobArray);
                if (info.Thread != 0) CloseHandle(info.Thread);
                if (info.Process != 0) CloseHandle(info.Process);
                Marshal.FreeHGlobal(block);
                CloseHandle(job); // kill on close: nothing the installer started outlives it
            }
        }

        private static uint ActiveProcesses(nint job)
        {
            if (!QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<BasicAccounting>(), 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return accounting.ActiveProcesses;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string? Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, Reserved2Size;
            public nint Reserved2, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx
        {
            public StartupInfo Startup;
            public nint AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public nint Process, Thread;
            public int ProcessId, ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicAccounting
        {
            public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processAttributes, nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string currentDirectory, ref StartupInfoEx startup, out ProcessInformation information);
        private static readonly nint JobList = 0x0002000D; // PROC_THREAD_ATTRIBUTE_JOB_LIST
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previousValue, nint returnSize);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern void DeleteProcThreadAttributeList(nint list);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern nint CreateJobObjectW(nint attributes, nint name);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(nint job, int infoClass, ref Shims.ShimJobExtendedLimits limits, uint length);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObject(nint job, int infoClass, out BasicAccounting information, uint length, nint returned);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(nint job, uint exitCode);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(nint process, out uint exitCode);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint handle);
    }
}
