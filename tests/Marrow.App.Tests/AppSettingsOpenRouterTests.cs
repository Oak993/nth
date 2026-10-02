using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234): the OpenRouter key in AppSettings, in the manner of the four key
// fields of AppSettings.swift:367-408 (a key goes to the protected store, trimmed; an empty one deletes it; a refused one
// is reported and kept nowhere; the key a provider is sent is the stored one, else the provider's environment variable,
// trimmed: AppSettings.swift:904-916). Every test builds AppSettings over a throwaway settings file and an in-memory
// Credential Manager (Fixtures/AppSettingsFixtures.cs): the real credential store and the real settings are never touched,
// and every key is made up.
[Collection("AppSettings")]
public sealed class AppSettingsOpenRouterTests
{
    private const ProviderKind OpenRouter = ProviderKind.OpenRouter;
    private const string Account = "OpenRouter API Key";
    private const string RefusalLine = "Credential Manager did not accept this key, so it was not saved.";

    private static string Target => SecretStore.TargetName(AppInfo.Current.Name, Account);

    private static string EnvironmentKey(string name) =>
        LoginEnvironment.Current.TryGetValue(name, out var value) ? value.Trimmed() : "";

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_key_the_store_accepts_is_kept_there_trimmed_under_its_own_account_and_read_back_after_a_relaunch()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("openRouterAPIKey", "legacy-plain"); // an entry no build ever wrote: cleared, never read as the key
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            _ = settings.ApiKeyInput(OpenRouter); // keys loaded, so the edit below is a user edit
            scope.Credentials.Operations.Clear();

            settings.OpenRouterAPIKeyInput = "  sk-or-accepted \n";

            Assert.Contains(scope.Credentials.Operations, o => o.Op == "write" && o.Target == Target);
            Assert.Equal("sk-or-accepted", APIKeychain.Key(OpenRouter, ""));
            Assert.Null(scope.Defaults.Object("openRouterAPIKey"));
            Assert.Null(settings.APIKeyRefusal(OpenRouter));
            Assert.Equal("  sk-or-accepted \n", settings.ApiKeyInput(OpenRouter)); // the field keeps what was typed
            Assert.Equal("sk-or-accepted", settings.ApiKey(OpenRouter)); // the key sent is trimmed

            scope.Relaunch();
            Assert.Equal("sk-or-accepted", new AppSettings().ApiKeyInput(OpenRouter));
        });
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void SetAPIKeyInput_edits_the_same_field_as_the_Settings_page() => WithSettings((_, settings) =>
    {
        settings.SetAPIKeyInput("sk-or-via-setter", OpenRouter);

        Assert.Equal("sk-or-via-setter", settings.OpenRouterAPIKeyInput);
        Assert.Equal("sk-or-via-setter", APIKeychain.Key(OpenRouter, ""));
        Assert.Equal("", settings.DeepseekAPIKeyInput); // no other provider's field moved
        Assert.Equal("", settings.MetaAPIKeyInput);
        Assert.Equal("", settings.ZaiAPIKeyInput);
        Assert.Equal("", settings.CommandcodeAPIKeyInput);
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere()
    {
        const string key = "sk-or-refused-7d41b0c2";
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            _ = settings.ApiKeyInput(OpenRouter);
            scope.Credentials.RefuseWrites = true;

            settings.OpenRouterAPIKeyInput = " " + key + " ";

            Assert.Equal(RefusalLine, settings.APIKeyRefusal(OpenRouter));
            Assert.Null(settings.APIKeyRefusal(ProviderKind.Deepseek));
            Assert.Equal("", APIKeychain.Key(OpenRouter, ""));
            Assert.False(scope.Credentials.AnyBlobContains(key));
            Assert.Null(scope.Defaults.Object("openRouterAPIKey"));
            Assert.DoesNotContain(scope.Defaults.Writes, w => w.Op == "set" && w.Key == "openRouterAPIKey");
            Assert.False(scope.AnyFileContains(key)); // never on disk in plain text
        });
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void Emptying_the_field_deletes_the_credential() => WithSettings((scope, settings) =>
    {
        settings.OpenRouterAPIKeyInput = "sk-or-to-clear";
        Assert.Equal("sk-or-to-clear", APIKeychain.Key(OpenRouter, ""));
        scope.Credentials.Operations.Clear();

        settings.OpenRouterAPIKeyInput = " \t\n ";

        Assert.Contains(scope.Credentials.Operations, o => o.Op == "delete" && o.Target == Target);
        Assert.Equal("", APIKeychain.Key(OpenRouter, ""));
        Assert.Equal("", settings.ApiKey(OpenRouter));
        Assert.False(settings.HasAPIKey(OpenRouter));
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void Assigning_the_key_it_already_holds_touches_neither_store() => WithSettings((scope, settings) =>
    {
        settings.OpenRouterAPIKeyInput = "sk-or-same";
        scope.Credentials.Operations.Clear();
        scope.Defaults.Writes.Clear();

        settings.OpenRouterAPIKeyInput = "sk-or-same";

        Assert.Empty(scope.Credentials.Operations);
        Assert.Empty(scope.Defaults.Writes);
    });

    // ---- the key a session is sent -------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public void With_nothing_stored_the_key_is_the_OPENROUTER_API_KEY_variable_trimmed() => WithSettings((_, settings) =>
    {
        // The login environment is the process environment Windows populated at logon (ED-028 (3)); the variable is named by
        // ProviderKind.ApiKeyEnvVar, which ProviderOpenRouterTests pins to OPENROUTER_API_KEY.
        Assert.Equal(EnvironmentKey("OPENROUTER_API_KEY"), settings.ApiKey(OpenRouter));
        Assert.Equal(EnvironmentKey("OPENROUTER_API_KEY").Length != 0, settings.HasAPIKey(OpenRouter));
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_stored_key_wins_over_the_environment_and_is_sent_trimmed() => WithSettings((_, settings) =>
    {
        settings.SetAPIKeyInput(" \n sk-or-typed-key \t", OpenRouter);

        Assert.Equal("sk-or-typed-key", settings.ApiKey(OpenRouter));
        Assert.True(settings.HasAPIKey(OpenRouter));
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_key_is_read_from_the_credential_store_on_the_first_ask_and_the_four_other_providers_stay_empty()
    {
        using var scope = AppSettingsScope.Create();
        Assert.True(SecretStore.Shared.Current.SetReplacing(AppInfo.Current.Name, Account, "sk-or-planted"));
        scope.Credentials.Operations.Clear();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Empty(scope.Credentials.Operations); // building the settings reads nothing

            Assert.Equal("sk-or-planted", settings.ApiKey(OpenRouter));

            Assert.Contains(scope.Credentials.Operations, o => o.Op == "read" && o.Target == Target);
            Assert.All(scope.Credentials.Operations, o => Assert.Equal("read", o.Op)); // the fill never writes a key back
            Assert.Equal("sk-or-planted", settings.OpenRouterAPIKeyInput);
            Assert.Equal("", settings.DeepseekAPIKeyInput);
            Assert.Equal("", settings.MetaAPIKeyInput);
        });
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_background_load_fills_the_OpenRouter_field_off_the_main_thread()
    {
        using var scope = AppSettingsScope.Create();
        Assert.True(SecretStore.Shared.Current.SetReplacing(AppInfo.Current.Name, Account, "sk-or-planted-2"));
        scope.Credentials.Operations.Clear();
        scope.Runtime.Loop.Run(async () =>
        {
            var settings = new AppSettings();
            await settings.LoadKeychainInBackground();

            Assert.Equal("sk-or-planted-2", settings.OpenRouterAPIKeyInput);
            var read = Assert.Single(scope.Credentials.Operations, o => o.Target == Target);
            Assert.Equal("read", read.Op);
            Assert.False(read.OnMain);
        });
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void Filling_clears_a_legacy_entry_only_when_the_store_holds_the_key_and_never_reads_it_as_the_key()
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("openRouterAPIKey", "legacy-plain-openrouter");
        scope.Runtime.Loop.Run(async () =>
        {
            var settings = new AppSettings();
            await settings.LoadKeychainInBackground();
            Assert.Equal("", settings.OpenRouterAPIKeyInput); // the plaintext entry is never taken for the key
            Assert.Equal("legacy-plain-openrouter", scope.Defaults.Object("openRouterAPIKey")); // the store holds none: left alone
        });

        using var held = AppSettingsScope.Create();
        held.Defaults.Seed("openRouterAPIKey", "legacy-plain-openrouter");
        Assert.True(SecretStore.Shared.Current.SetReplacing(AppInfo.Current.Name, Account, "sk-or-held"));
        held.Runtime.Loop.Run(async () =>
        {
            var settings = new AppSettings();
            await settings.LoadKeychainInBackground();
            Assert.Equal("sk-or-held", settings.OpenRouterAPIKeyInput);
            Assert.Null(held.Defaults.Object("openRouterAPIKey")); // the store holds the key, so the stale entry is removed
        });
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_provider_that_takes_no_key_still_gets_the_empty_string_and_OpenRouter_is_not_one_of_them() => WithSettings((_, settings) =>
    {
        Assert.True(OpenRouter.AcceptsAPIKey);
        Assert.Equal("", settings.ApiKey(ProviderKind.Claude));
        Assert.Equal("", settings.ApiKeyInput(ProviderKind.Claude));
    });

    private static void WithSettings(Action<AppSettingsScope, AppSettings> body)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() => body(scope, new AppSettings()));
    }
}
