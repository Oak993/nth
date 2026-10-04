// PORT-ONLY: SK-5 GC deletion, one immediate attempt; no waiting on a sharing failure (PLAN 2.1).
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreDeletion : IStoreDeletion
{
    public static StoreDeletion Instance { get; } = new();
    public void Delete(string folder) => throw new NotImplementedException();
}
