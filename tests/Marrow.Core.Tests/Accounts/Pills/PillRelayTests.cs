using System.Text.Json;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Pills;

// MA-7: the thread's pill and step persist on the thread, and a restart resumes the relay where it was.
public sealed class PillRelayTests
{
    private static ChatThread Thread() => new(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

    private static Pill Make(params string[] accounts) =>
        new("P", [.. accounts.Select(a => new PillStep(a))], Id: Guid.NewGuid());

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T224")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pill_and_step_ride_on_the_thread_through_its_json()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var pillId = Guid.NewGuid();
        var thread = Thread().WithPillRelay(new PillThreadState(pillId, 2, "x1"));
        var json = JsonSerializer.Serialize(thread, Marrow.Core.Support.Coding.StorageOptions);
        var back = JsonSerializer.Deserialize<ChatThread>(json, Marrow.Core.Support.Coding.StorageOptions)!;
        Assert.Equal(new PillThreadState(pillId, 2, "x1"), back.PillRelay);
        Assert.Equal(thread, back);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T225")]
    [Trait("Rule", "R-TEST-4")]
    public void A_thread_with_no_pill_writes_no_pill_keys_and_an_old_thread_reads_as_having_none()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var thread = Thread();
        var json = JsonSerializer.Serialize(thread, Marrow.Core.Support.Coding.StorageOptions);
        Assert.DoesNotContain("pill", json, StringComparison.OrdinalIgnoreCase);
        var back = JsonSerializer.Deserialize<ChatThread>(json, Marrow.Core.Support.Coding.StorageOptions)!;
        Assert.Null(back.PillRelay);
        Assert.Null(back.PillID);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T226")]
    [Trait("Rule", "R-TEST-4")]
    public void Pill_fields_that_cannot_be_read_mean_no_relay_and_a_partial_set_is_no_relay()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var thread = Thread();
        var json = JsonSerializer.Serialize(thread, Marrow.Core.Support.Coding.StorageOptions);
        var broken = json[..^1] + ",\"pillID\":\"not a guid\",\"pillStepIndex\":\"two\",\"pillStepAccountID\":\"x1\"}";
        var back = JsonSerializer.Deserialize<ChatThread>(broken, Marrow.Core.Support.Coding.StorageOptions)!;
        Assert.Null(back.PillRelay);
        var partial = json[..^1] + $",\"pillID\":\"{Guid.NewGuid()}\",\"pillStepAccountID\":\"x1\"}}";
        Assert.Null(JsonSerializer.Deserialize<ChatThread>(partial, Marrow.Core.Support.Coding.StorageOptions)!.PillRelay);
        Assert.Null(thread.WithPillRelay(new PillThreadState(Guid.NewGuid(), 1, "a")).WithPillRelay(null).PillRelay);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T227")]
    [Trait("Rule", "R-TEST-4")]
    public void A_restart_resumes_at_the_stored_step_when_the_pill_is_unchanged()
    {
        var pill = Make("c1", "c2", "x1");
        var resume = PillRelayResolver.Resolve([pill], new PillThreadState(pill.Id, 1, "c2"));
        Assert.Equal((pill, 1, PillResumeReason.Exact), (resume.Pill, resume.StepIndex, resume.Reason));
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T228")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_edited_while_the_app_was_closed_still_resumes_on_the_same_account_at_its_nearest_step()
    {
        var edited = Make("x1", "c1", "c2", "c1");
        var resume = PillRelayResolver.Resolve([edited], new PillThreadState(edited.Id, 2, "c1"));
        Assert.Equal((3, PillResumeReason.Moved), (resume.StepIndex, resume.Reason));
        var first = PillRelayResolver.Resolve([edited], new PillThreadState(edited.Id, 0, "c1"));
        Assert.Equal(1, first.StepIndex);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T229")]
    [Trait("Rule", "R-TEST-4")]
    public void When_the_account_left_the_pill_the_stored_position_is_kept_and_clamped_into_the_pill()
    {
        var edited = Make("a", "b");
        var kept = PillRelayResolver.Resolve([edited], new PillThreadState(edited.Id, 1, "gone"));
        Assert.Equal((1, PillResumeReason.Replaced), (kept.StepIndex, kept.Reason));
        var clamped = PillRelayResolver.Resolve([edited], new PillThreadState(edited.Id, 9, "gone"));
        Assert.Equal((1, PillResumeReason.Replaced), (clamped.StepIndex, clamped.Reason));
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T230")]
    [Trait("Rule", "R-TEST-4")]
    public void A_deleted_pill_clears_the_relay_and_an_empty_pill_has_no_step()
    {
        var other = Make("a");
        var gone = PillRelayResolver.Resolve([other], new PillThreadState(Guid.NewGuid(), 0, "a"));
        Assert.Equal((null, null, PillResumeReason.PillGone), (gone.Pill, gone.StepIndex, gone.Reason));
        var empty = new Pill("E", [], Id: Guid.NewGuid());
        var none = PillRelayResolver.Resolve([empty], new PillThreadState(empty.Id, 0, "a"));
        Assert.Equal((empty, null, PillResumeReason.Empty), (none.Pill, none.StepIndex, none.Reason));
    }
}
