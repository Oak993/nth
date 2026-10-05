// SK-11f / DL-12 for the native providers (skills-ONDEMAND-PLAN.md 5.2, section 7 row DL-12): the session emits the normalized CommandHeld, CommandFinished and
// SkillUsed events (the detector's own types, never provider JSON) from the committed transcripts of Fixtures/skills-native, and nothing else in the session
// changes. The events are fed to the real missing-tool detector (SK-10) on the real embedded catalogue to show they are the ones it reads.
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class NativeProviderTapTests
{
    private const string Command = "soffice --headless --convert-to pdf report.docx";
    private const string NotRecognized = "'soffice' is not recognized as an internal or external command, operable program or batch file.";

    private static NativeSessionRig Rig(string[] transcripts, List<ToolEvent> taps, RuntimeMode mode, Func<string, (string, int)>? script = null) =>
        NativeSessionRig.Create(transcripts, mode, configure: session =>
        {
            session.OnToolEvent = taps.Add;
            if (script is not null) ((NativeCommandSeam)session).Script = script;
        }, factory: (config, configuration) => new NativeCommandSeam(config, configuration));

    private static Detection Detect(IEnumerable<ToolEvent> events) => MissingToolDetector.Detect(
        new DetectionContext(SkillsCatalogueLoader.LoadEmbedded(), AddonsState.Empty, "chat-1", new HashSet<string>()), events);

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_failed_command_in_full_access_emits_CommandFinished_and_the_detector_offers_the_tool()
    {
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["run-command-missing-tool.sse", "final-answer.sse"], taps, RuntimeMode.FullAccess, _ => (NotRecognized, 9009));
        await rig.Begin();
        await rig.Turn("convert it");

        Assert.Equal([new CommandFinished(Command, 9009, true, NotRecognized)], taps);
        Assert.Equal(["T3"], Detect(taps).Card!.Requested);
        // the model still reads exactly what it always read
        Assert.Equal($"exit 9009\n{NotRecognized}", rig.Json(1)["messages"]!.JSONValueArray![^1]["content"]!.JSONValueString);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_command_that_succeeds_emits_CommandFinished_with_exit_zero_and_no_error()
    {
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["run-command-missing-tool.sse", "final-answer.sse"], taps, RuntimeMode.FullAccess, _ => ("converted", 0));
        await rig.Begin();
        await rig.Turn("convert it");
        Assert.Equal([new CommandFinished(Command, 0, false, "converted")], taps);
        Assert.Null(Detect(taps).Card);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_command_held_for_approval_emits_CommandHeld_before_the_approval_and_the_detector_offers_the_tool()
    {
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["run-command-missing-tool.sse", "final-answer.sse"], taps, RuntimeMode.Supervised, _ => (NotRecognized, 9009));
        await rig.Begin();
        var turn = rig.StartTurn("convert it", RuntimeMode.Supervised);
        rig.RunUntil(() => rig.Events.OfType<ProviderEvent.Approval>().Any());

        Assert.Equal([new CommandHeld(Command)], taps);
        Assert.Equal(["T3"], Detect(taps).Card!.Requested);
        var approval = rig.Events.OfType<ProviderEvent.Approval>().Single().Value;
        rig.Session.ResolveApproval(approval.Id, "allow");
        rig.RunUntil(() => turn.IsCompleted);
        await turn;
        Assert.Equal([new CommandHeld(Command), new CommandFinished(Command, 9009, true, NotRecognized)], taps);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_declined_command_was_held_and_never_finished()
    {
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["run-command-missing-tool.sse", "final-answer.sse"], taps, RuntimeMode.Supervised);
        await rig.Begin();
        var turn = rig.StartTurn("convert it", RuntimeMode.Supervised);
        rig.RunUntil(() => rig.Events.OfType<ProviderEvent.Approval>().Any());
        rig.Session.ResolveApproval(rig.Events.OfType<ProviderEvent.Approval>().Single().Value.Id, "decline");
        rig.RunUntil(() => turn.IsCompleted);
        await turn;
        Assert.Equal([new CommandHeld(Command)], taps);
        Assert.Empty(((NativeCommandSeam)rig.Session).Commands);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_command_that_needs_no_approval_is_never_held()
    {
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["run-command-missing-tool.sse", "final-answer.sse"], taps, RuntimeMode.FullAccess);
        await rig.Begin();
        await rig.Turn("convert it");
        Assert.DoesNotContain(taps, e => e is CommandHeld);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Using_a_skill_emits_SkillUsed_with_the_set_and_a_reference_read_does_not()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var taps = new List<ToolEvent>();
        await using var rig = NativeSessionRig.Create(["use-skill-call.sse", "use-skill-file-call.sse", "final-answer.sse"], configure: session =>
        {
            session.OnToolEvent = taps.Add;
            session.SkillTools = new NativeSkillTools(generation);
        });
        await rig.Begin();
        await rig.Turn("make a pdf");
        Assert.Equal([new SkillUsed("pdf", "A1")], taps);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task With_no_listener_the_turn_runs_the_same()
    {
        await using var rig = NativeSessionRig.Create(["run-command-missing-tool.sse", "final-answer.sse"], factory: (config, configuration) =>
            new NativeCommandSeam(config, configuration) { Script = _ => (NotRecognized, 9009) });
        await rig.Begin();
        await rig.Turn("convert it");
        Assert.Equal($"exit 9009\n{NotRecognized}", rig.Json(1)["messages"]!.JSONValueArray![^1]["content"]!.JSONValueString);
    }
}
