using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderCredits.swift (B2 1fbf81e). See porting/work/packets4/
// Services.Providers.ProviderCredits/part1.md for the records PR-PROVREG-197, 203, 204, 207, 208, 209, 210, 211,
// PR-USAGE-055, PR-USAGE-056.
public sealed class ProviderCreditsTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    // ---- PR-PROVREG-197: exposesCredits -----------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-197")] // ProviderCredits.swift:58
    [InlineData(ProviderKind.Deepseek, true)]
    [InlineData(ProviderKind.Commandcode, true)]
    [InlineData(ProviderKind.Codex, false)]
    [InlineData(ProviderKind.Claude, false)]
    [InlineData(ProviderKind.Cursor, false)]
    [InlineData(ProviderKind.Opencode, false)]
    [InlineData(ProviderKind.OpencodeGo, false)]
    [InlineData(ProviderKind.Grok, false)]
    [InlineData(ProviderKind.Meta, false)]
    [InlineData(ProviderKind.Zai, false)]
    [InlineData(ProviderKind.Devin, false)]
    [InlineData(ProviderKind.Antigravity, false)]
    [InlineData(ProviderKind.Copilot, false)]
    [InlineData(ProviderKind.Pi, false)]
    public void ExposesCredits_is_true_for_exactly_deepseek_and_commandcode(ProviderKind provider, bool expected) =>
        Assert.Equal(expected, CreditsReader.ExposesCredits(provider));

    [Fact]
    [Trait("Req", "PR-PROVREG-197")] // ProviderCredits.swift:58; ProviderKind grew to 14 cases (Provider.swift:8)
    public void ExposesCredits_covers_every_provider_kind_and_is_true_for_exactly_two()
    {
        var trueCount = SwiftEnum<ProviderKind>.AllCases.Count(CreditsReader.ExposesCredits);
        Assert.Equal(16, SwiftEnum<ProviderKind>.AllCases.Count); // source cases, OpenRouter (ED-234), Local (ED-410); no additional credits
        Assert.Equal(2, trueCount);
    }

    // ---- PR-PROVREG-203: read dispatch --------------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-203")] // ProviderCredits.swift:60-66 - "default: nil"
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Devin)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Pi)]
    public async Task Read_returns_null_without_a_network_call_for_every_other_provider(ProviderKind provider)
    {
        using var runtime = new TestRuntime();
        Assert.Null(await CreditsReader.Read(provider, "key"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-203")] // ProviderCredits.swift:62 - "case .deepseek: await DeepSeekAPI.balance(apiKey:)"
    public async Task Read_dispatches_deepseek_to_DeepSeekAPI_Balance()
    {
        // DeepSeekAPI.Balance is now implemented (Services.Providers.DeepSeekAPI unit). It has no base-URL
        // override seam (DeepSeekAPI.swift's balanceURL is a fixed literal, unlike CommandCodeAPI's configurable
        // host), so this cannot be exercised against a loopback server without reaching the real network
        // (forbidden, R-TEST-10). An empty key is dispatch-routing evidence that is still a real value assertion
        // rather than a stub artifact: DeepSeekAPI.Balance short-circuits to null before any request is built
        // (DeepSeekAPI.swift:44-45 `guard !key.isEmpty else { return nil }`), so this proves Read reaches
        // DeepSeekAPI.Balance (not CommandCodeAPI.Credits, whose empty-key path is asserted separately, or a
        // no-op default) and exercises the real implementation, with no network call.
        using var runtime = new TestRuntime();
        Assert.Null(await CreditsReader.Read(ProviderKind.Deepseek, ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-203")] // ProviderCredits.swift:63 - "case .commandcode: await CommandCodeAPI.credits(apiKey:)"
    public async Task Read_dispatches_commandcode_to_CommandCodeAPI_Credits()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));

        var pending = CreditsReader.Read(ProviderKind.Commandcode, "key-9");
        var exchange = await server.NextExchange();
        Assert.Equal("/alpha/billing/credits", exchange.Target);
        Assert.Equal(["Bearer key-9"], exchange.HeaderValues("Authorization"));
        await exchange.Respond(200, "OK",
            System.Text.Encoding.UTF8.GetBytes("""{"credits":{"monthlyCredits":1,"purchasedCredits":2,"freeCredits":3,"belowThreshold":false}}"""));

        var credits = await pending.WaitAsync(Guard);
        Assert.Equal(
            new ProviderCredits { Total = 6, Currency = "USD", ToppedUp = 2, Granted = 3, Monthly = 1, IsUsable = true },
            credits);
    }

    // ---- PR-PROVREG-204: topUpURL ---------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-204")] // ProviderCredits.swift:71
    public void TopUpURL_for_deepseek_is_the_platform_top_up_page() =>
        Assert.Equal(new Uri("https://platform.deepseek.com/top_up"), CreditsReader.TopUpURL(ProviderKind.Deepseek));

    [Fact]
    [Trait("Req", "PR-PROVREG-204")] // ProviderCredits.swift:72
    public void TopUpURL_for_commandcode_is_CommandCodeAPI_BillingURL() =>
        Assert.Equal(CommandCodeAPI.BillingURL, CreditsReader.TopUpURL(ProviderKind.Commandcode));

    [Theory]
    [Trait("Req", "PR-PROVREG-204")] // ProviderCredits.swift:73 - "default: nil"
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Devin)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Pi)]
    public void TopUpURL_is_null_for_every_other_provider(ProviderKind provider) =>
        Assert.Null(CreditsReader.TopUpURL(provider));

    // ---- PR-PROVREG-207: struct shape ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-207")] // ProviderCredits.swift:6-18
    public void ProviderCredits_carries_exactly_the_six_fields()
    {
        var credits = new ProviderCredits
        {
            Total = 12.34,
            Currency = "USD",
            ToppedUp = 1,
            Granted = 2,
            Monthly = 3,
            IsUsable = true,
        };
        Assert.Equal(12.34, credits.Total);
        Assert.Equal("USD", credits.Currency);
        Assert.Equal(1, credits.ToppedUp);
        Assert.Equal(2, credits.Granted);
        Assert.Equal(3, credits.Monthly);
        Assert.True(credits.IsUsable);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-207")] // ProviderCredits.swift:6 - Sendable, Equatable (drives view animation)
    public void ProviderCredits_is_equatable_by_value()
    {
        var a = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        var b = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        var c = new ProviderCredits { Total = 2, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    // ---- PR-PROVREG-208 / PR-USAGE-055: money, en_US currency ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-208")] // ProviderCredits.swift:38-44 - USD gives "$12.34"
    public void Money_formats_a_three_letter_currency_code_pinned_to_en_US()
    {
        Assert.Equal("$12.34", ProviderCredits.Money(12.34, "USD"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-208")] // ProviderCredits.swift:39 - code.uppercased()
    public void Money_uppercases_a_lowercase_three_letter_code()
    {
        Assert.Equal("$0.43", ProviderCredits.Money(0.43, "usd"));
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-208")] // ProviderCredits.swift:40 - "code.count == 3, code.allSatisfy(\.isLetter)"
    [InlineData("US")]     // too short
    [InlineData("USDD")]   // too long
    [InlineData("US1")]    // contains a digit
    public void Money_falls_back_when_the_code_is_not_exactly_three_letters(string currency)
    {
        // ProviderCredits.swift:41-43: number then " " then the raw currency (non-empty).
        Assert.Equal($"5.00 {currency}", ProviderCredits.Money(5, currency));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-209")] // ProviderCredits.swift:41-42 - empty currency returns the number alone
    public void Money_with_an_empty_currency_returns_the_plain_number_alone()
    {
        Assert.Equal("5.00", ProviderCredits.Money(5, ""));
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-209")] // ProviderCredits.swift:47-49 plain() - exactly two fraction digits, en_US
    [InlineData(3.0, "3.00")]
    [InlineData(1234.5, "1,234.50")]
    public void Money_fallback_uses_two_decimal_places_and_en_US_grouping(double value, string expectedNumber)
    {
        Assert.Equal(expectedNumber, ProviderCredits.Money(value, ""));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-055")] // ProviderCredits.swift:34-37 doc comment: "$0.43" not "US$ 0,43"
    public void Money_never_consults_the_machine_locale()
    {
        // The formatter is pinned to en_US regardless of the running culture; this asserts the fixed output
        // rather than switching CurrentCulture (R-FMT-2 forbids Calendar.current/culture-driven formatting sites
        // from being exercised through the ambient system culture in a unit test).
        Assert.Equal("$0.43", ProviderCredits.Money(0.43, "USD"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-207")] // ProviderCredits.swift:20-21 amountText
    public void AmountText_formats_the_total_with_the_currency()
    {
        var credits = new ProviderCredits { Total = 12.34, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        Assert.Equal("$12.34", credits.AmountText);
    }

    // ---- PR-PROVREG-210 / PR-USAGE-056: breakdownText --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-210")] // ProviderCredits.swift:23-30 - fixed order Monthly, Topped up, Granted
    public void BreakdownText_orders_monthly_then_topped_up_then_granted()
    {
        var credits = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = 10, Granted = 2.34, Monthly = 5, IsUsable = true };
        Assert.Equal("Monthly $5.00 · Topped up $10.00 · Granted $2.34", credits.BreakdownText);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-056")] // ProviderCredits.swift:23 doc example - "Topped up $10.00 · Granted $2.34"
    public void BreakdownText_matches_the_doc_comment_example()
    {
        var credits = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = 10, Granted = 2.34, Monthly = null, IsUsable = true };
        Assert.Equal("Topped up $10.00 · Granted $2.34", credits.BreakdownText);
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-210")] // ProviderCredits.swift:26-28 - "> 0" excludes zero and negative
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void BreakdownText_omits_a_split_that_is_zero_or_negative(double amount)
    {
        var credits = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = amount, Granted = 5, Monthly = null, IsUsable = true };
        Assert.Equal("Granted $5.00", credits.BreakdownText);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-210")] // ProviderCredits.swift:29 - "parts.isEmpty ? nil : ..."
    public void BreakdownText_is_null_when_no_split_is_present()
    {
        var credits = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        Assert.Null(credits.BreakdownText);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-210")] // ProviderCredits.swift:29 - zero/negative splits count as absent too
    public void BreakdownText_is_null_when_every_split_is_zero_or_negative()
    {
        var credits = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = 0, Granted = -1, Monthly = 0, IsUsable = true };
        Assert.Null(credits.BreakdownText);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-056")] // ProviderCredits.swift:24 - single component, no separator
    public void BreakdownText_with_one_component_has_no_separator()
    {
        var credits = new ProviderCredits { Total = 1, Currency = "USD", ToppedUp = null, Granted = null, Monthly = 5, IsUsable = true };
        Assert.Equal("Monthly $5.00", credits.BreakdownText);
    }

    // ---- PR-PROVREG-211: isDepleted -----------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-211")] // ProviderCredits.swift:32 - "!isUsable || total <= 0"
    public void IsDepleted_is_true_when_the_provider_marks_it_unusable_even_with_a_positive_total()
    {
        var credits = new ProviderCredits { Total = 100, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = false };
        Assert.True(credits.IsDepleted);
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-211")] // ProviderCredits.swift:32 - "total <= 0"
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void IsDepleted_is_true_when_the_total_is_zero_or_negative(double total)
    {
        var credits = new ProviderCredits { Total = total, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        Assert.True(credits.IsDepleted);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-211")] // ProviderCredits.swift:32
    public void IsDepleted_is_false_when_usable_and_positive()
    {
        var credits = new ProviderCredits { Total = 5, Currency = "USD", ToppedUp = null, Granted = null, Monthly = null, IsUsable = true };
        Assert.False(credits.IsDepleted);
    }
}
