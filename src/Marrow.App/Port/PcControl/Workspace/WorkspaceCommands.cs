// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-2, ED-NEW-pcws-5, ED-NEW-pcws-6; DESIGN.md 2.13,
// section 8 P18). The three command lines Program.Main hands here, before anything of the desktop app starts:
// - `--tactus-workspace-agent <pipe> <token>`: what the child session runs as its shell. It accepts only a workspace pipe name
//   and token of the exact shape nth makes, connects to that current-user pipe, and serves nth with its own engine over the
//   desk's kind of backends (WorkspaceAgent). It has no window of its own.
// - `--tactus-workspace-enable` and `--tactus-workspace-disable`: the user's own commands that turn Windows child sessions on
//   or off (WorkspaceSetup.Run). They refuse unless the user runs them in a terminal opened as administrator. Nothing in nth
//   runs them: Settings only shows and copies them.
using System.IO.Pipes;
using Marrow.Core.Support;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.App.Port.PcControl.Workspace;

/// <summary>What the workspace commands run on: the native pieces in the app, fakes in tests.</summary>
internal sealed class WorkspaceCommandSeams
{
    public required IChildSessionSwitch Switch { get; init; }

    public required Func<bool> Elevated { get; init; }

    public required TextWriter Output { get; init; }

    /// <summary>Connects to nth's current-user pipe by name; null when it is not there.</summary>
    public required Func<string, CancellationToken, Task<Stream?>> Connect { get; init; }

    /// <summary>The agent's composition over its backends.</summary>
    public required Func<WorkspaceAgentSettings, PcAssembly> Assembly { get; init; }

    public Action<string> Log { get; init; } = _ => { };
}

internal static class WorkspaceCommands
{
    public const int ConnectTimeoutMs = 10_000;

    /// <summary>Whether the command line is one of the workspace's three commands.</summary>
    public static bool Handles(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] is WorkspaceSetup.AgentCommand or WorkspaceSetup.EnableCommand or WorkspaceSetup.DisableCommand;

    public static Task<int> Run(string[] args) => Run(args, Desktop());

    public static async Task<int> Run(IReadOnlyList<string> args, WorkspaceCommandSeams seams)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(seams);
        if (!Handles(args)) return WorkspaceSetup.ExitUsage;
        if (args[0] != WorkspaceSetup.AgentCommand) return WorkspaceSetup.Run(args, seams.Switch, seams.Elevated, seams.Output);
        if (args.Count != 3 || !WorkspacePipe.IsName(args[1]) || !WorkspacePipe.IsToken(args[2]))
        {
            seams.Log("workspace.agent.usage");
            return WorkspaceSetup.ExitUsage;
        }
        var stream = await seams.Connect(args[1], CancellationToken.None).ConfigureAwait(false);
        if (stream is null)
        {
            seams.Log("workspace.agent.unreachable");
            return WorkspaceAgent.ExitBroken;
        }
        await using (stream.ConfigureAwait(false))
        {
            var settings = new WorkspaceAgentSettings();
            using var assembly = seams.Assembly(settings);
            var agent = new WorkspaceAgent(assembly, settings, seams.Log);
            await using (agent.ConfigureAwait(false))
            {
                seams.Log("workspace.agent.start");
                return await agent.Run(stream, args[2], CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static WorkspaceCommandSeams Desktop() => new()
    {
        Switch = new ChildSessionSwitch(),
        Elevated = ChildSessionNative.IsElevated,
        Output = Console.Out,
        Connect = Connect,
        Assembly = settings =>
        {
            // the agent's own capture: the same seams Program.Main binds for nth (Tactus's composition is not made here)
            PcCaptureSeams.Primary.SetProcessDefault(() => WgcWindowCapture.IsSupported ? WgcWindowCapture.Shared : null);
            PcCaptureSeams.Encoder.SetProcessDefault(() => JpegEncoder.Shared);
            var profiles = Path.Combine(StoragePaths.Directory("pc-control"), "workspace-browsers");
            return WorkspaceAgent.Assembly(WorkspaceAgent.Backends(TactusDesktop.Native(withWorkspace: false), profiles), settings);
        },
        Log = ChildSessionDesktop.AgentLog(),
    };

    private static async Task<Stream?> Connect(string name, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch (Exception error) when (error is TimeoutException or IOException or UnauthorizedAccessException)
        {
            _ = error;
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }
}
