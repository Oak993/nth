using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Marrow.App.Port.AskUser;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.5 look; owner quality bar: compare with the frames). The card painted by the real renderer in a
// headless window: it must paint (not blank, not flat) in every state, and with W25_CARD_DUMP set to a folder the frames are written
// there for review.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskCardFrameTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("32345678-abcd-1234-abcd-123456789abc");

    private const string Everything = """
        {"questions":[
         {"question":"Which database should the service use?","header":"Storage","type":"single","options":[{"label":"PostgreSQL","description":"A server, best for several writers"},{"label":"SQLite","description":"One file, nothing to run"}]},
         {"question":"Which features do you want in the first release?","type":"multiple","options":["Search","Export","Sharing"],"allow_other":false},
         {"question":"Anything else we should know?","type":"text","placeholder":"Optional notes","required":false,"multiline":true},
         {"question":"How much disk may it use?","type":"number","min":1,"max":512,"integer":true,"step":8,"unit":"GB","default":64}]}
        """;

    private static void Dump(TimelineMount mount, string name)
    {
        mount.Settle();
        var (_, bytes, _) = DictationProbe.Frame(mount.Window);
        var distinct = new HashSet<int>();
        for (var at = 0; at + 3 < bytes.Length; at += 4 * 97) distinct.Add(bytes[at] << 16 | bytes[at + 1] << 8 | bytes[at + 2]);
        Assert.True(distinct.Count > 8, name + ": the frame is flat");
        if (Environment.GetEnvironmentVariable("W25_CARD_DUMP") is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = mount.Window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static AskCardRow Mount(TimelineMount mount, string json, string id = "ask-1")
    {
        var card = AskUserSchema.Parse(JsonNode.Parse(json), id).Card!;
        mount.SetEntries([User("prompt", Turn, "Set up the storage for the new service"), Entry("ask", new TimelineItem.Content.Ask(card), Turn)]);
        mount.SetTurnStartedAt(SwiftDate.Now);
        mount.SetPhase(RuntimePhase.Running);
        mount.Settle();
        return LogicalQuery.SelfAndDescendants<AskCardRow>(mount.View).Single();
    }

    [Fact]
    public void A_waiting_card_with_all_four_kinds_paints() => ThreadTimelineRig.Run(headless, mount =>
    {
        var row = Mount(mount, Everything);
        Dump(mount, "ask-waiting-empty");
        var form = row.Form!;
        form.PlatesOf("q1")[0].Button.IsChecked = true;
        form.PlatesOf("q2")[0].Button.IsChecked = true;
        form.PlatesOf("q2")[2].Button.IsChecked = true;
        form.FieldOf("q3")!.Text = "Keep the first release small.";
        Dump(mount, "ask-waiting-filled");
        form.FieldOf("q4")!.Text = "9000";
        Dump(mount, "ask-waiting-problem");
    }, width: 960, height: 900);

    [Fact]
    public void A_closed_card_paints_in_each_state() => ThreadTimelineRig.Run(headless, mount =>
    {
        var row = Mount(mount, Everything);
        var form = row.Form!;
        form.PlatesOf("q1")[1].Button.IsChecked = true;
        form.PlatesOf("q2")[1].Button.IsChecked = true;
        form.FieldOf("q3")!.Text = "Nothing else.";
        form.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dump(mount, "ask-answered");
    }, width: 960, height: 700);

    [Fact]
    public void A_skipped_card_paints() => ThreadTimelineRig.Run(headless, mount =>
    {
        var row = Mount(mount, Everything);
        row.Form!.SkipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dump(mount, "ask-skipped");
    }, width: 960, height: 520);
}
