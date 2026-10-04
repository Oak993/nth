using Marrow.Services.Port.Skills.Shims;
namespace Marrow.Services.Tests.Port.Skills.Shims;
// PORT-ONLY: SK-6 Job Object seam test.
internal sealed class RecordingShimLifetime : IShimLifetime
{
    public bool Armed { get; private set; }
    public void Arm() => Armed = true;
}
