using System;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Views.Usage;

public sealed class UsageViewsTests
{
    [Fact]
    [Trait("Req", "PR-USAGE-028")] // App/Views/Usage/UsageViews.swift:180-190
    public void Reset_text_clamps_past_dates_and_formats_short_intervals_in_minutes_and_hours()
    {
        var now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal("in 1 min", Marrow.App.Views.Usage.LimitRow.ResetText(now.AddSeconds(-1), now));
        Assert.Equal("in 1 min", Marrow.App.Views.Usage.LimitRow.ResetText(now.AddSeconds(30), now));
        Assert.Equal("in 4 hr 0 min", Marrow.App.Views.Usage.LimitRow.ResetText(now.AddSeconds(4 * 3600 + 59), now));
        Assert.Equal("in 4 hr 1 min", Marrow.App.Views.Usage.LimitRow.ResetText(now.AddSeconds(4 * 3600 + 60), now));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-054")] // App/Views/Usage/UsageViews.swift:111-117; Services/Providers/ProviderCredits.swift:32
    public void Credits_are_depleted_when_unusable_or_the_total_is_not_positive()
    {
        var positiveButUnusable = new ProviderCredits { Total = 1, Currency = "USD", IsUsable = false };
        var usableAtZero = new ProviderCredits { Total = 0, Currency = "USD", IsUsable = true };
        var usableAndPositive = new ProviderCredits { Total = 1, Currency = "USD", IsUsable = true };

        Assert.True(positiveButUnusable.IsDepleted);
        Assert.True(usableAtZero.IsDepleted);
        Assert.False(usableAndPositive.IsDepleted);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-057")] // App/Views/Usage/UsageViews.swift:118-122; Services/Providers/ProviderCredits.swift:68-75; Services/Providers/CommandCodeAPI.swift:14
    public void Command_Code_top_up_uses_its_billing_page_and_other_providers_without_a_url_have_none()
    {
        Assert.Equal("https://commandcode.ai/billing", CreditsReader.TopUpURL(ProviderKind.Commandcode)?.AbsoluteUri);
        Assert.Null(CreditsReader.TopUpURL(ProviderKind.Claude));
    }

    [Fact]
    [Trait("Req", "UI-USAGE-022")] // App/Views/Usage/UsageViews.swift:268-270; Services/Providers/PlanLimits.swift:34-45
    public void Reset_outcomes_have_their_source_messages()
    {
        Assert.Equal("Usage was reset.", PlanLimits.ResetOutcome.Reset.Message);
        Assert.Equal("There was no active usage to reset.", PlanLimits.ResetOutcome.NothingToReset.Message);
        Assert.Equal("No banked resets are left.", PlanLimits.ResetOutcome.NoCredit.Message);
        Assert.Equal("That reset was already used.", PlanLimits.ResetOutcome.AlreadyRedeemed.Message);
    }

    [Fact]
    [Trait("Req", "UI-USAGE-023")] // App/Views/Usage/UsageViews.swift:275-277; Services/Providers/PlanLimits.swift:49-60
    public void Reset_failure_descriptions_are_the_error_text()
    {
        Assert.Equal("Codex CLI not found.", new ResetCreditError(new ResetCreditError.Case.CodexUnavailable()).ErrorDescription);
        Assert.Equal("Codex returned an unknown reset result.", new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()).ErrorDescription);
        Assert.Equal("provider rejection", new ResetCreditError(new ResetCreditError.Case.Rejected("provider rejection")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "UI-USAGE-031")] // App/Views/Usage/UsageViews.swift:99-104, :130-138; Services/Providers/ProviderCredits.swift:23-30
    public void Credits_breakdown_uses_the_fixed_order_and_omits_nonpositive_parts()
    {
        var credits = new ProviderCredits
        {
            Total = 15.00,
            Currency = "USD",
            Monthly = 12.00,
            ToppedUp = 2.50,
            Granted = 0.50,
            IsUsable = true,
        };

        Assert.Equal("Monthly $12.00 \U000000B7 Topped up $2.50 \U000000B7 Granted $0.50", credits.BreakdownText);

        var withoutPositiveParts = credits with { Monthly = 0, ToppedUp = -1, Granted = null };
        Assert.Null(withoutPositiveParts.BreakdownText);
    }

    [Fact]
    [Trait("Req", "UI-USAGE-032")] // App/Views/Usage/UsageViews.swift:111-117; Services/Providers/ProviderCredits.swift:20-21, :34-49
    public void Credits_amount_uses_the_provider_currency_format()
    {
        var credits = new ProviderCredits
        {
            Total = 0.43,
            Currency = "USD",
            IsUsable = true,
        };

        Assert.Equal("$0.43", credits.AmountText);
    }

    [Fact]
    [Trait("Req", "UI-USAGE-034")] // App/Views/Usage/UsageViews.swift:118-122; Services/Providers/ProviderCredits.swift:68-75
    public void DeepSeek_top_up_url_is_the_billing_page_and_Codex_has_none()
    {
        Assert.Equal("https://platform.deepseek.com/top_up", CreditsReader.TopUpURL(ProviderKind.Deepseek)?.AbsoluteUri);
        Assert.Null(CreditsReader.TopUpURL(ProviderKind.Codex));
    }
}
