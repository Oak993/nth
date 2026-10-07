using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Local;
using Marrow.Services.Port.Local;

namespace Marrow.Services.Providers;

// PORT-ONLY: OD-0114 / ED-410. Partial keeps Local integration out of the source-derived provider implementation.
public sealed partial class ProviderRegistry
{
    private const string LocalAddressesKey = "localServerAddresses";
    private LocalDiscovery? localDiscovery;
    private readonly HashSet<string> addedLocalAddresses = new(StringComparer.Ordinal);
    private Task? localRefresh;
    public LocalDiscovery Local => localDiscovery ??= LocalDiscovery.Factory.Current();

    internal void InitializeLocal(LocalDiscovery? discovery)
    {
        localDiscovery = discovery;
        if (UserDefaults.Standard.Current.Data(LocalAddressesKey) is not { } bytes) return;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } address) continue;
                try { addedLocalAddresses.Add(Local.AddAddress(address).BaseURL.AbsoluteUri); }
                catch (ArgumentException) { } // one malformed stored address cannot discard every other server
            }
        }
        catch (JsonException) { }
    }

    public LocalServer AddLocalAddress(string address)
    {
        var server = Local.AddAddress(address);
        if (addedLocalAddresses.Add(server.BaseURL.AbsoluteUri))
            UserDefaults.Standard.Current.Set(LocalAddressesKey, JsonSerializer.SerializeToUtf8Bytes(addedLocalAddresses.Order(StringComparer.Ordinal)));
        return server;
    }

    private Task RefreshLocal() => localRefresh is { IsCompleted: false } pending ? pending : localRefresh = DiscoverLocal();

    private async Task DiscoverLocal()
    {
        LoadingCatalogs = loadingCatalogs.Mutating(items => items.Add(ProviderKind.Local));
        try
        {
            var entries = await Local.Discover(SwiftTask.CancellationToken);
            if (accountStore is not null) Marrow.Services.Port.MultiAccount.LocalAccountCatalog.Synchronize(accountStore, entries);
            Catalogs = catalogs.Mutating(items => items[ProviderKind.Local] = new ValueList<ModelOption>(entries.SelectMany(entry => entry.Models).Select(model => model.Option)));
            Statuses = statuses.Mutating(items => items[ProviderKind.Local] = new ProviderStatus
            {
                LocalServerAvailable = entries.Count != 0,
                LocalSummary = entries.All(entry => entry.Server.BaseURL.IsLoopback) ? "No limit, stays on this PC" : "No limit, uses your chosen servers",
            });
        }
        finally { LoadingCatalogs = loadingCatalogs.Mutating(items => items.Remove(ProviderKind.Local)); }
    }
}
