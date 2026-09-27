using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B3 52bc792), :30-958.
// Swift @Observable writes must reach the port's tracked setters (R-OBS-1/3/6).
// All mutations run on TestRuntime's main actor. Executables are offline stand-ins or resolve to nil;
// the credits reader uses loopback HTTP. Private setter reflection only arranges already-cached state.
public sealed class ProviderRegistryNotificationTests
{
    [Trait("Req", "PR-PROVREG-215")] // Services/Providers/ProviderRegistry.swift:31-36,841-857
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:208-212
    [Fact]
    public void A_failed_command_load_notifies_an_error_observer_and_keeps_cached_commands()
    {
        using var runtime = new TestRuntime();
        var script = ProviderProbeStandIns.CopyFor("notification-error").SetExitCode(1);
        using var executable = StandIn(ProviderKind.Claude, script);
        var registry = Registry();
        var directory = script.Folder;
        var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
        runtime.OnMain(() => registry.UpdateCommands(new([new() { Name = "cached", Detail = "kept" }]), ProviderKind.Claude, directory));
        var notices = new List<string>();
        var tracking = ObservationTracking.Track(() => registry.CommandErrors, () => notices.Add(registry.CommandErrors[key]));
        try
        {
            runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, directory, force: true));
            Assert.NotEmpty(registry.CommandErrors[key]); // :857 stores localizedDescription.
            Assert.Equal([registry.CommandErrors[key]], notices); // Observer receives the stored description.
            Assert.Equal("cached", Assert.Single(registry.Commands[key]).Name); // catch never replaces the list.
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Req", "PR-PROVREG-214")] // Services/Providers/ProviderRegistry.swift:819-829
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:208-212
    [Fact]
    public void Updating_commands_notifies_the_error_observer_when_the_cached_error_is_cleared()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        var directory = TempDirectory();
        var key = ProviderRegistry.CommandKey(ProviderKind.Codex, directory);
        runtime.OnMain(() => Cached(registry, nameof(ProviderRegistry.CommandErrors), Errors((key, "old failure"))));
        var cleared = false;
        var tracking = ObservationTracking.Track(() => registry.CommandErrors, () => cleared = !registry.CommandErrors.ContainsKey(key));
        try
        {
            runtime.OnMain(() => registry.UpdateCommands(new([new() { Name = "new", Detail = "new list" }]), ProviderKind.Codex, directory));
            Assert.True(cleared); // :829 commandErrors[key] = nil is observable.
            Assert.Empty(registry.CommandErrors);
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Req", "PR-PROVREG-214")] // Services/Providers/ProviderRegistry.swift:821-829
    [Fact]
    public void Eviction_notifies_both_command_and_error_observers_before_inserting_the_new_list()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        var root = TempDirectory();
        for (var index = 0; index < 32; index++)
        {
            runtime.OnMain(() => registry.UpdateCommands(ValueList<SlashCommand>.Empty, ProviderKind.Codex, Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            runtime.AdvanceMilliseconds(1); // Distinct fetched times make index 0 the oldest.
        }
        var oldest = ProviderRegistry.CommandKey(ProviderKind.Codex, Path.Combine(root, "0"));
        runtime.OnMain(() => Cached(registry, nameof(ProviderRegistry.CommandErrors), Errors((oldest, "old failure"))));
        var seen = new List<string>();
        registry.PropertyChanged += (_, change) => seen.Add(change.PropertyName!);
        runtime.OnMain(() => registry.UpdateCommands(ValueList<SlashCommand>.Empty, ProviderKind.Codex, Path.Combine(root, "new")));
        Assert.Equal([nameof(ProviderRegistry.Commands), nameof(ProviderRegistry.CommandErrors), nameof(ProviderRegistry.Commands)], seen);
        Assert.DoesNotContain(oldest, registry.Commands.Keys);
        Assert.Empty(registry.CommandErrors);
        Assert.Equal(32, registry.Commands.Count);
    }

    [Trait("Req", "PR-PROVREG-215")] // Services/Providers/ProviderRegistry.swift:855-857
    [Fact]
    public void A_full_error_cache_notifies_on_wholesale_clear_and_on_the_new_error()
    {
        using var runtime = new TestRuntime();
        var script = ProviderProbeStandIns.CopyFor("notification-error-full").SetExitCode(1);
        using var executable = StandIn(ProviderKind.Claude, script);
        var registry = Registry();
        runtime.OnMain(() => Cached(registry, nameof(ProviderRegistry.CommandErrors),
            new ValueMap<string, string>(Enumerable.Range(0, 32).Select(i => KeyValuePair.Create("old-" + i, "old failure")), SwiftStringComparer.Instance)));
        var sizes = new List<int>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.CommandErrors)) sizes.Add(registry.CommandErrors.Count); };
        runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, script.Folder, force: true));
        Assert.Equal([0, 1], sizes); // :856 clears; :857 inserts the new failure.
        Assert.Equal(ProviderRegistry.CommandKey(ProviderKind.Claude, script.Folder), Assert.Single(registry.CommandErrors).Key);
    }

    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:205-207; Services/Providers/ProviderRegistry.swift:827
    [Fact]
    public void Delivered_commands_notify_the_tracker_that_reads_the_command_map()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        var changed = false;
        var tracking = ObservationTracking.Track(() => registry.Commands, () => changed = true);
        try
        {
            runtime.OnMain(() => registry.UpdateCommands(new([new() { Name = "review", Detail = "Review" }]), ProviderKind.Codex, TempDirectory()));
            Assert.True(changed);
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Req", "PR-PROVREG-216")] // Services/Providers/ProviderRegistry.swift:832-837
    [Fact]
    public void Recording_a_command_notifies_the_recent_commands_tracker()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        var changed = false;
        var tracking = ObservationTracking.Track(() => registry.RecentCommands, () => changed = true);
        try
        {
            runtime.OnMain(() => registry.RecordCommand("review", ProviderKind.Codex));
            Assert.True(changed);
            Assert.Equal(["review"], registry.RecentCommands["codex"]);
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Rule", "R-OBS-1")] // Services/Providers/ProviderRegistry.swift:31,34,796-807
    [Fact]
    public void Updating_a_catalog_notifies_the_catalog_tracker()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        var changed = false;
        var tracking = ObservationTracking.Track(() => registry.Catalogs, () => changed = true);
        try
        {
            runtime.OnMain(() => registry.UpdateCatalog(new([new("live", "Live")]), ProviderKind.Cursor));
            Assert.True(changed);
            Assert.Equal("live", Assert.Single(registry.Catalogs[ProviderKind.Cursor]).Id);
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Rule", "R-OBS-1")] // Services/Providers/ProviderRegistry.swift:33,366-376
    [Fact]
    public void Refreshing_a_missing_CLI_notifies_checking_and_the_final_status()
    {
        using var runtime = new TestRuntime();
        using var executables = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var registry = Registry();
        var states = new List<bool>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.Statuses)) states.Add(registry.Status(ProviderKind.Codex).IsChecking); };
        runtime.Loop.Run(() => registry.Refresh(ProviderKind.Codex));
        Assert.Equal([true, false], states);
    }

    [Trait("Rule", "R-OBS-1")] // Services/Providers/ProviderRegistry.swift:45,558-559,656-659
    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Zai)]
    public void Catalog_loading_notifies_start_and_cleanup_when_no_executable_or_key_is_available(ProviderKind provider)
    {
        using var runtime = new TestRuntime();
        using var executables = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var registry = Registry();
        var states = new List<bool>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.LoadingCatalogs)) states.Add(registry.LoadingCatalogs.Contains(provider)); };
        runtime.Loop.Run(() => registry.LoadCatalog(provider, force: true));
        Assert.Equal([true, false], states);
    }

    [Trait("Req", "PR-PROVREG-166")] // Services/Providers/ProviderRegistry.swift:689-693
    [Fact]
    public void An_empty_API_key_notifies_when_cached_plan_limits_are_removed()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        runtime.OnMain(() => Cached(registry, nameof(ProviderRegistry.PlanLimits),
            new ValueMap<ProviderKind, PlanLimits>([KeyValuePair.Create(ProviderKind.Zai, new PlanLimits { Windows = ValueList<PlanLimits.Window>.Empty })])));
        var changed = false;
        var tracking = ObservationTracking.Track(() => registry.PlanLimits, () => changed = true);
        try
        {
            runtime.OnMain(() => registry.RefreshPlanLimits(ProviderKind.Zai));
            Assert.True(changed);
            Assert.Empty(registry.PlanLimits);
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Req", "PR-PROVREG-169")] // Services/Providers/ProviderRegistry.swift:707-714
    [Fact]
    public void Limits_loading_notifies_insertion_and_deferred_removal_with_no_executable()
    {
        using var runtime = new TestRuntime();
        using var executables = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var registry = Registry();
        var states = new List<bool>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.LoadingLimits)) states.Add(registry.LoadingLimits.Contains(ProviderKind.Codex)); };
        runtime.OnMain(() => registry.RefreshPlanLimits(ProviderKind.Codex));
        runtime.Loop.RunUntil(() => registry.LoadingLimits.Count == 0);
        Assert.Equal([true, false], states);
    }

<    [Fact]
    [Trait("Req", "PR-PROVREG-166")] // Services/Providers/ProviderRegistry.swift:687-694
    public void Clearing_an_empty_key_removes_nonempty_limits_but_preserves_the_known_fetch_timestamp()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        var fetchedAt = SwiftDate.Now - TimeSpan.FromSeconds(30);
        var timestamps = (Dictionary<ProviderKind, DateTimeOffset>)typeof(ProviderRegistry)
            .GetField("limitsFetchedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        runtime.OnMain(() =>
        {
            timestamps[ProviderKind.Zai] = fetchedAt;
            var limits = new PlanLimits
            {
                Windows = new([new() { Id = "cached", Title = "Cached usage", Percent = 42 }]),
            };
            Cached(registry, nameof(ProviderRegistry.PlanLimits), new ValueMap<ProviderKind, PlanLimits>([KeyValuePair.Create(ProviderKind.Zai, limits)]));
        });
        var clears = new List<bool>();
        registry.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ProviderRegistry.PlanLimits)) clears.Add(!registry.PlanLimits.ContainsKey(ProviderKind.Zai));
        };
        // Force bypasses the one-minute gate to reach the empty-key branch even with a recent timestamp.
        runtime.OnMain(() => registry.RefreshPlanLimits(ProviderKind.Zai, force: true));
        Assert.Equal(new[] { true }, clears);
        Assert.Empty(registry.PlanLimits);
        Assert.Empty(registry.LoadingLimits);
        Assert.Equal(fetchedAt, timestamps[ProviderKind.Zai]); // :692 clears only planLimits, never limitsFetchedAt.
    }

    [Trait("Req", "PR-PROVREG-196")] // Services/Providers/ProviderRegistry.swift:729-735
    [Theory]
    [InlineData("credit-1", "credit-1")]
    [InlineData(null, "codex")]
    public void Redeeming_reset_notifies_the_credit_ID_and_deferred_clear_even_on_failure(string? creditID, string activeID)
    {
        using var runtime = new TestRuntime();
        using var executables = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var registry = Registry();
        var states = new List<string?>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.RedeemingReset)) states.Add(registry.RedeemingReset); };
<        Assert.Throws<ResetCreditError>(() => runtime.Loop.Run(() => registry.ConsumeResetCredit(ProviderKind.Codex, creditID)));
        Assert.Equal([activeID, null], states); // :730 creditID ?? provider.rawValue, then :731 defer nil.
    }

    [Trait("Req", "PR-PROVREG-199")] // Services/Providers/ProviderRegistry.swift:766-768
    [Fact]
    public void An_empty_credits_key_notifies_when_the_cached_balance_is_removed()
    {
        using var runtime = new TestRuntime();
        var registry = Registry();
        runtime.OnMain(() => Cached(registry, nameof(ProviderRegistry.Credits), new ValueMap<ProviderKind, ProviderCredits>(
            [KeyValuePair.Create(ProviderKind.Deepseek, new ProviderCredits { Total = 6, Currency = "USD", IsUsable = true })])));
        var changed = false;
        var tracking = ObservationTracking.Track(() => registry.Credits, () => changed = true);
        try
        {
            runtime.OnMain(() => registry.RefreshCredits(ProviderKind.Deepseek));
            Assert.True(changed);
            Assert.Empty(registry.Credits);
        }
        finally { tracking.Tracker.Cancel(); }
    }

    [Trait("Req", "PR-PROVREG-198")] // Services/Providers/ProviderRegistry.swift:771-776
    [Fact]
    public async Task Credits_delivery_notifies_loading_start_loading_finish_and_the_balance()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var settings = new FakeProviderSettings();
        settings.Keys[ProviderKind.Commandcode] = "fixture-key";
        using var executables = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var registry = Registry(settings);
        var states = new List<string>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName is nameof(ProviderRegistry.LoadingCredits) or nameof(ProviderRegistry.Credits)) states.Add(change.PropertyName); };
        runtime.Loop.Run(async () =>
        {
            registry.RefreshCredits(ProviderKind.Commandcode);
            var exchange = await server.NextExchange();
            await exchange.Respond(200, "OK", Encoding.UTF8.GetBytes("{\"credits\":{\"monthlyCredits\":1,\"purchasedCredits\":2,\"freeCredits\":3,\"belowThreshold\":false}}"));
            while (registry.LoadingCredits.Count != 0) await Task.Yield();
        });
        Assert.Equal([nameof(ProviderRegistry.LoadingCredits), nameof(ProviderRegistry.LoadingCredits), nameof(ProviderRegistry.Credits)], states);
        Assert.Equal(6, registry.Credits[ProviderKind.Commandcode].Total); // Delivered reader value is kept (:776).
    }

    [Trait("Rule", "R-OBS-1")] // Services/Providers/ProviderRegistry.swift:289,323-326
    [Fact]
    public void Refresh_everything_notifies_start_and_deferred_finish()
    {
        using var runtime = new TestRuntime();
        using var executables = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var registry = Registry();
        var states = new List<bool>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.IsRefreshing)) states.Add(registry.IsRefreshing); };
        runtime.Loop.Run(() => registry.RefreshEverything());
        Assert.Equal([true, false], states);
    }

    [Trait("Req", "PR-PROVREG-257")] // Services/Providers/ProviderRegistry.swift:54,510-511
    [Fact]
    public void CLI_updating_notifies_once_on_insert_and_once_on_deferred_removal()
    {
        using var runtime = new TestRuntime();
        var script = ProviderProbeStandIns.CopyFor("notification-update").SetStdout("1.0.0\n").SetExitCode(0);
        using var executable = StandIn(ProviderKind.Claude, script);
        var registry = Registry();
        var states = new List<bool>();
        registry.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(ProviderRegistry.UpdatingCLIs)) states.Add(registry.UpdatingCLIs.Contains(ProviderKind.Claude)); };
        runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true));
        Assert.Equal([true, false], states); // Setter notification replaces manual Notify; no double firing.
    }

    private static ProviderRegistry Registry(FakeProviderSettings? settings = null) => new(settings ?? new());
    private static string TempDirectory()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root));
        var path = Path.Combine(root!, "registry-notify", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static ValueMap<string, string> Errors(params (string Key, string Error)[] entries) =>
        new(entries.Select(entry => KeyValuePair.Create(entry.Key, entry.Error)), SwiftStringComparer.Instance);
    private static void Cached<T>(ProviderRegistry registry, string property, T value) =>
        typeof(ProviderRegistry).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(registry, value);
    private static IDisposable StandIn(ProviderKind provider, ProviderProbeScript script) =>
        ProviderRegistry.SetCLIUpdaterExecutableForTesting(kind => kind == provider
            ? SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/')) : null);
}
