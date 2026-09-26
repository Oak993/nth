using System.Reflection;
using Marrow.App;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.App.Fixtures;

public static class HydraCookbookResolveFixture
{
    public static void SetEnabled(AppSettings settings, ProviderKind provider, bool enabled) =>
        settings.SetEnabled(enabled, provider);

    public static void SetInstalled(ProviderRegistry registry, ProviderKind provider, bool installed)
    {
        var status = new ProviderStatus { ApiKeyConfigured = installed };
        var statuses = registry.Statuses.Mutating(values => values[provider] = status);
        var setter = typeof(ProviderRegistry).GetProperty(nameof(ProviderRegistry.Statuses))!
            .GetSetMethod(nonPublic: true)!;
        setter.Invoke(registry, [statuses]);
    }

    public static void SetModels(ProviderRegistry registry, ProviderKind provider, params ModelOption[] models) =>
        registry.UpdateCatalog(new ValueList<ModelOption>(models), provider);

    public static ModelOption Model(string id, string name, string[]? efforts = null, string? defaultEffort = null) =>
        new(id, name, efforts: efforts is null ? null : new ValueList<string>(efforts), defaultEffort: defaultEffort);
}
