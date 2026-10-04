// PORT-ONLY: SK-5, PLAN 2.1 whole-tree verification and ONDEMAND 3.5 per-launch seal.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreVerifier(IStoreProtection protection)
{
    private readonly IStoreProtection protection = protection;
    public StoreVerification Verify(string folder, StoreManifest manifest, bool sealOnly = false)
    {
        _ = protection;
        throw new NotImplementedException();
    }
}
