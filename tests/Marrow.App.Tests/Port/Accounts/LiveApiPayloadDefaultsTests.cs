using System.Text.Json;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiPayloadDefaultsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Destination_defaults_reach_the_first_new_request_but_a_refused_payload_remains_identical(bool rejected)
    {
        using var http = new InProcessHttpHandler();
        var calls = 0;
        http.Answer = _ =>
        {
            var call = ++calls;
            if (rejected && call == 1) return InProcessHttpHandler.Reply(402, "{\"error\":{\"message\":\"no credits\"}}");
            if (call == (rejected ? 2 : 1)) return InProcessHttpHandler.Reply(200,
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"read\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"note.txt\\\"}\"}}]}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
            return InProcessHttpHandler.Reply(200, "data: {\"choices\":[{\"delta\":{\"content\":\"Finished\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
        };
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek,
            configure: thread => thread with { Model = "offline-composer", Effort = "low", RuntimeMode = RuntimeMode.Auto }, install: false);
        File.WriteAllText(Path.Combine(rig.Directory, "note.txt"), "Recorded offline fact.");
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-next", next));
            rig.App.Settings.Accounts.SetDefaults(target, new() { Model = "offline-destination", Effort = "high", FastMode = true });
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            if (!rejected) rig.App.Settings.AccountKeys.SetCap(source.Id, 0);
        });
        rig.Send("Read the note and finish on the chosen account.");
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        Assert.Equal(rejected ? 3 : 2, http.Received.Count);
        if (rejected) Assert.Equal(http.Received[0].BodyText, http.Received[1].BodyText);
        foreach (var request in http.Received.Skip(rejected ? 2 : 0))
        {
            Assert.Equal("Bearer offline-next", request.Header("Authorization"));
            using var body = JsonDocument.Parse(request.BodyText);
            Assert.Equal("offline-destination", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("high", body.RootElement.GetProperty("reasoning_effort").GetString());
        }
        Assert.Equal(target, rig.Thread.ProviderAccountID);
        Assert.Single(rig.Runtime.Turns);
        Assert.Equal(TurnStatus.Completed, rig.Runtime.Turns[0].Status);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
