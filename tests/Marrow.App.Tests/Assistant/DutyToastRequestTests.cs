using System.Text;
using System.Xml.Linq;
using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in", "ToastRequest kind ..."; the canvas's 48 px notification; ED-NEW-dextra-as9-4, -7, -9): a duty's finding as
// the Windows toast. Its request is of kind AssistantDuty and names the mission; the toast shows the duty's name, one line of its report and "Dextra", Armilla's
// resting picture and one protocol button, Open. Nothing in it can answer anything: no card, no nonce. The Windows channel posts it through the same toast
// center and the same permission as every toast.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class DutyToastRequestTests
{
    private static readonly ToastBuildIdentity Release = new("nth.App", "nth-release");
    private static readonly string Logo = Path.Combine(Path.GetTempPath(), "dextra-news-dark.png");
    private static readonly Guid Mission = Guid.Parse("b0000000-0000-4000-8000-000000000001");

    private static DutyNotice Notice(string title = "Espresso price", string body = "The price fell to $640.", int runs = 1)
    {
        var ids = Enumerable.Range(0, runs).Select(i => Guid.Parse($"b0000000-0000-4000-8000-00000000000{i + 1}")).ToList();
        return new DutyNotice(ids[0], new ValueList<Guid>(ids), title, body);
    }

    private static UNNotification Decoded(string arguments) => ToastActivation.Decode(Release, arguments)!;

    [Fact]
    public void The_request_is_of_the_duty_kind_names_its_mission_and_is_tagged_by_it()
    {
        var request = DecisionToastRequests.BuildDuty(Notice(), Logo);
        Assert.Equal(ToastKind.AssistantDuty, request.Kind);
        Assert.Equal(Mission, request.Mission);
        Assert.Null(request.Decision);
        Assert.Equal("dextra-duty-b0000000000040008000000000000001", request.Identifier);
        Assert.True(request.Identifier.Length <= 64);
        Assert.Equal("Espresso price", request.Title);
        Assert.Equal("The price fell to $640.", request.Body);
        Assert.True(request.Sound);
        Assert.Equal("Dextra", request.Attribution);
        Assert.Equal(Logo, request.Logo);
    }

    [Fact]
    public void The_one_button_is_open_the_body_click_is_open_too_and_neither_carries_a_card_or_a_nonce()
    {
        var request = DecisionToastRequests.BuildDuty(Notice(), Logo);
        var button = Assert.Single(request.Buttons);
        Assert.Equal("Open", button.Content);
        foreach (var info in new[] { request.UserInfo, button.UserInfo })
        {
            Assert.Equal([DecisionToastKeys.Action, DecisionToastKeys.Mission], info.Keys.Order());
            Assert.Equal("b0000000-0000-4000-8000-000000000001", info[DecisionToastKeys.Mission]);
            Assert.Equal("open", info[DecisionToastKeys.Action]);
        }
    }

    [Fact]
    public void The_toast_xml_has_the_picture_first_then_the_name_the_line_and_dextra_then_one_protocol_action()
    {
        var xml = XDocument.Parse(WindowsToastCenter.Payload(DecisionToastRequests.BuildDuty(Notice(), Logo), Release));
        var toast = xml.Root!;
        Assert.Equal("protocol", (string?)toast.Attribute("activationType"));
        var children = toast.Element("visual")!.Element("binding")!.Elements().ToList();
        Assert.Equal(["image", "text", "text", "text"], children.Select(c => c.Name.LocalName));
        Assert.Equal(new Uri(Logo).AbsoluteUri, (string?)children[0].Attribute("src"));
        Assert.Equal(["Espresso price", "The price fell to $640.", "Dextra"], children.Skip(1).Select(c => c.Value));
        var action = Assert.Single(toast.Element("actions")!.Elements("action"));
        Assert.Equal("Open", (string?)action.Attribute("content"));
        Assert.Equal("protocol", (string?)action.Attribute("activationType"));
        var info = Decoded((string)action.Attribute("arguments")!).UserInfo;
        Assert.Equal("b0000000-0000-4000-8000-000000000001", info[DecisionToastKeys.Mission]);
        Assert.False(info.ContainsKey(DecisionToastKeys.Nonce));
        Assert.False(info.ContainsKey(DecisionToastKeys.Decision));
        Assert.Equal("b0000000-0000-4000-8000-000000000001", Decoded((string)toast.Attribute("launch")!).UserInfo[DecisionToastKeys.Mission]);
    }

    [Fact]
    public void Markup_in_the_name_or_the_line_is_escaped_never_interpreted_and_the_worst_case_fits()
    {
        var hostile = XDocument.Parse(WindowsToastCenter.Payload(DecisionToastRequests.BuildDuty(Notice("<b>Shop</b> & \"co\"", "</text><action content=\"Do it\"/>"), Logo), Release));
        Assert.Single(hostile.Descendants("action"));
        Assert.Equal("<b>Shop</b> & \"co\"", hostile.Descendants("text").First().Value);

        var identity = Release with { DevRoot = @"C:\Users\someone-with-a-long-name\AppData\Local\Dev Data\nth-dev-root\and-more" };
        var worst = WindowsToastCenter.Payload(DecisionToastRequests.BuildDuty(
            Notice(new string('N', DutyNotices.NameChars), new string('x', DecisionNotices.BodyChars)), @"C:\Users\someone-with-a-long-name\AppData\Local\nth\dextra-news-dark.png"), identity);
        Assert.True(Encoding.UTF8.GetByteCount(worst) < 4096, $"the payload is {Encoding.UTF8.GetByteCount(worst)} bytes");
    }

    [Fact]
    public void Without_a_picture_the_toast_is_text_and_its_button_alone()
    {
        var xml = XDocument.Parse(WindowsToastCenter.Payload(DecisionToastRequests.BuildDuty(Notice(), null), Release));
        Assert.Empty(xml.Descendants("image"));
        Assert.Single(xml.Descendants("action"));
    }

    // ---- the channel ----

    [Fact]
    public async Task The_windows_channel_hands_a_duty_notice_to_the_toast_center_as_a_duty_request_with_the_news_picture_and_asks_for_the_permission()
    {
        var center = new RecordingAppToastCenter();
        var permissions = 0;
        var channel = new WindowsNotificationChannel(logo: () => @"C:\nth\needs-you.png", requestPermission: () => permissions++, center: () => center, newsLogo: () => @"C:\nth\news.png");
        Assert.True(await channel.Send(Notice()));
        var request = Assert.Single(center.Requests);
        Assert.Equal(ToastKind.AssistantDuty, request.Kind);
        Assert.Equal(@"C:\nth\news.png", request.Logo);
        Assert.Equal(1, permissions);
    }

    [Fact]
    public async Task A_channel_with_no_news_picture_uses_the_one_it_has_and_a_canceled_send_posts_nothing()
    {
        var center = new RecordingAppToastCenter();
        var channel = new WindowsNotificationChannel(logo: () => @"C:\nth\mark.png", center: () => center);
        Assert.True(await channel.Send(Notice()));
        Assert.Equal(@"C:\nth\mark.png", center.Requests[0].Logo);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.Send(Notice(), cancel.Token));
        Assert.Single(center.Requests);
    }

    [Fact]
    public void The_real_center_posts_the_duty_toast_by_its_tag_and_the_toast_is_never_taken_back_by_the_channel()
    {
        using var runtime = new TestRuntime();
        var platform = new RecordingToastPlatform();
        var center = new WindowsToastCenter(platform, Release);
        center.InstallDelegate(new AppDelegate());
        using var binding = ToastNotifications.Center.Override(center);
        var channel = new WindowsNotificationChannel(newsLogo: () => Logo);
        runtime.Loop.Run(() => channel.Send(Notice()));
        var posted = Assert.Single(platform.Posts);
        Assert.Equal("dextra-duty-b0000000000040008000000000000001", posted.Tag);
        Assert.Single(XDocument.Parse(posted.Xml).Descendants("action"));
        Assert.Empty(platform.Removed);
    }

    // ---- the picture ----

    [Fact]
    public void The_news_picture_is_written_once_for_each_theme_beside_the_needs_you_one_and_is_a_different_picture()
    {
        var files = new RecordingToastIconFiles();
        var dark = true;
        var logo = new DecisionToastLogo(files, @"C:\nth\settings", () => dark);
        var news = logo.NewsPath();
        var needs = logo.Path();
        Assert.Equal(Path.Combine(@"C:\nth\settings", "dextra-news-dark.png"), news);
        Assert.Equal(Path.Combine(@"C:\nth\settings", "dextra-needs-you-dark.png"), needs);
        Assert.Equal(news, logo.NewsPath());
        Assert.Equal(2, files.Writes);
        Assert.NotEqual(files.Files[news!], files.Files[needs!]);
        using var bitmap = SKBitmap.Decode(files.Files[news!]);
        Assert.Equal((96, 96), (bitmap.Width, bitmap.Height));

        dark = false;
        Assert.Equal(Path.Combine(@"C:\nth\settings", "dextra-news-light.png"), logo.NewsPath());
        Assert.Equal(3, files.Writes);
    }

    [Fact]
    public void A_folder_that_cannot_be_written_gives_the_news_toast_no_picture_and_no_error()
    {
        var logo = new DecisionToastLogo(new RefusingToastIconFiles(), @"C:\nth\settings", () => true);
        Assert.Null(logo.NewsPath());
        Assert.Null(logo.NewsPath());
    }
}
