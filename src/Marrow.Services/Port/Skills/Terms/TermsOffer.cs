// PORT-ONLY: SK-13 / DL-15, skills-ONDEMAND-PLAN.md 2.5. What the terms step shows for one item.
namespace Marrow.Services.Port.Skills.Terms;

/// <summary>
/// The terms an item owes: its row label ("Under Anthropic's terms"), the licence by name and link, the hash the catalogue holds for the licence file and the words of the
/// separate "I have read these terms" control. <see cref="CanAccept"/> is false while the catalogue holds no hash for the licence file (the pin run has not filled it):
/// the terms are owed but cannot be keyed, so nothing can be accepted (fail closed).
/// </summary>
public sealed record TermsOffer(string ItemId, string Title, string Label, string? LicenceName, string? LicenceUrl, string? LicenceSha256, string ReadControl)
{
    public bool CanAccept => LicenceSha256 is not null;
}
