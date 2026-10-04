namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: DL-6 child-tree lifetime seam. Arm before any target process starts.
public interface IShimLifetime
{
    void Arm();
}
