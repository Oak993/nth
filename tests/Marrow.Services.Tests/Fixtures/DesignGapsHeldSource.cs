using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Import;

// DesignGapsImportTests' held imported source (ED-NEW-design-gaps-2), kept outside the test file (R-TEST-2/3).
internal sealed class DesignGapsHeldSource : IImportedSkillSource, IDisposable
{
    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();
    public IReadOnlyList<ImportedGenerationSkill> Snapshot()
    {
        Entered.Set();
        if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test did not release the source");
        return [];
    }
    public void Dispose() { Entered.Dispose(); Release.Dispose(); }
}
