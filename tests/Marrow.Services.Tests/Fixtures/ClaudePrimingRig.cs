using System.Reflection;
using Marrow.Core.Accounts;
using System.Text;
using Marrow.Testing;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class ClaudePrimingRig : IDisposable
{
    private readonly StorageScratchFolder folder = new();
    private readonly IDisposable help;
    private readonly IDisposable process;
    private readonly IDisposable? version;
    public ClaudePrimingRig(string failure = "")
    {
        Runtime = new TestRuntime();
        Configuration = new()
        {
            Provider = ProviderKind.Claude, Executable = SwiftURL.Create("/offline/claude-priming-" + Guid.NewGuid().ToString("N"))!,
            WorkingDirectory = SwiftURL.Create(folder.Root)!, Environment = ValueMap<string, string>.Empty,
            Model = "offline-model", FastMode = true, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--agents --settings"), [])));
        var seam = typeof(ClaudeProviderRuntime).GetProperty("Version", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            as Ambient<Func<SwiftURL, IReadOnlyDictionary<string, string>, Task<ShellResult>>>;
        version = seam?.Override((_, _) => Task.FromResult(new ShellResult(0,
            Encoding.UTF8.GetBytes(failure == "version" ? "2.1.160 (Claude Code)" : "2.1.284 (Claude Code)"), [])));
        process = ClaudeProviderRuntime.Process.Override((_, args, _, _) => Process = new(args, failure));
        Session = new ClaudeSession(Configuration);
    }
    public TestRuntime Runtime { get; }
    public SessionConfiguration Configuration { get; }
    public ClaudeSession Session { get; }
    public ClaudePrimingProcess? Process { get; private set; }
    public Task<HandshakeStandbySession> Start() => HandshakeStandbySession.StartAsync(Session, Configuration, CancellationToken.None);
    public static Task Prime(HandshakeStandbySession standby) => standby.ReceiveAsync(
        new(Bring.MarrowSummary, new("Config.cs: preserve the latest instruction.", null, ["Config.cs"]), null), true, CancellationToken.None);
    public static Task Restore(HandshakeStandbySession standby)
    {
        var method = typeof(HandshakeStandbySession).GetMethod("PrepareForAdoptionAsync");
        Assert.NotNull(method);
        return (Task)method.Invoke(standby, [CancellationToken.None])!;
    }
    public void Dispose() { Session.Stop(); Runtime.Loop.RunUntilIdle(); process.Dispose(); version?.Dispose(); help.Dispose(); Runtime.Dispose(); folder.Dispose(); }
}
