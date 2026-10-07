using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: AC-3 S2 organisation, user and exact-slug probes; unavailable never falls through. ED-NEW-models-palette-6.
[Trait("Decision","ED-NEW-modelspalette-6")]
public sealed class PaletteHfIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_validated_org_miss_checks_the_user_and_then_the_exact_maker_slug(bool exactSlug)
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();fake.Org="AcmeLabs";
        var answer=fake.Http.Answer!;fake.Http.Answer=r=>
        {
            if(r.Url.Host=="huggingface.co"&&r.Url.AbsolutePath.StartsWith("/api/",StringComparison.Ordinal))
            {
                var hit=exactSlug?r.Url.AbsolutePath=="/api/organizations/acme/overview":r.Url.AbsolutePath=="/api/users/AcmeLabs/overview";
                return hit?answer(r):InProcessHttpHandler.Reply(404,"{\"error\":\"Not found\"}","application/json");
            }
            return answer(r);
        };
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.RefreshNow();
        Assert.Equal(PaletteState.Ready,engine.Current.Models["acme/nova-1"].State);
        Assert.Contains(fake.Http.Received,r=>r.Url.AbsolutePath=="/api/users/AcmeLabs/overview");
        Assert.Equal(exactSlug?3:2,fake.Http.Received.Count(r=>r.Url.Host=="huggingface.co"&&r.Url.AbsolutePath.StartsWith("/api/",StringComparison.Ordinal)));
    }
}
