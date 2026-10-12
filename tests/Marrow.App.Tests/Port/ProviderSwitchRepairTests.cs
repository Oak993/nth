using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Port;

// ED-NEW-providersmerge-switch-repair-1: the Providers switch of builds before ED-NEW-providers-switch-1 wrote a provider into "disabledProviders" while its knob stayed on. On the owner's PC that
// left Claude and Z.ai off (read-only look at the owner's settings: disabledProviders = ["claude", "zai"], written after the 2026-10-11 copy from the old "Marrow Code" folder, which had none),
// so Claude's models were missing from the picker. Once per data folder, before AppSettings reads the store, those providers are switched back on.
[Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-providersmerge-switch-repair-1
public sealed class ProviderSwitchRepairTests
{
    private static string[] Disabled(ISettingsStore store) => Assert.IsAssignableFrom<IReadOnlyList<string>>(store.StringArray("disabledProviders")).ToArray();

    [Fact]
    public void The_providers_the_dead_switch_wrote_off_are_on_again_and_Claudes_models_are_back_in_the_picker()
    {
        using var harness = new AppModelHarness();
        harness.Defaults.Set("disabledProviders", new List<object> { "claude", "zai", "someFutureProvider" }); // the owner's list, plus a name this build does not know
        Assert.Equal(["claude", "zai", "someFutureProvider"], Disabled(harness.Defaults)); // as the release build leaves it
        Assert.Equal(["claude", "zai"], ProviderSwitchRepair.RunOnce(harness.Defaults));
        Assert.Equal(["someFutureProvider"], Disabled(harness.Defaults)); // a name the switch cannot write is kept, as AppSettings keeps it
        Assert.Equal(true, harness.Defaults.Object(ProviderSwitchRepair.Marker));
        var app = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            Assert.True(app.Settings.IsEnabled(ProviderKind.Claude));
            Assert.True(app.Settings.IsEnabled(ProviderKind.Zai));
            Assert.Contains(ProviderKind.Claude, app.Providers.AvailableProviders);
            Assert.Contains(ModelCatalog.Entries(app, null), entry => entry.Provider == ProviderKind.Claude);
        });
    }

    [Fact]
    public void It_runs_once_so_a_provider_switched_off_after_it_stays_off()
    {
        var store = new InMemorySettingsStore();
        Assert.Empty(ProviderSwitchRepair.RunOnce(store)); // a new data folder: nothing to undo, and the mark is set
        Assert.Equal(["providerSwitchRepair1"], store.Writes);
        store.Set("disabledProviders", new List<object> { "codex" }); // the working switch, later
        store.Writes.Clear();
        Assert.Empty(ProviderSwitchRepair.RunOnce(store));
        Assert.Equal(["codex"], Disabled(store));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void A_list_of_known_providers_only_is_removed_and_a_list_it_cannot_read_is_left_alone()
    {
        var store = new InMemorySettingsStore();
        store.Set("disabledProviders", new List<object> { "zai", "claude", "zai" });
        Assert.Equal(["zai", "claude"], ProviderSwitchRepair.RunOnce(store));
        Assert.Null(store.Object("disabledProviders"));

        store = new InMemorySettingsStore();
        store.Set("disabledProviders", new List<object> { "grok", 5L }); // not a list of strings: AppSettings disables nothing for it, and the repair does not touch it
        store.Writes.Clear();
        Assert.Empty(ProviderSwitchRepair.RunOnce(store));
        Assert.Equal(["providerSwitchRepair1"], store.Writes);
        Assert.Equal(new List<object> { "grok", 5L }, Assert.IsAssignableFrom<IReadOnlyList<object>>(store.Object("disabledProviders")));
    }

    [Fact]
    public void The_app_runs_it_before_it_reads_its_settings()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Marrow.App", "Program.cs"));
        var bound = program.IndexOf("UserDefaults.Standard.SetProcessDefault(settings);", StringComparison.Ordinal);
        var repaired = program.IndexOf("ProviderSwitchRepair.RunOnce(settings);", StringComparison.Ordinal);
        Assert.True(bound > 0 && repaired > bound, "the repair does not run right after the settings store is bound");
        Assert.True(program.IndexOf("new AppSettings(", StringComparison.Ordinal) is < 0 || program.IndexOf("new AppSettings(", StringComparison.Ordinal) > repaired);
        Assert.True(program.IndexOf("new AppModel(", StringComparison.Ordinal) is < 0 || program.IndexOf("new AppModel(", StringComparison.Ordinal) > repaired);
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "Marrow.App", "Program.cs"))) return folder.FullName;
        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
