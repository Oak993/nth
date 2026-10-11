using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.2 CdpBroker (5.8, 5.10): one ordered channel per view, CDP sessions for frames and workers (flatten mode),
// and cookie and authorization values redacted before anything leaves the engine layer (DESIGN 8.1).
[Trait("Decision", "ED-NEW-web-1")]
[Trait("Feature", "W-7")]
public sealed class CdpBrokerTests
{
    [Fact]
    public void Calls_run_strictly_in_order_one_at_a_time()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        var first = new TaskCompletionSource<string>();
        fake.CdpHandler = (method, _, _) => method == "Page.enable" ? first.Task : Task.FromResult("""{"ok":true}""");
        var broker = view.Cdp!;

        var one = broker.CallAsync("Page.enable");
        var two = broker.CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = "1+1" });

        Assert.Equal(["Page.enable"], fake.CdpCalls.Select(c => c.Method));
        first.SetResult("{}");
        Assert.True(one.IsCompletedSuccessfully);
        Assert.True(two.IsCompletedSuccessfully);
        Assert.Equal(["Page.enable", "Runtime.evaluate"], fake.CdpCalls.Select(c => c.Method));
        Assert.Equal("""{"expression":"1+1"}""", fake.CdpCalls[1].Params);
        Assert.True(WebRig.Done(two).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void A_failed_call_does_not_stop_the_next()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.CdpHandler = (method, _, _) => method == "Bad.call" ? Task.FromException<string>(new InvalidOperationException("no")) : Task.FromResult("{}");

        var bad = view.Cdp!.CallAsync("Bad.call");
        var good = view.Cdp.CallAsync("Page.enable");

        Assert.True(bad.IsFaulted);
        Assert.True(good.IsCompletedSuccessfully);
    }

    [Fact]
    public void Frames_and_workers_attach_as_flat_sessions_that_calls_can_target()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);

        Assert.True(view.Cdp!.AttachToFramesAsync().IsCompletedSuccessfully);
        var setAutoAttach = Assert.Single(fake.CdpCalls);
        Assert.Equal("Target.setAutoAttach", setAutoAttach.Method);
        using (var parameters = JsonDocument.Parse(setAutoAttach.Params))
        {
            Assert.True(parameters.RootElement.GetProperty("autoAttach").GetBoolean());
            Assert.False(parameters.RootElement.GetProperty("waitForDebuggerOnStart").GetBoolean());
            Assert.True(parameters.RootElement.GetProperty("flatten").GetBoolean());
        }

        fake.EmitCdp("Target.attachedToTarget", """{"sessionId":"S1","targetInfo":{"targetId":"T1","type":"iframe","url":"https://ads.test/"}}""");
        fake.EmitCdp("Target.attachedToTarget", """{"sessionId":"S2","targetInfo":{"targetId":"T2","type":"worker","url":"https://ads.test/w.js"}}""", sessionId: "S1");
        var frame = Assert.Single(view.Cdp.Sessions, s => s.SessionId == "S1");
        Assert.Equal(("T1", "iframe", "https://ads.test/", (string?)null), (frame.TargetId, frame.Type, frame.Url, frame.ParentSessionId));
        Assert.Equal("S1", Assert.Single(view.Cdp.Sessions, s => s.SessionId == "S2").ParentSessionId);

        _ = view.Cdp.CallAsync("DOM.getDocument", sessionId: "S1");
        Assert.Equal("S1", fake.CdpCalls[^1].SessionId);

        fake.EmitCdp("Target.detachedFromTarget", """{"sessionId":"S1"}""");
        Assert.Empty(view.Cdp.Sessions); // the worker went with its frame
    }

    [Fact]
    public void Results_and_events_reach_callers_with_cookies_and_authorization_redacted()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.CdpHandler = (_, _, _) => Task.FromResult("""{"cookies":[{"name":"sid","value":"secret-1","domain":"a.test"}]}""");
        var events = new List<string>();
        using var subscription = view.Cdp!.Subscribe("Network.requestWillBeSent", e => events.Add(e.ParamsJson));

        var result = WebRig.Done(view.Cdp.CallAsync("Network.getCookies"));
        fake.EmitCdp("Network.requestWillBeSent", """{"request":{"url":"https://a.test/","headers":{"Authorization":"Bearer secret-2","Accept":"*/*"}}}""");

        var cookie = result.GetProperty("cookies")[0];
        Assert.Equal("sid", cookie.GetProperty("name").GetString());
        Assert.Equal(CdpRedactor.Mask, cookie.GetProperty("value").GetString());
        var sent = Assert.Single(events);
        Assert.DoesNotContain("secret-2", sent, StringComparison.Ordinal);
        Assert.Contains("*/*", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void Disposing_the_broker_drops_its_subscriptions_and_sessions()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        var broker = new CdpBroker(fake);
        fake.EmitCdp("Target.attachedToTarget", """{"sessionId":"S9","targetInfo":{"targetId":"T9","type":"iframe","url":""}}""");
        Assert.Single(broker.Sessions);

        broker.Dispose();

        Assert.Empty(broker.Sessions);
        Assert.Equal(1, fake.CdpSubscribers("Target.attachedToTarget")); // the view's own broker remains
        Assert.Throws<ObjectDisposedException>(() => { _ = broker.CallAsync("Page.enable"); });
    }
}
