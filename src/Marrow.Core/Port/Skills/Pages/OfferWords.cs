// PORT-ONLY: first-release feature unit SK-14 (DL-18, skills-ONDEMAND-PLAN.md 5.3 to 5.6; OD-0099 Offer A, words S1). The words of the in-chat offer card. LEAD-PROPOSED WORDING (ED-NEW-cloud-sk-14 item 16): the plan gives the
// decline's meaning ("Don't ask again in this chat", 5.3), the deny reason and the continuation message (5.5) and says the declined card says where the item can be downloaded later; the design pages that drew the card are not
// recoverable (items 14 and 15), so the lead had the asked sentence, the why-needed line and the three captions written in the same S1 voice (short, plain, no jargon). The owner can revise them here, in one place.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.Pages;

public static class OfferWords
{
    /// <summary>The decline control's label (ONDEMAND 5.3: "Don't ask again in this chat" is the decline's meaning).</summary>
    public const string DeclineLabel = "Don't ask again in this chat";

    /// <summary>The asked sentence: what is missing. Its wording follows the plan's deny reason ("LibreOffice isn't on this PC").</summary>
    public static string Asked(IReadOnlyList<string> titles) => List(titles) + (titles.Count == 1 ? " isn't" : " aren't") + " on this PC.";

    /// <summary>The why-needed line for a command that was held or that failed: the step it blocks (5.6).</summary>
    public static string WhyCommand(string commandLine) => "`" + commandLine.Trim() + "` can't run without it.";

    /// <summary>The why-needed line for a skill that declared the item as a need.</summary>
    public static string WhySkill(string skill) => "The " + skill.Trim() + " skill needs it.";

    /// <summary>The model's own reason from its <c>request_tool</c>, as it gave it; none when it gave none.</summary>
    public static string? WhyModel(string reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    public static string Done(IReadOnlyList<string> titles) => List(titles) + (titles.Count == 1 ? " is" : " are") + " installed.";

    public static string Continuing(IReadOnlyList<string> titles) => Done(titles) + " Carrying on where it stopped.";

    /// <summary>The declined card's one line (5.5): it says where the item can be downloaded later, the Settings page of its kind.</summary>
    public static string Declined(IReadOnlyList<string> titles, IReadOnlyList<ItemKind> kinds)
    {
        var pages = kinds.Select(Page).Distinct(StringComparer.Ordinal).ToList();
        return "Not downloaded. You can get " + List(titles) + " later in Settings, under " + List(pages) + ".";
    }

    private static string Page(ItemKind kind) => kind switch { ItemKind.Skillset => "Skills", ItemKind.Plugin => "Plugins", _ => "Tools" };

    private static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
