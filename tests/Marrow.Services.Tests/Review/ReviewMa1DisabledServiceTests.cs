using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Review;

// Independent review of codex/ma-1 (finding MA1-2): turning a service's accounts Off stops launching it, but an installed CLI must still be versioned and updated.
// ProviderRegistry.Environment(provider) throws when no account is enabled, and InstalledCLIVersion / UpdateCLI / UpdateCLIIfDue / LoadCommands call it unguarded.
public sealed class ReviewMa1DisabledServiceTests
{
    [Fact]
    [Trait("Feature", "MA-1")]
    [Trait("Review", "MA-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task REVIEW_the_installed_cli_version_can_still_be_read_when_every_account_of_the_service_is_off()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        // A path that does not exist: no process is started either way; the check is whether the registry throws before it gets there.
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => SwiftURL.Create("marrow-native-path:/nonexistent/claude"));
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        foreach (var account in accounts.ForService(ProviderKind.Claude)) accounts.SetEnabled(account.Id, false);
        var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, folder.Path);

        string? version = "unset";
        Exception? error = null;
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            try { version = await registry.InstalledCLIVersion(ProviderKind.Claude); }
            catch (Exception e) { error = e; }
        }, TimeSpan.FromSeconds(30)));

        Assert.Null(error);
        Assert.Null(version);
    }
}
