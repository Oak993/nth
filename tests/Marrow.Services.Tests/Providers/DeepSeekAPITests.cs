// Source: DroppyCode/Services/Providers/DeepSeekAPI.swift (B2 1fbf81e), :1-99.
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Network-response paths are not exercised here: URLSession.Shared is fixed to the real API, and tests must not reach the internet.
public sealed class DeepSeekAPITests
{
    [Fact]
    [Trait("Req", "PR-PROVAPI-015")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:7-10
    public void Endpoint_urls_are_exact()
    {
        Assert.Equal("https://api.deepseek.com", DeepSeekAPI.BaseURL.OriginalString);
        Assert.Equal("https://api.deepseek.com/chat/completions", DeepSeekAPI.ChatURL.OriginalString);
        Assert.Equal("https://api.deepseek.com/models", DeepSeekAPI.ModelsURL.OriginalString);
        Assert.Equal("https://api.deepseek.com/user/balance", DeepSeekAPI.BalanceURL.OriginalString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-016")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:13-15
    public async Task Validate_returns_false_for_an_empty_key()
    {
        Assert.False(await DeepSeekAPI.Validate(""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-016")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:14-15
    public async Task Validate_returns_false_for_a_whitespace_only_key()
    {
        Assert.False(await DeepSeekAPI.Validate(" \t\r\n"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-017")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:24-26
    public async Task ListModels_returns_empty_for_an_empty_key()
    {
        Assert.Empty(await DeepSeekAPI.ListModels(""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-017")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:25-26
    public async Task ListModels_returns_empty_for_a_whitespace_only_key()
    {
        Assert.Empty(await DeepSeekAPI.ListModels(" \t\r\n"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-018")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:70-82
    [Trait("Req", "UI-PROVAPI-026")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:70,79-92; ProviderRegistry.swift:127
    public void V4_pro_option_has_the_recorded_picker_content()
    {
        var option = Assert.IsType<ModelOption>(DeepSeekAPI.Option("deepseek-v4-pro"));

        Assert.Equal("deepseek-v4-pro", option.Id);
        Assert.Equal("V4 Pro", option.Name);
        Assert.Equal("Best reasoning and coding quality", option.Detail);
        Assert.Equal(3, option.Efforts.Count);
        Assert.Equal("low", option.Efforts[0]);
        Assert.Equal("high", option.Efforts[1]);
        Assert.Equal("max", option.Efforts[2]);
        Assert.Equal("high", option.DefaultEffort);
        Assert.True(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-019")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:70,83-84
    [Trait("Req", "UI-PROVAPI-026")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:70,79-92
    public void Flash_option_has_the_recorded_picker_content()
    {
        var option = Assert.IsType<ModelOption>(DeepSeekAPI.Option("deepseek-flash"));

        Assert.Equal("deepseek-flash", option.Id);
        Assert.Equal("V4.1 Flash", option.Name);
        Assert.Equal("Newest, fast, reads images", option.Detail);
        Assert.Equal(3, option.Efforts.Count);
        Assert.Equal("low", option.Efforts[0]);
        Assert.Equal("high", option.Efforts[1]);
        Assert.Equal("max", option.Efforts[2]);
        Assert.Equal("high", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    [Theory]
    [InlineData("deepseek-v4-flash")]
    [InlineData("deepseek-v4-flash-vision-exp")]
    [InlineData("deepseek-chat")]
    [InlineData("deepseek-reasoner")]
    [Trait("Req", "PR-PROVAPI-020")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:85-86
    [Trait("Req", "UI-PROVAPI-026")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:85-86
    public void Retired_ids_have_no_picker_option(string id)
    {
        Assert.Null(DeepSeekAPI.Option(id));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-021")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:87-91
    [Trait("Req", "UI-PROVAPI-026")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:87-91
    public void An_unrecognized_id_is_offered_under_its_raw_name()
    {
        var option = Assert.IsType<ModelOption>(DeepSeekAPI.Option("deepseek-future-model"));

        Assert.Equal("deepseek-future-model", option.Id);
        Assert.Equal("deepseek-future-model", option.Name);
        Assert.Null(option.Detail);
        Assert.Equal(3, option.Efforts.Count);
        Assert.Equal("low", option.Efforts[0]);
        Assert.Equal("high", option.Efforts[1]);
        Assert.Equal("max", option.Efforts[2]);
        Assert.Equal("high", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-026")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:70,79-92; ProviderRegistry.swift:127
    public void Seeded_picker_rows_have_the_recorded_text_and_default_first()
    {
        var pro = Assert.IsType<ModelOption>(DeepSeekAPI.Option("deepseek-v4-pro"));
        var flash = Assert.IsType<ModelOption>(DeepSeekAPI.Option("deepseek-flash"));
        var seededRows = new[] { pro, flash };

        Assert.True(seededRows[0].IsDefault);
        Assert.False(seededRows[1].IsDefault);
        Assert.Equal(
            "V4 Pro \U00002014 Best reasoning and coding quality; V4.1 Flash \U00002014 Newest, fast, reads images", // U+2014 EM DASH is the UI record's row separator.
            string.Join("; ", seededRows.Select(row => row.Name + " \U00002014 " + row.Detail)));
    }

    [Theory]
    [InlineData("deepseek-flash", true)]
    [InlineData("deepseek-v4-flash", true)]
    [InlineData("deepseek-v4-flash-vision-exp", true)]
    [InlineData("deepseek-v4-pro", false)]
    [InlineData("deepseek-chat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [Trait("Req", "PR-PROVAPI-022")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:94-98
    public void Vision_membership_matches_the_three_recorded_ids(string? id, bool expected)
    {
        Assert.Equal(expected, DeepSeekAPI.IsVisionModel(id));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-023")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:72-73
    public void Context_window_is_1048576()
    {
        Assert.Equal(1_048_576, DeepSeekAPI.ContextWindow);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-024")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:43-45
    public async Task Balance_returns_null_for_an_empty_key()
    {
        Assert.Null(await DeepSeekAPI.Balance(""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-024")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:44-45
    public async Task Balance_returns_null_for_a_whitespace_only_key()
    {
        Assert.Null(await DeepSeekAPI.Balance(" \t\r\n"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-025")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:55-68
    public void Credits_read_only_the_first_balance_entry()
    {
        var json = Assert.IsType<JSONValue.Object>(JSONValue.Parse(
            """{"balance_infos":[{"currency":"USD","total_balance":"12.34"},{"currency":"CNY","total_balance":"99.99"}]}"""));

        var credits = Assert.IsType<ProviderCredits>(DeepSeekAPI.Credits(json));

        Assert.Equal(12.34, credits.Total);
        Assert.Equal("USD", credits.Currency);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-025")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:58-66
    public void Credits_parse_string_amounts_and_availability()
    {
        var json = Assert.IsType<JSONValue.Object>(JSONValue.Parse(
            """{"is_available":false,"balance_infos":[{"currency":"USD","total_balance":"12.34","granted_balance":"2.34","topped_up_balance":"10.00"}]}"""));

        var credits = Assert.IsType<ProviderCredits>(DeepSeekAPI.Credits(json));

        Assert.Equal(12.34, credits.Total);
        Assert.Equal("USD", credits.Currency);
        Assert.Equal(10.00, credits.ToppedUp);
        Assert.Equal(2.34, credits.Granted);
        Assert.False(credits.IsUsable);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-025")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:63-66
    public void Credits_default_currency_and_availability_and_leave_optional_amounts_absent()
    {
        var json = Assert.IsType<JSONValue.Object>(JSONValue.Parse(
            """{"balance_infos":[{"total_balance":"1.50"}]}"""));

        var credits = Assert.IsType<ProviderCredits>(DeepSeekAPI.Credits(json));

        Assert.Equal("", credits.Currency);
        Assert.True(credits.IsUsable);
        Assert.Null(credits.ToppedUp);
        Assert.Null(credits.Granted);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"balance_infos":[]}""")]
    [InlineData("""{"balance_infos":[{}]}""")]
    [InlineData("""{"balance_infos":[{"total_balance":"not-a-number"}]}""")]
    [InlineData("""{"balance_infos":[{"total_balance":12.34}]}""")]
    [Trait("Req", "PR-PROVAPI-025")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:58-60
    public void Credits_are_null_without_a_parseable_string_total(string text)
    {
        var json = Assert.IsType<JSONValue.Object>(JSONValue.Parse(text));

        Assert.Null(DeepSeekAPI.Credits(json));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-025")] // DroppyCode/Services/Providers/DeepSeekAPI.swift:64-65
    public void Credits_leave_optional_amounts_absent_when_they_are_not_strings()
    {
        var json = Assert.IsType<JSONValue.Object>(JSONValue.Parse(
            """{"balance_infos":[{"total_balance":"1.00","topped_up_balance":5,"granted_balance":true}]}"""));

        var credits = Assert.IsType<ProviderCredits>(DeepSeekAPI.Credits(json));

        Assert.Null(credits.ToppedUp);
        Assert.Null(credits.Granted);
    }
}
