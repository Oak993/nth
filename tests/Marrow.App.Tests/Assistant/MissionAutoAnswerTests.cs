using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.3 point 2, 12.5, 16 "Ordering rule"; ED-NEW-dextra-as7-8): while the injection
// checks have not passed, the engine's Allow still lands on a card; once they have, it is answered alone, logged first
// with the rule or grant that allowed it, labelled in the chat, and the grant it used records the use. A line that
// cannot be written refuses the step.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-8")]
public sealed class MissionAutoAnswerTests
{
    private const string AutoAnswerOff = "Asks until nth's injection checks pass";

    [Fact]
    public void Until_the_checks_pass_the_engines_allow_lands_on_a_card()
    {
        using var never = new DextraTaintRig(autoAnswer: false);
        using var failed = new DextraTaintRig(check: () => new InjectionReport(60, 3, new ValueList<string>(["web-02: the lead: Command curl: allowed: Alone"])));
        foreach (var rig in new[] { never, failed })
        {
            var record = rig.Mission();
            var lead = record.LeadThread!.Value;
            Assert.True(rig.Ask(lead, "r1", "dotnet test"));
            Assert.Null(rig.Answer(lead, "r1"));
            var card = Assert.Single(rig.Controller.Queue.Of(record.Id));
            Assert.Equal(AutoAnswerOff, card.Rule);
            Assert.Empty(rig.Agents.Notes);
            Assert.Contains(rig.Logged(), a => a.Tool == "approval" && a.Result == "asked: AutoAnswerOff");
        }
    }

    [Fact]
    public void Once_they_pass_it_is_answered_alone_logged_first_and_labelled_in_the_chat()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        Assert.True(rig.Ask(lead, "r1", "git status"));
        Assert.True(rig.Answer(lead, "r1"));
        Assert.Empty(rig.Controller.Queue.Of(record.Id));

        var line = Assert.Single(rig.Logged(), a => a.Tool == "approval");
        Assert.Equal("allowed: Alone", line.Result);
        Assert.Equal("rule:Alone", line.AllowedBy);
        Assert.Equal(lead.ToString("D"), line.Agent);
        var note = Assert.Single(rig.Agents.Notes);
        Assert.Equal(lead, note.Thread);
        Assert.StartsWith("Dextra allowed this for you: Runs alone.", note.Text, StringComparison.Ordinal);

        // What is not the engine's Allow still waits for the owner: an Ask, an always-confirmed send.
        Assert.True(rig.Ask(lead, "p1", "git push origin main"));
        Assert.True(rig.Ask(lead, "u1", "someprogram --do-things"));
        Assert.Null(rig.Answer(lead, "p1"));
        Assert.Null(rig.Answer(lead, "u1"));
        Assert.Equal(2, rig.Controller.Queue.Of(record.Id).Count);
    }

    [Fact]
    public void A_step_whose_line_cannot_be_written_is_refused()
    {
        using var rig = new DextraTaintRig();
        var faults = new List<string>();
        rig.Controller.Runner.Faulted += faults.Add;
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        rig.AuditFails = e => e.Tool == "approval";
        Assert.True(rig.Ask(lead, "r1", "git status"));
        Assert.False(rig.Answer(lead, "r1"));
        Assert.Empty(rig.Agents.Notes);
        Assert.Contains(faults, f => f.StartsWith("The activity log could not be written", StringComparison.Ordinal));
    }

    [Fact]
    public void A_grant_that_answers_alone_records_the_use_and_names_itself_in_the_log()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        var elsewhere = Path.Combine(Path.GetDirectoryName(rig.Project.Path)!, "notes", "today.txt");

        Assert.True(rig.Controller.OnApproval(lead, Write("g1", elsewhere)));
        var card = Assert.Single(rig.Controller.Queue.Of(record.Id));
        Assert.Equal("Asks: writing outside access folders", card.Why);
        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.ForThisMission));
        var grant = Assert.Single(rig.Controller.Grants);
        Assert.Empty(grant.UsedBy);

        Assert.True(rig.Controller.OnApproval(lead, Write("g2", elsewhere)));
        Assert.True(rig.Answer(lead, "g2"));
        Assert.Empty(rig.Controller.Queue.Of(record.Id));
        var line = Assert.Single(rig.Logged(), a => a.Tool == "approval" && a.Result == "allowed: Grant");
        Assert.Equal(grant.Id.ToString("D"), line.AllowedBy);
        Assert.Equal(["approval:g2"], Assert.Single(rig.Controller.Grants).UsedBy);
        Assert.StartsWith("Dextra allowed this for you: Your grant.", Assert.Single(rig.Agents.Notes).Text, StringComparison.Ordinal);
    }

    private static ApprovalRequest Write(string id, string path) =>
        new(id, ApprovalRequest.Kind.FileChange, path, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));
}
