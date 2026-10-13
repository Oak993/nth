using Marrow.App.Port.PcControl.Workspace;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: the child session for ChildSessionHost tests (unit P18, ED-NEW-pcws-5, ED-NEW-pcws-7). The real agent with its
// own engine on the fake desktop, over an in-memory pipe: the first workspace window the host makes connects by starting the
// agent with the token at the end of its program line; every later one fails to connect with LaterProblem. No Remote Desktop,
// no named pipe, no other process. Notepad and Calculator open one window each when launched.
internal sealed class WorkspaceAgentStandIn : IAsyncDisposable
{
    public const string Exe = "C:\\nth\\current\\nth.exe";
    public const string Name = "nth-workspace-0123456789abcdef0123456789abcdef";
    public const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string LaterProblem = "Remote Desktop could not connect (0x80004005)";

    private readonly PcAssemblyRig rig = new();
    private readonly PcAssembly assembly;
    private readonly WorkspaceAgent agent;
    private readonly (WorkspaceDuplex Nth, WorkspaceDuplex Agent) pipe = WorkspaceDuplex.Pair();

    public WorkspaceAgentStandIn()
    {
        var settings = new WorkspaceAgentSettings();
        assembly = WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        agent = new WorkspaceAgent(assembly, settings, line =>
        {
            lock (AgentLog) AgentLog.Add(line);
        }, new PcManualClock());
        rig.Desktop.OnLaunch = target => target.EndsWith("calc.exe", StringComparison.OrdinalIgnoreCase)
            ? rig.Desktop.AddApp(0x2b, "Calculator", "calc.exe", 102)
            : rig.Desktop.AddApp(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        First = new FakeWorkspaceSurface { OnConnect = program => Serving = agent.Run(pipe.Agent, program[(program.LastIndexOf(' ') + 1)..], CancellationToken.None) };
    }

    /// <summary>The first workspace window, the one that connects.</summary>
    public FakeWorkspaceSurface First { get; }

    /// <summary>Every workspace window the host made, in order.</summary>
    public List<FakeWorkspaceSurface> Made { get; } = [];

    /// <summary>The pipe names the host listened on.</summary>
    public List<string> Names { get; } = [];

    /// <summary>The agent's run, once the first window connected.</summary>
    public Task<int>? Serving { get; private set; }

    public List<string> Log { get; } = [];

    public List<string> AgentLog { get; } = [];

    public ChildSessionSeams Seams(IChildSessionReadiness readiness) => new()
    {
        Readiness = readiness,
        Surface = () =>
        {
            var made = Made.Count == 0 ? First : new FakeWorkspaceSurface { Problem = LaterProblem };
            Made.Add(made);
            return made;
        },
        Accept = (name, _) =>
        {
            Names.Add(name);
            return Task.FromResult<Stream>(pipe.Nth);
        },
        Executable = Exe,
        Clock = new PcManualClock(),
        Log = line =>
        {
            lock (Log) Log.Add(line);
        },
        Released = true,
        NewName = () => Name,
        NewToken = () => Token,
    };

    public async ValueTask DisposeAsync()
    {
        await agent.DisposeAsync();
        assembly.Dispose();
        rig.Dispose();
        await pipe.Nth.DisposeAsync();
        await pipe.Agent.DisposeAsync();
    }
}
