using Marrow.Core.Models;
using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

// Unit UE-2: a provider CLI as an update item, through ProviderRegistry's update run (the ICliUpdateRunner it implements) and a latest-version listing seam. OD-0100: "every installed
// provider CLI that has an updater". The runner is fake: no CLI runs.
public sealed class ProviderCliUpdateItemTests
{
    private const string NpmDoc = "{\"name\":\"@anthropic-ai/claude-code\",\"version\":\"2.1.3\"}";
    private const string ReleaseDoc = "{\"tag_name\":\"rust-v0.136.0\",\"name\":\"x\"}";

    private static FetchedVersionListing Listing() => new((uri, _) => Task.FromResult(uri.AbsoluteUri.Contains("npmjs", StringComparison.Ordinal) ? NpmDoc : ReleaseDoc));

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_item_reads_the_installed_version_from_the_runner_and_the_latest_from_the_listing()
    {
        var runner = new FakeRunner();
        runner.Versions[ProviderKind.Claude] = "2.1.0 (Claude Code)";
        var item = new ProviderCliUpdateItem(ProviderKind.Claude, runner, Listing());
        Assert.Equal("cli:claude", item.Id);
        Assert.Equal(UpdateItemKind.ProviderCli, item.Kind);
        Assert.Equal(ProviderKind.Claude.DisplayName, item.Title);
        Assert.Equal("2.1.0 (Claude Code)", await item.InstalledVersion(default));
        Assert.Equal("2.1.3", await item.LatestVersion(default));
        var codex = new ProviderCliUpdateItem(ProviderKind.Codex, runner, Listing());
        Assert.Equal("0.136.0", await codex.LatestVersion(default));
        Assert.Null(await new ProviderCliUpdateItem(ProviderKind.Claude, runner, new NoVersionListing()).LatestVersion(default));
    }

    [Theory]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(CliUpdateStatus.Updated, true, true)]
    [InlineData(CliUpdateStatus.Unchanged, true, false)]
    [InlineData(CliUpdateStatus.Skipped, true, false)]
    [InlineData(CliUpdateStatus.Failed, false, false)]
    [InlineData(CliUpdateStatus.NotInstalled, false, false)]
    public async Task The_runner_s_result_becomes_the_item_s_outcome(CliUpdateStatus status, bool succeeded, bool changed)
    {
        var runner = new FakeRunner();
        runner.Results[ProviderKind.Claude] = new CliUpdateResult(status, "1", "2");
        var outcome = await new ProviderCliUpdateItem(ProviderKind.Claude, runner, new NoVersionListing()).Update(new Progress<double?>(), default);
        Assert.Equal((succeeded, changed, false), (outcome.Succeeded, outcome.Changed, outcome.RestartRequired));
        Assert.Equal(succeeded && status == CliUpdateStatus.Updated ? "2" : null, outcome.NewVersion);
        if (!succeeded) Assert.False(string.IsNullOrEmpty(outcome.Message));
        Assert.Equal([ProviderKind.Claude], runner.Ran);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_orchestrator_with_cli_items_compares_the_version_numbers_inside_the_texts()
    {
        var runner = new FakeRunner();
        runner.Versions[ProviderKind.Claude] = "2.1.3 (Claude Code)";
        runner.Versions[ProviderKind.Codex] = "codex-cli 0.135.0";
        var o = new UpdateOrchestrator([new ProviderCliUpdateItem(ProviderKind.Claude, runner, Listing()), new ProviderCliUpdateItem(ProviderKind.Codex, runner, Listing())]);
        await o.CheckNow();
        Assert.Equal([UpdateRowState.UpToDate, UpdateRowState.UpdateAvailable], o.Rows.Select(r => r.State));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Items_are_made_for_the_given_providers_only()
    {
        var items = ProviderCliUpdateItem.For([ProviderKind.Claude, ProviderKind.Codex], new FakeRunner(), new NoVersionListing());
        Assert.Equal(["cli:claude", "cli:codex"], items.Select(i => i.Id));
    }
}
