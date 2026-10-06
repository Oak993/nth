// PORT-ONLY: UE-3; explicit application-only process binding for the commands recorded in trial H.
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Marrow.Services.Port.Updates;

public sealed class WindowsDevToolProcess
{
    private readonly Func<string, CancellationToken, Task<string?>> readInstallerLog;
    public WindowsDevToolProcess(Func<string, CancellationToken, Task<string?>>? readInstallerLog = null) => this.readInstallerLog = readInstallerLog ?? ((_, _) => Task.FromResult<string?>(null));
    public static string Validate(DevToolCommand command)
    {
        var args = command.Arguments;
        if (command.Executable == "winget")
        {
            var upgrade = args.Count == 10 && args[0] == "upgrade" && WingetUpdater.HasTrial(args[2])
                && args.SequenceEqual(WingetUpdater.UpgradeCommand(args[2]).Arguments);
            var list = new[] { new[] { "--id", "Git.Git", "-e" }, ["--id", "GitHub.cli", "-e"], ["--name", "Node.js"], ["--name", "Python"] }
                .Any(query => args.SequenceEqual(WingetUpdater.ListCommand(query).Arguments));
            // Individual items can also read their exact installed-package match.
            var exact = args.Count == 6 && args[0] == "list" && args[1] == "--id" && WingetUpdater.HasTrial(args[2])
                && args.SequenceEqual(WingetUpdater.ListCommand(["--id", args[2], "-e"]).Arguments);
            if (upgrade || list || exact) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        }
        if (command.Executable is "git" or "gh" or "node" && args.SequenceEqual(["--version"])) return command.Executable;
        if (command.Executable == "py" && args.SequenceEqual(["-0p"])) return "py.exe";
        if (command.Executable == "where.exe" && args.SequenceEqual(["python"])) return Path.Combine(Environment.SystemDirectory, "where.exe");
        var python = Path.IsPathFullyQualified(command.Executable) ? Path.GetFullPath(command.Executable) : null;
        if (python is not null && Path.GetFileName(python).Equals("python.exe", StringComparison.OrdinalIgnoreCase)
            && !python.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) && args.SequenceEqual(["--version"])) return python;
        var office = WindowsOfficeInstallation.ClientPath;
        if (command.Executable.Equals(office, StringComparison.OrdinalIgnoreCase) && args.SequenceEqual(OfficeC2RUpdater.UpgradeCommand(office).Arguments)) return office;
        throw new InvalidOperationException("This command is outside the supervised developer-tool recipes.");
    }

    public async Task<DevToolCommandResult> Run(DevToolCommand command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var executable = Validate(command);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception error) { process.Dispose(); return new(null, "", error.Message); }
        var completion = Collect(process);
        // A timeout/cancel stops awaiting a read-only probe. Its completion is still observed/drained;
        // no process is terminated. A launched updater is always awaited through its natural exit.
        var mutating = command.Executable == "winget" && command.Arguments[0] == "upgrade"
            || command.Executable.Equals(WindowsOfficeInstallation.ClientPath, StringComparison.OrdinalIgnoreCase);
        // ENGINEERING DECISION: bound each read-only source/probe to 30 seconds, independently of installer waits.
        using var probeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        if (!mutating) probeDeadline.CancelAfter(TimeSpan.FromSeconds(30));
        DevToolCommandResult result;
        try { result = mutating ? await completion : await completion.WaitAsync(probeDeadline.Token); }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested) return new(null, "", "The developer-tool inventory probe timed out; its process was left running.");
            throw;
        }
        return await GitInstallerLog.Attach(command, result, readInstallerLog, cancellation);
    }

    private static async Task<DevToolCommandResult> Collect(Process process)
    {
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception) { return new(null, "", error.Message); }
        finally { process.Dispose(); }
    }
}
