using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). How ProviderRegistry treats OpenRouter, in the manner of its three
// API-key providers: the status check (refreshAPIProvider, ProviderRegistry.swift:380-425), the catalogue load
// (loadAPICatalog :567-591: the seed first, no fetch without a key, an update only for a non-empty list) and the cache
// (updateCatalog :717-734, the catalogue and dates stored under providerModelCatalogs and providerModelCatalogDates, read
// back at init :165-195). Models(OpenRouter) is the list the composer's picker reads for every provider. Every request
// goes to https://openrouter.ai through the HTTP client's test seam (OpenRouterRegistryRig); the key is in a settings
// double, never the real credential store (R-TEST-9, R-TEST-10).
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenRouterRegistryTests
{
    private const ProviderKind OpenRouter = ProviderKind.OpenRouter;
    private static readonly string[] Listed =
        ["openrouter/auto", "anthropic/claude-sonnet-4.5", "deepseek/deepseek-r1", "meta-llama/llama-3.3-70b-instruct:free"];

    private static ProviderStatus ApiStatus(ProviderStatus.Auth auth) =>
        new() { Version = "API", ProviderStatusAuth = auth, ApiKeyConfigured = true };

    // ---- the picker's list ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public void Before_any_list_loads_the_picker_has_the_router_row_and_nothing_was_asked_of_the_network()
    {
        using var rig = new OpenRouterRegistryRig();

        Assert.Equal(ProviderRegistry.OpenRouterSeed, rig.Registry.Models(OpenRouter));
        Assert.Equal(["openrouter/auto"], rig.Registry.Models(OpenRouter).Select(option => option.Id));
        Assert.True(Assert.Single(rig.Registry.Models(OpenRouter)).IsDefault);
        Assert.Empty(rig.Handler.Received);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task With_no_key_the_catalogue_load_keeps_the_seed_and_fetches_nothing()
    {
        using var rig = new OpenRouterRegistryRig();

        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter));

        Assert.Equal(ProviderRegistry.OpenRouterSeed, rig.Registry.Models(OpenRouter));
        Assert.Empty(rig.Handler.Received);
        Assert.Empty(rig.Registry.LoadingCatalogs);
        Assert.Empty(rig.StoredDates()); // nothing was delivered, so no date
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task With_a_key_the_catalogue_load_replaces_the_seed_with_the_fetched_list_through_the_bearer_key()
    {
        using var rig = new OpenRouterRegistryRig();
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;

        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter));

        Assert.Equal(Listed, rig.Registry.Models(OpenRouter).Select(option => option.Id));
        var request = Assert.Single(rig.Handler.Received);
        Assert.Equal(("GET", "/api/v1/models"), (request.Method, request.Url.AbsolutePath));
        Assert.Equal(OpenRouterBodies.Bearer, request.Header("Authorization"));
        Assert.Empty(rig.Registry.LoadingCatalogs);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_delivered_catalogue_is_cached_with_its_date_and_the_next_launch_reads_it_without_a_request()
    {
        using var rig = new OpenRouterRegistryRig();
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;
        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter));

        Assert.Equal(Listed, rig.StoredCatalogueIds()["openrouter"]); // the whole list is persisted under the provider's raw value
        Assert.True(rig.StoredDates().ContainsKey("openrouter"));

        var requestsBefore = rig.Handler.Received.Count;
        var relaunched = rig.Reopen();

        Assert.Equal(Listed, relaunched.Models(OpenRouter).Select(option => option.Id));
        Assert.Equal(requestsBefore, rig.Handler.Received.Count);
        // The rows come back whole: name, detail and efforts survive the cache.
        var sonnet = Assert.Single(relaunched.Models(OpenRouter), option => option.Id == "anthropic/claude-sonnet-4.5");
        Assert.Equal("Claude Sonnet 4.5", sonnet.Name);
        Assert.Equal(["low", "medium", "high"], sonnet.Efforts);
    }

    [Theory]
    [InlineData(401, "{}")]
    [InlineData(500, "oops")]
    [InlineData(200, """{"data":[]}""")]
    [Trait("Decision", "ED-234")]
    public async Task A_catalogue_answer_that_is_not_a_list_leaves_the_rows_and_stores_no_date(int status, string body)
    {
        using var rig = new OpenRouterRegistryRig { ModelsStatus = status, ModelsBody = body };
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;

        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter));

        Assert.Equal(1, rig.Count("/api/v1/models")); // it was asked
        Assert.Equal(ProviderRegistry.OpenRouterSeed, rig.Registry.Models(OpenRouter));
        Assert.Empty(rig.StoredDates());
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_catalogue_fetch_that_cannot_reach_the_host_leaves_the_rows_and_is_not_an_error()
    {
        using var rig = new OpenRouterRegistryRig();
        rig.Handler.Answer = null; // the connection is refused; no socket is opened
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;

        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter));

        Assert.Equal(ProviderRegistry.OpenRouterSeed, rig.Registry.Models(OpenRouter));
        Assert.Empty(rig.Registry.LoadingCatalogs);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_forced_load_fetches_again_and_an_unforced_one_after_a_delivery_does_not()
    {
        using var rig = new OpenRouterRegistryRig();
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;
        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter));
        Assert.Equal(1, rig.Count("/api/v1/models"));

        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter)); // once per launch (:461)
        Assert.Equal(1, rig.Count("/api/v1/models"));

        await rig.Run(() => rig.Registry.LoadCatalog(OpenRouter, force: true)); // a key just typed in Settings forces it
        Assert.Equal(2, rig.Count("/api/v1/models"));
    }

    // ---- the status check ----------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task With_no_key_the_status_is_signed_out_and_nothing_is_asked_of_the_network()
    {
        using var rig = new OpenRouterRegistryRig();

        await rig.Run(() => rig.Registry.Refresh(OpenRouter));

        var status = rig.Registry.Status(OpenRouter);
        Assert.IsType<ProviderStatus.Auth.SignedOut>(status.ProviderStatusAuth);
        Assert.False(status.ApiKeyConfigured);
        Assert.False(status.IsInstalled);
        Assert.Equal("Not installed", status.Summary);
        Assert.Empty(rig.Handler.Received);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_key_OpenRouter_accepts_is_signed_in_with_the_api_version_and_counts_as_installed()
    {
        using var rig = new OpenRouterRegistryRig();
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;

        await rig.Run(() => rig.Registry.Refresh(OpenRouter));

        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedIn(null)), rig.Registry.Status(OpenRouter));
        Assert.Equal("Signed in", rig.Registry.Status(OpenRouter).Summary);
        var request = Assert.Single(rig.Handler.Received);
        Assert.Equal(("GET", "/api/v1/key"), (request.Method, request.Url.AbsolutePath));
        Assert.Equal(OpenRouterBodies.Bearer, request.Header("Authorization"));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_key_OpenRouter_rejects_is_signed_out_but_still_counts_as_installed()
    {
        using var rig = new OpenRouterRegistryRig { KeyStatus = 401 };
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;

        await rig.Run(() => rig.Registry.Refresh(OpenRouter));

        var status = rig.Registry.Status(OpenRouter);
        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedOut()), status);
        Assert.True(status.IsInstalled); // apiKeyConfigured is true whether or not the key is valid
        Assert.Equal("Not signed in", status.Summary);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task OpenRouter_is_checked_as_an_API_provider_so_no_executable_is_looked_for()
    {
        using var rig = new OpenRouterRegistryRig();
        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;
        rig.Settings.Paths[OpenRouter] = @"C:\nowhere\openrouter.exe"; // a binary path is not consulted for a key provider

        await rig.Run(() => rig.Registry.Refresh(OpenRouter));

        Assert.Null(rig.Registry.Executable(OpenRouter));
        Assert.Null(rig.Registry.Status(OpenRouter).Executable);
        Assert.IsType<ProviderStatus.Auth.SignedIn>(rig.Registry.Status(OpenRouter).ProviderStatusAuth);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_CLI_auth_probe_has_nothing_to_say_about_OpenRouter()
    {
        // refreshAPIProvider answers for a key provider before any CLI is looked for; the probe table still has a row for every
        // provider, and OpenRouter's is unknown, as DeepSeek's, Meta's and Z.ai's are (ProviderRegistry.swift:864-865).
        var executable = SwiftURL.Create("file:///no/such/openrouter")!;

        var auth = await ProviderRegistry.Auth(OpenRouter, executable, new Dictionary<string, string>());

        Assert.IsType<ProviderStatus.Auth.Unknown>(auth);
    }

    // ---- the provider lists --------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task OpenRouter_is_an_available_provider_when_it_is_enabled_and_has_not_been_found_missing()
    {
        using var rig = new OpenRouterRegistryRig();
        Assert.DoesNotContain(OpenRouter, rig.Registry.AvailableProviders); // not enabled

        rig.Settings.Enabled.Add(OpenRouter);
        Assert.Contains(OpenRouter, rig.Registry.AvailableProviders); // no status yet counts as installed (:207)

        await rig.Run(() => rig.Registry.Refresh(OpenRouter)); // no key: not installed
        Assert.DoesNotContain(OpenRouter, rig.Registry.AvailableProviders);

        rig.Settings.Keys[OpenRouter] = OpenRouterBodies.Key;
        await rig.Run(() => rig.Registry.Refresh(OpenRouter));
        Assert.Contains(OpenRouter, rig.Registry.AvailableProviders);
    }
}
