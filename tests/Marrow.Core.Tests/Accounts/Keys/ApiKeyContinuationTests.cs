using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyContinuationTests
{
    [Theory]
    [InlineData(402)]
    [InlineData(429)]
    public void Accepting_a_withheld_retry_keeps_the_rejected_requests_tried_keys_and_grants_only_one_move(int status)
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        Assert.Equal("a", rotation.Current()!.Id);
        Assert.Null(rotation.OnFailure(status, TimeSpan.Zero));
        Assert.Equal(ApiKeyEventKind.MoveNeedsAnswer, Assert.Single(rig.Events).Kind);
        rotation.AllowMoveOnce();
        var method = rotation.GetType().GetMethod("ContinueRejectedRequest");
        Assert.NotNull(method);
        var accepted = (KeyChoice?)method.Invoke(rotation, []);
        Assert.Equal("b", accepted!.Id);
        Assert.Null(rotation.OnFailure(status, TimeSpan.Zero));
        Assert.Equal(ApiKeyEventKind.MoveNeedsAnswer, rig.Events.Last().Kind);
        rotation.AllowMoveOnce();
        Assert.Equal("c", ((KeyChoice?)method.Invoke(rotation, []))!.Id);
        Assert.Null(rotation.OnFailure(status, TimeSpan.Zero));
        Assert.Null(method.Invoke(rotation, []));
        Assert.Equal(KeyMoveSetting.Unset, rig.Moves.Setting(ApiKeyRig.Thread));
    }

    [Fact]
    public void A_reopened_thread_binds_its_recorded_key_before_any_unapproved_priority_move()
    {
        var rig = new ApiKeyRig(autoMove: false);
        var method = typeof(ApiKeyPool).GetMethods().SingleOrDefault(method => method.Name == "ForThread" && method.GetParameters().Length == 2);
        Assert.NotNull(method);
        var rotation = (IApiKeyRotation)method.Invoke(rig.Pool, [ApiKeyRig.Thread, "b"])!;
        Assert.Equal("b", rotation.Current()!.Id);
        Assert.Equal("b", rotation.Current()!.Id);
        Assert.Empty(rig.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_explicit_binding_on_a_cached_rotation_uses_the_recorded_key_even_if_the_cache_was_created_earlier(bool used)
    {
        var rig = new ApiKeyRig(autoMove: false);
        var old = rig.Pool.ForThread(ApiKeyRig.Thread);
        if (used) Assert.Equal("a", old.Current()!.Id);
        var bound = rig.Pool.ForThread(ApiKeyRig.Thread, "b");
        Assert.Equal("b", bound.Current()!.Id);
        Assert.Equal("b", rig.Pool.ForThread(ApiKeyRig.Thread, "b").Current()!.Id);
        Assert.Null(old.Current());
        Assert.Null(old.OnFailure(429, TimeSpan.Zero));
        Assert.Empty(rig.Events);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    public void A_cached_rotation_cannot_ignore_a_missing_or_foreign_explicit_account_binding(string key)
    {
        var rig = new ApiKeyRig(autoMove: false);
        rig.Store.Add(new("foreign", "zai", "Another service"), "offline-foreign");
        rig.Pool.ForThread(ApiKeyRig.Thread);
        Assert.Throws<ArgumentException>(() => rig.Pool.ForThread(ApiKeyRig.Thread, key));
        Assert.Empty(rig.Events);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(429)]
    public void A_retired_requests_actual_key_still_reports_exhaustion_without_retaining_permission_to_move_its_thread(int status)
    {
        var rig = new ApiKeyRig(autoMove: false);
        var old = rig.Pool.ForThread(ApiKeyRig.Thread, "a");
        Assert.Equal("a", old.Current()!.Id);
        var replacement = rig.Pool.ForThread(ApiKeyRig.Thread, "b");
        Assert.Null(old.OnFailure(status, TimeSpan.FromHours(1)));
        Assert.Equal(2, rig.Pool.State().Available);
        Assert.Equal("b", replacement.Current()!.Id);
        Assert.Equal("b", rig.Pool.ForThread(ApiKeyRig.Other).Current()!.Id);
        Assert.Empty(rig.Events);
        Assert.Null(old.Current());
    }
}
