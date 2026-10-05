using Marrow.App.Port.Skills;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Providers;
namespace Marrow.App.Tests.Fixtures;
public sealed class SkillsOfferRig : IDisposable
{
    private readonly IDisposable shell;
    public ThreadRuntimeTurnRig Turn { get; }
    public SkillsOfferDownloadFake Download { get; } = new();
    public SkillsOfferFlow Flow { get; }
    public AddonsState State { get; private set; } = AddonsState.Empty;
    public string InstalledPath { get; }
    public SkillsOfferRig()
    {
        shell = ((Ambient<Func<ShellInvocation, Task<ShellResult>>>)typeof(Shell).GetProperty("RunForTesting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!).Override(call =>
        {
            if (call.Arguments.Contains("rev-parse") || call.Arguments.Contains("status") || call.Arguments.Contains("diff"))
                return Task.FromResult(new ShellResult(1, [], []));
            throw new InvalidOperationException("Unexpected offline process invocation.");
        });
        Turn = new();
        InstalledPath = Path.Combine(Turn.H.NewFolder("offer"), "fixture-soffice.exe");
        var catalogue = SkillsCatalogueLoader.LoadEmbedded();
        catalogue = catalogue with { Items = catalogue.Items.Select(i => i.Id == "A2" ? i with { Skills = i.Skills.Select(skill => skill.Folder == "docx" ? skill with { Needs = new[] { "T3" } } : skill).ToArray() } : i).ToArray() };
        Flow = Turn.H.Main.OnMain(() => new SkillsOfferFlow(Turn.Runtime, catalogue, () => State, s => State = s, Download));
    }
    public void SetState(AddonsState state) => Main(() => State = state);
    public void Main(Action action) => Turn.H.Main.OnMain(action);
    public void Run(Func<Task> action) => Turn.H.Main.Loop.Run(action, AppModelHarness.Bound);
    public void Offer(ApprovalRequest? approval = null) => Main(() => Flow.Observe(approval is null ? new CommandFinished("soffice --headless", 127, true, "soffice: command not found") : new CommandHeld("soffice --headless"), approval));
    public void Succeed()
    {
        Download.Script = (items, progress, _) =>
        {
            progress(new(OfferStage.Downloading, 10, 20)); progress(new(OfferStage.Checking, 20, 20));
            return Task.FromResult<IReadOnlyDictionary<string, string>>(items.ToDictionary(i => i.Id, _ => InstalledPath, StringComparer.Ordinal));
        };
    }
    public ApprovalRequest Held(string? description = null)
    {
        var approval = new ApprovalRequest("held-soffice", ApprovalRequest.Kind.Command, "soffice --headless", new ValueList<ApprovalRequest.Option>([new("allow", "Allow", ApprovalRequest.Option.Role.Approve), new("deny", "Decline", ApprovalRequest.Option.Role.Decline)]), description ?? "soffice --headless");
        Main(() => Turn.Session.Emit(new ProviderEvent.Approval(approval)));
        return approval;
    }
    public void End(TurnStatus status)
    {
        Main(() => Turn.Session.Emit(new ProviderEvent.TurnCompleted(status, null)));
        Turn.H.Main.Loop.RunUntil(() => Turn.Session.Inputs.Count == 2, AppModelHarness.Bound);
    }
    public void Dispose() { Turn.Dispose(); shell.Dispose(); }
}
