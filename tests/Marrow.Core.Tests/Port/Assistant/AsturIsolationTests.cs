using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.AsturTeamSample;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1 "Isolation that is real", 16.2 AS-16; ED-NEW-dextra-as16-7): every assistant has its own browser profile, so Channel B's
// sign-in is never Channel A's. A profile is shared only when the owner shared it; one step only, never with itself, never from a retired assistant, and a
// profile that borrows never lends.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-7")]
public sealed class AsturIsolationTests
{
    [Fact]
    public void Every_assistant_uses_its_own_browser_profile_unless_the_owner_shared_one()
    {
        var team = Channels();
        Assert.All(team, who => Assert.Equal(who.Id, AsturIsolation.BrowserProfileOwner(who, team)));
        Assert.False(AsturIsolation.ShareProfile(team[1], team[2], team));
        Assert.False(AsturIsolation.ShareProfile(team[0], team[1], team));
        Assert.Equal("dextra-channel-a", AsturIsolation.BrowserProfileName("channel-a"));
        Assert.NotEqual(AsturIsolation.BrowserProfileName(team[1].Id), AsturIsolation.BrowserProfileName(team[2].Id));
    }

    [Fact]
    public void A_shared_profile_is_the_lenders_and_the_answer_is_the_same_whichever_is_asked_first()
    {
        var lender = Astur("Channel A", AsturShape.Squircle, "a");
        var borrower = Astur("Channel B", AsturShape.Hexagon, "b") with { SharesBrowserOf = "a" };
        var team = Team(lender, borrower);
        Assert.Equal("a", AsturIsolation.BrowserProfileOwner(borrower, team));
        Assert.Equal("a", AsturIsolation.BrowserProfileOwner(lender, team));
        Assert.True(AsturIsolation.ShareProfile(lender, borrower, team));
        Assert.True(AsturIsolation.ShareProfile(borrower, lender, team));
    }

    [Fact]
    public void A_profile_that_borrows_never_lends_so_a_share_cannot_chain_into_a_third_assistants_logins()
    {
        var c = Astur("Channel C", AsturShape.Diamond, "c");
        var b = Astur("Channel B", AsturShape.Hexagon, "b") with { SharesBrowserOf = "c" };
        var a = Astur("Channel A", AsturShape.Squircle, "a") with { SharesBrowserOf = "b" };
        var team = Team(a, b, c);
        Assert.Equal("Channel B uses another assistant's browser profile, so it cannot lend it on.", AsturIsolation.ShareRefusal(a, b, team));
        Assert.Equal("a", AsturIsolation.BrowserProfileOwner(a, team));                // A falls back to its own, not C's
        Assert.Equal("c", AsturIsolation.BrowserProfileOwner(b, team));
        Assert.False(AsturIsolation.ShareProfile(a, c, team));
    }

    [Fact]
    public void A_share_with_oneself_a_retired_or_unknown_assistant_is_refused_and_stops_at_once()
    {
        var a = Astur("Channel A", AsturShape.Squircle, "a");
        var retired = Astur("Old", AsturShape.Star, "old", retired: true);
        var team = Team(a, retired);
        Assert.Equal("An assistant already uses its own browser profile.", AsturIsolation.ShareRefusal(a, a, team));
        Assert.Equal("Old is retired.", AsturIsolation.ShareRefusal(a, retired, team));
        Assert.Equal("That assistant is not on the team.", AsturIsolation.ShareRefusal(a, null, team));
        Assert.Equal("That assistant is not on the team.", AsturIsolation.ShareRefusal(a, Astur("Ghost", AsturShape.Clover, "ghost"), team));
        // A share that names a lender who has since been retired no longer applies.
        var borrower = Astur("Channel B", AsturShape.Hexagon, "b") with { SharesBrowserOf = "old" };
        Assert.Equal("b", AsturIsolation.BrowserProfileOwner(borrower, Team(a, retired, borrower)));
        Assert.Null(AsturIsolation.ShareRefusal(a, Astur("Channel B", AsturShape.Hexagon, "b"), Team(a, Astur("Channel B", AsturShape.Hexagon, "b"))));
    }

    [Fact]
    public void Dextras_own_profile_is_shared_only_the_same_explicit_way()
    {
        var team = Channels();
        var borrower = team[1] with { SharesBrowserOf = DextraId };
        var shared = new[] { team[0], borrower, team[2] };
        Assert.Equal(DextraId, AsturIsolation.BrowserProfileOwner(borrower, shared));
        Assert.False(AsturIsolation.ShareProfile(team[2], team[0], shared));          // a third assistant gets nothing from the share
    }
}
