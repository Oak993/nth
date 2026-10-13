using Marrow.Core.Web;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Services.Web;
using System.Text.Json.Nodes;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-review-1")]
public sealed class WebReviewLifecycleTests
{
    [Fact]
    public void Cancelling_a_queued_CDP_call_must_not_wait_for_a_stalled_predecessor()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var first = new TaskCompletionSource<string>();
        WebRig.Fake(view).CdpHandler = (_, _, _) => first.Task;
        _ = view.Cdp!.CallAsync("Runtime.evaluate");
        using var stop = new CancellationTokenSource();
        var queued = view.Cdp.CallAsync("DOM.getDocument", cancellationToken: stop.Token);
        try
        {
            stop.Cancel();
            Assert.True(queued.IsCanceled, "A cancelled queued call is still waiting for the stalled predecessor.");
        }
        finally { first.TrySetResult("{}"); }
    }

    [Fact]
    public void A_first_capture_that_stalls_must_not_hold_a_gesture_forever()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var capture = new TaskCompletionSource<WebSnapshot?>();
        WebRig.Fake(view).HeldCapture = capture;
        var gesture = view.CaptureForGestureAsync(CancellationToken.None);
        try
        {
            rig.Time.Advance(TimeSpan.FromSeconds(30));
            Assert.True(gesture.IsCompleted, "The first gesture is still waiting after 30 seconds.");
        }
        finally { capture.TrySetResult(null); }
    }

    [Fact]
    public void A_failed_storage_remove_must_keep_the_committed_value_in_memory()
    {
        using var rig = new ArtifactRuntimeRig();
        var snapshot = rig.Create(ArtifactKind.Html, "<p>hello</p>");
        var address = new ArtifactAddress(snapshot.Artifact.Thread, snapshot.Artifact.Id);
        var data = rig.Runtime.Data;
        data.Set(address, "important", "keep me");
        var path = Path.Combine(rig.Store.Folder(address.Thread, address.Id), ArtifactData.StorageFile);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Record.Exception(() => data.Remove(address, "important"));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected the held storage file to reject replacement; got {failure?.GetType().Name ?? "no error"}.");
        }
        data.Set(address, "other", "new value");
        var restarted = new ArtifactData(rig.Store);
        Assert.Equal("keep me", restarted.Get(address, "important"));
    }

    [Fact]
    public void A_stalled_capture_must_not_disable_the_process_live_view_cap()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(shown: true);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        var fake = WebRig.Fake(first);
        var capture = new TaskCompletionSource<WebSnapshot?>();
        fake.HeldCapture = capture;
        try
        {
            for (var i = 0; i < 6; i++) rig.OpenLive();
            rig.Time.Advance(TimeSpan.FromSeconds(30));
            rig.Surface.Tick();
            Assert.True(rig.Surface.LiveCount <= rig.Surface.Options.MaxLiveViews,
                $"Live count {rig.Surface.LiveCount}; configured cap {rig.Surface.Options.MaxLiveViews}");
        }
        finally
        {
            first.SetLiveVisible(false);
            capture.TrySetResult(null);
        }
    }

    [Fact]
    public void Completing_a_refresh_after_close_must_not_resurrect_the_closed_snapshot()
    {
        using var rig = new WebRig();
        rig.Surface.Cache.Enabled = true;
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        var capture = new TaskCompletionSource<WebSnapshot?>();
        fake.HeldCapture = capture;
        view.NoteIntent();
        rig.Surface.Close(view);
        Assert.Null(view.LastSnapshot);
        capture.SetResult(new WebSnapshot(new byte[1024], 16, 16, 1, rig.Time.GetUtcNow(), null, WebSnapshotSource.Cdp));
        Assert.Null(view.LastSnapshot);
    }

    [Fact]
    public void Wrong_typed_artifact_bytes_must_be_skipped_without_throwing()
    {
        using var rig = new ArtifactRuntimeRig();
        var snapshot = rig.Create(ArtifactKind.Html, "<p>hello</p>");
        var meta = Path.Combine(rig.Store.Folder(snapshot.Artifact.Thread, snapshot.Artifact.Id), "meta.json");
        var json = JsonNode.Parse(File.ReadAllText(meta))!;
        json["versions"]![0]!["bytes"] = "bad";
        File.WriteAllText(meta, json.ToJsonString());
        Assert.Empty(rig.Store.List(snapshot.Artifact.Thread));
    }

}
