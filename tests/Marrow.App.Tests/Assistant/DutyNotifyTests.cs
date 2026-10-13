using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in", 7.1; ED-NEW-dextra-as9-9): "Notify me" on a duty is the one opt-in to a toast. The duty tools take
// "notify" (a new duty, an edit and a switch off again) and the card that confirms the duty says so in full; the host tells the owner when a run of such a
// duty ends with something to report, and only then: not for another report style, not for a quiet run, a failed or stopped one or a dry run.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class DutyNotifyTests
{
    private static readonly Guid Thread = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static AssistantToolCall Call(string tool, string arguments) =>
        new(new AssistantChat(Thread, "dextra", null, AssistantChatRole.Home, ProviderKind.Claude, "model-a"), AssistantToolCatalog.Find(tool)!, JSONValue.Parse(arguments)!,
            new Classification(ActionClass.WriteElsewhere, "changes the assistant's standing configuration"),
            new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Asks: creating a duty always asks"), AssistantRoute.Mcp);

    private static (DutyTools Tools, DutyHostRig Rig) Make()
    {
        var rig = new DutyHostRig();
        rig.Host.Start();
        var tools = new DutyTools(rig.Host.Scheduler, new DutyToolsOptions
        {
            ZoneId = () => "UTC",
            AssistantName = id => id == "dextra" ? "Dextra" : id,
            ApproveFolder = rig.Host.Switches.ApproveFolder,
            Time = rig.Clock,
            ChatProject = _ => rig.Project.Id,
        });
        return (tools, rig);
    }

    private static Dictionary<string, string> Inputs(IReadOnlyList<DecisionInput>? inputs) => inputs!.ToDictionary(i => i.Name, i => i.Value);

    private const string Schedule = """{"title":"Espresso price","instructions":"Tell me when it drops.","schedule":"daily 08:00"%EXTRA%}""";

    private static AssistantToolCall Schedule_(string extra = "") => Call("schedule_duty", Schedule.Replace("%EXTRA%", extra, StringComparison.Ordinal));

    // ---- the tools ----

    [Fact]
    public void The_three_duty_tools_offer_notify_as_an_optional_yes_or_no()
    {
        foreach (var name in new[] { "schedule_duty", "watch_folder", "watch_page" })
        {
            var parameter = Assert.Single(AssistantToolCatalog.Find(name)!.Parameters, p => p.Name == "notify");
            Assert.Equal(AssistantParameterType.Boolean, parameter.Type);
            Assert.False(parameter.Required);
            Assert.Contains("notification", parameter.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_duty_with_no_notify_is_as_quiet_as_it_was_and_its_card_does_not_mention_a_notification()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var inputs = Inputs(tools.Describe(Schedule_()));
            Assert.Equal("Silent unless changed", inputs["Report"]);
            tools.Create(Schedule_(), _ => { });
            Assert.Equal(ReportStyle.SilentUnlessChanged, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
        }
    }

    [Fact]
    public void Notify_true_puts_the_one_opt_in_on_the_card_in_full_and_a_yes_makes_a_notify_me_duty()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var inputs = Inputs(tools.Describe(Schedule_(""","notify":true""")));
            Assert.Equal("Notify me: a notification each time a run finds something", inputs["Report"]);
            Assert.Empty(rig.Host.Scheduler.Duties);                                    // describing makes nothing
            var result = tools.Create(Schedule_(""","notify":true"""), _ => { });
            Assert.False(result.IsError, result.Text);
            Assert.Equal(ReportStyle.NotifyMe, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
        }
    }

    [Fact]
    public void Notify_wins_over_silent_and_an_edit_shows_the_change_on_its_card()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            tools.Create(Schedule_(""","silent":false"""), _ => { });
            Assert.Equal(ReportStyle.DigestOnly, rig.Host.Scheduler.Duties[0].Duty.Report);
            var edit = Schedule_(""","silent":false,"notify":true""");
            Assert.Equal("Notify me: a notification each time a run finds something (was: Digest only)", Inputs(tools.Describe(edit))["Report"]);
            tools.Create(edit, _ => { });
            Assert.Equal(ReportStyle.NotifyMe, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
        }
    }

    [Fact]
    public void An_edit_that_says_nothing_about_notify_keeps_it_and_notify_false_takes_it_off()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            tools.Create(Schedule_(""","notify":true"""), _ => { });
            tools.Create(Call("schedule_duty", """{"title":"Espresso price","instructions":"Tell me the price.","schedule":"daily 09:00"}"""), _ => { });
            Assert.Equal(ReportStyle.NotifyMe, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
            tools.Create(Schedule_(""","notify":false"""), _ => { });
            Assert.Equal(ReportStyle.SilentUnlessChanged, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
            tools.Create(Schedule_(""","notify":true"""), _ => { });
            tools.Create(Schedule_(""","notify":false,"silent":false"""), _ => { });
            Assert.Equal(ReportStyle.DigestOnly, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
        }
    }

    [Fact]
    public void Notify_that_is_not_a_yes_or_no_is_left_out()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            tools.Create(Schedule_(""","notify":"yes","silent":false"""), _ => { });
            Assert.Equal(ReportStyle.DigestOnly, Assert.Single(rig.Host.Scheduler.Duties).Duty.Report);
        }
    }

    [Fact]
    public void A_folder_watch_and_a_page_watch_take_notify_too_on_their_cards()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var folder = Path.Combine(rig.Folder, "watched");
            Directory.CreateDirectory(folder);
            var escaped = folder.Replace("\\", "\\\\", StringComparison.Ordinal);
            var quiet = Inputs(tools.Describe(Call("watch_folder", $$"""{"path":"{{escaped}}","instructions":"Sort it."}""")));
            var loud = Inputs(tools.Describe(Call("watch_folder", $$"""{"path":"{{escaped}}","instructions":"Sort it.","notify":true}""")));
            Assert.Equal("Silent unless changed", quiet["Report"]);
            Assert.StartsWith("Notify me", loud["Report"], StringComparison.Ordinal);
            var page = Inputs(tools.Describe(Call("watch_page", """{"url":"https://shop.example/espresso","instructions":"Tell me the price.","notify":true}""")));
            Assert.StartsWith("Notify me", page["Report"], StringComparison.Ordinal);
        }
    }

    // ---- the host ----

    private static Duty Notifying(string id, int hour, string name = "Espresso price") => DutyHostRig.Daily(id, hour) with { Name = name, Report = ReportStyle.NotifyMe };

    [Fact]
    public void A_notify_me_run_that_finishes_with_something_tells_the_owner_with_its_mission_the_duty_name_and_the_report()
    {
        using var rig = new DutyHostRig();
        var duty = rig.Save(Notifying("n1", 8));
        rig.Host.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var mission = rig.MissionOf(duty);
        rig.Complete(mission, "The price fell to $640.");
        MissionRig.Until(() => rig.Findings.Count == 1, "the duty's news");
        Assert.Equal(new DutyFinding(mission.Id, "Espresso price", "The price fell to $640."), Assert.Single(rig.Findings));
    }

    [Fact]
    public void Other_report_styles_a_quiet_run_and_a_failed_one_tell_nobody_and_only_the_marker_run_does()
    {
        using var rig = new DutyHostRig();
        var plain = rig.Save(DutyHostRig.Daily("plain", 8));                                         // Silent unless changed, with something to report
        var digest = rig.Save(DutyHostRig.Daily("digest", 8) with { Report = ReportStyle.DigestOnly });
        var always = rig.Save(DutyHostRig.Daily("always", 8) with { Report = ReportStyle.Always });
        var quiet = rig.Save(Notifying("quiet", 8));                                                 // Notify me, but nothing changed
        var failing = rig.Save(Notifying("failing", 8));                                             // Notify me, but the run failed
        var marker = rig.Save(Notifying("marker", 8, "Marker"));
        rig.Host.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        rig.Complete(rig.MissionOf(plain), "The price fell to $640.");
        rig.Complete(rig.MissionOf(digest), "The price fell to $640.");
        rig.Complete(rig.MissionOf(always), "The price fell to $640.");
        rig.Complete(rig.MissionOf(quiet), "Looked at the page.\nRESULT: nothing_changed");
        var failed = rig.MissionOf(failing);
        var lead = rig.LeadOf(failed);
        rig.Controller.OnTurnFinished(lead, TurnStatus.Failed, false);
        MissionRig.Until(() => rig.Host.Scheduler.Find("failing")!.History[0].Outcome == DutyRunOutcome.Failed, "the failure");
        rig.Complete(rig.MissionOf(marker), "Found it.");
        MissionRig.Until(() => rig.Findings.Count >= 1, "the marker's news");
        var finding = Assert.Single(rig.Findings);
        Assert.Equal("Marker", finding.Duty);
    }

    [Fact]
    public void A_notify_me_dry_run_tells_nobody_because_nothing_was_really_found()
    {
        using var rig = new DutyHostRig();
        var dry = rig.Save(Notifying("dry", 20));                                                    // not due while the clock moves two hours
        var marker = rig.Save(Notifying("marker", 8, "Marker"));
        rig.Host.Start();
        Assert.Null(rig.Host.Scheduler.RunNow(dry.Id, dry: true));
        var drill = rig.MissionOf(dry);
        Assert.True(drill.DryRun);
        rig.Clock.Advance(TimeSpan.FromHours(2));
        rig.Complete(rig.MissionOf(marker), "Found it.");
        MissionRig.Until(() => rig.Findings.Count >= 1, "the marker's news");
        Assert.All(rig.Findings, finding => Assert.Equal("Marker", finding.Duty));
    }

    [Fact]
    public void A_toast_that_cannot_be_made_is_told_to_the_board_by_the_kind_of_error_only_and_never_breaks_the_run()
    {
        using var rig = new DutyHostRig();
        var duty = rig.Save(Notifying("n1", 8));
        rig.NotifyFails = new InvalidOperationException("sentinel-secret-sk-abcdefghijklmnopqrstuvwxyz0123456789");
        rig.Host.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        rig.Complete(rig.MissionOf(duty), "The price fell to $640.");
        MissionRig.Until(() => { lock (rig.Faults) return rig.Faults.Any(f => f.StartsWith("A duty's news could not be sent", StringComparison.Ordinal)); }, "the fault");
        lock (rig.Faults)
        {
            Assert.Equal("A duty's news could not be sent: InvalidOperationException", Assert.Single(rig.Faults, f => f.Contains("news", StringComparison.Ordinal)));
            Assert.DoesNotContain(rig.Faults, f => f.Contains("sentinel-secret", StringComparison.Ordinal));
        }
        Assert.Equal(DutyRunOutcome.Done, rig.Host.Scheduler.Find("n1")!.History[0].Outcome);
    }
}
