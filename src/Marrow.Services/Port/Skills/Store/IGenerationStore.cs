// PORT-ONLY: integ-sk8-sk9. What SK-9's generations need from the one owner of a data root: SK-5's SkillsStore, or SK-8's SkillsPipeline
// when the app updates and removes items (SK-8 owns the whole root and its GC; a second owner on the same root would delete the other's trees).
namespace Marrow.Services.Port.Skills.Store;

public interface IGenerationStore
{
    /// <summary>A lease on the installed pin of a skill set, its whole tree verified first; null when it is not usable.</summary>
    StoreLease? AcquireForViewBuild(string id);
    /// <summary>A lease on a generation verified against its trusted composed manifest; null when damaged, retired or already leased.</summary>
    StoreLease? AcquireGeneration(string folder, StoreManifest manifest);
    /// <summary>Never hand this generation out again; delete it once nothing leases it.</summary>
    void Retire(string folder);
    void Collect();
}
