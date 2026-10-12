using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Artifacts;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-nc6-1")]
public sealed class TabulaQueueTests(HeadlessSession headless)
{
    private static void Ask(TabulaRig rig, string text)
    {
        rig.Invoke("Ask to change");
        rig.View.Ask.Text = text;
        TabulaRig.Click(rig.View.Ask.SendButton);
        rig.Layout();
    }

    [Fact]
    public void A_rejected_fix_keeps_the_error_available_to_retry() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(trySendAsk: (_, _) => false);
        rig.Show(ArtifactKind.Html, "<html><body>Orbit</body></html>");
        var view = rig.View.Preview.View!;
        view.AttachNative(1, 2);
        var fake = Assert.IsType<FakeWebView>(view.Engine);
        fake.RaiseMessage(rig.View.Preview.Mount!.Origin, "{\"nth\":1,\"s\":\"doc1\",\"type\":\"error\",\"message\":\"oops\"}");
        rig.Layout();
        TabulaRig.Click(Assert.Single(rig.View.Notice.Actions));
        rig.Layout();
        Assert.Equal("This artifact hit an error", rig.View.Notice.Title);
        Assert.Single(rig.View.Notice.Actions);
        Assert.Empty(rig.State.Asks);
    });

    [Fact]
    public void Every_additional_request_gets_a_distinct_queued_node_without_a_silent_cap() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(web: false);
        rig.Show(ArtifactKind.Markdown, "first");
        for (var n = 1; n <= 12; n++) Ask(rig, "Change " + n);
        Assert.Equal(12, rig.Sent.Count);
        Assert.Equal(13, rig.View.Strip.Labels.Count);
        Assert.Equal("v13 queued", rig.View.Strip.Labels[^1].Label);
        Assert.Equal("Queued after v12", rig.View.StatusText);
    });

    [Fact]
    public void A_second_change_keeps_the_first_pending_and_shows_its_queued_version_and_toast() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(web: false);
        rig.Show(ArtifactKind.Markdown, "first", id: "orbit");
        Ask(rig, "Change one");
        Ask(rig, "Change two");
        Assert.Equal("Change one", rig.State.PendingAsk!.Prompt);
        Assert.Equal(["v1", "v2", "v3 queued"], rig.View.Strip.Labels.Select(l => l.Label));
        Assert.Equal("Queued after v2", rig.View.StatusText);
        Assert.Equal(2, rig.Sent.Count);
        Assert.All(rig.Sent, text => Assert.Contains("id orbit, v1,", text, StringComparison.Ordinal));
        Assert.Equal("", rig.View.Ask.Text);
        Assert.Equal("", rig.State.AskText);
    });

    [Fact]
    public void An_earlier_stream_ending_does_not_erase_the_queued_request() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(web: false);
        rig.Show(ArtifactKind.Markdown, "first");
        Ask(rig, "Change one");
        Ask(rig, "Change two");
        rig.Streams.EndAll(TabulaRig.Chat);
        rig.Layout();
        Assert.Contains(rig.View.Strip.Labels, l => l.Label.EndsWith("queued", StringComparison.Ordinal) && l.Caption == "Change two");
    });

    [Fact]
    public void A_landed_version_does_not_consume_the_queued_request() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(web: false);
        var first = rig.Show(ArtifactKind.Markdown, "first");
        Ask(rig, "Change one");
        Ask(rig, "Change two");
        rig.Update(first.Artifact.Id, "second", "Change one");
        rig.Layout();
        Assert.Contains(rig.View.Strip.Labels, l => l.Label == "v3 queued" && l.Caption == "Change two");
    });

    [Fact]
    public void The_composer_is_not_cleared_or_closed_until_the_thread_accepts_the_request() => headless.OnUiThread(() =>
    {
        var accept = false;
        var received = new List<string>();
        using var rig = new TabulaRig(web: false, trySendAsk: (_, text) =>
        {
            if (!accept) return false;
            received.Add(text);
            return true;
        });
        rig.Show(ArtifactKind.Markdown, "first");
        Ask(rig, "Keep this draft");
        Assert.Equal("Keep this draft", rig.View.Ask.Text);
        Assert.Equal("Keep this draft", rig.State.AskText);
        Assert.True(rig.State.IsAskOpen);
        Assert.Null(rig.State.PendingAsk);
        Assert.Single(rig.View.Strip.Labels);
        accept = true;
        TabulaRig.Click(rig.View.Ask.SendButton);
        Assert.Single(received);
        Assert.Equal("", rig.View.Ask.Text);
        Assert.False(rig.State.IsAskOpen);
    });

    [Fact]
    public void Ask_to_fix_queues_after_a_change_with_the_error_and_current_artifact_version() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, "<html><body>Orbit</body></html>", id: "orbit");
        var view = rig.View.Preview.View!;
        view.AttachNative(1, 2); // fake engine handle; no real HWND
        var fake = Assert.IsType<FakeWebView>(view.Engine);
        Ask(rig, "Change one");
        var mount = rig.View.Preview.Mount!;
        fake.RaiseMessage(mount.Origin, "{\"nth\":1,\"s\":\"doc1\",\"type\":\"error\",\"message\":\"oops\",\"stack\":\"at render:12\"}");
        rig.Layout();
        TabulaRig.Click(Assert.Single(rig.View.Notice.Actions));
        rig.Layout();
        Assert.Equal("Change one", rig.State.PendingAsk!.Prompt);
        Assert.Equal("v3 queued", rig.View.Strip.Labels[^1].Label);
        Assert.Equal("Queued after v2", rig.View.StatusText);
        Assert.Contains("id orbit, v1)", rig.Sent[1], StringComparison.Ordinal);
        Assert.Contains("at render:12", rig.Sent[1], StringComparison.Ordinal);
    });
}
