using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). The switches over the provider kind that sit in Services and the key
// store behind them: the account the OpenRouter key lives under (APIKeychain.swift:25-34 lists the four of the source), the
// plan limits and credits readers (PlanLimits.swift:65-90, ProviderCredits.swift:58-75), which OpenRouter has none of. The
// store is the in-memory Credential Manager double of the other key tests (Fixtures/InMemoryCredentialBackend.cs): the real
// credential store is never read or written (R-TEST-9, R-TEST-10). Every key is made up.
public sealed class OpenRouterKeyStoreTests
{
    private const string Service = "Marrow";
    private const string OpenRouterAccount = "OpenRouter API Key"; // ED-234, in the manner of "DeepSeek API Key" (APIKeychain.swift:28)

    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp() =>
        AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = Service }, isDevelopment: false));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    private static string Target(string account) => SecretStore.TargetName(Service, account);

    private void Plant(string account, string text) =>
        Assert.Equal(0, backend.Write(new GenericCredential(Target(account), account, System.Text.Encoding.UTF8.GetBytes(text), 1, 2, [])));

    // ---- the credential store -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_key_is_stored_under_its_own_account_in_the_credential_store()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-or-made-up-1", ProviderKind.OpenRouter));

        var held = backend.Peek(Target(OpenRouterAccount));
        Assert.NotNull(held);
        Assert.Equal(OpenRouterAccount, held.UserName);
        Assert.Equal("sk-or-made-up-1", System.Text.Encoding.UTF8.GetString(held.Blob));
        Assert.Equal([Target(OpenRouterAccount)], backend.TargetNames); // nothing else was written
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_key_is_read_back_from_the_store_and_the_fallback_is_used_only_when_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();
        Assert.Equal("fallback", APIKeychain.Key(ProviderKind.OpenRouter, fallback: "fallback")); // nothing stored yet

        Plant(OpenRouterAccount, "sk-or-planted");

        Assert.Equal("sk-or-planted", APIKeychain.Key(ProviderKind.OpenRouter, fallback: "fallback"));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void An_empty_key_removes_the_stored_one()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(OpenRouterAccount, "sk-or-planted");

        Assert.True(APIKeychain.SetKey("  ", ProviderKind.OpenRouter));

        Assert.Null(backend.Peek(Target(OpenRouterAccount)));
        Assert.Equal("", APIKeychain.Key(ProviderKind.OpenRouter, fallback: ""));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_OpenRouter_key_and_the_other_native_providers_keys_do_not_see_each_other()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant("DeepSeek API Key", "sk-deepseek-planted");

        Assert.Equal("", APIKeychain.Key(ProviderKind.OpenRouter, fallback: "")); // DeepSeek's key is not OpenRouter's
        APIKeychain.SetKey("sk-or-made-up-2", ProviderKind.OpenRouter);
        Assert.Equal("sk-deepseek-planted", APIKeychain.Key(ProviderKind.Deepseek, fallback: ""));
        Assert.Equal("sk-or-made-up-2", APIKeychain.Key(ProviderKind.OpenRouter, fallback: ""));
    }

    // ---- plan limits and credits: OpenRouter has neither ----------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task OpenRouter_shows_no_plan_limits_and_no_credits()
    {
        Assert.False(PlanLimitsReader.ExposesLimits(ProviderKind.OpenRouter));
        Assert.False(CreditsReader.ExposesCredits(ProviderKind.OpenRouter));
        Assert.Null(CreditsReader.TopUpURL(ProviderKind.OpenRouter));
        Assert.Null(await CreditsReader.Read(ProviderKind.OpenRouter, "sk-or-made-up-3"));
        Assert.Null(await PlanLimitsReader.Read(ProviderKind.OpenRouter, "sk-or-made-up-3"));
        var executable = SwiftURL.Create("file:///no/such/openrouter")!;
        Assert.Null(await PlanLimitsReader.Read(ProviderKind.OpenRouter, executable, ValueMap<string, string>.Empty));
    }
}
