// PORT-ONLY: first-release feature unit SK-13 (DL-15); there is no Swift twin. The words of the terms step and of the Skills and Plugins pages' footer.
namespace Marrow.Core.Port.Skills.Terms;

/// <summary>
/// The fixed words of skills-ONDEMAND-PLAN.md 2.5 (the terms step) and skills-PLAN.md 3.1 (the footer). Vendor and product names are plain text only: no logo, no icon file
/// and no image anywhere (3.1's wording rules; the NoVendorImageTests text test holds the sources to it).
/// </summary>
public static class SkillsNotices
{
    /// <summary>The not-affiliated line the footer of the Plugins and Skills pages carries (skills-PLAN.md 3.1 "Footer").</summary>
    public const string Footer =
        "Nth is not affiliated with or endorsed by Anthropic or OpenAI. Claude and Anthropic are trademarks of Anthropic, PBC. " +
        "ChatGPT, Codex and OpenAI are trademarks of OpenAI. Other names belong to their owners.";

    /// <summary>The separate control beside the licence text (ONDEMAND-PLAN.md 2.5).</summary>
    public const string ReadControl = "I have read these terms";

    /// <summary>
    /// The row's words for an item whose terms class is not open: "Under Anthropic's terms", "Under OpenAI's terms" (2.5). ENGINEERING DECISION (ED-339): one
    /// pattern over the catalogue's vendor field, so the two printed strings are exact and any other vendor reads the same way.
    /// </summary>
    public static string UnderTerms(string vendor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendor);
        return "Under " + vendor + "'s terms";
    }
}
