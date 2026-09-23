using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Library.swift (B2 1fbf81e), unit Core.Models.Library, part 2
// of 3. Every expected value is transcribed from the cited Swift lines or the record itself, never from the C#
// under test (R-TEST-4). Records: PR-STORE-098, PR-STORE-099, PR-STORE-100, PR-STORE-144 through PR-STORE-164.
public sealed class LibrarySecondTests
{
    private static string Uuid(Guid id) => SwiftUUID.UuidString(id);

    private static JsonElement ThreadElement(Guid id, Guid projectID, string extraJson = "")
    {
        var json = "{\"id\":\"" + Uuid(id) + "\",\"projectID\":\"" + Uuid(projectID) + "\"" +
            (extraJson.Length > 0 ? "," + extraJson : "") + "}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static ChatThread Decode(string extraJson = "") =>
        ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), extraJson));

    // ---- PR-STORE-098: hydraPairID on a thread is legacy and decode-only ----

    [Trait("Req", "PR-STORE-098")] // Core/Models/Library.swift:180-182, :261
    [Fact]
    public void HydraPairID_on_an_old_thread_decodes_and_round_trips()
    {
        var stored = Guid.NewGuid();
        var thread = Decode("\"hydraPairID\":\"" + Uuid(stored) + "\"");

        Assert.Equal(stored, thread.HydraPairID);

        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;
        Assert.Equal(stored, reEncoded.HydraPairID);
    }

    // ---- PR-STORE-099: hydraSpawnCount records how many heads a chat has sent out ----

    [Trait("Req", "PR-STORE-099")] // Core/Models/Library.swift:183-184, :262
    [Fact]
    public void HydraSpawnCount_survives_a_restart_and_defaults_to_zero()
    {
        var withoutIt = Decode();
        Assert.Equal(0, withoutIt.HydraSpawnCount);

        var withIt = Decode("\"hydraSpawnCount\":7");
        Assert.Equal(7, withIt.HydraSpawnCount);

        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(withIt))!;
        Assert.Equal(7, reEncoded.HydraSpawnCount);
    }

    // ---- PR-STORE-100: a settled thread keeps the time it settled ----

    [Trait("Req", "PR-STORE-100")] // Core/Models/Library.swift:141-147
    [Fact]
    public void IsSettled_and_settledAt_both_round_trip()
    {
        var settledAt = DateTimeOffset.Parse("2024-05-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised) with
        {
            IsSettled = true,
            SettledAt = settledAt,
        };

        var decoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;

        Assert.True(decoded.IsSettled);
        Assert.Equal(settledAt, decoded.SettledAt);
    }

    [Trait("Req", "PR-STORE-100")] // Core/Models/Library.swift:245-246
    [Fact]
    public void IsSettled_true_with_settledAt_absent_is_decodable()
    {
        var thread = Decode("\"isSettled\":true");

        Assert.True(thread.IsSettled);
        Assert.Null(thread.SettledAt);
    }

    // ---- PR-STORE-144: effort ----

    [Trait("Req", "PR-STORE-144")] // Core/Models/Library.swift:131, :232
    [Fact]
    public void Effort_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().Effort);
        Assert.Null(Decode("\"effort\":null").Effort);
        Assert.Null(Decode("\"effort\":7").Effort);
    }

    [Trait("Req", "PR-STORE-144")] // Core/Models/Library.swift:131, :232
    [Fact]
    public void Effort_is_a_free_string_an_unrecognised_value_round_trips_unchanged()
    {
        var thread = Decode("\"effort\":\"some-future-effort\"");
        Assert.Equal("some-future-effort", thread.Effort);

        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;
        Assert.Equal("some-future-effort", reEncoded.Effort);
    }

    // ---- PR-STORE-145: fastMode ----

    [Trait("Req", "PR-STORE-145")] // Core/Models/Library.swift:132, :233
    [Fact]
    public void FastMode_absent_null_or_wrong_type_decodes_to_false()
    {
        Assert.False(Decode().FastMode);
        Assert.False(Decode("\"fastMode\":null").FastMode);
        Assert.False(Decode("\"fastMode\":\"nope\"").FastMode);
    }

    // ---- PR-STORE-146: interactionMode ----

    [Trait("Req", "PR-STORE-146")] // Core/Models/Library.swift:134, :238
    [Fact]
    public void InteractionMode_absent_null_or_unrecognised_decodes_to_build()
    {
        Assert.Equal(InteractionMode.Build, Decode().InteractionMode);
        Assert.Equal(InteractionMode.Build, Decode("\"interactionMode\":null").InteractionMode);
        Assert.Equal(InteractionMode.Build, Decode("\"interactionMode\":\"not-a-mode\"").InteractionMode);
        Assert.Equal(InteractionMode.Build, Decode("\"interactionMode\":42").InteractionMode);

        // A valid value is honoured, never collapsed to build unconditionally.
        Assert.Equal(InteractionMode.Plan, Decode("\"interactionMode\":\"plan\"").InteractionMode);
    }

    // ---- PR-STORE-147: worktreePath ----

    [Trait("Req", "PR-STORE-147")] // Core/Models/Library.swift:135, :239
    [Fact]
    public void WorktreePath_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().WorktreePath);
        Assert.Null(Decode("\"worktreePath\":null").WorktreePath);
        Assert.Null(Decode("\"worktreePath\":7").WorktreePath);
    }

    // ---- PR-STORE-148: branch ----

    [Trait("Req", "PR-STORE-148")] // Core/Models/Library.swift:136, :240
    [Fact]
    public void Branch_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().Branch);
        Assert.Null(Decode("\"branch\":null").Branch);
        Assert.Null(Decode("\"branch\":7").Branch);
    }

    // ---- PR-STORE-149: providerSessionID ----

    [Trait("Req", "PR-STORE-149")] // Core/Models/Library.swift:137, :241
    [Fact]
    public void ProviderSessionID_absent_null_or_wrong_type_decodes_to_nil_never_empty_string()
    {
        Assert.Null(Decode().ProviderSessionID);
        Assert.Null(Decode("\"providerSessionID\":null").ProviderSessionID);
        Assert.Null(Decode("\"providerSessionID\":7").ProviderSessionID);

        var empty = Decode("\"providerSessionID\":\"\"");
        Assert.Equal("", empty.ProviderSessionID);
        Assert.NotNull(empty.ProviderSessionID);
    }

    // ---- PR-STORE-150: providerResumeAt ----

    [Trait("Req", "PR-STORE-150")] // Core/Models/Library.swift:138, :242
    [Fact]
    public void ProviderResumeAt_absent_null_or_wrong_type_decodes_to_nil_and_is_carried_verbatim()
    {
        Assert.Null(Decode().ProviderResumeAt);
        Assert.Null(Decode("\"providerResumeAt\":null").ProviderResumeAt);
        Assert.Null(Decode("\"providerResumeAt\":7").ProviderResumeAt);

        // It is a free string, not a Date: an opaque provider-written value round trips untouched.
        var thread = Decode("\"providerResumeAt\":\"opaque-cursor-42\"");
        Assert.Equal("opaque-cursor-42", thread.ProviderResumeAt);
        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;
        Assert.Equal("opaque-cursor-42", reEncoded.ProviderResumeAt);
    }

    // ---- PR-STORE-151: isPinned ----

    [Trait("Req", "PR-STORE-151")] // Core/Models/Library.swift:139, :243
    [Fact]
    public void IsPinned_absent_null_or_wrong_type_decodes_to_false()
    {
        Assert.False(Decode().IsPinned);
        Assert.False(Decode("\"isPinned\":null").IsPinned);
        Assert.False(Decode("\"isPinned\":\"nope\"").IsPinned);
    }

    // ---- PR-STORE-152: isArchived ----

    [Trait("Req", "PR-STORE-152")] // Core/Models/Library.swift:140, :244
    [Fact]
    public void IsArchived_absent_null_or_wrong_type_decodes_to_false()
    {
        Assert.False(Decode().IsArchived);
        Assert.False(Decode("\"isArchived\":null").IsArchived);
        Assert.False(Decode("\"isArchived\":\"nope\"").IsArchived);
    }

    // ---- PR-STORE-153: isSettled ----

    [Trait("Req", "PR-STORE-153")] // Core/Models/Library.swift:141-143, :245
    [Fact]
    public void IsSettled_absent_null_or_wrong_type_decodes_to_false()
    {
        Assert.False(Decode().IsSettled);
        Assert.False(Decode("\"isSettled\":null").IsSettled);
        Assert.False(Decode("\"isSettled\":\"nope\"").IsSettled);
    }

    // ---- PR-STORE-154: settledAt ----

    [Trait("Req", "PR-STORE-154")] // Core/Models/Library.swift:144-145, :246
    [Fact]
    public void SettledAt_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().SettledAt);
        Assert.Null(Decode("\"settledAt\":null").SettledAt);
        Assert.Null(Decode("\"settledAt\":7").SettledAt);
        Assert.Null(Decode("\"settledAt\":\"not-a-date\"").SettledAt);
    }

    // ---- PR-STORE-155: hasUnread ----

    [Trait("Req", "PR-STORE-155")] // Core/Models/Library.swift:146, :247
    [Fact]
    public void HasUnread_absent_null_or_wrong_type_decodes_to_false()
    {
        Assert.False(Decode().HasUnread);
        Assert.False(Decode("\"hasUnread\":null").HasUnread);
        Assert.False(Decode("\"hasUnread\":\"nope\"").HasUnread);
    }

    // ---- PR-STORE-156: hasCustomTitle, DEVIATION OD-0008 A11 (BLK-0011 FIX) ----
    // Upstream (Library.swift:248) decodes an unreadable hasCustomTitle to false; the port decodes it to
    // true, so a typed name is never overwritten by the auto-titler. A stored true or false is read as
    // stored either way, and ChatThread's own initializer (Library.swift:219) still starts a brand-new
    // chat with false.

    [Trait("Req", "PR-STORE-156")] // Core/Models/Library.swift:248; DEVIATION OD-0008 A11 (BLK-0011 FIX)
    [Fact]
    public void HasCustomTitle_absent_null_or_wrong_type_decodes_to_true_by_deviation()
    {
        Assert.True(Decode().HasCustomTitle);
        Assert.True(Decode("\"hasCustomTitle\":null").HasCustomTitle);
        Assert.True(Decode("\"hasCustomTitle\":\"nope\"").HasCustomTitle);
    }

    [Trait("Req", "PR-STORE-156")] // Core/Models/Library.swift:248; DEVIATION OD-0008 A11 (BLK-0011 FIX)
    [Fact]
    public void HasCustomTitle_stored_true_or_false_is_read_as_stored()
    {
        Assert.True(Decode("\"hasCustomTitle\":true").HasCustomTitle);
        Assert.False(Decode("\"hasCustomTitle\":false").HasCustomTitle);
    }

    [Trait("Req", "PR-STORE-156")] // Core/Models/Library.swift:219
    [Fact]
    public void HasCustomTitle_still_starts_false_for_a_brand_new_chat()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);
        Assert.False(thread.HasCustomTitle);
    }

    // ---- PR-STORE-157: sortOrder ----

    [Trait("Req", "PR-STORE-157")] // Core/Models/Library.swift:148-149, :249
    [Fact]
    public void SortOrder_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().SortOrder);
        Assert.Null(Decode("\"sortOrder\":null").SortOrder);
        Assert.Null(Decode("\"sortOrder\":\"nope\"").SortOrder);
    }

    [Trait("Req", "PR-STORE-157")] // Core/Models/Library.swift:148-149
    [Fact]
    public void SortOrder_zero_is_a_real_position_not_treated_as_absent()
    {
        var thread = Decode("\"sortOrder\":0");
        Assert.Equal(0.0, thread.SortOrder);
    }

    // ---- PR-STORE-158: activityOrder ----

    [Trait("Req", "PR-STORE-158")] // Core/Models/Library.swift:150-151, :250
    [Fact]
    public void ActivityOrder_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().ActivityOrder);
        Assert.Null(Decode("\"activityOrder\":null").ActivityOrder);
        Assert.Null(Decode("\"activityOrder\":\"nope\"").ActivityOrder);
    }

    // ---- PR-STORE-159: activityOrderDay ----

    [Trait("Req", "PR-STORE-159")] // Core/Models/Library.swift:152-153, :251
    [Fact]
    public void ActivityOrderDay_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().ActivityOrderDay);
        Assert.Null(Decode("\"activityOrderDay\":null").ActivityOrderDay);
        Assert.Null(Decode("\"activityOrderDay\":7").ActivityOrderDay);
        Assert.Null(Decode("\"activityOrderDay\":\"not-a-date\"").ActivityOrderDay);
    }

    // ---- PR-STORE-160: lastStatus ----

    [Trait("Req", "PR-STORE-160")] // Core/Models/Library.swift:154, :252
    [Fact]
    public void LastStatus_absent_null_or_unrecognised_decodes_to_nil()
    {
        Assert.Null(Decode().LastStatus);
        Assert.Null(Decode("\"lastStatus\":null").LastStatus);
        Assert.Null(Decode("\"lastStatus\":\"not-a-status\"").LastStatus);
        Assert.Null(Decode("\"lastStatus\":42").LastStatus);

        // A valid stored status is honoured.
        Assert.Equal(TurnStatus.Failed, Decode("\"lastStatus\":\"failed\"").LastStatus);
    }

    // ---- PR-STORE-161: parentThreadID ----

    [Trait("Req", "PR-STORE-161")] // Core/Models/Library.swift:155-158, :189, :253
    [Fact]
    public void ParentThreadID_absent_null_or_malformed_decodes_to_nil_and_the_thread_is_top_level()
    {
        Assert.Null(Decode().ParentThreadID);
        Assert.False(Decode().IsHelper);

        Assert.Null(Decode("\"parentThreadID\":null").ParentThreadID);
        Assert.Null(Decode("\"parentThreadID\":\"not-a-uuid\"").ParentThreadID);
        Assert.False(Decode("\"parentThreadID\":\"not-a-uuid\"").IsHelper);

        var parent = Guid.NewGuid();
        var helper = Decode("\"parentThreadID\":\"" + Uuid(parent) + "\"");
        Assert.Equal(parent, helper.ParentThreadID);
        Assert.True(helper.IsHelper);
    }

    // ---- PR-STORE-162: isInPanel ----

    [Trait("Req", "PR-STORE-162")] // Core/Models/Library.swift:159-161, :254
    [Fact]
    public void IsInPanel_absent_null_or_wrong_type_decodes_to_false_never_true()
    {
        Assert.False(Decode().IsInPanel);
        Assert.False(Decode("\"isInPanel\":null").IsInPanel);
        Assert.False(Decode("\"isInPanel\":\"nope\"").IsInPanel);
    }

    // ---- PR-STORE-163: expandsHelpers ----

    [Trait("Req", "PR-STORE-163")] // Core/Models/Library.swift:162-166, :255
    [Fact]
    public void ExpandsHelpers_absent_null_or_wrong_type_decodes_to_false_so_helpers_stay_folded()
    {
        Assert.False(Decode().ExpandsHelpers);
        Assert.True(Decode().FoldsHelpers);
        Assert.False(Decode("\"expandsHelpers\":null").ExpandsHelpers);
        Assert.False(Decode("\"expandsHelpers\":\"nope\"").ExpandsHelpers);
    }

    // ---- PR-STORE-164: hydraPairID absent/null/wrong-type ----

    [Trait("Req", "PR-STORE-164")] // Core/Models/Library.swift:180-182, :261
    [Fact]
    public void HydraPairID_absent_null_or_wrong_type_decodes_to_nil()
    {
        Assert.Null(Decode().HydraPairID);
        Assert.Null(Decode("\"hydraPairID\":null").HydraPairID);
        Assert.Null(Decode("\"hydraPairID\":\"not-a-uuid\"").HydraPairID);
    }
}
