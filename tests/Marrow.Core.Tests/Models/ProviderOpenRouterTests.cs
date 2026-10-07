using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234): ProviderKind.OpenRouter, an API-key provider in the manner of
// deepseek, meta and zai (Provider.swift:101-141 give those three their host, key page and variable). Every expected
// value below is the ED-234 decision text, not read back from the code: the key variable OPENROUTER_API_KEY is the one
// PiCLI.swift:38 already passes through, the display name is "OpenRouter", the base URL is https://openrouter.ai/api/v1.
// Each test covers one member that switches over the provider kind (Provider.cs).
public sealed class ProviderOpenRouterTests
{
    private const ProviderKind OpenRouter = ProviderKind.OpenRouter;

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_new_case_comes_after_the_fourteen_source_cases_with_the_raw_value_openrouter()
    {
        var cases = SwiftEnum<ProviderKind>.AllCases;
        // OD-0114 / ED-410 appends Local; OpenRouter and the source cases keep their original indices.
        Assert.Equal(OpenRouter, cases[14]);
        Assert.Equal(ProviderKind.Pi, cases[13]); // the fourteen of Provider.swift:3-18 keep their places
        Assert.Equal("openrouter", SwiftEnum<ProviderKind>.RawValue(OpenRouter));
        Assert.Equal("openrouter", OpenRouter.Id);
        Assert.Equal(OpenRouter, SwiftEnum<ProviderKind>.FromRawValue("openrouter"));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_display_name_is_OpenRouter() => Assert.Equal("OpenRouter", OpenRouter.DisplayName);

    [Fact]
    [Trait("Decision", "ED-234")]
    public void It_has_no_local_CLI_so_the_executable_name_is_empty_and_it_is_not_an_ACP_provider()
    {
        Assert.Equal("", OpenRouter.ExecutableName);
        Assert.False(OpenRouter.UsesACP);
        Assert.False(OpenRouter.SupportsRewind);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_icon_name_follows_the_provider_prefix_rule() => Assert.Equal("provider-openrouter", OpenRouter.IconName);

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_login_command_names_the_key_variable_the_way_the_other_key_providers_do() =>
        Assert.Equal("OPENROUTER_API_KEY=sk-or-...", OpenRouter.LoginCommand);

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_install_page_is_where_a_key_is_made() => Assert.Equal("https://openrouter.ai/keys", OpenRouter.InstallURL.AbsoluteString);

    [Fact]
    [Trait("Decision", "ED-234")]
    public void It_is_key_based_and_accepts_a_key()
    {
        Assert.True(OpenRouter.IsAPIKeyBased);
        Assert.True(OpenRouter.AcceptsAPIKey);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_key_is_named_by_host_source_and_environment_variable()
    {
        Assert.Equal("openrouter.ai/api/v1", OpenRouter.ApiHost);
        Assert.Equal("openrouter.ai/keys", OpenRouter.ApiKeySource);
        Assert.Equal("OPENROUTER_API_KEY", OpenRouter.ApiKeyEnvVar);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void It_compacts_and_takes_images_like_the_other_native_providers()
    {
        Assert.True(OpenRouter.SupportsCompact);
        Assert.True(OpenRouter.SupportsImages);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void Only_the_four_native_API_providers_are_key_based_and_the_other_eleven_still_are_not()
    {
        ProviderKind[] keyBased = [ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, OpenRouter];
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
        {
            Assert.Equal(keyBased.Contains(kind), kind.IsAPIKeyBased);
            Assert.Equal(keyBased.Contains(kind), kind.ApiHost is not null);
        }
    }
}
