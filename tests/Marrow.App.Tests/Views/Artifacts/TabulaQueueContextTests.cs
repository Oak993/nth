using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Artifacts;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-nc6-2")]
public sealed class TabulaQueueContextTests(HeadlessSession headless)
{
    [Fact]
    public void Multiple_updates_in_one_turn_keep_queued_numbers_after_every_written_version() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(web: false, trySendAsk: (_, _) => true);
        var first = rig.Show(ArtifactKind.Markdown, "one", id: "orbit");
        foreach (var text in new[] { "first", "second" })
        {
            rig.Invoke("Ask to change");
            rig.View.Ask.Text = text;
            TabulaRig.Click(rig.View.Ask.SendButton);
        }
        rig.Update(first.Artifact.Id, "two", "first");
        rig.Update(first.Artifact.Id, "three", "first continued");
        rig.Layout();
        Assert.Equal(["v1", "v2", "v3", "v4 queued"], rig.View.Strip.Labels.Select(l => l.Label));
        Assert.Equal(2, rig.State.Asks.Count);
        Assert.False(rig.View.Strip.IsPulsing);
    });

    [Fact]
    public void An_update_before_turn_completion_does_not_make_a_queued_change_start_from_a_scrubbed_old_version() => headless.OnUiThread(() =>
    {
        var sent = new List<string>();
        using var rig = new TabulaRig(web: false, trySendAsk: (_, text) => { sent.Add(text); return true; });
        var first = rig.Show(ArtifactKind.Markdown, "one", id: "orbit");
        rig.Invoke("Ask to change");
        rig.View.Ask.Text = "first";
        TabulaRig.Click(rig.View.Ask.SendButton);
        rig.Update(first.Artifact.Id, "two", "first");
        rig.State.ShowVersion(1);
        rig.Layout();
        rig.Invoke("Ask to change");
        rig.View.Ask.Text = "second";
        TabulaRig.Click(rig.View.Ask.SendButton);
        Assert.Contains("id orbit, v2,", sent[1], StringComparison.Ordinal);
        Assert.Equal(2, rig.State.Asks.Count);
        Assert.Equal("v3 queued", rig.View.Strip.Labels[^1].Label);
    });
}
