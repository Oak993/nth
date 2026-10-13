// PORT-ONLY: CU-DX-OPEN2 item 6; ED-NEW-dextra-open-6. Partial constructor failures release subscribed services.
namespace Marrow.App.Assistant;
internal sealed class DextraStartOwnership : IDisposable
{
    private readonly List<IDisposable> owned = [];
    internal T Add<T>(T item) where T : IDisposable { owned.Add(item); return item; }
    internal T Transfer<T>(T item) where T : IDisposable { owned.Clear(); owned.Add(item); return item; }
    internal T Release<T>(T item) { owned.Clear(); return item; }
    public void Dispose() { foreach (var item in owned.AsEnumerable().Reverse()) item.Dispose(); owned.Clear(); }
}
