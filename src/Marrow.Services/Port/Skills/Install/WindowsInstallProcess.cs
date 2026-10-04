// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Diagnostics;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>Opt-in executable allow-list by absolute path, length and hash. No implicit binding, shell, or PATH search.</summary>
public sealed class WindowsInstallProcess(IReadOnlyList<LocalExecutable> executables) : IInstallProcess
{
    public async Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installer executable execution is WINDOWS-ONLY.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(command.Executable) || !Path.IsPathFullyQualified(command.WorkingDirectory)) throw new InvalidDataException("Absolute process paths are required.");
        var pin = executables.SingleOrDefault(p => p.Path.Equals(command.Executable, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Installer executable is not allowed.");
        InstallHash.Verify(pin.Path, pin.Size, pin.Sha256);
        InstallPaths.NoReparseAncestors(command.WorkingDirectory);
        var start = new ProcessStartInfo(command.Executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = command.WorkingDirectory };
        foreach (var arg in command.Arguments) start.ArgumentList.Add(arg);
        using var child = Process.Start(start) ?? throw new IOException("Installer process did not start.");
        try { await child.WaitForExitAsync(cancellationToken); }
        catch { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); } throw; }
        return child.ExitCode;
    }
}
