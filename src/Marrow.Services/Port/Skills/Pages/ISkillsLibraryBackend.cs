// PORT-ONLY: first-release feature unit SK-15 (DL-17). Everything a library page reads and asks of the skills machinery, behind one interface so the page's logic never needs a data root.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Pages;

public interface ISkillsLibraryBackend
{
    /// <summary>True only when a real first-install path is bound. An unbound seam must never queue a download.</summary>
    bool CanFetch { get; }

    SkillsCatalogue Catalogue { get; }

    /// <summary>The state the pipeline holds now (installed items, accepted terms, found-on-this-PC).</summary>
    AddonsState State { get; }

    /// <summary>The switches of enabled.json as they stand.</summary>
    ISwitchReader Switches { get; }

    /// <summary>Writes one switch to enabled.json. Running chats keep their generation; the switch applies to new chats (ONDEMAND-PLAN.md 4.4).</summary>
    void SetSwitch(SwitchLine line, bool on);

    /// <summary>The terms the item owes now, or null when it is open or already accepted.</summary>
    TermsOffer? Terms(string itemId);

    Task<string> LicenceTextAsync(string itemId, CancellationToken cancellation);

    /// <summary>Stores the acceptance (the separate "I have read these terms" control was used).</summary>
    void AcceptTerms(string itemId);

    IFirstInstall Installs { get; }

    Task UpdateAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation);

    Task<bool> RepairAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation);

    Task RemoveAsync(string itemId, CancellationToken cancellation);

    /// <summary>Reset skills and tools: removes everything downloaded, keeps the switches and the accepted terms (ONDEMAND-PLAN.md 3.8).</summary>
    Task ResetAsync(CancellationToken cancellation);

    /// <summary>Free space on the data root's volume, or null when it cannot be read.</summary>
    DriveSpace? Free();

    IReadOnlyList<ProviderSkillGroup> Providers();
}
