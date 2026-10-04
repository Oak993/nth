using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;
public sealed class PermissionTapTests
{
    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "DL-12")]
    public void Copilot_fixture_emits_held_failed_and_skill_events_without_changing_timeline_events()
    {
        using var runtime = new TestRuntime();
        var (session, timeline) = CopilotSessionHarness.Offline();
        var skills = new List<ToolEvent>();
        SetSink(session, skills.Add);
        foreach (var row in ProviderTranscripts.Read("copilot")) CopilotSessionHarness.Notify(runtime, session, row["params"]!);
        Assert.Equal(new ToolEvent[] { new CommandHeld("python render.py"), new CommandFinished("python render.py", null, true, "ModuleNotFoundError: No module named 'docx'"), new SkillUsed("documents") }, skills);
        Assert.Single(timeline.OfType<ProviderEvent.Approval>());
        Assert.Equal(2, timeline.OfType<ProviderEvent.ToolStarted>().Count());
        Assert.Single(timeline.OfType<ProviderEvent.ToolUpdated>());
    }

    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "DL-12")]
    public void ACP_fixture_correlates_sparse_completion_with_the_original_command()
    {
        using var runtime = new TestRuntime();
        var session = new ACPSession(CopilotSessionHarness.Config() with { Provider = ProviderKind.Cursor });
        var skills = new List<ToolEvent>();
        var timeline = new List<ProviderEvent>();
        session.OnEvent = timeline.Add;
        SetSink(session, skills.Add);
        foreach (var row in ProviderTranscripts.Read("acp")) Deliver(runtime, session, row);
        Assert.Equal(new ToolEvent[] { new CommandHeld("soffice --headless --convert-to pdf sample.docx"), new CommandFinished("soffice --headless --convert-to pdf sample.docx", 127, true, "soffice: command not found") }, skills);
        Assert.Single(timeline.OfType<ProviderEvent.Approval>());
        Assert.Single(timeline.OfType<ProviderEvent.ToolStarted>());
    }

    [Theory, InlineData(false), InlineData(true), Trait("Feature", "SK-11c"), Trait("Rule", "DL-12")]
    public void Automatically_approved_commands_never_emit_held_events(bool acp)
    {
        using var runtime = new TestRuntime();
        object session = acp ? new ACPSession(CopilotSessionHarness.Config(runtimeMode: RuntimeMode.FullAccess) with { Provider = ProviderKind.Cursor }) : CopilotSessionHarness.Offline(CopilotSessionHarness.Config(runtimeMode: RuntimeMode.FullAccess)).Session;
        var skills = new List<ToolEvent>();
        SetSink(session, skills.Add);
        var row = ProviderTranscripts.Read(acp ? "acp" : "copilot").ElementAt(1);
        Deliver(runtime, session, row);
        Assert.Empty(skills);
    }

    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "DL-12")]
    public void Prose_unknown_results_and_replayed_ACP_updates_are_not_signals()
    {
        using var runtime = new TestRuntime();
        var session = new ACPSession(CopilotSessionHarness.Config());
        var skills = new List<ToolEvent>();
        SetSink(session, skills.Add);
        Deliver(runtime, session, JSONValue.Parse("{\"method\":\"session/update\",\"params\":{\"update\":{\"sessionUpdate\":\"agent_message_chunk\",\"content\":{\"text\":\"python: command not found\"}}}}")!);
        Deliver(runtime, session, ProviderTranscripts.Read("acp").Last());
        session.GetType().GetField("isReplaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, true);
        foreach (var row in ProviderTranscripts.Read("acp").Where(r => r["id"] is null)) Deliver(runtime, session, row);
        Assert.Empty(skills);
    }

    private static void SetSink(object session, Action<ToolEvent> sink)
    {
        var property = session.GetType().GetProperty("OnSkillsEvent");
        Assert.NotNull(property);
        property.SetValue(session, sink);
    }
    private static void Deliver(TestRuntime runtime, object session, JSONValue row) => runtime.OnMain(() =>
    {
        var method = row["id"] is null ? "HandleNotification" : "HandleRequest";
        object?[] args = row["id"] is null ? [row["method"]!.JSONValueString!, row["params"]!] : [RPCID.Create(row["id"]!)!, row["method"]!.JSONValueString!, row["params"]!];
        session.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args);
    });
}
