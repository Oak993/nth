// PORT-ONLY: first-release feature unit SK-16 (DL-19, OD-0100; ED-343's hook). There is no Swift twin. The skills pipeline's installed items as the Updates page's "Skills and tools" rows.
using Marrow.App.Port.Updates;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Port.Skills;

/// <summary>
/// UE-4 left <see cref="UpdateEverythingProduction.ExtraItems"/> for this unit to set: the host's pipeline gives one <c>SkillUpdateItem</c> per installed item (kind SkillsAndTools, the page's "Skills and tools"
/// section, SK-8). The service is given its items once at launch, so the pipeline's <c>Changed</c> (an install, update, repair, remove or reset has finished) gives it the set again
/// (<see cref="UpdateEverythingProduction.Refresh"/>); a service that is running an update is left alone and reads the new set at the next change. Program.Main attaches the app's host (<see cref="SkillsStartup.Bind"/>).
/// </summary>
internal static class SkillsUpdateRows
{
    internal static IDisposable Attach(SkillsHost host, Func<IEnumerable<IUpdateItem>>? items = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        UpdateEverythingProduction.ExtraItems = items ?? host.Pipeline.UpdateItems;
        Action changed = UpdateEverythingProduction.Refresh;
        host.Pipeline.Changed += changed;
        return new Detach(host, changed);
    }

    private sealed class Detach(SkillsHost host, Action changed) : IDisposable
    {
        public void Dispose()
        {
            host.Pipeline.Changed -= changed;
            UpdateEverythingProduction.ExtraItems = () => [];
        }
    }
}
