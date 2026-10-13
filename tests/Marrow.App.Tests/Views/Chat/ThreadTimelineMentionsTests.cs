using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.Services.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// A head's name in the prose (DroppyCode/App/Views/Chat/ThreadTimeline.swift:117-155,209-217, B3 52bc792), on a real timeline over a lead with heads under it.
[Collection("Headless")]
public sealed class ThreadTimelineMentionsTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");

    private static HydraHeadInfo Head(long index) => TimelineRowsFinTeam.Head(index, HydraHeadInfo.Status.Completed);

    // The rectangle of the second character of `text` in the paragraph (a mention's name: its glyph is one character before it, a space between).
    private static Rect CharacterRect(ParagraphTextPresenter block, string text)
    {
        var at = block.Text.String.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, "no run holds " + text);
        return block.RangeRects(at + 1, 1)[0];
    }

    private static void Press(TimelineMount mount, Control host, Point at)
    {
        var point = host.TranslatePoint(at, mount.Window)!.Value;
        mount.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); mount.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        mount.Advance(50);
    }

    private static (LinkTextView View, ParagraphTextPresenter Block) Prose(TimelineMount mount)
    {
        var views = LogicalQuery.SelfAndDescendants<LinkTextView>(mount.View).ToList();
        Assert.True(views.Count == 1, $"{views.Count} link text views; blocks [{string.Join(", ", mount.Blocks().Select(block => block.Id + ":" + block.Block.GetType().Name))}]; "
            + string.Join(", ", LogicalQuery.SelfAndDescendants<Control>(mount.View).GroupBy(control => control.GetType().Name).Select(group => group.Key + "x" + group.Count())));
        var view = views[0]; return (view, Assert.IsType<ParagraphTextPresenter>(view.Child));
    }

    // One object-replacement character per drawn glyph.
    private static int Glyphs(ParagraphTextPresenter block) => block.Text.String.Count(character => character == '￼');

    [Fact]
    [Trait("Req", "PR-CHAT-156")] // ThreadTimeline.swift:123-131,147-155,209-217,1784-1795; windows_path: oldest first so a reused name lands on the newest head
    public void A_heads_name_in_the_prose_is_drawn_as_that_head_after_it_left_the_panel_opens_its_chat_and_other_links_keep_the_systems_handling()
    {
        var hank = HydraRoster.Persona(0).Name; var walter = HydraRoster.Persona(1).Name; var next = HydraRoster.Persona(2).Name;
        // Two heads under the lead by the same persona (the second sent out later) and one by another: all finished, none in the panel.
        ThreadTimelineRig.RunWithTeam(headless, mount =>
        {
            var heads = mount.Model.Children(mount.Runtime.ThreadID).OrderBy(thread => thread.Hydra!.Index).ThenBy(thread => thread.CreatedAt).ToList();
            Assert.Equal(3, heads.Count); Assert.All(heads, thread => Assert.False(thread.IsInPanel));
            var older = heads[0]; var newer = heads[1]; var other = heads[2];
            mount.Model.UpdateThread(older.Id, thread => thread with { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(10) });
            mount.Model.UpdateThread(newer.Id, thread => thread with { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(20) });
            mount.Model.Settings.HydraEnabled = false;
            // The link's host is a private one (.invalid), so the icon fetch of a public host (a real request) is never started.
            mount.SetEntries(new[] { User("prompt", Turn, "who did what"), Reply("answer", Turn, $"{hank} finished the check and {walter} is next, then {next}. See [the docs](https://thread-timeline-docs.invalid/docs)."), End("end", Turn) });
            mount.Settle();
            var (view, block) = Prose(mount);
            // :123-131 every head the chat ever sent out is drawn as that head, panel or not: a glyph before each name.
            Assert.Equal(2, Glyphs(block));
            Assert.NotNull(view.HeadMention(CharacterRect(block, hank).Center)); Assert.NotNull(view.HeadMention(CharacterRect(block, walter).Center));
            // :213 `next` is a roster name with no head sent out: it stays plain text while Hydra is off.
            // The seam the link open goes through is recorded: nothing reaches the system for a head.
            var previous = ShellWorkspace.OpenWithDefaultApp; var opened = new List<string>(); ShellWorkspace.OpenWithDefaultApp = opened.Add;
            var previousHook = InlineText.OpenUrl; var hooked = new List<SwiftURL>(); InlineText.OpenUrl = hooked.Add;
            try
            {
                // Selecting a head's chat: a reused name points at the newest head (:128-131, oldest first).
                mount.Model.SelectedThreadID = null;
                Press(mount, block, CharacterRect(block, hank).Center);
                Assert.Equal(newer.Id, mount.Model.SelectedThreadID); Assert.Empty(opened); Assert.Empty(hooked); // handled: nothing reaches the system's open
                Press(mount, block, CharacterRect(block, walter).Center);
                Assert.Equal(other.Id, mount.Model.SelectedThreadID); Assert.Empty(opened);
                // Every other link keeps the system's handling (:216 `.systemAction`): the chat selected stays as it was.
                // PORT-ONLY (web surfaces W10, DESIGN 5.11, ED-NEW-web-w10-2): "Open links from chats in" is Ask the first time, so the first click asks and opens nothing; once the reader answers Default
                // browser the link goes to the system exactly as it always did, and the next click goes there at once.
                var questions = new List<Marrow.App.Port.Web.ChatLinkChoice>();
                var previousQuestion = Marrow.App.Port.Web.ChatLinks.ShowChoice;
                Marrow.App.Port.Web.ChatLinks.ShowChoice = (choice, _) => questions.Add(choice);
                try
                {
                    mount.Model.SelectedThreadID = null;
                    Press(mount, block, CharacterRect(block, "the docs").Center);
                    Assert.Null(mount.Model.SelectedThreadID); Assert.Empty(opened); Assert.Empty(hooked);
                    var asked = Assert.Single(questions);
                    asked.Choose(visus: false);
                    Assert.Equal(new[] { "https://thread-timeline-docs.invalid/docs" }, opened);
                    Assert.Equal(Marrow.Core.Web.ChatLinkTarget.DefaultBrowser, mount.Model.Settings.Web.ChatLinks);
                    opened.Clear();
                    Press(mount, block, CharacterRect(block, "the docs").Center);
                    Assert.Null(mount.Model.SelectedThreadID); Assert.Equal(new[] { "https://thread-timeline-docs.invalid/docs" }, opened); Assert.Single(questions);
                }
                finally { Marrow.App.Port.Web.ChatLinks.ShowChoice = previousQuestion; }
            }
            finally { ShellWorkspace.OpenWithDefaultApp = previous; InlineText.OpenUrl = previousHook; }
            // :120-127 with Hydra on, the whole roster after them: a name is drawn as its head from the first frame, before the spawn lands.
            mount.Model.Settings.HydraEnabled = true; mount.Advance(200);
            Assert.Equal(3, Glyphs(Prose(mount).Block));
        }, [Head(0), Head(0), Head(1)]);
    }
}
