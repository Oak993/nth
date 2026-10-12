using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-c9b-1, owner's C9b decision: inherited flags need a readable direct app origin.
public sealed class AmbientWindowCloakOwnerTests
{
    [Theory]
    [Trait("Decision", "ED-NEW-lean-6")]
    [InlineData(0x1, null, true)]
    [InlineData(0x2, null, false)]
    [InlineData(0x3, null, false)]
    [InlineData(0x4, 0x1, true)]
    [InlineData(0x4, 0x2, false)]
    [InlineData(0x4, 0x3, false)]
    [InlineData(0x4, 0x4, false)]
    [InlineData(0x4, 0, false)]
    [InlineData(0x4, null, false)]
    [InlineData(0x5, 0x2, true)]
    [InlineData(0x6, 0x1, false)]
    [InlineData(0x7, 0x1, false)]
    [InlineData(0x8, null, false)]
    public void LookAt_grants_the_exemption_only_for_a_proven_direct_app_origin(int mask, int? ownerMask, bool appOnly)
    {
        var look = AmbientWindowState.EventHub.LookAt(1, handle => handle == 1 ? mask : ownerMask,
            handle => handle == 1 ? 2 : 0);
        Assert.True(look.Cloaked);
        Assert.False(look.Covered);
        Assert.Empty(look.Coverers);
        Assert.Equal(appOnly, look.AppCloakOnly);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-lean-6")]
    [InlineData(0x1, true)]
    [InlineData(0x2, false)]
    [InlineData(0x3, false)]
    public void Inherited_cloaks_follow_multiple_owners_to_the_first_direct_bits(int origin, bool appOnly)
    {
        var look = AmbientWindowState.EventHub.LookAt(1, handle => handle == 3 ? origin : 0x4,
            handle => handle < 3 ? handle + 1 : 0);
        Assert.Equal(appOnly, look.AppCloakOnly);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-6")]
    public void A_readable_owner_without_direct_bits_does_not_end_the_search_for_the_origin()
    {
        var look = AmbientWindowState.EventHub.LookAt(1, handle => handle == 1 ? 0x4 : handle == 2 ? 0 : 0x1,
            handle => handle < 3 ? handle + 1 : 0);
        Assert.True(look.AppCloakOnly);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-6")]
    public void Missing_owners_and_cyclic_owner_chains_cannot_grant_an_app_exemption()
    {
        Assert.False(AmbientWindowState.EventHub.LookAt(1, _ => 0x4, _ => 0).AppCloakOnly);
        var reads = 0;
        var cyclic = AmbientWindowState.EventHub.LookAt(1, _ => { Assert.True(++reads <= 3); return 0x4; },
            handle => handle == 1 ? 2 : 1);
        Assert.False(cyclic.AppCloakOnly);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-6")]
    public void A_direct_origin_is_decisive_without_reading_unrelated_owners()
    {
        foreach (var mask in new[] { 0x1, 0x2, 0x3, 0x5, 0x6 })
            Assert.Equal((mask & 0x3) == 0x1, AmbientWindowState.EventHub.LookAt(1, _ => mask,
                _ => throw new InvalidOperationException("a direct cloak has its origin already")).AppCloakOnly);
    }
}
