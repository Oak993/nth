using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class SkillsObserverFailureReviewTests
{
    [Theory, Trait("Feature", "SK-11c"), Trait("Rule", "R-TEST-8")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_throwing_skills_observer_preserves_the_existing_timeline_dispatch(bool acp)
    {
        using var runtime = new TestRuntime();
        var before = new List<ProviderEvent>();
        var after = new List<ProviderEvent>();
        object Make(List<ProviderEvent> timeline)
        {
            if (acp) return new ACPSession(CopilotSessionHarness.Config() with { Provider = ProviderKind.Cursor }) { OnEvent = timeline.Add };
            var (session, _) = CopilotSessionHarness.Offline();
            session.OnEvent = timeline.Add;
            return session;
        }
        var baseline = Make(before);
        var current = Make(after);
        var seen = new List<ToolEvent>();
        current.GetType().GetProperty("OnSkillsEvent")!.SetValue(current, (Action<ToolEvent>)(e => { seen.Add(e); throw new InvalidOperationException("fixture observer failure"); }));
        var errors = new List<Exception>();
        foreach (var row in ProviderTranscripts.Read(acp ? "acp" : "copilot"))
        {
            Deliver(runtime, baseline, row);
            // Keep delivering after each error so all observer paths are exercised on the old code too.
            if (Record.Exception(() => Deliver(runtime, current, row)) is { } error) errors.Add(error);
        }
        Assert.Contains(seen, e => e is CommandHeld);
        Assert.Contains(seen, e => e is CommandFinished);
        if (!acp) Assert.Contains(seen, e => e is SkillUsed);
        Assert.NotEmpty(before.OfType<ProviderEvent.ToolStarted>());
        Assert.NotEmpty(before.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(before, after);
        Assert.Empty(errors);
    }

    private static void Deliver(TestRuntime runtime, object session, JSONValue row) => runtime.OnMain(() =>
    {
        var method = row["id"] is null ? "HandleNotification" : "HandleRequest";
        object?[] args = row["id"] is null ? [row["method"]!.JSONValueString!, row["params"]!] : [RPCID.Create(row["id"]!)!, row["method"]!.JSONValueString!, row["params"]!];
        session.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args);
    });
}
