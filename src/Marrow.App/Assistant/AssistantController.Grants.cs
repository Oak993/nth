// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.2 structured grants, 12.5 the audit; the AS-5 note "grant persistence
// belongs to Settings › Assistant"; ED-NEW-dextra-as8-8). No Swift twin.
// The grants' life outside a card: the ones Settings kept come back at start, the owner revokes one from Settings ›
// Assistant › Permissions, and every change is raised so Settings can keep the list.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class AssistantController
{
    /// <summary>A grant was made from a card or revoked (raised on the thread that made the change).</summary>
    public event Action? GrantsChanged;

    /// <summary>
    /// Brings back the grants Settings kept. Each was logged when it was made, so nothing is logged again; one that is no
    /// longer valid (its class no longer takes a grant) or already known by id is skipped. Returns how many came back.
    /// </summary>
    public int RestoreGrants(IEnumerable<Grant> kept)
    {
        ArgumentNullException.ThrowIfNull(kept);
        var restored = 0;
        lock (gate)
        {
            foreach (var grant in kept)
            {
                if (grant is null || GrantRules.Invalid(grant) is not null || grants.Any(g => g.Id == grant.Id)) continue;
                grants.Add(grant);
                restored++;
            }
        }
        return restored;
    }

    /// <summary>
    /// Revokes a grant at <paramref name="when"/>: enforced at the next action. Revoking only takes power away, so it is
    /// kept even when the audit cannot be written (the failure is reported). False when no live grant has that id.
    /// </summary>
    public bool RevokeGrant(Guid id, DateTimeOffset when)
    {
        Grant revoked;
        lock (gate)
        {
            var index = grants.FindIndex(g => g.Id == id);
            if (index < 0 || grants[index].RevokedAt is not null) return false;
            revoked = grants[index].Revoked(when);
            grants[index] = revoked;
        }
        try
        {
            o.Audit(new AuditEntry
            {
                Assistant = revoked.AssistantId,
                Tool = "grant",
                ActionClass = revoked.Class.ToString(),
                Target = string.Join(", ", revoked.Target.Values),
                Result = "revoked by the owner",
                AllowedBy = "settings",
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Runner.ReportFault("A revoked grant could not be logged: " + error.Message);
        }
        GrantsChanged?.Invoke();
        return true;
    }
}
