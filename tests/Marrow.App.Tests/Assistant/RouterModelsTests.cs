using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6; ED-NEW-dextra-as14-2): the tier of a model is read from the output price the Ignis points know for
// it, against the dearest model its provider lists. The bundled points are used (nothing is fetched); a model they do not know, or no
// points at all, is Standard.
[Trait("Decision", "ED-NEW-dextra-as14-2")]
[Trait("Feature", "AS-14")]
public sealed class RouterModelsTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static ModelOption Model(string id, bool isDefault = false) => new(id, id, isDefault: isDefault);

    [Fact]
    public void A_providers_models_are_tiered_by_the_price_the_points_know()
    {
        var points = new IgnisPointsStore(temp.Folder);
        var tiers = RouterModels.Of(ProviderKind.Claude, [Model("claude-haiku-5-5"), Model("claude-sonnet-5-5", isDefault: true), Model("claude-opus-5-5"), Model("claude-unheard-of")], points)
            .ToDictionary(m => m.Id, m => m.Tier);
        Assert.Equal(ModelTier.Light, tiers["claude-haiku-5-5"]);          // 0.5 of 20 dollars per million output tokens
        Assert.Equal(ModelTier.Standard, tiers["claude-sonnet-5-5"]);      // 10
        Assert.Equal(ModelTier.Strong, tiers["claude-opus-5-5"]);          // 20: the dearest listed
        Assert.Equal(ModelTier.Standard, tiers["claude-unheard-of"]);      // no price known
    }

    [Fact]
    public void With_no_points_every_model_is_standard_and_the_providers_default_is_kept()
    {
        var models = RouterModels.Of(ProviderKind.Claude, [Model("a"), Model("b", isDefault: true)], null);
        Assert.All(models, m => Assert.Equal(ModelTier.Standard, m.Tier));
        Assert.Equal(["b"], models.Where(m => m.IsDefault).Select(m => m.Id));
        Assert.Empty(RouterModels.Of(ProviderKind.Claude, [], new IgnisPointsStore(temp.Folder)));
    }
}
