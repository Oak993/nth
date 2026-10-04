using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-218 and ED-282-12: a row that is leaving stays where it stands, behind the row before it, while the rows that
// stay settle round it. A leaving row used to be moved to the end of the panel by the survivors' placement.
[Collection("Headless")]
public sealed class ForEachLeavingRowTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")] // collection mechanics only; the first user is UI-COMPOSER-007's chip row
    public void A_leaving_row_keeps_its_place_between_the_rows_that_stay_and_a_new_row_goes_after_it() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var host = new ForEachHost();
        host.Rows.Apply(["a", "b", "c"]);
        host.Layout();
        var (a, b, c) = (host.Made[0], host.Made[1], host.Made[2]);
        Assert.Equal([a, b, c], host.Panel.Children);
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a", "c"]));
        Assert.Equal([a, b, c], host.Panel.Children); // b stands where it was while it fades; c did not move behind it
        Assert.Equal([a, c], host.Rows.Controls);
        clocks.Frame(0.2);
        Assert.Equal([a, b, c], host.Panel.Children);
        Assert.Equal(0.5, host.Opacity(b), 8);
        clocks.Frame(0.2);
        Assert.Equal([a, c], host.Panel.Children);
        Assert.Equal(["b"], host.Disposed);

        // A row that arrives while another leaves goes in behind the row before it and the leaving row that stood there.
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a"]));
        Assert.Equal([a, c], host.Panel.Children);
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a", "d"]));
        var d = host.Made[^1];
        Assert.Equal([a, c, d], host.Panel.Children);
        clocks.Frame(0.4);
        Assert.Equal([a, d], host.Panel.Children);
    });
}
