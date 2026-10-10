using System.Text;
using System.Text.Json;
using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Theme;

namespace Marrow.App.Tests;

// Source under test: DroppyCode/App/AppSettings.swift (B3 52bc792), part 1 of 5: the Hydra keys and pair lookup, the
// API key fields over the protected store (OD-0008 A6, B3; OD-0012 E5), the provider switches, binary paths and last
// models, and the first eight General settings. Every test builds AppSettings on the main loop over a throwaway
// settings file and an in-memory Credential Manager (Fixtures/AppSettingsFixtures.cs).
// B3 (PR-PROVAPI-192): the key-provider table below reads back through the shared Services.Providers.APIKeychain
// (keyed by ProviderKind) instead of the deleted DeepSeekKeychain/MetaKeychain/ZaiKeychain/CommandCodeKeychain
// wrapper types.
[Collection("AppSettings")]
public sealed class AppSettingsTests
{
    // OD-0012 E5: the one line shown under a key field whose key the protected store refused.
    private const string RefusalLine = "Credential Manager did not accept this key, so it was not saved.";

    private static readonly ProviderKind[] AllProviders =
    [
        ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Cursor, ProviderKind.Opencode, ProviderKind.OpencodeGo,
        ProviderKind.Grok, ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, ProviderKind.Devin,
        ProviderKind.Antigravity, ProviderKind.Copilot, ProviderKind.Commandcode, ProviderKind.Pi,
    ];

    private static void WithSettings(Action<AppSettingsScope, AppSettings> body)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() => body(scope, new AppSettings()));
    }

    private static string CredentialTarget(string account) => SecretStore.TargetName(AppInfo.Current.Name, account);

    // OD-0135: an account with no key under "Nth" is looked up once more: its carry-over marker, then the earlier name ("Marrow").
    private static IEnumerable<string> CarryOverTargets(string account) =>
        [CredentialTarget(account) + SecretStore.CarriedMarkerSuffix, SecretStore.TargetName(AppInfo.LegacyNameFor(AppInfo.Current.IsDevelopment), account)];

    // Provider.swift:133-141 apiKeyEnvVar, read at AppSettings.swift:915 from the captured login environment, trimmed.
    private static string EnvironmentKey(string name) =>
        LoginEnvironment.Current.TryGetValue(name, out var value) ? value.Trimmed() : "";

    private static string[] StoredStrings(AppSettingsScope scope, string key) =>
        Assert.IsAssignableFrom<IReadOnlyList<object>>(scope.Defaults.Object(key)).Cast<string>().ToArray();

    private static Dictionary<string, object> StoredMap(AppSettingsScope scope, string key) =>
        new(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(scope.Defaults.Object(key)));

    private static int WritesOf(AppSettingsScope scope, string key) => scope.Defaults.Writes.Count(w => w.Key == key);

    // ---- The shared shapes of PR-SETTINGS-029 to PR-SETTINGS-036 (AppSettings.swift:189-223, :705-712) -------------

    // A String-raw-value setting: `T(rawValue: defaults.string(forKey: k) ?? "") ?? default`, written by its didSet as
    // `defaults.set(x.rawValue, forKey: k)`.
    private static void AssertRawValueSetting<T>(string key, T fallback, Func<AppSettings, T> get, Action<AppSettings, T> set,
        (T Value, string Raw)[] cases)
    {
        // Absent: the default, and building the settings writes nothing (init assigns without didSet).
        WithSettings((scope, settings) =>
        {
            Assert.Equal(fallback, get(settings));
            Assert.Empty(scope.Defaults.Writes);
            Assert.Null(scope.Defaults.Object(key));
        });
        foreach (var (value, raw) in cases)
        {
            // A stored raw value is read back as its case.
            using (var scope = AppSettingsScope.Create())
            {
                scope.Defaults.Seed(key, raw);
                scope.OnMain(() => Assert.Equal(value, get(new AppSettings())));
            }
            // Assigning writes the raw value at once, with no save step, and a relaunch reads it back.
            using (var scope = AppSettingsScope.Create())
            {
                scope.OnMain(() =>
                {
                    var settings = new AppSettings();
                    set(settings, value);
                    Assert.Equal(raw, scope.Defaults.Object(key));
                    Assert.Contains(("set", key), scope.Defaults.Writes);
                    scope.Relaunch();
                    Assert.Equal(value, get(new AppSettings()));
                });
            }
        }
        // An unknown raw value and a value of the wrong type both give the default and stay in the store until the
        // next write replaces them.
        using (var scope = AppSettingsScope.Create())
        {
            scope.Defaults.Seed(key, "notACase");
            scope.OnMain(() =>
            {
                Assert.Equal(fallback, get(new AppSettings()));
                Assert.Equal("notACase", scope.Defaults.Object(key));
            });
        }
        using (var scope = AppSettingsScope.Create())
        {
            scope.Defaults.Seed(key, new List<object> { cases[0].Raw });
            scope.OnMain(() =>
            {
                var settings = new AppSettings();
                Assert.Equal(fallback, get(settings));
                Assert.IsAssignableFrom<IReadOnlyList<object>>(scope.Defaults.Object(key));
                set(settings, cases[^1].Value);
                Assert.Equal(cases[^1].Raw, scope.Defaults.Object(key));
            });
        }
    }

    // A Bool setting read as `defaults.object(forKey: k) as? Bool ?? default` and written by its didSet as
    // `defaults.set(x, forKey: k)`.
    private static void AssertBoolSetting(string key, bool fallback, Func<AppSettings, bool> get, Action<AppSettings, bool> set)
    {
        WithSettings((scope, settings) =>
        {
            Assert.Equal(fallback, get(settings));
            Assert.Empty(scope.Defaults.Writes);
            Assert.Null(scope.Defaults.Object(key));
            set(settings, !fallback);
            Assert.Equal(!fallback, scope.Defaults.Object(key));
            Assert.Equal([("set", key)], scope.Defaults.Writes);
            scope.Relaunch();
            Assert.Equal(!fallback, get(new AppSettings()));
        });
        foreach (var stored in new[] { true, false })
        {
            using var scope = AppSettingsScope.Create();
            scope.Defaults.Seed(key, stored);
            scope.OnMain(() => Assert.Equal(stored, get(new AppSettings())));
        }
        // A wrong type fails the `as? Bool` cast: the default, with the bad value left until the next write.
        using (var scope = AppSettingsScope.Create())
        {
            scope.Defaults.Seed(key, "yes");
            scope.OnMain(() =>
            {
                var settings = new AppSettings();
                Assert.Equal(fallback, get(settings));
                Assert.Equal("yes", scope.Defaults.Object(key));
                set(settings, fallback);
                Assert.Equal(fallback, scope.Defaults.Object(key));
            });
        }
    }

    // ---- PR-SETTINGS-029 to PR-SETTINGS-036 --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-029")] // App/AppSettings.swift:118, :189-191, :705
    public void DefaultProvider_is_stored_under_defaultProvider_as_its_raw_value_and_defaults_to_codex() =>
        AssertRawValueSetting("defaultProvider", ProviderKind.Codex, s => s.DefaultProvider, (s, v) => s.DefaultProvider = v,
        [
            (ProviderKind.Claude, "claude"), (ProviderKind.Cursor, "cursor"), (ProviderKind.Opencode, "opencode"),
            (ProviderKind.OpencodeGo, "opencodeGo"), (ProviderKind.Grok, "grok"), (ProviderKind.Deepseek, "deepseek"),
            (ProviderKind.Meta, "meta"), (ProviderKind.Zai, "zai"), (ProviderKind.Devin, "devin"),
            (ProviderKind.Antigravity, "antigravity"), (ProviderKind.Copilot, "copilot"),
            (ProviderKind.Commandcode, "commandcode"), (ProviderKind.Pi, "pi"), (ProviderKind.Codex, "codex"),
        ]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-030")] // App/AppSettings.swift:119, :193-195, :706
    public void DefaultRuntimeMode_is_stored_under_defaultRuntimeMode_and_defaults_to_full_access() =>
        AssertRawValueSetting("defaultRuntimeMode", RuntimeMode.FullAccess, s => s.DefaultRuntimeMode, (s, v) => s.DefaultRuntimeMode = v,
        [
            (RuntimeMode.Supervised, "supervised"), (RuntimeMode.AutoAcceptEdits, "autoAcceptEdits"),
            (RuntimeMode.Auto, "auto"), (RuntimeMode.FullAccess, "fullAccess"),
        ]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-031")] // App/AppSettings.swift:120, :197-199, :707
    public void DefaultWorkspaceMode_is_stored_under_defaultWorkspaceMode_and_defaults_to_local() =>
        AssertRawValueSetting("defaultWorkspaceMode", WorkspaceMode.Local, s => s.DefaultWorkspaceMode, (s, v) => s.DefaultWorkspaceMode = v,
        [(WorkspaceMode.Worktree, "worktree"), (WorkspaceMode.Local, "local")]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-032")] // App/AppSettings.swift:121, :201-203, :708
    public void NotifyWhenFinished_is_stored_under_notifyWhenFinished_and_an_absent_value_is_true() =>
        AssertBoolSetting("notifyWhenFinished", true, s => s.NotifyWhenFinished, (s, v) => s.NotifyWhenFinished = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-033")] // App/AppSettings.swift:122, :206-208, :709
    [Trait("Req", "PR-THEME-083")] // App/AppSettings.swift:122, :205-208, :709 (chimeWhenFinished, default true)
    public void ChimeWhenFinished_is_stored_under_chimeWhenFinished_and_defaults_to_true() =>
        AssertBoolSetting("chimeWhenFinished", true, s => s.ChimeWhenFinished, (s, v) => s.ChimeWhenFinished = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-034")] // App/AppSettings.swift:123, :210-212, :710
    public void ConfirmBeforeDeleting_is_stored_under_confirmBeforeDeleting_and_defaults_to_true() =>
        AssertBoolSetting("confirmBeforeDeleting", true, s => s.ConfirmBeforeDeleting, (s, v) => s.ConfirmBeforeDeleting = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-035")] // App/AppSettings.swift:124, :216-218, :711
    public void AutoContinueAfterLimit_is_stored_under_autoContinueAfterLimit_and_defaults_to_false() =>
        AssertBoolSetting("autoContinueAfterLimit", false, s => s.AutoContinueAfterLimit, (s, v) => s.AutoContinueAfterLimit = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-036")] // App/AppSettings.swift:125, :221-223, :712
    public void ThreadFinishAction_is_stored_under_threadFinishAction_and_defaults_to_settle() =>
        AssertRawValueSetting("threadFinishAction", ThreadFinishAction.Settle, s => s.ThreadFinishAction, (s, v) => s.ThreadFinishAction = v,
        [(ThreadFinishAction.Archive, "archive"), (ThreadFinishAction.Settle, "settle")]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-029")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-030")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-031")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-032")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-033")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-034")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-035")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-SETTINGS-036")] // App/AppSettings.swift:703-786
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:752-785
    public void Building_the_settings_over_stored_values_writes_nothing_back() => WithSettings((scope, _) =>
    {
        // Seeded values of every kind, including wrong types: init reads them and writes none of them back.
        scope.Defaults.Seed("defaultProvider", "grok");
        scope.Defaults.Seed("notifyWhenFinished", "yes");
        scope.Defaults.Seed("hydraMaxHeads", 12L);
        scope.Defaults.Seed("disabledProviders", new List<object> { "pi" });
        _ = new AppSettings();
        Assert.Empty(scope.Defaults.Writes);
        Assert.Equal("yes", scope.Defaults.Object("notifyWhenFinished"));
        Assert.Equal(12L, scope.Defaults.Object("hydraMaxHeads"));
    });

    // ---- PR-HYDRA-029 ------------------------------------------------------------------------------------------------

    private static readonly (string Key, bool Default, Func<AppSettings, bool> Get, Action<AppSettings, bool> Set)[] HydraSwitches =
    [
        ("hydraEnabled", true, s => s.HydraEnabled, (s, v) => s.HydraEnabled = v),
        ("hydraDefaultEnabled", true, s => s.HydraDefaultEnabled, (s, v) => s.HydraDefaultEnabled = v),
        ("hydraQueueHeads", true, s => s.HydraQueueHeads, (s, v) => s.HydraQueueHeads = v),
        ("hydraAlwaysHeads", false, s => s.HydraAlwaysHeads, (s, v) => s.HydraAlwaysHeads = v),
        ("hydraIsolateHeads", true, s => s.HydraIsolateHeads, (s, v) => s.HydraIsolateHeads = v),
        ("hydraAutoMerge", false, s => s.HydraAutoMerge, (s, v) => s.HydraAutoMerge = v),
        ("hydraReviewHeads", false, s => s.HydraReviewHeads, (s, v) => s.HydraReviewHeads = v),
        ("hydraAutoClearFinished", false, s => s.HydraAutoClearFinished, (s, v) => s.HydraAutoClearFinished = v),
        ("hydraAutoHidesIdleHeads", false, s => s.HydraAutoHidesIdleHeads, (s, v) => s.HydraAutoHidesIdleHeads = v),
        ("hydraShowsHeadDetails", false, s => s.HydraShowsHeadDetails, (s, v) => s.HydraShowsHeadDetails = v),
        ("hydraAutoPopsHeads", false, s => s.HydraAutoPopsHeads, (s, v) => s.HydraAutoPopsHeads = v),
        ("hydraTempersHeadEffort", true, s => s.HydraTempersHeadEffort, (s, v) => s.HydraTempersHeadEffort = v),
    ];

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:752-785
    [Trait("Req", "PR-SETTINGS-062")] // App/AppSettings.swift:156, :529-531, :752 (hydraEnabled defaults to true)
    [Trait("Req", "PR-SETTINGS-063")] // App/AppSettings.swift:158, :536-538, :753 (hydraDefaultEnabled defaults to true)
    [Trait("Req", "PR-SETTINGS-066")] // App/AppSettings.swift:159, :551-553, :759 (hydraQueueHeads defaults to true)
    [Trait("Req", "PR-SETTINGS-067")] // App/AppSettings.swift:160, :558-560, :760 (hydraAlwaysHeads defaults to false)
    [Trait("Req", "PR-SETTINGS-068")] // App/AppSettings.swift:161, :565-567, :761 (hydraIsolateHeads defaults to true)
    [Trait("Req", "PR-SETTINGS-070")] // App/AppSettings.swift:162, :583-585, :763 (hydraAutoMerge defaults to false)
    [Trait("Req", "PR-SETTINGS-071")] // App/AppSettings.swift:163, :589-591, :764 (hydraReviewHeads defaults to false)
    [Trait("Req", "PR-SETTINGS-072")] // App/AppSettings.swift:164, :595-597, :765 (hydraAutoClearFinished defaults to false)
    [Trait("Req", "PR-SETTINGS-073")] // App/AppSettings.swift:165, :602-604, :766 (hydraAutoHidesIdleHeads defaults to false)
    [Trait("Req", "PR-SETTINGS-074")] // App/AppSettings.swift:166, :609-611, :767 (hydraShowsHeadDetails defaults to false)
    [Trait("Req", "PR-SETTINGS-075")] // App/AppSettings.swift:167, :615-617, :768 (hydraAutoPopsHeads defaults to false)
    [Trait("Req", "PR-SETTINGS-076")] // App/AppSettings.swift:168, :623-625, :769 (hydraTempersHeadEffort defaults to true)
    public void Hydra_settings_start_with_Hydra_on_and_the_upstream_defaults() => WithSettings((scope, settings) =>
    {
        Assert.True(settings.HydraEnabled);
        Assert.True(settings.HydraDefaultEnabled);
        Assert.True(settings.HydraQueueHeads);
        Assert.True(settings.HydraIsolateHeads);
        Assert.True(settings.HydraTempersHeadEffort);
        Assert.False(settings.HydraAlwaysHeads);
        Assert.False(settings.HydraAutoMerge);
        Assert.False(settings.HydraReviewHeads);
        Assert.False(settings.HydraAutoClearFinished);
        Assert.False(settings.HydraAutoHidesIdleHeads);
        Assert.False(settings.HydraShowsHeadDetails);
        Assert.False(settings.HydraAutoPopsHeads);
        Assert.Null(settings.HydraMaxHeads); // genuinely absent: not 0 and not 8
        Assert.Empty(settings.HydraPairs);
        Assert.Empty(scope.Defaults.Writes);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:156-177, :529-625
    [Trait("Req", "PR-SETTINGS-062")] // App/AppSettings.swift:156, :529-531 (hydraEnabled writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-063")] // App/AppSettings.swift:158, :536-538 (hydraDefaultEnabled writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-066")] // App/AppSettings.swift:159, :551-553 (hydraQueueHeads writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-067")] // App/AppSettings.swift:160, :558-560 (hydraAlwaysHeads writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-068")] // App/AppSettings.swift:161, :565-567 (hydraIsolateHeads writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-070")] // App/AppSettings.swift:162, :583-585 (hydraAutoMerge writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-071")] // App/AppSettings.swift:163, :589-591 (hydraReviewHeads writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-072")] // App/AppSettings.swift:164, :595-597 (hydraAutoClearFinished writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-073")] // App/AppSettings.swift:165, :602-604 (hydraAutoHidesIdleHeads writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-074")] // App/AppSettings.swift:166, :609-611 (hydraShowsHeadDetails writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-075")] // App/AppSettings.swift:167, :615-617 (hydraAutoPopsHeads writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-076")] // App/AppSettings.swift:168, :623-625 (hydraTempersHeadEffort writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-085")] // App/AppSettings.swift:110-112, :184 (every write persists at once, in the property's own didSet, no save step)
    public void Each_Hydra_switch_is_written_under_its_exact_key_at_once_and_read_back_after_a_relaunch() => WithSettings((scope, settings) =>
    {
        foreach (var (key, fallback, get, set) in HydraSwitches)
        {
            Assert.Equal(fallback, get(settings));
            set(settings, !fallback);
            Assert.Equal(!fallback, scope.Defaults.Object(key));
            Assert.Equal(("set", key), scope.Defaults.Writes[^1]);
        }
        scope.Relaunch();
        var relaunched = new AppSettings();
        foreach (var (key, fallback, get, _) in HydraSwitches)
            Assert.True(!fallback == get(relaunched), key);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:115, :158, :536-538; Core/Models/Library.swift:195-201
    public void The_per_chat_Hydra_default_shares_its_key_with_ChatThread() => WithSettings((scope, settings) =>
    {
        Assert.Equal("hydraDefaultEnabled", AppSettings.HydraDefaultEnabledKey);
        Assert.Equal(ChatThread.HydraDefaultKey, AppSettings.HydraDefaultEnabledKey);
        Assert.True(ChatThread.HydraDefaultEnabled());
        settings.HydraDefaultEnabled = false;
        Assert.False(ChatThread.HydraDefaultEnabled()); // the thread layer reads the same entry without the settings
    });

    [Theory]
    [InlineData(12L, 8L)]
    [InlineData(9L, 8L)]
    [InlineData(8L, 8L)]
    [InlineData(3L, 3L)]
    [InlineData(1L, 1L)]
    [InlineData(0L, 1L)]
    [InlineData(-4L, 1L)]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:762; Core/Models/Hydra.swift:109-111
    [Trait("Req", "PR-SETTINGS-069")] // App/AppSettings.swift:169, :571-579, :762 (clamped into 1...8 on read)
    public void A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(long stored, long expected)
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("hydraMaxHeads", stored);
        scope.OnMain(() => Assert.Equal(expected, new AppSettings().HydraMaxHeads));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:762 (`as? Int` fails, clampedCap(nil) is nil)
    public void A_hydraMaxHeads_of_the_wrong_type_reads_as_no_cap()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("hydraMaxHeads", "4");
        scope.OnMain(() => Assert.Null(new AppSettings().HydraMaxHeads));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:571-579
    [Trait("Req", "PR-SETTINGS-069")] // App/AppSettings.swift:169, :571-579 (no cap is genuinely absent from the store, nil removes the key)
    public void Setting_hydraMaxHeads_writes_the_number_and_setting_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        settings.HydraMaxHeads = 4;
        Assert.Equal(4L, scope.Defaults.Object("hydraMaxHeads"));
        scope.Relaunch();
        var relaunched = new AppSettings();
        Assert.Equal(4L, relaunched.HydraMaxHeads);
        relaunched.HydraMaxHeads = null;
        Assert.Null(scope.Defaults.Object("hydraMaxHeads")); // removed, no sentinel written
        Assert.DoesNotContain(("set", "hydraMaxHeads"), scope.Defaults.Writes);
        scope.Relaunch();
        Assert.Null(new AppSettings().HydraMaxHeads);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:785; Core/Models/Hydra.swift:82-94
    [Trait("Req", "PR-SETTINGS-084")] // App/AppSettings.swift:177, :785 (the only lenient decode: one corrupt element is dropped, not the whole array)
    [Trait("Req", "PR-SETTINGS-087")] // App/AppSettings.swift:864-867 (decode failure -> nil -> empty default)
    public void A_stored_pair_that_fails_to_decode_is_dropped_and_the_others_survive_in_order()
    {
        using var scope = AppSettingsScope.Create();
        var first = new Guid("0B5F1C9E-5D2A-4F7B-9C1E-2A3B4C5D6E7F");
        var broken = new Guid("1C6A2D0F-6E3B-4A8C-8D2F-3B4C5D6E7F80");
        var third = new Guid("2D7B3E1A-7F4C-4B9D-9E3A-4C5D6E7F8091");
        // Bytes a plain JSONEncoder writes for [HydraPair] (Hydra.swift:58-72), the middle one from a newer build
        // naming a provider this build does not know, which `decode(ProviderKind.self, forKey: .provider)` rejects.
        var json = "[{\"id\":\"0B5F1C9E-5D2A-4F7B-9C1E-2A3B4C5D6E7F\",\"provider\":\"claude\",\"headProfiles\":[]},"
            + "{\"id\":\"1C6A2D0F-6E3B-4A8C-8D2F-3B4C5D6E7F80\",\"provider\":\"futureProvider\",\"headProfiles\":[]},"
            + "{\"id\":\"2D7B3E1A-7F4C-4B9D-9E3A-4C5D6E7F8091\",\"provider\":\"codex\",\"orchestratorModel\":\"gpt-5\",\"maxHeads\":3,\"headProfiles\":[]}]";
        scope.Defaults.Seed("hydraPairs", Encoding.UTF8.GetBytes(json));
        scope.OnMain(() =>
        {
            var pairs = new AppSettings().HydraPairs;
            Assert.Equal(2, pairs.Count);
            Assert.Equal(first, pairs[0].Id);
            Assert.Equal(ProviderKind.Claude, pairs[0].Provider);
            Assert.Equal(third, pairs[1].Id);
            Assert.Equal(ProviderKind.Codex, pairs[1].Provider);
            Assert.Equal("gpt-5", pairs[1].OrchestratorModel);
            Assert.Equal(3L, pairs[1].MaxHeads);
            Assert.DoesNotContain(pairs, p => p.Id == broken);
        });
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"provider\":\"claude\"}")]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:785, :864-867 (`try?` gives nil, `?? []`)
    public void A_stored_pair_list_that_cannot_be_read_as_a_list_gives_no_pairs(string stored)
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("hydraPairs", Encoding.UTF8.GetBytes(stored));
        scope.OnMain(() => Assert.Empty(new AppSettings().HydraPairs));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-029")] // App/AppSettings.swift:699-701, :790-792, :785
    [Trait("Req", "PR-SETTINGS-084")] // App/AppSettings.swift:177, :699-701, :785 (hydraPairs writes at once and reads back)
    [Trait("Req", "PR-SETTINGS-099")] // App/AppSettings.swift:790-792 (add appends)
    public void An_added_pair_is_written_under_hydraPairs_and_read_back_after_a_relaunch() => WithSettings((scope, settings) =>
    {
        var pair = new HydraPair(ProviderKind.Claude) with { OrchestratorModel = "opus", MaxHeads = 5 };
        settings.AddHydraPair(pair);
        Assert.IsType<byte[]>(scope.Defaults.Object("hydraPairs"));
        Assert.Contains(("set", "hydraPairs"), scope.Defaults.Writes);
        scope.Relaunch();
        var pairs = new AppSettings().HydraPairs;
        Assert.Single(pairs);
        Assert.Equal(pair, pairs[0]);
    });

    // ---- PR-HYDRA-151 ------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-HYDRA-151")] // App/AppSettings.swift:814-819
    [Trait("Req", "PR-SETTINGS-100")] // App/AppSettings.swift:812-819 (exact, then any-model, then first)
    public void The_pair_for_a_chat_is_the_exact_lead_model_then_an_any_model_pair_then_the_providers_first() => WithSettings((_, settings) =>
    {
        var claudeSonnet = new HydraPair(ProviderKind.Claude) with { OrchestratorModel = "sonnet" };
        var codexAny = new HydraPair(ProviderKind.Codex);
        var claudeAny = new HydraPair(ProviderKind.Claude);
        var claudeOpus = new HydraPair(ProviderKind.Claude) with { OrchestratorModel = "opus" };
        var claudeAnySecond = new HydraPair(ProviderKind.Claude);
        foreach (var pair in new[] { claudeSonnet, codexAny, claudeAny, claudeOpus, claudeAnySecond }) settings.AddHydraPair(pair);

        Assert.Equal(claudeOpus.Id, settings.HydraPair(ProviderKind.Claude, "opus")?.Id);      // exact wins over earlier ones
        Assert.Equal(claudeSonnet.Id, settings.HydraPair(ProviderKind.Claude, "sonnet")?.Id);
        Assert.Equal(claudeAny.Id, settings.HydraPair(ProviderKind.Claude, "haiku")?.Id);      // first any-model pair
        Assert.Equal(claudeAny.Id, settings.HydraPair(ProviderKind.Claude, null)?.Id);         // nil model skips the exact step
        Assert.Equal(codexAny.Id, settings.HydraPair(ProviderKind.Codex, "opus")?.Id);         // only that provider's pairs
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-151")] // App/AppSettings.swift:815-818
    [Trait("Req", "PR-SETTINGS-100")] // App/AppSettings.swift:812-819 (provider with no pairs -> nil)
    public void With_no_any_model_pair_the_providers_first_pair_is_chosen_and_a_provider_without_pairs_has_none() => WithSettings((_, settings) =>
    {
        var codexOnly = new HydraPair(ProviderKind.Codex) with { OrchestratorModel = "gpt-5" };
        var claudeSonnet = new HydraPair(ProviderKind.Claude) with { OrchestratorModel = "sonnet" };
        var claudeOpus = new HydraPair(ProviderKind.Claude) with { OrchestratorModel = "opus" };
        settings.AddHydraPair(codexOnly);
        settings.AddHydraPair(claudeSonnet);
        settings.AddHydraPair(claudeOpus);

        Assert.Equal(claudeSonnet.Id, settings.HydraPair(ProviderKind.Claude, "haiku")?.Id);
        Assert.Equal(claudeSonnet.Id, settings.HydraPair(ProviderKind.Claude, null)?.Id);
        Assert.Null(settings.HydraPair(ProviderKind.Grok, "grok-4"));
        Assert.Null(settings.HydraPair(ProviderKind.Grok, null));
    });

    // ---- PR-PROVAPI-008 to PR-PROVAPI-011, PR-PROVREG-239: key edits over the protected store ------------------------

    // (provider, account, legacy settings key, stored-key reader)
    private static readonly (ProviderKind Provider, string Account, string LegacyKey, Func<string> Stored)[] KeyProviders =
    [
        (ProviderKind.Deepseek, "DeepSeek API Key", "deepseekAPIKey", () => APIKeychain.Key(ProviderKind.Deepseek, "")),
        (ProviderKind.Meta, "Meta API Key", "metaAPIKey", () => APIKeychain.Key(ProviderKind.Meta, "")),
        (ProviderKind.Zai, "Z.ai API Key", "zaiAPIKey", () => APIKeychain.Key(ProviderKind.Zai, "")),
        (ProviderKind.Commandcode, "Command Code API Key", "commandcodeAPIKey", () => APIKeychain.Key(ProviderKind.Commandcode, "")),
    ];

    private static (ProviderKind Provider, string Account, string LegacyKey, Func<string> Stored) KeyProvider(ProviderKind provider) =>
        KeyProviders.Single(k => k.Provider == provider);

    // The accepted half: the key goes to the protected store (trimmed by APIKeychain.swift:39), the provider's legacy
    // plaintext entry is removed (AppSettings.swift:369 with :491-494), and no refusal is shown.
    private static void AssertAcceptedKeyIsStoredAndItsLegacyEntryRemoved(ProviderKind provider, Action<AppSettings, string> edit)
    {
        var (_, account, legacyKey, stored) = KeyProvider(provider);
        using var scope = AppSettingsScope.Create();
        foreach (var other in KeyProviders) scope.Defaults.Seed(other.LegacyKey, "legacy-plain-" + other.LegacyKey);
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            _ = settings.ApiKeyInput(provider); // keys loaded, so the edit below is a user edit
            scope.Credentials.Operations.Clear();

            edit(settings, "  sk-accepted-key \n");

            Assert.Equal("sk-accepted-key", stored());
            Assert.Contains(scope.Credentials.Operations, o => o.Op == "write" && o.Target == CredentialTarget(account));
            Assert.Null(scope.Defaults.Object(legacyKey));
            Assert.DoesNotContain(("set", legacyKey), scope.Defaults.Writes);
            foreach (var other in KeyProviders.Where(k => k.Provider != provider))
                Assert.Equal("legacy-plain-" + other.LegacyKey, scope.Defaults.Object(other.LegacyKey)); // only its own entry
            Assert.Null(settings.APIKeyRefusal(provider));
            Assert.Equal("  sk-accepted-key \n", settings.ApiKeyInput(provider));
            scope.Relaunch();
            var relaunched = new AppSettings();
            Assert.Equal("sk-accepted-key", relaunched.ApiKeyInput(provider)); // read back from the store
        });
    }

    // The refused half (OD-0008 A6, OD-0012 E5): nothing is stored anywhere, the line is shown, and upstream's
    // plaintext write under the legacy key never happens.
    private static void AssertRefusedKeyIsReportedAndStoredNowhere(ProviderKind provider, Action<AppSettings, string> edit)
    {
        var (_, account, legacyKey, stored) = KeyProvider(provider);
        const string key = "sk-refused-5f2c9a7e";
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            _ = settings.ApiKeyInput(provider);
            scope.Credentials.RefuseWrites = true;
            scope.Credentials.Operations.Clear();

            edit(settings, " " + key + " ");

            Assert.Contains(scope.Credentials.Operations, o => o.Op == "write" && o.Target == CredentialTarget(account));
            Assert.Equal(RefusalLine, settings.APIKeyRefusal(provider));
            foreach (var other in KeyProviders.Where(k => k.Provider != provider))
                Assert.Null(settings.APIKeyRefusal(other.Provider));
            Assert.Equal("", stored());
            Assert.False(scope.Credentials.AnyBlobContains(key));
            Assert.Null(scope.Defaults.Object(legacyKey));
            Assert.DoesNotContain(scope.Defaults.Writes, w => w.Op == "set" && KeyProviders.Any(k => k.LegacyKey == w.Key));
            Assert.False(scope.AnyFileContains(key)); // never on disk in plain text
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/APIKeychain.swift:28 (unit plus restart test: the deleted DeepSeekKeychain's own coverage)
    [Trait("Req", "PR-PROVAPI-008")] // App/AppSettings.swift:367-373, :152, :485-494
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:367-373
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:363-373, :415-423, :485-494 (protected store only, no plaintext fallback)
    public void A_DeepSeek_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed() =>
        AssertAcceptedKeyIsStoredAndItsLegacyEntryRemoved(ProviderKind.Deepseek, (s, v) => s.DeepseekAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-PROVAPI-008")] // App/AppSettings.swift:367-373, :485-494
    [Trait("Req", "PR-PROVAPI-011")] // App/AppSettings.swift:371, :487-494
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:367-373
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:363-373, :485-494 (refusal shown, nothing stored)
    public void A_DeepSeek_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere() =>
        AssertRefusedKeyIsReportedAndStoredNowhere(ProviderKind.Deepseek, (s, v) => s.DeepseekAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/APIKeychain.swift:29 (unit plus restart test: the deleted MetaKeychain's own coverage)
    [Trait("Req", "PR-PROVAPI-009")] // App/AppSettings.swift:378-384, :153, :485-494
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:378-384
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:375-408, :415-423, :485-494
    public void A_Meta_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed() =>
        AssertAcceptedKeyIsStoredAndItsLegacyEntryRemoved(ProviderKind.Meta, (s, v) => s.MetaAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-PROVAPI-009")] // App/AppSettings.swift:378-384, :485-494
    [Trait("Req", "PR-PROVAPI-011")] // App/AppSettings.swift:382, :487-494
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:378-384
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:375-408, :485-494
    public void A_Meta_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere() =>
        AssertRefusedKeyIsReportedAndStoredNowhere(ProviderKind.Meta, (s, v) => s.MetaAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/APIKeychain.swift:30 (unit plus restart test: the deleted ZaiKeychain's own coverage)
    [Trait("Req", "PR-PROVAPI-010")] // App/AppSettings.swift:391-397, :154, :485-494
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:391-397
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:375-408, :415-423, :485-494
    public void A_Zai_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed() =>
        AssertAcceptedKeyIsStoredAndItsLegacyEntryRemoved(ProviderKind.Zai, (s, v) => s.ZaiAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-PROVAPI-010")] // App/AppSettings.swift:391-397, :485-494
    [Trait("Req", "PR-PROVAPI-011")] // App/AppSettings.swift:395, :487-494
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:391-397
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:375-408, :485-494
    public void A_Zai_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere() =>
        AssertRefusedKeyIsReportedAndStoredNowhere(ProviderKind.Zai, (s, v) => s.ZaiAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/APIKeychain.swift:31 (write trimmed under "Command Code API Key", read returns it, restart test)
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:402-408, :155, :485-494
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:375-408, :415-423, :485-494
    public void A_Command_Code_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed() =>
        AssertAcceptedKeyIsStoredAndItsLegacyEntryRemoved(ProviderKind.Commandcode, (s, v) => s.CommandcodeAPIKeyInput = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/APIKeychain.swift:31 (refused write stores nothing and shows the refusal line)
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:402-408, :485-494
    [Trait("Req", "PR-PROVAPI-011")] // App/AppSettings.swift:406, :487-494
    [Trait("Req", "PR-SETTINGS-088")] // App/AppSettings.swift:375-408, :485-494
    public void A_Command_Code_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere() =>
        AssertRefusedKeyIsReportedAndStoredNowhere(ProviderKind.Commandcode, (s, v) => s.CommandcodeAPIKeyInput = v);

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Commandcode)]
    [Trait("Req", "PR-PROVAPI-008")] // App/AppSettings.swift:934-943, :367-373
    [Trait("Req", "PR-PROVAPI-009")] // App/AppSettings.swift:934-943, :378-384
    [Trait("Req", "PR-PROVAPI-010")] // App/AppSettings.swift:934-943, :391-397
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:934-943, :402-408
    [Trait("Req", "PR-SETTINGS-091")] // App/AppSettings.swift:934-943 (four-provider switch)
    public void SetAPIKeyInput_edits_the_same_field_as_the_Settings_page(ProviderKind provider)
    {
        AssertAcceptedKeyIsStoredAndItsLegacyEntryRemoved(provider, (s, v) => s.SetAPIKeyInput(v, provider));
        AssertRefusedKeyIsReportedAndStoredNowhere(provider, (s, v) => s.SetAPIKeyInput(v, provider));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-008")] // App/AppSettings.swift:369 (`deepseekAPIKeyInput != oldValue`)
    public void Assigning_the_key_it_already_holds_touches_neither_store() => WithSettings((scope, settings) =>
    {
        settings.DeepseekAPIKeyInput = "sk-same";
        scope.Credentials.Operations.Clear();
        scope.Defaults.Writes.Clear();
        settings.DeepseekAPIKeyInput = "sk-same";
        Assert.Empty(scope.Credentials.Operations);
        Assert.Empty(scope.Defaults.Writes);
    });

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Commandcode)]
    [Trait("Req", "PR-PROVAPI-011")] // App/AppSettings.swift:491-494; Services/Providers/APIKeychain.swift:39-43
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:367-408; Services/Providers/APIKeychain.swift:39-43
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/APIKeychain.swift:31, :39-43 (an empty write deletes the Command Code credential and counts as stored)
    public void Emptying_a_key_field_deletes_the_credential_and_removes_the_legacy_entry(ProviderKind provider)
    {
        var (_, account, legacyKey, stored) = KeyProvider(provider);
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            settings.SetAPIKeyInput("sk-to-clear", provider);
            Assert.Equal("sk-to-clear", stored());
            scope.Defaults.Seed(legacyKey, "legacy-plain");
            scope.Credentials.Operations.Clear();

            settings.SetAPIKeyInput(" \t\n ", provider); // empty once trimmed: a delete, which counts as stored

            Assert.Contains(scope.Credentials.Operations, o => o.Op == "delete" && o.Target == CredentialTarget(account));
            Assert.Equal("", stored());
            Assert.Null(scope.Defaults.Object(legacyKey));
            Assert.Null(settings.APIKeyRefusal(provider));
            Assert.Equal(" \t\n ", settings.ApiKeyInput(provider));
            Assert.DoesNotContain(("set", legacyKey), scope.Defaults.Writes);
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:367-373, :904-910
    public void A_refused_key_stays_in_the_field_for_the_session_and_is_gone_after_a_relaunch()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            _ = settings.ApiKeyInput(ProviderKind.Zai);
            scope.Credentials.RefuseWrites = true;
            settings.ZaiAPIKeyInput = " zai-typed-key ";
            Assert.Equal(" zai-typed-key ", settings.ApiKeyInput(ProviderKind.Zai));
            Assert.Equal("zai-typed-key", settings.ApiKey(ProviderKind.Zai));
            scope.Relaunch();
            var relaunched = new AppSettings();
            Assert.Equal("", relaunched.ApiKeyInput(ProviderKind.Zai));
            Assert.Null(relaunched.APIKeyRefusal(ProviderKind.Zai));
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:407-415, :483-487; Services/Providers/APIKeychain.swift:50-52
    // (a refused update keeps the previously stored key; after relaunch the previously stored key, if there was one,
    // is read back).
    [Trait("Req", "PR-SETTINGS-088")]
    public void A_refused_edit_of_an_existing_key_leaves_the_old_key_in_place_and_it_survives_a_relaunch()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            settings.DeepseekAPIKeyInput = "sk-old-kept"; // the store accepts this one
            Assert.Equal("sk-old-kept", APIKeychain.Key(ProviderKind.Deepseek, ""));

            scope.Credentials.RefuseWrites = true;
            settings.DeepseekAPIKeyInput = "sk-new-refused";

            Assert.Equal(RefusalLine, settings.APIKeyRefusal(ProviderKind.Deepseek));
            Assert.Equal("sk-old-kept", APIKeychain.Key(ProviderKind.Deepseek, "")); // the old credential is untouched
            Assert.False(scope.Credentials.AnyBlobContains("sk-new-refused"));
            Assert.False(scope.AnyFileContains("sk-new-refused"));

            scope.Relaunch();
            var relaunched = new AppSettings();
            Assert.Equal("sk-old-kept", relaunched.ApiKeyInput(ProviderKind.Deepseek));
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:367-373 (the line speaks of the last edit only)
    public void A_later_key_the_store_accepts_takes_the_refusal_line_away() => WithSettings((scope, settings) =>
    {
        _ = settings.ApiKeyInput(ProviderKind.Deepseek);
        scope.Credentials.RefuseWrites = true;
        settings.DeepseekAPIKeyInput = "sk-first";
        Assert.Equal(RefusalLine, settings.APIKeyRefusal(ProviderKind.Deepseek));
        scope.Credentials.RefuseWrites = false;
        settings.DeepseekAPIKeyInput = "sk-second";
        Assert.Null(settings.APIKeyRefusal(ProviderKind.Deepseek));
        Assert.Equal("sk-second", APIKeychain.Key(ProviderKind.Deepseek, ""));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-239")] // App/AppSettings.swift:367-408 (a store unavailable altogether refuses every edit)
    public void With_the_store_refusing_everything_every_key_edit_shows_the_same_line_and_nothing_reaches_disk() => WithSettings((scope, settings) =>
    {
        _ = settings.ApiKeyInput(ProviderKind.Deepseek);
        scope.Credentials.RefuseWrites = true;
        foreach (var (provider, _, _, _) in KeyProviders) settings.SetAPIKeyInput("sk-everywhere-" + SwiftEnum<ProviderKind>.RawValue(provider), provider);
        foreach (var (provider, _, legacyKey, _) in KeyProviders)
        {
            Assert.Equal(RefusalLine, settings.APIKeyRefusal(provider));
            Assert.Null(scope.Defaults.Object(legacyKey));
        }
        Assert.False(scope.AnyFileContains("sk-everywhere-"));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-239")] // OD-0012 E5 wording, byte for byte
    public void The_refusal_line_is_the_exact_ruled_sentence() => WithSettings((scope, settings) =>
    {
        _ = settings.ApiKeyInput(ProviderKind.Meta);
        scope.Credentials.RefuseWrites = true;
        settings.MetaAPIKeyInput = "sk-meta";
        var line = Assert.IsType<string>(settings.APIKeyRefusal(ProviderKind.Meta));
        Assert.Equal(Encoding.UTF8.GetBytes("Credential Manager did not accept this key, so it was not saved."),
            Encoding.UTF8.GetBytes(line));
        Assert.Null(settings.APIKeyRefusal(ProviderKind.Codex));
    });

    // ---- PR-PROVAPI-012, PR-PROVAPI-013: the one deferred read of the stored keys ------------------------------------

    private static void SeedCredentials(AppSettingsScope scope, params ProviderKind[] providers)
    {
        foreach (var provider in providers)
        {
            var (_, account, _, _) = KeyProvider(provider);
            Assert.True(SecretStore.Shared.Current.SetReplacing(AppInfo.Current.Name, account, "stored-" + account));
        }
        scope.Credentials.Operations.Clear();
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-012")] // App/AppSettings.swift:740-744
    public void Building_the_settings_reads_no_credential_and_every_key_field_starts_empty()
    {
        using var scope = AppSettingsScope.Create();
        SeedCredentials(scope, ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, ProviderKind.Commandcode);
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Empty(scope.Credentials.Operations);
            Assert.Equal("", settings.DeepseekAPIKeyInput);
            Assert.Equal("", settings.MetaAPIKeyInput);
            Assert.Equal("", settings.ZaiAPIKeyInput);
            Assert.Equal("", settings.CommandcodeAPIKeyInput);
            Assert.Empty(scope.Credentials.Operations);
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-012")] // App/AppSettings.swift:441-449, :459-466
    [Trait("Req", "PR-PROVAPI-013")] // App/AppSettings.swift:468-476
    [Trait("Req", "PR-SETTINGS-089")] // App/AppSettings.swift:441-483 (one lazy cached read, re-entry guard)
    public void The_background_load_reads_each_account_once_off_the_main_thread_and_fills_the_fields_without_writing_back()
    {
        using var scope = AppSettingsScope.Create();
        SeedCredentials(scope, ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, ProviderKind.Commandcode);
        scope.Runtime.Loop.Run(async () =>
        {
            var settings = new AppSettings();
            await settings.LoadKeychainInBackground();

            Assert.Equal("stored-DeepSeek API Key", settings.DeepseekAPIKeyInput);
            Assert.Equal("stored-Meta API Key", settings.MetaAPIKeyInput);
            Assert.Equal("stored-Z.ai API Key", settings.ZaiAPIKeyInput);
            Assert.Equal("stored-Command Code API Key", settings.CommandcodeAPIKeyInput);
            var operations = scope.Credentials.Operations.ToList();
            Assert.All(operations, o => Assert.Equal("read", o.Op));      // the fill never writes a key back
            Assert.All(operations, o => Assert.False(o.OnMain));          // detached, off the main loop
            Assert.Equal(
                new[] { "DeepSeek API Key", "Meta API Key", "Z.ai API Key", "Command Code API Key", "OpenRouter API Key" /* ED-234 */ }.Select(CredentialTarget)
                    .Concat(CarryOverTargets("OpenRouter API Key")) // the one account with no stored key (OD-0135)
                    .Order(StringComparer.Ordinal),
                operations.Select(o => o.Target).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(scope.Defaults.Writes, w => w.Op == "set");

            // Once per process: a second load, an ask and an edit-free read all reach the store no more.
            await settings.LoadKeychainInBackground();
            _ = settings.ApiKeyInput(ProviderKind.Meta);
            _ = settings.ApiKey(ProviderKind.Zai);
            Assert.Equal(operations.Count, scope.Credentials.Operations.Count);
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-012")] // App/AppSettings.swift:454-457, :923-925
    [Trait("Req", "PR-PROVREG-089")] // App/AppSettings.swift:904-905 (read at most once per run, on the first call)
    [Trait("Req", "PR-SETTINGS-089")] // App/AppSettings.swift:454-457 (synchronous ask wins the race)
    public void The_first_ask_reads_the_credentials_itself_exactly_once()
    {
        using var scope = AppSettingsScope.Create();
        SeedCredentials(scope, ProviderKind.Deepseek, ProviderKind.Commandcode);
        scope.Runtime.Loop.Run(async () =>
        {
            var settings = new AppSettings();
            Assert.Equal("stored-DeepSeek API Key", settings.ApiKey(ProviderKind.Deepseek));
            var reads = scope.Credentials.Operations.Count;
            Assert.Equal( // one batch, four accounts and OpenRouter's (ED-234), each read once; the three with no stored key also look up the earlier name once (OD-0135)
                new[] { "DeepSeek API Key", "Meta API Key", "Z.ai API Key", "Command Code API Key", "OpenRouter API Key" }.Select(CredentialTarget)
                    .Concat(new[] { "Meta API Key", "Z.ai API Key", "OpenRouter API Key" }.SelectMany(CarryOverTargets)).Order(StringComparer.Ordinal),
                scope.Credentials.Operations.Select(o => o.Target).Distinct().Order(StringComparer.Ordinal));
            Assert.All(scope.Credentials.Operations, o => Assert.Equal("read", o.Op));
            Assert.Equal("stored-Command Code API Key", settings.ApiKeyInput(ProviderKind.Commandcode));
            _ = settings.ApiKey(ProviderKind.Commandcode);
            _ = settings.HasAPIKey(ProviderKind.Deepseek);
            await settings.LoadKeychainInBackground(); // the ask came first: the background load has nothing to do
            Assert.Equal(reads, scope.Credentials.Operations.Count);
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-012")] // App/AppSettings.swift:419-422, :432-439 (DEVIATION OD-0008 A6: no plaintext read)
    [Trait("Req", "PR-PROVAPI-013")] // App/AppSettings.swift:477-482
    [Trait("Req", "PR-PROVREG-089")] // App/AppSettings.swift:904-916 (DEVIATION OD-0008 A6)
    public void Filling_removes_legacy_entries_only_for_accounts_the_store_holds_and_never_reads_them_as_keys()
    {
        using var scope = AppSettingsScope.Create();
        SeedCredentials(scope, ProviderKind.Deepseek, ProviderKind.Zai);
        foreach (var (_, _, legacyKey, _) in KeyProviders) scope.Defaults.Seed(legacyKey, "legacy-plain-" + legacyKey);
        scope.Runtime.Loop.Run(async () =>
        {
            var settings = new AppSettings();
            await settings.LoadKeychainInBackground();

            Assert.Equal("stored-DeepSeek API Key", settings.DeepseekAPIKeyInput);
            Assert.Equal("stored-Z.ai API Key", settings.ZaiAPIKeyInput);
            Assert.Equal("", settings.MetaAPIKeyInput);          // upstream would take the plaintext copy here
            Assert.Equal("", settings.CommandcodeAPIKeyInput);
            Assert.Null(scope.Defaults.Object("deepseekAPIKey"));
            Assert.Null(scope.Defaults.Object("zaiAPIKey"));
            Assert.Equal("legacy-plain-metaAPIKey", scope.Defaults.Object("metaAPIKey"));
            Assert.Equal("legacy-plain-commandcodeAPIKey", scope.Defaults.Object("commandcodeAPIKey"));
            Assert.DoesNotContain(scope.Defaults.Writes, w => w.Op == "set");
            Assert.All(scope.Credentials.Operations, o => Assert.Equal("read", o.Op));
            Assert.Equal("", settings.ApiKeyInput(ProviderKind.Meta));
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-089")] // App/AppSettings.swift:904-916 (DEVIATION OD-0008 A6: no plaintext fallback)
    [Trait("Req", "PR-PROVAPI-012")] // App/AppSettings.swift:419-422 (DEVIATION OD-0008 A6)
    public void A_legacy_plaintext_entry_is_never_sent_as_the_key_the_environment_is_used_instead()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("metaAPIKey", "legacy-plain-meta");
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal(EnvironmentKey("MODEL_API_KEY"), settings.ApiKey(ProviderKind.Meta));
            Assert.NotEqual("legacy-plain-meta", settings.ApiKey(ProviderKind.Meta));
            Assert.Equal("legacy-plain-meta", scope.Defaults.Object("metaAPIKey")); // not in the store: left alone
        });
    }

    // ---- PR-PROVAPI-014, PR-PROVREG-089, PR-PROVREG-090: the key a provider receives ---------------------------------

    [Theory]
    [InlineData(ProviderKind.Deepseek, "DEEPSEEK_API_KEY")]
    [InlineData(ProviderKind.Meta, "MODEL_API_KEY")]
    [InlineData(ProviderKind.Zai, "ZAI_API_KEY")]
    [InlineData(ProviderKind.Commandcode, "COMMAND_CODE_API_KEY")]
    [Trait("Req", "PR-PROVAPI-014")] // App/AppSettings.swift:913-915; Core/Models/Provider.swift:133-141
    [Trait("Req", "PR-PROVREG-089")] // App/AppSettings.swift:913-915
    [Trait("Decision", "ED-028")]
    [Trait("Req", "PR-SETTINGS-090")] // App/AppSettings.swift:900-916 (environment fallback)
    public void With_nothing_stored_the_key_is_the_providers_environment_variable_trimmed(ProviderKind provider, string variable) =>
        WithSettings((_, settings) =>
        {
            // ED-028 (3): the login environment is the process environment Windows populated at logon.
            Assert.Equal(EnvironmentKey(variable), settings.ApiKey(provider));
            Assert.Equal(EnvironmentKey(variable).Length != 0, settings.HasAPIKey(provider));
        });

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Commandcode)]
    [Trait("Req", "PR-PROVAPI-014")] // App/AppSettings.swift:906-913
    [Trait("Req", "PR-PROVREG-089")] // App/AppSettings.swift:906-913
    [Trait("Req", "PR-SETTINGS-090")] // App/AppSettings.swift:900-916 (stored value wins)
    public void A_stored_key_wins_over_the_environment_and_is_sent_trimmed(ProviderKind provider) => WithSettings((_, settings) =>
    {
        settings.SetAPIKeyInput(" \n sk-typed-key \t", provider);
        Assert.Equal("sk-typed-key", settings.ApiKey(provider));
        Assert.True(settings.HasAPIKey(provider));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-089")] // App/AppSettings.swift:911, :914
    [Trait("Req", "PR-SETTINGS-090")] // App/AppSettings.swift:900-916 (acceptsAPIKey false -> "")
    public void Providers_that_take_no_key_get_the_empty_string() => WithSettings((_, settings) =>
    {
        foreach (var provider in AllProviders.Where(p => !p.AcceptsAPIKey))
        {
            Assert.Equal("", settings.ApiKey(provider));
            Assert.False(settings.HasAPIKey(provider));
        }
    });

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Commandcode)]
    [Trait("Req", "PR-PROVREG-090")] // App/AppSettings.swift:904-910, :923-931
    [Trait("Req", "PR-SETTINGS-091")] // App/AppSettings.swift:922-932 (apiKeyInput(for:))
    public void The_field_value_is_untrimmed_while_the_key_sent_is_trimmed(ProviderKind provider) => WithSettings((_, settings) =>
    {
        settings.SetAPIKeyInput("  sk-spaced ", provider);
        Assert.Equal("  sk-spaced ", settings.ApiKeyInput(provider));
        Assert.Equal("sk-spaced", settings.ApiKey(provider));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-090")] // App/AppSettings.swift:925-931, :936-942
    [Trait("Req", "PR-SETTINGS-091")] // App/AppSettings.swift:922-943 (silent default branch)
    public void Providers_without_a_key_field_read_an_empty_field_and_ignore_edits() => WithSettings((scope, settings) =>
    {
        foreach (var provider in AllProviders.Where(p => !p.AcceptsAPIKey))
        {
            settings.SetAPIKeyInput("sk-ignored", provider);
            Assert.Equal("", settings.ApiKeyInput(provider));
        }
        Assert.DoesNotContain(scope.Credentials.Operations, o => o.Op != "read");
        Assert.Empty(scope.Defaults.Writes);
    });

    // ---- PR-PROVREG-084, PR-PROVREG-085: provider switches --------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-084")] // App/AppSettings.swift:746, :878-880
    [Trait("Req", "PR-SETTINGS-093")] // App/AppSettings.swift:878-885 (absent is enabled)
    public void Every_provider_is_enabled_on_a_fresh_store() => WithSettings((scope, settings) =>
    {
        foreach (var provider in AllProviders) Assert.True(settings.IsEnabled(provider));
        Assert.Empty(settings.DisabledProviders);
        Assert.Null(scope.Defaults.Object("disabledProviders"));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-084")] // App/AppSettings.swift:139, :746, :878-880
    public void Only_a_raw_value_listed_under_disabledProviders_disables_and_unknown_entries_are_kept()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("disabledProviders", new List<object> { "grok", "someFutureProvider", "opencodeGo" });
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            foreach (var provider in AllProviders)
                Assert.Equal(provider is not (ProviderKind.Grok or ProviderKind.OpencodeGo), settings.IsEnabled(provider));
            Assert.Equal(["grok", "someFutureProvider", "opencodeGo"], settings.DisabledProviders);
            settings.SetEnabled(false, ProviderKind.Pi);
            Assert.Equal(["grok", "someFutureProvider", "opencodeGo", "pi"], StoredStrings(scope, "disabledProviders"));
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-084")] // App/AppSettings.swift:746 (`stringArray(forKey:) ?? []`)
    public void A_disabledProviders_value_that_is_not_a_list_of_strings_disables_nothing()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("disabledProviders", new List<object> { "grok", 5L });
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.True(settings.IsEnabled(ProviderKind.Grok));
            Assert.Empty(settings.DisabledProviders);
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-085")] // App/AppSettings.swift:882-885, :500-502
    [Trait("Req", "PR-SETTINGS-093")] // App/AppSettings.swift:882-885 (remove-then-append, no duplicates)
    public void Disabling_moves_the_provider_to_the_end_and_enabling_removes_every_copy_one_write_each()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("disabledProviders", new List<object> { "codex", "claude", "codex", "grok" });
        scope.OnMain(() =>
        {
            var settings = new AppSettings();

            settings.SetEnabled(false, ProviderKind.Codex);
            Assert.Equal(["claude", "grok", "codex"], StoredStrings(scope, "disabledProviders"));
            Assert.Equal(1, WritesOf(scope, "disabledProviders"));
            Assert.False(settings.IsEnabled(ProviderKind.Codex));

            settings.SetEnabled(false, ProviderKind.Claude);
            Assert.Equal(["grok", "codex", "claude"], StoredStrings(scope, "disabledProviders"));
            Assert.Equal(2, WritesOf(scope, "disabledProviders"));

            settings.SetEnabled(true, ProviderKind.Codex);
            Assert.Equal(["grok", "claude"], StoredStrings(scope, "disabledProviders"));
            Assert.Equal(3, WritesOf(scope, "disabledProviders"));
            Assert.True(settings.IsEnabled(ProviderKind.Codex));

            settings.SetEnabled(true, ProviderKind.Pi); // not listed: still one write of the unchanged list
            Assert.Equal(["grok", "claude"], StoredStrings(scope, "disabledProviders"));
            Assert.Equal(4, WritesOf(scope, "disabledProviders"));

            scope.Relaunch();
            var relaunched = new AppSettings();
            Assert.Equal(["grok", "claude"], relaunched.DisabledProviders);
            Assert.False(relaunched.IsEnabled(ProviderKind.Claude));
        });
    }

    // ---- PR-PROVREG-086: binary paths ---------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-086")] // App/AppSettings.swift:138, :496-498, :869-876
    [Trait("Req", "PR-SETTINGS-092")] // App/AppSettings.swift:873-876 (trimmed)
    public void A_binary_path_is_stored_trimmed_under_the_providers_raw_value_one_write_per_change() => WithSettings((scope, settings) =>
    {
        Assert.Equal("", settings.BinaryPath(ProviderKind.OpencodeGo));
        settings.SetBinaryPath("  C:\\Tools\\opencode.exe \n", ProviderKind.OpencodeGo);
        Assert.Equal("C:\\Tools\\opencode.exe", settings.BinaryPath(ProviderKind.OpencodeGo));
        Assert.Equal(new Dictionary<string, object> { ["opencodeGo"] = "C:\\Tools\\opencode.exe" }, StoredMap(scope, "providerBinaryPaths"));
        Assert.Equal(1, WritesOf(scope, "providerBinaryPaths"));
        settings.SetBinaryPath("C:\\Tools\\claude.cmd", ProviderKind.Claude);
        Assert.Equal(2, WritesOf(scope, "providerBinaryPaths"));
        scope.Relaunch();
        var relaunched = new AppSettings();
        Assert.Equal("C:\\Tools\\opencode.exe", relaunched.BinaryPath(ProviderKind.OpencodeGo));
        Assert.Equal("C:\\Tools\\claude.cmd", relaunched.BinaryPath(ProviderKind.Claude));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-086")] // App/AppSettings.swift:873-876
    [Trait("Req", "PR-SETTINGS-092")] // App/AppSettings.swift:873-876 (empty removes rather than stores)
    public void Clearing_a_binary_path_removes_its_entry_instead_of_storing_an_empty_string() => WithSettings((scope, settings) =>
    {
        settings.SetBinaryPath("C:\\Tools\\codex.exe", ProviderKind.Codex);
        settings.SetBinaryPath("C:\\Tools\\grok.exe", ProviderKind.Grok);
        settings.SetBinaryPath(" \t\n", ProviderKind.Codex);
        Assert.Equal("", settings.BinaryPath(ProviderKind.Codex));
        Assert.Equal(new Dictionary<string, object> { ["grok"] = "C:\\Tools\\grok.exe" }, StoredMap(scope, "providerBinaryPaths"));
        Assert.False(settings.BinaryPaths.ContainsKey("codex"));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-086")] // App/AppSettings.swift:745 (`as? [String: String] ?? [:]`)
    public void A_stored_binary_path_map_holding_a_non_string_is_discarded_on_load()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("providerBinaryPaths", new Dictionary<string, object> { ["codex"] = "C:\\codex.exe", ["claude"] = 5L });
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal("", settings.BinaryPath(ProviderKind.Codex));
            Assert.Empty(settings.BinaryPaths);
        });
    }

    // ---- PR-PROVREG-087: last model per provider ----------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-087")] // App/AppSettings.swift:140-141, :504-510, :887-898
    [Trait("Req", "PR-SETTINGS-094")] // App/AppSettings.swift:887-898 (one call, two dictionaries)
    public void Remember_stores_the_last_model_and_effort_by_raw_value_and_a_relaunch_reads_them_back() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.LastModel(ProviderKind.Codex));
        settings.Remember("gpt-5-codex", "high", ProviderKind.Codex);
        Assert.Equal("gpt-5-codex", settings.LastModel(ProviderKind.Codex));
        Assert.Equal("high", settings.LastEffort(ProviderKind.Codex));
        Assert.Equal(new Dictionary<string, object> { ["codex"] = "gpt-5-codex" }, StoredMap(scope, "lastModels"));
        Assert.Equal(new Dictionary<string, object> { ["codex"] = "high" }, StoredMap(scope, "lastEfforts"));
        scope.Relaunch();
        var relaunched = new AppSettings();
        Assert.Equal("gpt-5-codex", relaunched.LastModel(ProviderKind.Codex));
        Assert.Null(relaunched.LastModel(ProviderKind.Claude));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-087")] // App/AppSettings.swift:747, :887-889 (a stale id is kept, never cleared)
    public void A_stored_model_id_is_returned_as_is_and_reading_it_writes_nothing()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("lastModels", new Dictionary<string, object> { ["claude"] = "retired-model-2024" });
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal("retired-model-2024", settings.LastModel(ProviderKind.Claude));
            Assert.Empty(scope.Defaults.Writes);
        });
    }

    // ---- PR-SETTINGS-037 to PR-SETTINGS-061 (part 2 of 5) ------------------------------------------------------------

    // A Double setting read as `defaults.object(forKey: k) as? Double ?? default` and written by its didSet as
    // `defaults.set(x, forKey: k)`, applied verbatim with no clamp.
    private static void AssertDoubleSetting(string key, double fallback, double sample, Func<AppSettings, double> get, Action<AppSettings, double> set)
    {
        WithSettings((scope, settings) =>
        {
            Assert.Equal(fallback, get(settings));
            Assert.Empty(scope.Defaults.Writes);
            Assert.Null(scope.Defaults.Object(key));
            set(settings, sample);
            Assert.Equal(sample, scope.Defaults.Object(key));
            Assert.Equal([("set", key)], scope.Defaults.Writes);
            scope.Relaunch();
            Assert.Equal(sample, get(new AppSettings()));
        });
        using (var scope = AppSettingsScope.Create())
        {
            scope.Defaults.Seed(key, sample);
            scope.OnMain(() => Assert.Equal(sample, get(new AppSettings())));
        }
        using (var scope = AppSettingsScope.Create())
        {
            scope.Defaults.Seed(key, "not-a-number");
            scope.OnMain(() =>
            {
                var settings = new AppSettings();
                Assert.Equal(fallback, get(settings));
                Assert.Equal("not-a-number", scope.Defaults.Object(key));
            });
        }
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-037")] // App/AppSettings.swift:126, :226-228, :713
    [Trait("Req", "PR-THEME-085")] // App/AppSettings.swift:126, :225-228, :713 (settleSound, default true)
    public void SettleSound_is_stored_under_settleSound_and_defaults_to_true() =>
        AssertBoolSetting("settleSound", true, s => s.SettleSound, (s, v) => s.SettleSound = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-038")] // App/AppSettings.swift:127, :230-232, :714
    public void ShowReasoning_is_stored_under_showReasoning_and_defaults_to_false() =>
        AssertBoolSetting("showReasoning", false, s => s.ShowReasoning, (s, v) => s.ShowReasoning = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-266")] // App/AppSettings.swift:128, :234-239, :715 (chronologicalTimeline, default false, no UI control)
    public void ChronologicalTimeline_is_stored_under_chronologicalTimeline_and_defaults_to_false() =>
        AssertBoolSetting("chronologicalTimeline", false, s => s.ChronologicalTimeline, (s, v) => s.ChronologicalTimeline = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-039")] // App/AppSettings.swift:129, :241-243, :716
    public void AntiSlopEnabled_is_stored_under_antiSlopEnabled_and_defaults_to_true() =>
        AssertBoolSetting("antiSlopEnabled", true, s => s.AntiSlopEnabled, (s, v) => s.AntiSlopEnabled = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-040")] // App/AppSettings.swift:130, :247-249, :717
    public void ShowsWorkingCard_is_stored_under_showsWorkingCard_and_defaults_to_true() =>
        AssertBoolSetting("showsWorkingCard", true, s => s.ShowsWorkingCard, (s, v) => s.ShowsWorkingCard = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-265")] // App/AppSettings.swift:131, :251-255, :718; UI/Common/AppZoom.swift:6,8-10 (appZoom, default 100, normalized to the eight levels on read)
    public void AppZoom_is_stored_under_appZoom_defaults_to_100_and_is_normalized_to_a_known_level_on_read() => WithSettings((scope, settings) =>
    {
        Assert.Equal(100L, settings.AppZoom);
        Assert.Empty(scope.Defaults.Writes);
        Assert.Null(scope.Defaults.Object("appZoom"));

        settings.AppZoom = 150;
        Assert.Equal(150L, scope.Defaults.Object("appZoom"));
        Assert.Equal([("set", "appZoom")], scope.Defaults.Writes);
        scope.Relaunch();
        Assert.Equal(150L, new AppSettings().AppZoom);
    });

    [Theory]
    [InlineData(75L, 75L)]
    [InlineData(90L, 90L)]
    [InlineData(100L, 100L)]
    [InlineData(110L, 110L)]
    [InlineData(125L, 125L)]
    [InlineData(150L, 150L)]
    [InlineData(175L, 175L)]
    [InlineData(200L, 200L)]
    [InlineData(130L, 100L)] // not one of the eight levels: reads as 100, not clamped to the nearest
    [InlineData(0L, 100L)]
    [Trait("Req", "PR-SETTINGS-265")] // UI/Common/AppZoom.swift:8-10 normalized(_:) - only the eight levels survive a read
    public void A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(long stored, long expected)
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("appZoom", stored);
        scope.OnMain(() =>
        {
            Assert.Equal(expected, new AppSettings().AppZoom);
            Assert.Equal(stored, scope.Defaults.Object("appZoom")); // left as-is until the next write
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-265")] // App/AppSettings.swift:718 (`as? Int` fails the wrong type, 100 is used)
    public void An_appZoom_of_the_wrong_type_reads_as_100()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("appZoom", "150");
        scope.OnMain(() => Assert.Equal(100L, new AppSettings().AppZoom));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-042")] // App/AppSettings.swift:157, :260-268, :756-758
    public void PanelSize_persists_as_a_two_element_array_and_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.PanelSize);
        Assert.Null(scope.Defaults.Object("floatingPanelSize"));

        settings.PanelSize = new Size(640, 480);
        Assert.Equal(new List<object> { 640.0, 480.0 }, scope.Defaults.Object("floatingPanelSize"));
        scope.Relaunch();
        Assert.Equal(new Size(640, 480), new AppSettings().PanelSize);

        var relaunched = new AppSettings();
        relaunched.PanelSize = null;
        Assert.Equal([("remove", "floatingPanelSize")], scope.Defaults.Writes[^1..]);
        Assert.Null(scope.Defaults.Object("floatingPanelSize"));
        scope.Relaunch();
        Assert.Null(new AppSettings().PanelSize);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-042")] // App/AppSettings.swift:756-758 (`stored.count == 2` else nil)
    public void An_array_of_the_wrong_length_leaves_PanelSize_nil()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("floatingPanelSize", new List<object> { 640.0 });
        scope.OnMain(() => Assert.Null(new AppSettings().PanelSize));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-043")] // App/AppSettings.swift:132, :271-273, :719
    public void SidebarActivityView_is_stored_under_sidebarActivityView_and_defaults_to_false() =>
        AssertBoolSetting("sidebarActivityView", false, s => s.SidebarActivityView, (s, v) => s.SidebarActivityView = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-044")] // App/AppSettings.swift:133, :277-279, :720
    public void ActivityThreadStyle_is_stored_under_activityThreadStyle_and_defaults_to_icon() =>
        AssertRawValueSetting("activityThreadStyle", ActivityThreadStyle.Icon, s => s.ActivityThreadStyle, (s, v) => s.ActivityThreadStyle = v,
        [(ActivityThreadStyle.ProjectName, "projectName"), (ActivityThreadStyle.Icon, "icon")]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-045")] // App/AppSettings.swift:134, :282-284, :721
    [Trait("Req", "PR-SIDEBAR-124")] // App/AppSettings.swift:134, :282-284, :721 (key differs from the property name: settledSectionCollapsed)
    public void SettledCollapsed_is_stored_under_settledSectionCollapsed_and_defaults_to_true() =>
        AssertBoolSetting("settledSectionCollapsed", true, s => s.SettledCollapsed, (s, v) => s.SettledCollapsed = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-046")] // App/AppSettings.swift:135, :286-291, :723-728
    [Trait("Req", "PR-THEME-044")] // App/AppSettings.swift:135, :286-291 (persist then mirror into ThemeManager, in that order)
    public void Theme_is_stored_under_appTheme_defaults_to_system_and_writing_it_updates_ThemeManager() => WithSettings((scope, settings) =>
    {
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Empty(scope.Defaults.Writes);

        settings.Theme = AppTheme.Dracula;
        Assert.Equal("dracula", scope.Defaults.Object("appTheme"));
        Assert.Equal(AppTheme.Dracula, ThemeManager.Current);
        scope.Relaunch();
        var relaunched = new AppSettings();
        Assert.Equal(AppTheme.Dracula, relaunched.Theme);
        Assert.Equal(AppTheme.Dracula, ThemeManager.Current);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-046")] // App/AppSettings.swift:723-726 (legacy "appearance" fallback, never migrated)
    [Trait("Req", "PR-THEME-045")] // App/AppSettings.swift:137, :723-728 (appTheme, then legacy appearance, then System; legacy key never written)
    public void An_absent_appTheme_falls_back_to_the_legacy_appearance_key_which_stays_unwritten()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("appearance", "dark");
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal(AppTheme.Dark, settings.Theme);
            Assert.Null(scope.Defaults.Object("appTheme"));
            Assert.Equal("dark", scope.Defaults.Object("appearance"));
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-045")] // App/AppSettings.swift:724-726 (an unknown appTheme id falls through to appearance too)
    public void An_unrecognized_appTheme_id_falls_through_to_the_legacy_appearance_key()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("appTheme", "notARealTheme");
        scope.Defaults.Seed("appearance", "light");
        scope.OnMain(() => Assert.Equal(AppTheme.Light, new AppSettings().Theme));
    }

    [Fact]
    [Trait("Req", "PR-THEME-045")] // App/AppSettings.swift:724-728 (both keys missing or unknown: System)
    public void Both_theme_keys_missing_or_unknown_resolves_to_System()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("appTheme", "notARealTheme");
        scope.Defaults.Seed("appearance", "alsoNotReal");
        scope.OnMain(() => Assert.Equal(AppTheme.System, new AppSettings().Theme));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-047")] // App/AppSettings.swift:136, :293-299, :722
    public void BackdropOpacity_is_stored_under_backdropOpacity_defaults_to_one_half_and_is_never_clamped() =>
        AssertDoubleSetting("backdropOpacity", AppSettings.DefaultBackdropOpacity, 1.25, s => s.BackdropOpacity, (s, v) => s.BackdropOpacity = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-048")] // App/AppSettings.swift:143, :301-303, :729
    public void TextGeneration_is_stored_under_textGeneration_and_defaults_to_automatic() =>
        AssertRawValueSetting("textGeneration", TextGenerationChoice.Automatic, s => s.TextGeneration, (s, v) => s.TextGeneration = v,
        [(TextGenerationChoice.Claude, "claude"), (TextGenerationChoice.Codex, "codex"), (TextGenerationChoice.Off, "off"),
            (TextGenerationChoice.Automatic, "automatic")]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-049")] // App/AppSettings.swift:144, :305-307, :730
    public void CommitInstructions_persists_free_text_verbatim_with_no_trimming_and_defaults_to_empty() => WithSettings((scope, settings) =>
    {
        Assert.Equal("", settings.CommitInstructions);
        Assert.Empty(scope.Defaults.Writes);
        Assert.Null(scope.Defaults.Object("commitInstructions"));
        const string text = "  keep this text exactly\nincluding the final line  ";
        settings.CommitInstructions = text;
        Assert.Equal(text, scope.Defaults.Object("commitInstructions"));
        scope.Relaunch();
        Assert.Equal(text, new AppSettings().CommitInstructions);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-050")] // App/AppSettings.swift:145, :309-311, :731
    public void TerminalHeight_is_stored_under_terminalHeight_defaults_to_260_and_is_never_clamped() =>
        AssertDoubleSetting("terminalHeight", 260, 999, s => s.TerminalHeight, (s, v) => s.TerminalHeight = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-051")] // App/AppSettings.swift:148, :313-316, :732
    public void RecentDownloadsPicker_is_stored_under_recentDownloadsPicker_and_defaults_to_true() =>
        AssertBoolSetting("recentDownloadsPicker", true, s => s.RecentDownloadsPicker, (s, v) => s.RecentDownloadsPicker = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-052")] // App/AppSettings.swift:149, :320-328, :733-737
    public void LastProjectID_persists_as_its_uppercase_uuidString_and_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.LastProjectID);
        var id = Guid.Parse("7b3d0dd5-4de2-4f34-9f45-a2bf3657d671");

        settings.LastProjectID = id;
        Assert.Equal("7B3D0DD5-4DE2-4F34-9F45-A2BF3657D671", scope.Defaults.Object("lastProjectID"));
        scope.Relaunch();
        Assert.Equal(id, new AppSettings().LastProjectID);

        var relaunched = new AppSettings();
        relaunched.LastProjectID = null;
        Assert.Null(scope.Defaults.Object("lastProjectID"));
        scope.Relaunch();
        Assert.Null(new AppSettings().LastProjectID);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-052")] // App/AppSettings.swift:733-737 (an invalid uuidString yields nil, key untouched)
    public void An_invalid_stored_lastProjectID_string_yields_nil_without_clearing_the_key()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("lastProjectID", "not-a-uuid");
        scope.OnMain(() =>
        {
            Assert.Null(new AppSettings().LastProjectID);
            Assert.Equal("not-a-uuid", scope.Defaults.Object("lastProjectID"));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-053")] // App/AppSettings.swift:150, :333-335, :738
    public void ProjectsEnabled_is_stored_under_projectsEnabled_and_defaults_to_true() =>
        AssertBoolSetting("projectsEnabled", true, s => s.ProjectsEnabled, (s, v) => s.ProjectsEnabled = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-054")] // App/AppSettings.swift:151, :337-361, :739
    [Trait("Req", "PR-SETTINGS-095")] // App/AppSettings.swift:344-361 (three-state override model)
    [Trait("Req", "PR-SIDEBAR-132")] // App/AppSettings.swift:151, :338-361, :739 (Activate/Deactivate set, Follow global clears, keyed by uuidString)
    public void ProjectActivationOverrides_follow_the_global_switch_until_explicitly_overridden() => WithSettings((scope, settings) =>
    {
        var id = Guid.Parse("7b3d0dd5-4de2-4f34-9f45-a2bf3657d671");
        Assert.Empty(settings.ProjectActivationOverrides);
        Assert.False(settings.HasProjectActivationOverride(id));
        Assert.True(settings.IsProjectActive(id)); // follows projectsEnabled (default true)

        settings.ProjectsEnabled = false;
        Assert.False(settings.IsProjectActive(id)); // still follows the switch

        settings.SetProjectActive(true, id);
        Assert.True(settings.HasProjectActivationOverride(id));
        Assert.True(settings.IsProjectActive(id)); // its own explicit choice
        var stored = Assert.IsType<byte[]>(scope.Defaults.Object("projectActivationOverrides"));
        using (var document = JsonDocument.Parse(stored))
            Assert.True(document.RootElement.GetProperty("7B3D0DD5-4DE2-4F34-9F45-A2BF3657D671").GetBoolean());

        scope.Relaunch();
        var relaunched = new AppSettings();
        Assert.True(relaunched.IsProjectActive(id));

        relaunched.ClearProjectActivationOverride(id);
        Assert.False(relaunched.HasProjectActivationOverride(id));
        Assert.False(relaunched.IsProjectActive(id)); // follows the switch again
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-055")] // App/AppSettings.swift:138, :496-498, :745
    public void BinaryPaths_persists_under_the_exact_key_providerBinaryPaths() => WithSettings((scope, settings) =>
    {
        settings.SetBinaryPath("C:\\Tools\\codex.exe", ProviderKind.Codex);
        Assert.Equal(new Dictionary<string, object> { ["codex"] = "C:\\Tools\\codex.exe" }, StoredMap(scope, "providerBinaryPaths"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-056")] // App/AppSettings.swift:139, :500-502, :746
    public void DisabledProviders_persists_under_the_exact_key_disabledProviders() => WithSettings((scope, settings) =>
    {
        settings.SetEnabled(false, ProviderKind.Grok);
        Assert.Equal(["grok"], StoredStrings(scope, "disabledProviders"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-057")] // App/AppSettings.swift:140, :504-506, :747
    public void LastModels_persists_under_the_exact_key_lastModels() => WithSettings((scope, settings) =>
    {
        settings.Remember("model-from-picker", null, ProviderKind.Codex);
        Assert.Equal(new Dictionary<string, object> { ["codex"] = "model-from-picker" }, StoredMap(scope, "lastModels"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-058")] // App/AppSettings.swift:141, :508-510, :748
    public void LastEfforts_persists_under_the_exact_key_lastEfforts() => WithSettings((scope, settings) =>
    {
        settings.Remember(null, "high", ProviderKind.Codex);
        Assert.Equal(new Dictionary<string, object> { ["codex"] = "high" }, StoredMap(scope, "lastEfforts"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-059")] // App/AppSettings.swift:142, :515-517, :749
    public void LastHydraPairID_persists_as_its_uuidString_and_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.LastHydraPairID);
        var id = Guid.Parse("7b3d0dd5-4de2-4f34-9f45-a2bf3657d671");

        settings.LastHydraPairID = id;
        Assert.Equal("7B3D0DD5-4DE2-4F34-9F45-A2BF3657D671", scope.Defaults.Object("lastHydraPairID"));
        scope.Relaunch();
        Assert.Equal(id, new AppSettings().LastHydraPairID);

        var relaunched = new AppSettings();
        relaunched.LastHydraPairID = null;
        Assert.Null(scope.Defaults.Object("lastHydraPairID"));
        scope.Relaunch();
        Assert.Null(new AppSettings().LastHydraPairID);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-060")] // App/AppSettings.swift:96-101, :146, :519-521, :750
    [Trait("Req", "PR-SETTINGS-096")] // App/AppSettings.swift:823-834 (cap 15, silent no-op past the cap)
    public void ModelList_persists_as_JSON_capped_at_15_and_ModelPin_encodes_only_provider_and_modelID() => WithSettings((scope, settings) =>
    {
        Assert.Empty(settings.ModelList);
        Assert.Null(scope.Defaults.Object("modelList"));

        var pin = new ModelPin(ProviderKind.Codex, "model-a");
        settings.AddToModelList(pin);
        Assert.Equal([pin], settings.ModelList);
        var data = Assert.IsType<byte[]>(scope.Defaults.Object("modelList"));
        using (var document = JsonDocument.Parse(data))
        {
            var entry = Assert.Single(document.RootElement.EnumerateArray().ToList());
            Assert.Equal(["modelID", "provider"], entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
        scope.Relaunch();
        Assert.Equal([pin], new AppSettings().ModelList);

        var full = new AppSettings();
        for (var i = 0; i < AppSettings.ModelListLimit; i++) full.AddToModelList(new ModelPin(ProviderKind.Codex, $"model-{i}"));
        Assert.Equal(AppSettings.ModelListLimit, full.ModelList.Count);
        full.AddToModelList(new ModelPin(ProviderKind.Codex, "one-too-many"));
        Assert.Equal(AppSettings.ModelListLimit, full.ModelList.Count); // the cap refuses the extra entry
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-060")] // App/AppSettings.swift:750 (decode failure yields an empty list)
    [Trait("Req", "PR-SETTINGS-087")] // App/AppSettings.swift:750, :864-867 (decode failure -> nil -> empty default)
    public void ModelList_decode_failure_yields_an_empty_list()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("modelList", "not-json-data"u8.ToArray());
        scope.OnMain(() => Assert.Empty(new AppSettings().ModelList));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-061")] // App/AppSettings.swift:104-107, :147, :523-525, :751
    [Trait("Req", "PR-SETTINGS-098")] // App/AppSettings.swift:850-858 (equal to default is removed, not stored)
    public void ModelPreferences_persists_by_ModelPin_id_and_a_default_preference_is_removed_not_stored() => WithSettings((scope, settings) =>
    {
        Assert.Equal(new ModelPreference(), settings.Preference(ProviderKind.Codex, "model-a"));
        Assert.Null(scope.Defaults.Object("modelPreferences"));

        settings.SetPreference(new ModelPreference { Effort = "high", FastMode = true }, ProviderKind.Codex, "model-a");
        Assert.NotNull(scope.Defaults.Object("modelPreferences"));
        scope.Relaunch();
        var relaunched = new AppSettings();
        Assert.Equal(new ModelPreference { Effort = "high", FastMode = true }, relaunched.Preference(ProviderKind.Codex, "model-a"));

        relaunched.SetPreference(new ModelPreference(), ProviderKind.Codex, "model-a"); // equal to default: removed
        Assert.False(relaunched.ModelPreferences.ContainsKey("codex/model-a"));
        scope.Relaunch();
        Assert.Equal(new ModelPreference(), new AppSettings().Preference(ProviderKind.Codex, "model-a"));
    });

    // ---- PR-SETTINGS-064 to PR-SETTINGS-085 (part 3 of 5) ---------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-064")] // App/AppSettings.swift:178, :540-542, :754
    public void HasSeenTour_is_stored_under_hasSeenTour_and_defaults_to_false() =>
        AssertBoolSetting("hasSeenTour", false, s => s.HasSeenTour, (s, v) => s.HasSeenTour = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-065")] // App/AppSettings.swift:179, :545-547, :755
    public void HasSeenHydraIntro_is_stored_under_hasSeenHydraIntro_and_defaults_to_false() =>
        AssertBoolSetting("hasSeenHydraIntro", false, s => s.HasSeenHydraIntro, (s, v) => s.HasSeenHydraIntro = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-077")] // App/AppSettings.swift:170, :631-633, :770
    [Trait("Req", "PR-USAGE-075")] // App/AppSettings.swift:170, :631-633, :770 (showsUsagePanel, default false)
    public void ShowsUsagePanel_is_stored_under_showsUsagePanel_and_defaults_to_false() =>
        AssertBoolSetting("showsUsagePanel", false, s => s.ShowsUsagePanel, (s, v) => s.ShowsUsagePanel = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-078")] // App/AppSettings.swift:171, :637-639, :771
    public void SidebarFloats_is_stored_under_sidebarFloats_and_defaults_to_false() =>
        AssertBoolSetting("sidebarFloats", false, s => s.SidebarFloats, (s, v) => s.SidebarFloats = v);

    [Fact]
    [Trait("Req", "PR-SETTINGS-079")] // App/AppSettings.swift:172, :643-645, :772
    public void SidebarOnlyFloats_is_stored_under_sidebarOnlyFloats_and_defaults_to_false() =>
        AssertBoolSetting("sidebarOnlyFloats", false, s => s.SidebarOnlyFloats, (s, v) => s.SidebarOnlyFloats = v);

    [Fact]
    [Trait("Rule", "R-COND-1")] // App/AppSettings.swift:648-657, computed from sidebarFloats and sidebarOnlyFloats
    [Trait("Req", "PR-SETTINGS-101")] // App/AppSettings.swift:647-657 (one three-way choice over two booleans)
    public void SidebarMode_is_the_computed_pair_of_the_two_sidebar_switches() => WithSettings((_, settings) =>
    {
        Assert.Equal(Marrow.App.SidebarMode.Column, settings.SidebarMode);

        settings.SidebarMode = Marrow.App.SidebarMode.Floating;
        Assert.True(settings.SidebarFloats);
        Assert.False(settings.SidebarOnlyFloats);
        Assert.Equal(Marrow.App.SidebarMode.Floating, settings.SidebarMode);

        settings.SidebarMode = Marrow.App.SidebarMode.PanelOnly;
        Assert.True(settings.SidebarFloats);
        Assert.True(settings.SidebarOnlyFloats);
        Assert.Equal(Marrow.App.SidebarMode.PanelOnly, settings.SidebarMode);

        // sidebarFloats false and sidebarOnlyFloats true is reachable in the store but reads back as .column.
        settings.SidebarOnlyFloats = true;
        settings.SidebarFloats = false;
        Assert.Equal(Marrow.App.SidebarMode.Column, settings.SidebarMode);

        settings.SidebarMode = Marrow.App.SidebarMode.Column;
        Assert.False(settings.SidebarFloats);
        Assert.False(settings.SidebarOnlyFloats);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-080")] // App/AppSettings.swift:173, :660-668, :773-777
    public void SidebarPanelOrigin_persists_as_a_two_element_array_and_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.SidebarPanelOrigin);
        Assert.Null(scope.Defaults.Object("sidebarPanelOrigin"));

        settings.SidebarPanelOrigin = new Point(12.5, -3.25);
        Assert.Equal(new List<object> { 12.5, -3.25 }, scope.Defaults.Object("sidebarPanelOrigin"));
        scope.Relaunch();
        Assert.Equal(new Point(12.5, -3.25), new AppSettings().SidebarPanelOrigin);

        var relaunched = new AppSettings();
        relaunched.SidebarPanelOrigin = null;
        Assert.Equal([("remove", "sidebarPanelOrigin")], scope.Defaults.Writes[^1..]);
        Assert.Null(scope.Defaults.Object("sidebarPanelOrigin"));
        scope.Relaunch();
        Assert.Null(new AppSettings().SidebarPanelOrigin);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-080")] // App/AppSettings.swift:773-777 (`stored.count == 2` else nil)
    public void An_array_of_the_wrong_length_leaves_SidebarPanelOrigin_nil()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("sidebarPanelOrigin", new List<object> { 12.5 });
        scope.OnMain(() => Assert.Null(new AppSettings().SidebarPanelOrigin));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-081")] // App/AppSettings.swift:174, :671-679, :778-782
    public void SidebarPanelSize_persists_as_a_two_element_array_and_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.SidebarPanelSize);
        Assert.Null(scope.Defaults.Object("sidebarPanelSize"));

        settings.SidebarPanelSize = new Size(320, 480);
        Assert.Equal(new List<object> { 320.0, 480.0 }, scope.Defaults.Object("sidebarPanelSize"));
        scope.Relaunch();
        Assert.Equal(new Size(320, 480), new AppSettings().SidebarPanelSize);

        var relaunched = new AppSettings();
        relaunched.SidebarPanelSize = null;
        Assert.Equal([("remove", "sidebarPanelSize")], scope.Defaults.Writes[^1..]);
        Assert.Null(scope.Defaults.Object("sidebarPanelSize"));
        scope.Relaunch();
        Assert.Null(new AppSettings().SidebarPanelSize);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-081")] // App/AppSettings.swift:778-782 (`stored.count == 2` else nil)
    public void An_array_of_the_wrong_length_leaves_SidebarPanelSize_nil()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("sidebarPanelSize", new List<object> { 320.0 });
        scope.OnMain(() => Assert.Null(new AppSettings().SidebarPanelSize));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-082")] // App/AppSettings.swift:175, :683-685, :783
    [Trait("Req", "PR-USAGE-075")] // App/AppSettings.swift:175, :683-685, :783 (usagePanelDock, default bottomLeading)
    public void UsagePanelDock_is_stored_under_usagePanelDock_and_defaults_to_bottomLeading() =>
        AssertRawValueSetting("usagePanelDock", PanelDockCorner.BottomLeading, s => s.UsagePanelDock, (s, v) => s.UsagePanelDock = v,
        [
            (PanelDockCorner.TopLeading, "topLeading"), (PanelDockCorner.TopTrailing, "topTrailing"),
            (PanelDockCorner.BottomTrailing, "bottomTrailing"), (PanelDockCorner.BottomLeading, "bottomLeading"),
        ]);

    [Fact]
    [Trait("Req", "PR-SETTINGS-083")] // App/AppSettings.swift:176, :688-696, :784
    [Trait("Req", "PR-USAGE-075")] // App/AppSettings.swift:176, :688-696, :784 (usagePanelHeight, no default, nil removes the key)
    public void UsagePanelHeight_persists_as_a_Double_and_nil_removes_the_key() => WithSettings((scope, settings) =>
    {
        Assert.Null(settings.UsagePanelHeight);
        Assert.Null(scope.Defaults.Object("usagePanelHeight"));

        settings.UsagePanelHeight = 212.5;
        Assert.Equal(212.5, scope.Defaults.Object("usagePanelHeight"));
        Assert.Equal([("set", "usagePanelHeight")], scope.Defaults.Writes[^1..]);
        scope.Relaunch();
        Assert.Equal(212.5, new AppSettings().UsagePanelHeight);

        var relaunched = new AppSettings();
        relaunched.UsagePanelHeight = null;
        Assert.Equal([("remove", "usagePanelHeight")], scope.Defaults.Writes[^1..]);
        Assert.Null(scope.Defaults.Object("usagePanelHeight"));
        scope.Relaunch();
        Assert.Null(new AppSettings().UsagePanelHeight);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-083")] // App/AppSettings.swift:784 (`as? Double` fails the wrong type, nil, not clamped)
    public void A_usagePanelHeight_of_the_wrong_type_reads_as_nil_and_is_never_clamped()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("usagePanelHeight", "212.5");
        scope.OnMain(() => Assert.Null(new AppSettings().UsagePanelHeight));
    }

    // ---- ED-149: ModelPin as a value RowDrag can hold -----------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-149")]
    public void ModelPin_is_a_value_with_a_real_nil_in_RowDrag_content_equality_and_a_two_member_encoding() => WithSettings((scope, settings) =>
    {
        Assert.Null(new RowDrag<ModelPin>().Id);
        var pin = new ModelPin(ProviderKind.OpencodeGo, "kimi-k2");
        Assert.True(pin == new ModelPin(ProviderKind.OpencodeGo, "kimi-k2"));
        Assert.False(pin == new ModelPin(ProviderKind.Opencode, "kimi-k2"));
        Assert.Equal("opencodeGo/kimi-k2", pin.Id); // AppSettings.swift:100
        Assert.True(new RowDrag<ModelPin> { Id = pin }.Id == pin);

        settings.AddToModelList(pin);
        var data = Assert.IsType<byte[]>(scope.Defaults.Object("modelList"));
        using var document = JsonDocument.Parse(data);
        var entry = Assert.Single(document.RootElement.EnumerateArray().ToList());
        Assert.Equal(["modelID", "provider"], entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("opencodeGo", entry.GetProperty("provider").GetString());
        Assert.Equal("kimi-k2", entry.GetProperty("modelID").GetString());
    });

    // ---- PR-SETTINGS-086 to PR-SETTINGS-100, PR-SETTINGS-106, PR-SETTINGS-253 to PR-SETTINGS-259 (part 4 of 5) ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-086")] // App/AppSettings.swift:184, :708, :869 (CaptureRun.defaults NOT-PORTED, ED-001;
                                       // B2 named the same indirection WebsiteCaptures.defaults; porting/dead_code.md
                                       // entry 44: capture-mode short circuits are user-unreachable)
    public void Settings_resolve_a_single_store_so_a_write_from_one_construction_is_seen_by_another() => WithSettings((scope, settings) =>
    {
        settings.NotifyWhenFinished = false;
        Assert.Equal(false, scope.Defaults.Object("notifyWhenFinished"));
        var again = new AppSettings();
        Assert.False(again.NotifyWhenFinished); // the same one store, not a second, capture-only suite
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-087")] // App/AppSettings.swift:864-867 (decode failure -> nil -> empty default)
    public void ModelPreferences_decode_failure_yields_an_empty_map()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("modelPreferences", "not-json-data"u8.ToArray());
        scope.OnMain(() => Assert.Empty(new AppSettings().ModelPreferences));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-087")] // App/AppSettings.swift:864-867 (decode failure -> nil -> empty default)
    public void ProjectActivationOverrides_decode_failure_yields_an_empty_map()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("projectActivationOverrides", "not-json-data"u8.ToArray());
        scope.OnMain(() => Assert.Empty(new AppSettings().ProjectActivationOverrides));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-097")] // App/AppSettings.swift:836-848 (moveModel, with the from < dest correction)
    public void MoveModel_places_the_pin_before_or_after_the_target_with_the_index_correction() => WithSettings((_, settings) =>
    {
        var a = new ModelPin(ProviderKind.Codex, "a");
        var b = new ModelPin(ProviderKind.Codex, "b");
        var c = new ModelPin(ProviderKind.Codex, "c");
        settings.AddToModelList(a);
        settings.AddToModelList(b);
        settings.AddToModelList(c);
        Assert.Equal([a, b, c], settings.ModelList);

        // Move a (index 0) to after c (index 2): dest = 2 + 1 = 3, from(0) < dest(3) so dest -= 1 => 2.
        Assert.True(settings.MoveModel(a, c, placeAfter: true));
        Assert.Equal([b, c, a], settings.ModelList);

        // Move a (now index 2) to before b (index 0): dest = 0, from(2) not < dest(0), no correction.
        Assert.True(settings.MoveModel(a, b, placeAfter: false));
        Assert.Equal([a, b, c], settings.ModelList);

        // Same pin as target: false, no change.
        Assert.False(settings.MoveModel(a, a, placeAfter: false));
        Assert.Equal([a, b, c], settings.ModelList);

        // A pin not in the list: false, no change.
        var missing = new ModelPin(ProviderKind.Codex, "missing");
        Assert.False(settings.MoveModel(missing, a, placeAfter: false));
        Assert.False(settings.MoveModel(a, missing, placeAfter: false));

        // Computed destination equal to source: no-op, false. Moving b (index 1) to before c (index 2) lands
        // back at index 1.
        Assert.False(settings.MoveModel(b, c, placeAfter: false));
        Assert.Equal([a, b, c], settings.ModelList);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-099")] // App/AppSettings.swift:794-805 (update by id, clamp, equality guard; remove)
    public void UpdateHydraPair_clamps_the_cap_and_drops_a_no_op_write_and_remove_drops_by_id() => WithSettings((scope, settings) =>
    {
        var pair = new Marrow.Core.Models.HydraPair(ProviderKind.Codex);
        settings.AddHydraPair(pair);
        var writesAfterAdd = scope.Defaults.Writes.Count(w => w.Key == "hydraPairs");

        // Unknown id: silent no-op, no persist.
        settings.UpdateHydraPair(Guid.NewGuid(), p => p with { Name = "nope" });
        Assert.Equal(writesAfterAdd, scope.Defaults.Writes.Count(w => w.Key == "hydraPairs"));

        // A real change with an out-of-range cap: clamped into 1...8 and persisted.
        settings.UpdateHydraPair(pair.Id, p => p with { MaxHeads = 99 });
        Assert.Equal(8, settings.HydraPair(pair.Id)!.MaxHeads);
        Assert.Equal(writesAfterAdd + 1, scope.Defaults.Writes.Count(w => w.Key == "hydraPairs"));

        // A no-op mutation (returns the same values, post-clamp): dropped, no persist.
        settings.UpdateHydraPair(pair.Id, p => p with { MaxHeads = 99 }); // clamps to 8 again, same as stored
        Assert.Equal(writesAfterAdd + 1, scope.Defaults.Writes.Count(w => w.Key == "hydraPairs"));

        // nil stays nil under the clamp.
        settings.UpdateHydraPair(pair.Id, p => p with { MaxHeads = null });
        Assert.Null(settings.HydraPair(pair.Id)!.MaxHeads);

        settings.RemoveHydraPair(pair.Id);
        Assert.Null(settings.HydraPair(pair.Id));
    });

    [Theory]
    [InlineData(TextGenerationChoice.Automatic, "Automatic")]
    [InlineData(TextGenerationChoice.Claude, "Claude")]
    [InlineData(TextGenerationChoice.Codex, "Codex")]
    [InlineData(TextGenerationChoice.Off, "Off")]
    [Trait("Req", "PR-SETTINGS-106")] // App/AppSettings.swift:11-18
    public void TextGenerationChoice_titles_are_exact(TextGenerationChoice choice, string title) =>
        Assert.Equal(title, choice.Title);

    [Fact]
    [Trait("Req", "PR-SETTINGS-106")] // App/AppSettings.swift:9, :3-7 (raw values are the persisted ids, in declaration order)
    public void TextGenerationChoice_ids_are_the_raw_values_in_declaration_order()
    {
        Assert.Equal(
            [TextGenerationChoice.Automatic, TextGenerationChoice.Claude, TextGenerationChoice.Codex, TextGenerationChoice.Off],
            Enum.GetValues<TextGenerationChoice>());
        Assert.Equal("automatic", TextGenerationChoice.Automatic.Id);
        Assert.Equal("claude", TextGenerationChoice.Claude.Id);
        Assert.Equal("codex", TextGenerationChoice.Codex.Id);
        Assert.Equal("off", TextGenerationChoice.Off.Id);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-253")] // App/AppSettings.swift:32, :39
    public void ThreadFinishAction_settle_title_and_detail_are_exact()
    {
        Assert.Equal("Settle", ThreadFinishAction.Settle.Title);
        Assert.Equal("A settled thread drops to the bottom of the sidebar, small and grey, until you reopen it",
            ThreadFinishAction.Settle.Detail);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-254")] // App/AppSettings.swift:33, :40
    public void ThreadFinishAction_archive_title_and_detail_are_exact()
    {
        Assert.Equal("Archive", ThreadFinishAction.Archive.Title);
        Assert.Equal("An archived thread leaves the sidebar for the Archive page", ThreadFinishAction.Archive.Detail);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-255")] // App/AppSettings.swift:55, :63
    public void SidebarMode_column_title_and_detail_are_exact()
    {
        Assert.Equal("Column", Marrow.App.SidebarMode.Column.Title);
        Assert.Equal("A column beside the chat; the list button in the toolbar shows and hides it",
            Marrow.App.SidebarMode.Column.Detail);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-256")] // App/AppSettings.swift:56, :64
    public void SidebarMode_floating_title_and_detail_are_exact()
    {
        Assert.Equal("Floating", Marrow.App.SidebarMode.Floating.Title);
        Assert.Equal("With the column collapsed, the thread list floats as a panel over the chat and stays where you leave it",
            Marrow.App.SidebarMode.Floating.Detail);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-257")] // App/AppSettings.swift:57, :65
    public void SidebarMode_panelOnly_title_and_detail_are_exact()
    {
        Assert.Equal("Panel only", Marrow.App.SidebarMode.PanelOnly.Title);
        Assert.Equal("No column and no toolbar button; the floating panel is the sidebar",
            Marrow.App.SidebarMode.PanelOnly.Detail);
        Assert.Equal("panelOnly", Marrow.App.SidebarMode.PanelOnly.Id); // the id, never persisted (PR-SETTINGS-257)
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-258")] // App/AppSettings.swift:82, :89
    public void ActivityThreadStyle_icon_title_and_detail_are_exact()
    {
        Assert.Equal("Icon", ActivityThreadStyle.Icon.Title);
        Assert.Equal("Each project's emoji or symbol before the thread's name, one line per thread",
            ActivityThreadStyle.Icon.Detail);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-259")] // App/AppSettings.swift:83, :90
    public void ActivityThreadStyle_projectName_title_and_detail_are_exact()
    {
        Assert.Equal("Project name", ActivityThreadStyle.ProjectName.Title);
        Assert.Equal("The project's name, with a folder mark, on a line under the thread's title",
            ActivityThreadStyle.ProjectName.Detail);
        Assert.Equal("projectName", ActivityThreadStyle.ProjectName.Id); // the persisted raw value (PR-SETTINGS-259)
    }
}
