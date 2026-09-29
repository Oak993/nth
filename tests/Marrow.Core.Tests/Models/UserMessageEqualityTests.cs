using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// Audit fix (svc): Timeline.swift:58 `struct UserMessage: Hashable` synthesizes equality and hashing over its Swift String text, which is
// canonical equivalence (R-STR-2), so "e" + U+0301 and U+00E9 name one message text.
public sealed class UserMessageEqualityTests
{
    [Trait("Req", "PR-CHAT-004")] // Core/Models/Timeline.swift:58-70
    [Fact]
    public void Messages_whose_texts_are_canonically_equivalent_are_equal_and_hash_alike()
    {
        var composed = new UserMessage { Text = "é" };
        var decomposed = new UserMessage { Text = "é" };

        Assert.Equal(composed, decomposed);
        Assert.Equal(composed.GetHashCode(), decomposed.GetHashCode());
        Assert.Single(new HashSet<UserMessage> { composed, decomposed });
    }

    [Trait("Req", "PR-CHAT-004")] // Core/Models/Timeline.swift:58-70
    [Fact]
    public void Messages_differing_in_any_other_field_stay_unequal()
    {
        var plain = new UserMessage { Text = "a" };

        Assert.NotEqual(plain, plain with { HydraBrief = true });
        Assert.NotEqual(plain, plain with { HydraHeads = new Marrow.Core.Port.ValueList<long>(new long[] { 1 }) });
        Assert.NotEqual(plain, plain with { Text = "b" });
        Assert.Equal(plain with { HydraBrief = true }, plain with { HydraBrief = true });
    }
}
