// PORT-ONLY: Tactus, the smallest seam into a chat (ED-NEW-pcctl-39). On a real offline chat runtime: Open chat puts the
// card's draft in the chat's composer, alone when the composer is empty and under the user's own text otherwise (never
// over it, never twice, the attachments kept); Send to chat queues the draft as the user's next message behind the running
// turn and leaves the composer as it was; nothing goes to a chat that is gone or reverting, and blank drafts go nowhere.
using Marrow.App.Port.PcControl;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-39")]
public sealed class TactusChatInboxTests(HeadlessSession headless)
{
    [Fact]
    public void Carried_puts_the_draft_alone_in_an_empty_composer_and_under_the_user_s_text_but_never_twice()
    {
        Assert.Equal("the second one", TactusChatInbox.Carried("", "the second one"));
        Assert.Equal("the second one", TactusChatInbox.Carried("  \n ", "the second one"));
        Assert.Equal("my notes\n\nthe second one", TactusChatInbox.Carried("my notes \n", "the second one"));
        Assert.Equal("my notes\n\nthe second one", TactusChatInbox.Carried("my notes\n\nthe second one", "the second one"));
    }

    [Fact]
    public void Open_chat_and_Send_to_chat_reach_the_chat_s_composer_and_its_queue_and_nothing_reaches_a_chat_gone_or_reverting() => headless.OnUiThread(() =>
    {
        var (app, runtime, store) = FollowUpQueueTabRig.Make();
        using var storeDisposable = store;
        var thread = runtime.ThreadID.ToString();
        var inbox = new TactusChatInbox(id => id == runtime.ThreadID ? runtime : null);

        store.Main.OnMain(() =>
        {
            // Open chat: into the empty composer, then under what the user types there, not twice
            Assert.True(inbox.Carry(thread, " the second one \n"));
            Assert.Equal("the second one", runtime.Draft.Text);
            var attachment = new ValueList<Attachment>(new[] { FollowUpQueueTabRig.File("notes.txt") });
            runtime.Draft = runtime.Draft with { Text = "my notes", Attachments = attachment };
            Assert.True(inbox.Carry(thread, "the second one"));
            Assert.True(inbox.Carry(thread, "the second one"));
            Assert.Equal("my notes\n\nthe second one", runtime.Draft.Text);
            Assert.Equal(attachment, runtime.Draft.Attachments);
            Assert.False(inbox.Carry(thread, "  "));
            Assert.False(inbox.Carry(Guid.NewGuid().ToString(), "the second one"));
            Assert.False(inbox.Carry("chat-a", "the second one"));

            // Send to chat while a turn runs: queued behind it as the user's message, by the chat's own queue rules (a stored
            // follow-up here, not a Hydra head dispatched from the queue); the composer is left as it was
            app.Settings.HydraQueueHeads = false;
            FollowUpQueueTabRig.SetPhase(runtime, RuntimePhase.Running);
            Assert.True(inbox.Send(thread, "half an answer"));
            Assert.Equal("half an answer", Assert.Single(runtime.FollowUps).Text);
            Assert.Equal("my notes\n\nthe second one", runtime.Draft.Text);
            Assert.False(inbox.Send(thread, " \n"));
            Assert.False(inbox.Send(Guid.NewGuid().ToString(), "half an answer"));

            // a chat reverting takes nothing
            typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsReverting))!.SetValue(runtime, true);
            Assert.False(inbox.Send(thread, "my own words"));
            Assert.Single(runtime.FollowUps);
        });
    });
}
