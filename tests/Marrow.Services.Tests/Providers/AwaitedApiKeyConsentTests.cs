using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class AwaitedApiKeyConsentTests
{
    [Theory]
    [InlineData(402)]
    [InlineData(429)]
    public async Task A_rejected_request_waits_for_the_user_and_then_retries_the_identical_payload_in_the_same_turn(int status)
    {
        await using var rig = new KeyRotationRig([status + ":0", KeyRotationRig.Ok], autoMove: false);
        var rotation = new DeferredKeyRotation(rig.Pool.ForThread(KeyRotationRig.Thread));
        rig.Session.KeyRotation = rotation;
        await rig.Session.Start();
        var send = rig.Session.Send(Input());
        rig.Runtime.Loop.RunUntil(() => rotation.FailureCalls != 0 || send.IsCompleted);
        Assert.Equal(1, rotation.CurrentCalls);
        Assert.Equal(1, rotation.FailureCalls);
        Assert.False(send.IsCompleted);
        Assert.Single(rig.Requests);
        Assert.DoesNotContain(rig.Events, item => item is ProviderEvent.TurnCompleted);
        rotation.Answer.SetResult(true);
        rig.Runtime.Loop.RunUntil(() => send.IsCompleted);
        await send;
        Assert.Equal(["Bearer sk-a", "Bearer sk-b"], rig.Requests.Select(item => item.Bearer));
        Assert.Equal(rig.Requests[0].Body, rig.Requests[1].Body);
        Assert.Single(rig.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Completed, rig.Events.OfType<ProviderEvent.TurnCompleted>().Single().Status);
        Assert.Equal(KeyMoveSetting.Unset, rig.Moves.Setting(KeyRotationRig.Thread));
    }

    [Fact]
    public async Task Stopping_a_request_waiting_for_its_first_choice_cancels_the_answer_and_a_late_yes_sends_nothing()
    {
        await using var rig = new KeyRotationRig(["429:0", KeyRotationRig.Ok], autoMove: false);
        var rotation = new DeferredKeyRotation(rig.Pool.ForThread(KeyRotationRig.Thread));
        rig.Session.KeyRotation = rotation;
        await rig.Session.Start();
        var send = rig.Session.Send(Input());
        rig.Runtime.Loop.RunUntil(() => rotation.FailureCalls != 0 || send.IsCompleted);
        Assert.Equal(1, rotation.FailureCalls);
        rig.Session.Stop();
        rig.Runtime.Loop.RunUntil(() => send.IsCompleted);
        await send;
        Assert.True(rotation.AnswerCancellation.IsCancellationRequested);
        rotation.Answer.SetResult(true);
        rig.Runtime.RunUntilIdle();
        Assert.Single(rig.Requests);
        Assert.DoesNotContain(rig.Events, item => item is ProviderEvent.TurnCompleted { Status: TurnStatus.Completed });
    }

    private static TurnInput Input() => new()
    {
        Text = "Keep the complete request and original turn.", Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
}
