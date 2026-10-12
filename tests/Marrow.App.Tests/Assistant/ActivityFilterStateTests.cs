using Marrow.App.Assistant.Views;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the day choices of the Activity view's filter bar (DESIGN.md 12.5: "Filter by ... date"; ED-NEW-dextra-as21-6).
// A choice is one word (today, the last 7 days, the last 30, one day or a range) that means two days, and two days make one
// choice again, so a filter set elsewhere shows as what it is in the Day picker. Today is 2026-10-13.
[Trait("Decision", "ED-NEW-dextra-as21-6")]
[Trait("Feature", "AS-21")]
public sealed class ActivityFilterStateTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    [Fact]
    public void A_choice_means_two_days()
    {
        Assert.Equal((null, null), ActivityFilterState.DaysOf(null, Today));
        Assert.Equal((Today, Today), ActivityFilterState.DaysOf("today", Today));
        Assert.Equal((new DateOnly(2026, 10, 7), Today), ActivityFilterState.DaysOf("7", Today));
        Assert.Equal((new DateOnly(2026, 9, 14), Today), ActivityFilterState.DaysOf("30", Today));
        Assert.Equal((new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11)), ActivityFilterState.DaysOf("2026-10-11", Today));
        Assert.Equal((null, null), ActivityFilterState.DaysOf("range", Today)); // a range cannot be rebuilt from its word
        Assert.Equal((null, null), ActivityFilterState.DaysOf("garbage", Today));
    }

    [Fact]
    public void Two_days_make_the_choice_that_means_them_and_any_other_pair_is_a_range()
    {
        ActivityFilter Days(DateOnly? from, DateOnly? to) => new() { From = from, To = to };

        Assert.Null(ActivityFilterState.DayChoiceOf(new ActivityFilter(), Today));
        Assert.Equal("today", ActivityFilterState.DayChoiceOf(Days(Today, Today), Today));
        Assert.Equal("7", ActivityFilterState.DayChoiceOf(Days(new DateOnly(2026, 10, 7), Today), Today));
        Assert.Equal("30", ActivityFilterState.DayChoiceOf(Days(new DateOnly(2026, 9, 14), Today), Today));
        Assert.Equal("2026-10-12", ActivityFilterState.DayChoiceOf(Days(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)), Today));
        Assert.Equal("range", ActivityFilterState.DayChoiceOf(Days(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12)), Today));
        Assert.Equal("range", ActivityFilterState.DayChoiceOf(Days(new DateOnly(2026, 10, 12), null), Today));
        Assert.Equal("range", ActivityFilterState.DayChoiceOf(Days(null, new DateOnly(2026, 10, 12)), Today));
    }

    [Fact]
    public void Every_choice_the_picker_offers_survives_the_round_trip()
    {
        foreach (var choice in new[] { "today", "7", "30", "2026-10-11" })
        {
            var (from, to) = ActivityFilterState.DaysOf(choice, Today);
            Assert.Equal(choice, ActivityFilterState.DayChoiceOf(new ActivityFilter { From = from, To = to }, Today));
        }
    }
}
