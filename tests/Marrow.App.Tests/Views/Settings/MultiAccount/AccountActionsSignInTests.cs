using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// ED-NEW-providersmerge-2: "Add another account" on Settings > Providers makes the account before its sign-in runs; a sign-in that fails, throws or is cancelled takes it away again, so no
// half-made account is left in the card's list. The registry's own sign-in seams stand in for the CLI: nothing is launched.
[Trait("Feature", "MA-8"), Trait("Decision", "ED-NEW-providersmerge-2")]
public sealed class AccountActionsSignInTests
{
    [Fact]
    public void A_sign_in_that_throws_leaves_no_half_made_account()
    {
        using var harness = new AppModelHarness();
        var app = harness.MakeModel();
        harness.PrepareBootstrap(app);
        var before = harness.Main.OnMain(() => app.Settings.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id).ToArray());
        harness.Main.OnMain(() => { app.Providers.AccountLoginLoad = () => Task.FromException(new IOException("The sign-in environment could not be read.")); });
        var error = Assert.Throws<IOException>(() => harness.Main.Loop.Run(() => new AppAccountActions(app).AddAccountAsync(ProviderKind.Claude), AppModelHarness.Bound));
        Assert.Equal("The sign-in environment could not be read.", error.Message);
        Assert.Equal(before, harness.Main.OnMain(() => app.Settings.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id).ToArray()));
    }

    [Fact]
    public void A_sign_in_that_fails_says_why_and_leaves_no_account()
    {
        using var harness = new AppModelHarness();
        var app = harness.MakeModel();
        harness.PrepareBootstrap(app);
        var before = harness.Main.OnMain(() => app.Settings.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id).ToArray());
        harness.Main.OnMain(() => { app.Providers.AccountLoginLoad = () => Task.FromException(new InvalidOperationException("Claude Code did not finish signing in.")); });
        var problem = harness.Main.Loop.Run(() => new AppAccountActions(app).AddAccountAsync(ProviderKind.Claude), AppModelHarness.Bound);
        Assert.Equal("Claude Code did not finish signing in.", problem);
        Assert.Equal(before, harness.Main.OnMain(() => app.Settings.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id).ToArray()));
    }
}
