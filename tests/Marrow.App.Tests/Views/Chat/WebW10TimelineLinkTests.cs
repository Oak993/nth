using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// PORT-ONLY: web surfaces W10, a link clicked in a chat (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11; ED-NEW-web-w10-2). On a real timeline: the first plain click asks where to open (Ask is the default) and a Ctrl click asks with Control remembered for
// that link; Visus takes the link into the chat's own workspace and Ctrl sends it to the default browser (and the other way round); a local path goes to the local-file opener; an artifact's chip still opens Tabula; the Open button of a Hydra
// report follows the same hook.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class WebW10TimelineLinkTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");

    private const string Answer = "Read [the docs](https://thread-timeline-docs.invalid/docs), open [the readme](C:/fake/readme.md) or [the pricing chip](nth-artifact://pricing) here.";

    private static Rect CharacterRect(ParagraphTextPresenter block, string text)
    {
        var at = block.Text.String.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, "no run holds " + text);
        return block.RangeRects(at + 1, 1)[0];
    }

    private static void Press(TimelineMount mount, Control host, Point at, RawInputModifiers modifiers)
    {
        var point = host.TranslatePoint(at, mount.Window)!.Value;
        mount.Window.MouseDown(point, MouseButton.Left, modifiers);
        mount.Window.MouseUp(point, MouseButton.Left, modifiers);
        mount.Advance(50);
    }

    private static (LinkTextView View, ParagraphTextPresenter Block) Prose(TimelineMount mount)
    {
        var views = LogicalQuery.SelfAndDescendants<LinkTextView>(mount.View).ToList();
        Assert.True(views.Count == 1, views.Count + " link text views");
        return (views[0], Assert.IsType<ParagraphTextPresenter>(views[0].Child));
    }

    private void Run(Action<TimelineMount, ParagraphTextPresenter, ChatLinkSinks> body) => ThreadTimelineRig.Run(headless, mount =>
    {
        using var sinks = new ChatLinkSinks();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = _ => throw new InvalidOperationException("a link reached the shell without going through the chat's route");
        try
        {
            mount.SetEntries([User("prompt", Turn, "where"), Reply("answer", Turn, Answer), End("end", Turn)]);
            mount.Settle();
            body(mount, Prose(mount).Block, sinks);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    });

    [Fact]
    public void The_first_plain_click_asks_and_opens_nothing_and_a_Ctrl_click_asks_with_Control_remembered() => Run((mount, block, sinks) =>
    {
        Press(mount, block, CharacterRect(block, "the docs").Center, RawInputModifiers.None);
        var first = Assert.Single(sinks.Questions);
        Assert.False(first.Choice.Control);
        Assert.Equal("https://thread-timeline-docs.invalid/docs", first.Choice.Url.AbsoluteString);
        Assert.Empty(sinks.Opened);
        Assert.NotNull(first.Anchor);
        first.Choice.Cancel();

        Press(mount, block, CharacterRect(block, "the docs").Center, RawInputModifiers.Control);
        var second = sinks.Questions[1];
        Assert.True(second.Choice.Control);
        second.Choice.Choose(visus: true); // Control inverts this one link: Visus asked for, the default browser gets it
        Assert.Equal(["external:https://thread-timeline-docs.invalid/docs"], sinks.Opened);
        Assert.Equal(ChatLinkTarget.Visus, mount.Model.Settings.Web.ChatLinks);
    });

    [Fact]
    public void With_Visus_chosen_a_plain_click_opens_it_for_this_chat_and_Ctrl_sends_it_to_the_default_browser() => Run((mount, block, sinks) =>
    {
        mount.Model.Settings.Web.ChatLinks = ChatLinkTarget.Visus;
        Press(mount, block, CharacterRect(block, "the docs").Center, RawInputModifiers.None);
        Assert.Equal(["visus:https://thread-timeline-docs.invalid/docs"], sinks.Opened);
        Assert.Same(mount.Runtime, Assert.Single(sinks.VisusRuntimes));
        Press(mount, block, CharacterRect(block, "the docs").Center, RawInputModifiers.Control);
        Assert.Equal(["visus:https://thread-timeline-docs.invalid/docs", "external:https://thread-timeline-docs.invalid/docs"], sinks.Opened);
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void With_the_default_browser_chosen_Ctrl_takes_the_link_into_Visus() => Run((mount, block, sinks) =>
    {
        mount.Model.Settings.Web.ChatLinks = ChatLinkTarget.DefaultBrowser;
        Press(mount, block, CharacterRect(block, "the docs").Center, RawInputModifiers.None);
        Press(mount, block, CharacterRect(block, "the docs").Center, RawInputModifiers.Control);
        Assert.Equal(["external:https://thread-timeline-docs.invalid/docs", "visus:https://thread-timeline-docs.invalid/docs"], sinks.Opened);
    });

    [Fact]
    public void A_local_path_in_the_answer_goes_to_the_local_file_opener_whatever_the_choice_and_Control() => Run((mount, block, sinks) =>
    {
        mount.Model.Settings.Web.ChatLinks = ChatLinkTarget.Visus;
        Press(mount, block, CharacterRect(block, "the readme").Center, RawInputModifiers.None);
        Press(mount, block, CharacterRect(block, "the readme").Center, RawInputModifiers.Control);
        Assert.Equal(["local:C:/fake/readme.md", "local:C:/fake/readme.md"], sinks.Opened);
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void An_artifacts_chip_still_opens_Tabula_and_never_the_system_or_a_question() => Run((mount, block, sinks) =>
    {
        var requests = new List<(Guid, string)>();
        var host = new ArtifactHost(null, null);
        host.OpenRequested += (thread, id) => requests.Add((thread, id));
        using var scope = ArtifactHost.Shared.Override(host);
        Press(mount, block, CharacterRect(block, "the pricing chip").Center, RawInputModifiers.None);
        Assert.Equal([(mount.Runtime.ThreadID, "pricing")], requests);
        Assert.Empty(sinks.Opened);
        Assert.Empty(sinks.Questions);
    });

    [Fact]
    public void The_Open_button_of_a_Hydra_report_follows_the_chats_hook_and_the_system_when_there_is_none() => headless.OnUiThread(() =>
    {
        var link = SwiftURL.Create("https://gitlab.example.org/g/p/-/merge_requests/7")!;
        var heard = new List<(string, KeyModifiers)>();
        var opened = new List<string>();
        using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });

        var inChat = new Border { Child = HydraReportRow.OpenLink(link) };
        MarkdownView.SetChatLinkAction(inChat, (url, modifiers) => { heard.Add((url.AbsoluteString, modifiers)); return true; });
        var window = new Avalonia.Controls.Window { Width = 300, Height = 100, Content = inChat };
        window.Show();
        try
        {
            var button = (Button)inChat.Child!;
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([(link.AbsoluteString, KeyModifiers.None)], heard);
            Assert.Empty(opened);
        }
        finally
        {
            window.Close();
        }

        var outside = new Border { Child = HydraReportRow.OpenLink(link) };
        var other = new Avalonia.Controls.Window { Width = 300, Height = 100, Content = outside };
        other.Show();
        try
        {
            ((Button)outside.Child!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([link.AbsoluteString], opened);
        }
        finally
        {
            other.Close();
        }
    });
}
