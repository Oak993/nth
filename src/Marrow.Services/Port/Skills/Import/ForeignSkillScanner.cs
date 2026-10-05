// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The places other AI tools keep skills, under an EXPLICIT home (never the ambient one), read-only.
namespace Marrow.Services.Port.Skills.Import;

public sealed class ForeignSkillScanner(string home, IEnumerable<string>? pickedFolders = null, ScanLimits? limits = null)
{
    public ForeignScan Scan(CancellationToken cancellation = default) => throw new NotImplementedException();

    internal object Unused() => (home, pickedFolders, limits);
}
