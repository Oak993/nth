using System.Text;
using System.Xml.Linq;
using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "ToastRequest kind AssistantDecision, target decision id", "Toast buttons"; the canvas's 48 px notification;
// ED-NEW-dextra-as9-4, -5): a decision notice as the Windows toast. Its request is of kind AssistantDecision and names the target card; the toast shows
// the title, the body and "Dextra", Armilla's picture and one protocol button for each action, each carrying the card, the action and the nonce of
// this toast. A chat toast is what it was: text alone.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-4")]
public sealed class DecisionToastRequestTests
{
    private static readonly ToastBuildIdentity Release = new("nth.App", "nth-release");
    private static readonly string Logo = Path.Combine(Path.GetTempPath(), "dextra-needs-you-dark.png");

    private static DecisionNotice Notice(IEnumerable<NoticeAction>? actions = null, string title = "Store needs you", string body = "Run npm test?", int cards = 1)
    {
        var ids = Enumerable.Range(0, cards).Select(i => Guid.Parse($"a0000000-0000-4000-8000-00000000000{i + 1}")).ToList();
        return new DecisionNotice(ids[0], new ValueList<Guid>(ids), title, body,
            new ValueList<NoticeAction>(actions ?? [NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open])) { Nonce = "0123456789abcdef0123456789abcdef" };
    }

    private static UNNotification Decoded(string arguments) => ToastActivation.Decode(Release, arguments)!;

    [Fact]
    public void The_request_is_of_the_decision_kind_names_its_target_and_is_tagged_by_it()
    {
        var notice = Notice();
        var request = DecisionToastRequests.Build(notice, Logo);
        Assert.Equal(ToastKind.AssistantDecision, request.Kind);
        Assert.Equal(notice.Target, request.Decision);
        Assert.Equal("dextra-decision-a0000000000040008000000000000001", request.Identifier);
        Assert.True(request.Identifier.Length <= 64);
        Assert.Equal("Store needs you", request.Title);
        Assert.Equal("Run npm test?", request.Body);
        Assert.True(request.Sound);
        Assert.Equal("Dextra", request.Attribution);
        Assert.Equal(Logo, request.Logo);
    }

    [Fact]
    public void A_reversible_toast_has_three_buttons_and_its_body_click_is_the_last_of_them()
    {
        var request = DecisionToastRequests.Build(Notice(), Logo);
        Assert.Equal(["Allow once", "Deny", "Open"], request.Buttons.Select(b => b.Content));
        Assert.Equal(["allowOnce", "deny", "open"], request.Buttons.Select(b => (string)b.UserInfo[DecisionToastKeys.Action]));
        Assert.Equal("open", request.UserInfo[DecisionToastKeys.Action]);
        Assert.All(request.Buttons, b =>
        {
            Assert.Equal("a0000000-0000-4000-8000-000000000001", b.UserInfo[DecisionToastKeys.Decision]);
            Assert.Equal("0123456789abcdef0123456789abcdef", b.UserInfo[DecisionToastKeys.Nonce]);
        });
    }

    [Fact]
    public void Every_other_toast_has_one_button_review_and_the_body_click_is_review_too()
    {
        var request = DecisionToastRequests.Build(Notice([NoticeAction.Review]), Logo);
        Assert.Equal("Review", Assert.Single(request.Buttons).Content);
        Assert.Equal("review", request.UserInfo[DecisionToastKeys.Action]);
        Assert.Equal(request.UserInfo[DecisionToastKeys.Action], request.Buttons[0].UserInfo[DecisionToastKeys.Action]);
    }

    [Fact]
    public void The_toast_xml_has_the_picture_first_then_the_title_the_body_and_dextra_then_one_protocol_action_for_each_button()
    {
        var request = DecisionToastRequests.Build(Notice(), Logo);
        var xml = XDocument.Parse(WindowsToastCenter.Payload(request, Release));
        var toast = xml.Root!;
        Assert.Equal("protocol", (string?)toast.Attribute("activationType"));
        var binding = toast.Element("visual")!.Element("binding")!;
        Assert.Equal("ToastGeneric", (string?)binding.Attribute("template"));
        var children = binding.Elements().ToList();
        Assert.Equal(["image", "text", "text", "text"], children.Select(c => c.Name.LocalName));
        Assert.Equal("appLogoOverride", (string?)children[0].Attribute("placement"));
        Assert.Equal(new Uri(Logo).AbsoluteUri, (string?)children[0].Attribute("src"));
        Assert.Equal(["Store needs you", "Run npm test?", "Dextra"], children.Skip(1).Select(c => c.Value));
        Assert.Equal("attribution", (string?)children[3].Attribute("placement"));
        Assert.Equal(["visual", "actions", "audio"], toast.Elements().Select(e => e.Name.LocalName));
        var actions = toast.Element("actions")!.Elements("action").ToList();
        Assert.Equal(["Allow once", "Deny", "Open"], actions.Select(a => (string?)a.Attribute("content")));
        Assert.All(actions, a => Assert.Equal("protocol", (string?)a.Attribute("activationType")));
        Assert.Equal(["allowOnce", "deny", "open"], actions.Select(a => Decoded((string)a.Attribute("arguments")!).UserInfo[DecisionToastKeys.Action]));
        Assert.All(actions, a =>
        {
            var info = Decoded((string)a.Attribute("arguments")!).UserInfo;
            Assert.Equal("a0000000-0000-4000-8000-000000000001", info[DecisionToastKeys.Decision]);
            Assert.Equal("0123456789abcdef0123456789abcdef", info[DecisionToastKeys.Nonce]);
        });
        Assert.Equal("open", Decoded((string)toast.Attribute("launch")!).UserInfo[DecisionToastKeys.Action]);
        Assert.Equal("ms-winsoundevent:Notification.Default", (string?)toast.Element("audio")!.Attribute("src"));
    }

    [Fact]
    public void Without_a_picture_or_with_a_path_that_is_not_absolute_the_toast_is_text_and_buttons_alone()
    {
        foreach (var logo in new string?[] { null, "", @"relative\mark.png", "mark.png" })
        {
            var xml = XDocument.Parse(WindowsToastCenter.Payload(DecisionToastRequests.Build(Notice(), logo), Release));
            Assert.Empty(xml.Descendants("image"));
            Assert.Equal(3, xml.Descendants("action").Count());
        }
    }

    [Fact]
    public void Markup_in_the_title_the_body_or_a_button_is_escaped_never_interpreted()
    {
        var request = DecisionToastRequests.Build(Notice(title: "<b>Store</b> & \"co\" needs you", body: "</text><action content=\"Do it\"/>"), Logo);
        var xml = XDocument.Parse(WindowsToastCenter.Payload(request, Release));
        Assert.Equal(3, xml.Descendants("action").Count());
        Assert.Equal("<b>Store</b> & \"co\" needs you", xml.Descendants("text").First().Value);
        Assert.Equal("</text><action content=\"Do it\"/>", xml.Descendants("text").Skip(1).First().Value);
    }

    [Fact]
    public void The_worst_case_toast_stays_inside_the_five_kilobytes_windows_takes()
    {
        var longBody = new string('x', DecisionNotices.BodyChars);
        var title = new string('N', DecisionNotices.WhoChars) + " needs you";
        var identity = Release with { DevRoot = @"C:\Users\someone-with-a-long-name\AppData\Local\Dev Data\nth-dev-root\and-more" };
        var xml = WindowsToastCenter.Payload(DecisionToastRequests.Build(Notice(title: title, body: longBody), @"C:\Users\someone-with-a-long-name\AppData\Local\nth\dextra-needs-you-dark.png"), identity);
        Assert.True(Encoding.UTF8.GetByteCount(xml) < 4096, $"the payload is {Encoding.UTF8.GetByteCount(xml)} bytes");
    }

    [Fact]
    public void A_chat_toast_is_what_it_was_text_alone_with_no_picture_no_buttons_and_no_extra_line()
    {
        var chat = new ToastRequest("thread", "Title", "Body", true, new Dictionary<string, object> { ["threadID"] = "00112233-4455-6677-8899-AABBCCDDEEFF" });
        Assert.Equal(ToastKind.Thread, chat.Kind);
        Assert.Null(chat.Decision);
        Assert.Empty(chat.Buttons);
        var xml = XDocument.Parse(WindowsToastCenter.Payload(chat, Release));
        Assert.Equal(["Title", "Body"], xml.Descendants("text").Select(t => t.Value));
        Assert.Empty(xml.Descendants("image"));
        Assert.Empty(xml.Descendants("actions"));
        Assert.Equal(["visual", "audio"], xml.Root!.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void The_toast_carries_no_input_of_the_card_only_its_id_the_action_and_the_nonce()
    {
        var request = DecisionToastRequests.Build(Notice(), Logo);
        Assert.Equal([DecisionToastKeys.Action, DecisionToastKeys.Decision, DecisionToastKeys.Nonce], request.UserInfo.Keys.Order());
        Assert.All(request.Buttons, b => Assert.Equal([DecisionToastKeys.Action, DecisionToastKeys.Decision, DecisionToastKeys.Nonce], b.UserInfo.Keys.Order()));
    }
}
