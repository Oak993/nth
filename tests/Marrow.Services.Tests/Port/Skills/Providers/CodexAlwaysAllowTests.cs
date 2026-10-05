// PORT-ONLY: SK-11b / WU-7 offline acceptance of skills-PLAN.md 2.1 "Codex Always allow for skill scripts". Fake approval requests only.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class CodexAlwaysAllowTests
{
    private static JSONValue Available(params JSONValue[] decisions) => new JSONValue.Array(new ValueList<JSONValue>(decisions));

    private static JSONValue Amendment(params string[] command) => JSONValue.FromDictionaryLiteral(("acceptWithExecpolicyAmendment",
        JSONValue.FromDictionaryLiteral(("execpolicy_amendment", new JSONValue.Array(new ValueList<JSONValue>(command.Select(c => (JSONValue)new JSONValue.String(c))))))));

    private static readonly string Views = Path.Combine(Path.GetTempPath(), "Marrow Code", "skills", "views");

    private static IReadOnlyList<JSONValue> Decisions(params JSONValue[] items) => items;

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void An_amendment_naming_the_views_root_is_withheld_in_either_slash_form_and_any_letter_case()
    {
        var script = Path.Combine(Views, "codex", "chat-a", "pdf", "scripts", "run.py");
        Assert.True(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", script)), Views));
        Assert.True(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", script.Replace('\\', '/'))), Views));
        Assert.True(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", script.Replace('/', '\\'))), Views));
        Assert.True(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", script.ToUpperInvariant())), Views));
        Assert.True(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", script.ToLowerInvariant())), Views));
        Assert.True(CodexAlwaysAllow.Withholds(Decisions(new JSONValue.String("accept"), Amendment("node", "-e", "x", script)), Views.Replace('/', '\\') + Path.DirectorySeparatorChar));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void An_amendment_outside_the_views_root_and_every_other_decision_is_left_alone()
    {
        Assert.False(CodexAlwaysAllow.Withholds(Decisions(Amendment("git", "status")), Views));
        Assert.False(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", Path.Combine(Path.GetTempPath(), "Marrow Code", "skills", "store", "x.py"))), Views));
        Assert.False(CodexAlwaysAllow.Withholds(Decisions(Amendment("python3", Views + "-sibling/x.py")), Views)); // a longer sibling name is not under the root
        Assert.False(CodexAlwaysAllow.Withholds(Decisions(new JSONValue.String("accept"), new JSONValue.String("acceptForSession")), Views));
        Assert.False(CodexAlwaysAllow.Withholds(null, Views));
        Assert.False(CodexAlwaysAllow.Withholds(Decisions(), Views));
        // The views path mentioned only as plain decision text is not an amendment.
        Assert.False(CodexAlwaysAllow.Withholds(Decisions(new JSONValue.String(Path.Combine(Views, "x"))), Views));
    }

    private static (CodexSession Session, List<ProviderEvent> Events, GenerationFixture Fixture, Generation Chat) Wired()
    {
        var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var chat = f.Open().Start("codex", "chat-a", [item], f.Switches((item, true)), f.Budget);
        var wiring = new CodexSkillWiring(chat, f.Paths);
        var session = CodexWiringFixture.Session(wiring);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events, f, chat);
    }

    private static void Ask(CodexSession session, int id, JSONValue decisions) => CodexWiringFixture.Call(session, "HandleRequest", new RPCID.Int(id),
        "item/commandExecution/requestApproval", JSONValue.FromDictionaryLiteral(("command", "python3 run.py"), ("availableDecisions", decisions)));

    private static bool HasAmendPayload(CodexSession session, string requestId)
    {
        var pending = CodexWiringFixture.Field<System.Collections.IDictionary>(session, "pendingRequests")[requestId]!;
        var payloads = (ValueMap<string, JSONValue>)pending.GetType().GetProperty("Payloads")!.GetValue(pending)!;
        return payloads.ContainsKey("amend");
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_skill_script_request_offers_Approve_and_Approve_for_session_but_no_Always_allow()
    {
        var (session, events, f, chat) = Wired();
        using var _ = f;
        using var __ = chat;
        var script = Path.Combine(f.Paths.Views, "codex", "chat-a", "pdf", "scripts", "run.py");
        Ask(session, 1, Available("accept", "acceptForSession", Amendment("python3", script), "decline", "cancel"));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(["accept", "acceptForSession", "decline", "cancel"], approval.Options.Select(o => o.Id));
        Assert.DoesNotContain(approval.Options, o => o.Title == "Always allow");
        Assert.False(HasAmendPayload(session, "1"));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_command_outside_the_views_root_keeps_Always_allow_exactly_as_today_with_or_without_wiring()
    {
        var (session, events, f, chat) = Wired();
        using var _ = f;
        using var __ = chat;
        var decisions = Available("accept", "acceptForSession", Amendment("git", "status"), "decline", "cancel");
        Ask(session, 2, decisions);
        var bare = CodexWiringFixture.Session();
        var bareEvents = new List<ProviderEvent>();
        bare.OnEvent = bareEvents.Add;
        Ask(bare, 2, decisions);
        var wired = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        var plain = Assert.Single(bareEvents.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(["accept", "acceptForSession", "amend", "decline", "cancel"], plain.Options.Select(o => o.Id));
        Assert.Equal(plain.Options.Select(o => (o.Id, o.Title, o.OptionRole)), wired.Options.Select(o => (o.Id, o.Title, o.OptionRole)));
        Assert.True(HasAmendPayload(session, "2"));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void Without_wiring_a_skill_script_amendment_is_unchanged_because_there_is_no_views_root_to_compare()
    {
        var session = CodexWiringFixture.Session();
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        Ask(session, 3, Available("accept", Amendment("python3", Path.Combine(Views, "codex", "chat-a", "pdf", "run.py"))));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(["accept", "amend"], approval.Options.Select(o => o.Id));
    }
}
