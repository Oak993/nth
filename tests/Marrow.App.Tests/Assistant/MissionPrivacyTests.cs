using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.1 "Personal data out", 12.4 "Privacy shield on the way out" and "the first message to
// a new third party is always confirmed"; ED-NEW-dextra-as7-4, ED-NEW-dextra-as7-11): personal data on a mission's
// outward card is marked where it is and noted; the card, and the chat's own Allow, take no approval until every
// recipient is on the owner's list; a grant does not carry personal data past it; and the first step to a third party
// the owner has not confirmed asks even under a grant.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-11")]
public sealed class MissionPrivacyTests
{
    private const string Event = "mcp__calendar__create_event";

    [Fact]
    public void Personal_data_to_an_unlisted_attendee_is_highlighted_and_takes_no_approval_until_he_is_listed()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        const string Detail = "{\"attendees\":[\"raj@partner.example\"],\"notes\":\"Call Jane on +1 415 555 0134\"}";

        Assert.True(rig.Controller.OnApproval(lead, Tool("e1", Detail)));
        var card = Assert.Single(rig.Controller.Queue.Of(record.Id));
        Assert.Equal(ActionClass.ConnectorWrite, card.Class);
        var phone = Assert.Single(card.Highlights, h => h.Kind == PrivacyKind.Phone);
        Assert.Equal("Detail", phone.Input);
        Assert.Contains("415 555 0134", Detail.Substring(phone.Start, phone.Length), StringComparison.Ordinal);
        Assert.StartsWith("Personal data: ", card.Note, StringComparison.Ordinal);
        Assert.Contains("raj@partner.example", card.Note, StringComparison.Ordinal);

        var refused = rig.Controller.Queue.Answer(card.Id, DecisionAnswer.AllowOnce);
        Assert.NotNull(refused);
        Assert.Contains("raj@partner.example", refused, StringComparison.Ordinal);
        Assert.Null(rig.Answer(lead, "e1"));
        Assert.Equal(DecisionState.Open, Assert.Single(rig.Controller.Queue.Of(record.Id)).State);

        // Deny still works, and once the owner lists the attendee the same card takes his Allow.
        rig.Recipients.Add("raj@partner.example");
        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.True(rig.Answer(lead, "e1"));
        Assert.Equal(["raj@partner.example"], rig.Controller.ConfirmedRecipients());
    }

    [Fact]
    public void The_chats_own_allow_is_held_to_the_same_rule()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        var request = Tool("e1", "{\"attendees\":[\"raj@partner.example\"],\"notes\":\"Passport X1234567\"}");
        Assert.True(rig.Controller.OnApproval(lead, request));

        var chosen = rig.Controller.Runner.OnChatAnswer(lead, request, request.Options[0]);
        Assert.Equal("deny", chosen.Id);
        Assert.StartsWith("Dextra declined this for you: ", Assert.Single(rig.Agents.Notes).Text, StringComparison.Ordinal);
        Assert.Contains(rig.Logged(), a => a.Tool == "approval" && (a.Result ?? "").StartsWith("refused: ", StringComparison.Ordinal));
        Assert.Empty(rig.Controller.Queue.Of(record.Id));
        Assert.Empty(rig.Controller.ConfirmedRecipients());
        Assert.False(rig.Controller.Runner.TaintOf(record.Id, lead).IsTainted);
    }

    [Fact]
    public void A_grant_carries_no_personal_data_past_the_list_and_no_first_step_to_someone_new()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        // Each step comes from its own head: an agent that used a connector is tainted, and its next write asks anyway.
        var heads = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var (head, index) in heads.Select((h, i) => (h, i)))
        {
            rig.Controller.Runner.HeadAdded(lead, head, "Head " + index);
            rig.Chat(head, record, "Head " + index);
        }

        // The owner grants calendar writes for this mission on the first card, which confirms its attendee.
        Assert.True(rig.Controller.OnApproval(lead, Tool("e1", "{\"attendees\":[\"new@z.example\"],\"notes\":\"Lunch\"}")));
        var first = Assert.Single(rig.Controller.Queue.Of(record.Id));
        Assert.Equal("Needs a grant", first.Rule);
        Assert.Null(rig.Controller.Queue.Answer(first.Id, DecisionAnswer.ForThisMission));
        Assert.Single(rig.Controller.Grants);
        Assert.Equal(["new@z.example"], rig.Controller.ConfirmedRecipients());

        // Under the grant, personal data to a confirmed but unlisted attendee still asks.
        Assert.True(rig.Controller.OnApproval(heads[0], Tool("e2", "{\"attendees\":[\"new@z.example\"],\"notes\":\"Call +1 415 555 0134\"}")));
        Assert.Null(rig.Answer(heads[0], "e2"));
        var personal = Assert.Single(rig.Controller.Queue.Of(record.Id));
        Assert.Equal("Personal data: only to your listed recipients", personal.Rule);
        Assert.NotEmpty(personal.Highlights);

        // Under the grant, the first step to someone nobody confirmed asks; once allowed, he is confirmed.
        Assert.True(rig.Controller.OnApproval(heads[1], Tool("e3", "{\"attendees\":[\"other@w.example\"],\"notes\":\"Lunch\"}")));
        Assert.Null(rig.Answer(heads[1], "e3"));
        var fresh = rig.Controller.Queue.Of(record.Id).Single(c => c.RequestId == "e3");
        Assert.Equal("First message to someone new: always confirmed", fresh.Rule);
        Assert.Contains("other@w.example", fresh.Why, StringComparison.Ordinal);
        Assert.Null(rig.Controller.Queue.Answer(fresh.Id, DecisionAnswer.AllowOnce));
        Assert.True(rig.Answer(heads[1], "e3"));
        Assert.Contains("other@w.example", rig.Controller.ConfirmedRecipients());

        // The next step to him runs alone under the grant.
        Assert.True(rig.Controller.OnApproval(heads[2], Tool("e4", "{\"attendees\":[\"other@w.example\"],\"notes\":\"Lunch again\"}")));
        Assert.True(rig.Answer(heads[2], "e4"));
        Assert.Equal(["approval:e4"], Assert.Single(rig.Controller.Grants).UsedBy);
    }

    private static ApprovalRequest Tool(string id, string detail) =>
        new(id, ApprovalRequest.Kind.Tool, Event, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]), detail);
}
