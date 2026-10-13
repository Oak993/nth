using System.Diagnostics;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 6 and 10, unit U0, ED-NEW-inst-u0-1). Starts a Velopack program as a hidden child:
/// no shell, no console window (<c>CreateNoWindow</c>), a hidden window style, the command's arguments one by one (no
/// quoting by hand), its working folder, and the setup's environment plus the command's variables. It never kills the
/// child; <see cref="VelopackRun.Exited"/> completes with the exit code when the child ends on its own.
/// </summary>
public sealed class ProcessVelopackRunner : IVelopackRunner
{
    public VelopackRun Start(VelopackCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var start = new ProcessStartInfo(command.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = command.WorkingDirectory,
        };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in command.Environment) start.Environment[name] = value;
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(command.Executable)} did not start.");
        return new VelopackRun(process.Id, WaitAsync(process));
    }

    private static async Task<int> WaitAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
