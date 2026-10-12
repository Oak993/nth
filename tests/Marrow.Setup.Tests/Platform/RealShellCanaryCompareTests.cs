using Marrow.Testing;

namespace Marrow.Setup.Tests.Platform;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, "Canary", unit U10 (ED-NEW-inst-u10-2). The canary's comparison itself: a shortcut
// that appeared, changed (write time or size) or went is named; the same listing is no difference; the real listing is
// read only and stable between two looks.
public sealed class RealShellCanaryCompareTests
{
    private const string Desktop = @"C:\Users\Ana-María Østergård-Fernández\OneDrive\Desktop";

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_same_listing_is_no_difference_and_case_of_the_path_does_not_matter()
    {
        string[] listing = [$@"{Desktop}\nth.lnk|1000|1500", $@"{Desktop}\nth (2).lnk|2000|1500"];

        Assert.Empty(RealShellCanary.Compare(listing, listing));
        Assert.Empty(RealShellCanary.Compare(listing, [$@"{Desktop.ToUpperInvariant()}\NTH.LNK|1000|1500", $@"{Desktop}\nth (2).lnk|2000|1500"]));
        Assert.Empty(RealShellCanary.Compare([], []));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shortcut_that_appeared_changed_or_went_is_named()
    {
        string[] start = [$@"{Desktop}\nth.lnk|1000|1500", $@"{Desktop}\nth old.lnk|3000|1500"];
        string[] now = [$@"{Desktop}\nth.lnk|1001|1500", $@"{Desktop}\nth new.lnk|4000|1500"];

        var differences = RealShellCanary.Compare(start, now);

        Assert.Equal(
        [
            $@"appeared: {Desktop}\nth new.lnk|4000|1500",
            $@"changed: {Desktop}\nth.lnk|1000|1500 -> {Desktop}\nth.lnk|1001|1500",
            $@"went: {Desktop}\nth old.lnk|3000|1500",
        ], differences);
        Assert.Single(RealShellCanary.Compare([$@"{Desktop}\nth.lnk|1000|1500"], [$@"{Desktop}\nth.lnk|1000|1501"]));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_real_listing_is_read_only_and_the_same_on_two_looks_with_a_baseline_taken()
    {
        RealShellCanary.Begin();

        Assert.True(RealShellCanary.HasBaseline);
        Assert.Equal(RealShellCanary.List(), RealShellCanary.List());
    }
}
