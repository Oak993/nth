// PORT-ONLY: SK-5, PLAN 2.1 lock and unlock order. Never follows a reparse point.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreTrees(IStoreProtection protection)
{
    private readonly IStoreProtection protection = protection;
    public static IEnumerable<string> Entries(string folder) => throw new NotImplementedException();
    public void Lock(string folder) { _ = protection; throw new NotImplementedException(); }
    public void Unlock(string folder) => throw new NotImplementedException();
}
