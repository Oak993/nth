// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1 "Isolation that is real", 16.2 AS-16 fallback; unit AS-16; ED-NEW-dextra-as16-7). No Swift twin.
// Whose browser profile an assistant uses. Every assistant has its own (so Channel B's sign-in is never Channel A's), and a profile is
// shared only when the owner shared it explicitly: one assistant then uses another's, one step only (a profile that borrows never
// lends, so a share cannot chain into a third assistant's logins), never with itself, never with a retired assistant, and never
// from or to Dextra's own unless the owner shares it the same way. Visus is not in the tree yet: until it is, web reads carry no
// cookies at all (AssistantWebClients), and this rule decides which profile Visus opens when it lands. Pure.
namespace Marrow.Core.Port.Assistant;

public static class AsturIsolation
{
    /// <summary>The assistant whose browser profile <paramref name="who"/> uses: its own id, or the one the owner shared with it.</summary>
    public static string BrowserProfileOwner(AssistantProfile who, IReadOnlyList<AssistantProfile> team)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(team);
        if (who.SharesBrowserOf is not { } lender) return who.Id;
        return ShareRefusal(who, team.FirstOrDefault(p => p.Id == lender), team) is null ? lender : who.Id;
    }

    /// <summary>The name Visus gives an assistant's browser profile; the same for everyone who uses it.</summary>
    public static string BrowserProfileName(string ownerId) => "dextra-" + ownerId;

    /// <summary>
    /// Why <paramref name="borrower"/> may not use <paramref name="lender"/>'s browser profile, or null when it may. The check runs when
    /// the owner shares and again at every use, so a lender that borrows later, or is retired, stops the share at once.
    /// </summary>
    public static string? ShareRefusal(AssistantProfile borrower, AssistantProfile? lender, IReadOnlyList<AssistantProfile> team)
    {
        ArgumentNullException.ThrowIfNull(borrower);
        if (lender is null || team.All(p => p.Id != lender.Id)) return "That assistant is not on the team.";
        if (lender.Retired) return $"{lender.Name} is retired.";
        if (lender.Id == borrower.Id) return "An assistant already uses its own browser profile.";
        if (lender.SharesBrowserOf is not null) return $"{lender.Name} uses another assistant's browser profile, so it cannot lend it on.";
        return null;
    }

    /// <summary>
    /// Whether two assistants use one browser profile (their logins are shared). Only an explicit share makes it so; the answer is
    /// the same whichever is asked first.
    /// </summary>
    public static bool ShareProfile(AssistantProfile a, AssistantProfile b, IReadOnlyList<AssistantProfile> team) =>
        BrowserProfileOwner(a, team) == BrowserProfileOwner(b, team);
}
