using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :902-930 (a merge note: the settle note and the word sent
// out about it). The toast centre and the foreground test are the app's own seams; no real notification is shown.
public sealed class AppModelHydraMergeAlertTests
{
    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, true)]
    [Trait("Req", "PR-HYDRAMERGE-085")] // AppModel+HydraMerge.swift:923-929
    [Trait("Req", "UI-HYDRAMERGE-032")] // AppModel+HydraMerge.swift:923-929 (title: the chat's, body: the note's)
    [Trait("Req", "UI-HYDRAMERGE-033")] // AppModel+HydraMerge.swift:925-926 (unread and the badge)
    public void A_merge_note_marks_the_chat_unread_badges_and_notifies_only_when_the_user_is_not_looking_at_it(bool active, bool selected, bool notifyOn, bool alerts)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Chat title", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var center = new RecordingAppToastCenter();
        using var toasts = ToastNotifications.Center.Override(center);
        using var foreground = NSApplication.IsActive.Override(() => active);
        f.H.Main.OnMain(() =>
        {
            f.Model.Settings.NotifyWhenFinished = notifyOn;
            f.Model.SelectedThreadID = selected ? lead.Id : null;
        });
        Assert.False(f.Model.Thread(lead.Id)!.HasUnread);

        // A merge that fails for want of an origin says so in a note.
        f.Run(lead);
        f.H.Main.Loop.RunUntilIdle();

        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        Assert.Equal("Hydra did not merge: no remote.", HydraMergeSpec.Title(note));
        Assert.Equal(alerts, f.Model.Thread(lead.Id)!.HasUnread);
        // The badge count is the unread bookkeeping's own: one chat when the note alerted, none when the user was looking.
        var count = (long)typeof(AppModel).GetField("dockUnreadCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Model)!;
        Assert.Equal(alerts ? 1 : 0, count);
        if (alerts && notifyOn)
        {
            // The chat's title is the notification's title and the note's title is its body (swapped from what a reader expects).
            var request = Assert.Single(center.Requests);
            Assert.Equal("Chat title", request.Title);
            Assert.Equal("Hydra did not merge: no remote.", request.Body);
            Assert.Equal(SwiftUUID.UuidString(lead.Id), request.Identifier);
            Assert.Equal(SwiftUUID.UuidString(lead.Id), request.UserInfo["threadID"]);
        }
        else Assert.Empty(center.Requests);
    }

    [Fact]
    [Trait("Req", "UI-CHAT-084")] // AppModel+HydraMerge.swift:829-833, :860-867; TimelineRows.swift:802-807
    [Trait("Req", "UI-HYDRAMERGE-036")] // AppModel+HydraMerge.swift:829-833
    public void When_every_share_is_already_on_its_branch_the_settle_note_lands_under_the_pills_id_with_the_ruled_words()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        // What the turn reported is committed already (a.txt is unchanged from HEAD): nothing to send.
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        string? pill = null;

        f.Run(lead, call => pill ??= f.Runtime(lead).HydraMergeNoteID);

        var record = Assert.Single(HydraMergeSpec.Records(f, lead));
        Assert.Equal(HydraMergeRecord.Outcome.Settled, record.HydraMergeRecordOutcome);
        Assert.Null(record.Project);
        Assert.Null(record.Label);
        Assert.Null(record.Url);
        Assert.Equal(0, record.Files);
        Assert.Null(record.Detail);
        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        // The stored title ends with a full stop; the pill shows it without that one stop. The body leads with no URL.
        Assert.Equal("Hydra has no files left to merge.", HydraMergeSpec.Title(note));
        Assert.Equal("Hydra has no files left to merge", HydraReportRow.WithoutTrailingStop(HydraMergeSpec.Title(note)));
        Assert.Equal(["The checkout has no remaining changes from this job to send. No new merge request was needed."], HydraMergeSpec.BodyLines(note));
        // It lands under the id the merging pill was drawn with: one timeline row from the first stage to the outcome.
        Assert.Equal(pill, note.ID);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "UI-CHAT-084")] // AppModel+HydraMerge.swift:902-906 (not posted while the lead still has unmerged files)
    [Trait("Req", "UI-HYDRAMERGE-036")] // AppModel+HydraMerge.swift:902-906
    public void The_settle_note_is_not_posted_while_the_lead_still_has_unmerged_files(bool filesLeft)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", filesLeft ? ["a.txt"] : []);
        f.Start(lead);

        f.H.Main.OnMain(() => typeof(AppModel).GetMethod("RecordHydraSettlement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Model, [lead.Id, f.Runtime(lead)]));

        if (filesLeft)
        {
            Assert.Empty(HydraMergeSpec.Records(f, lead));
            Assert.Empty(HydraMergeSpec.NoteEntries(f, lead));
        }
        else
        {
            Assert.Equal(HydraMergeRecord.Outcome.Settled, Assert.Single(HydraMergeSpec.Records(f, lead)).HydraMergeRecordOutcome);
            Assert.Equal("Hydra has no files left to merge.", HydraMergeSpec.Title(Assert.Single(HydraMergeSpec.NoteEntries(f, lead))));
        }
    }
}
