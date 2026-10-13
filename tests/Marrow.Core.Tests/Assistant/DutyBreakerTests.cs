using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Failed runs never retry endlessly", 7.1; ED-NEW-dextra-as15-5): a duty that fails 3 times in a row is
// paused with a decision card. This is the rule, pure: failures count in a row, a success (even one that finds nothing changed) starts the
// count again, the third failure pauses once, a paused duty counts nothing until the owner lets it go on.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-5")]
public sealed class DutyBreakerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static DutyBreak Fail(DutyHealth health, string reason = "the site is down") => DutyBreaker.Record(health, DutyOutcome.Failed, reason, T0);

    private static DutyBreak Succeed(DutyHealth health) => DutyBreaker.Record(health, DutyOutcome.Succeeded, null, T0);

    [Fact]
    public void The_limit_is_three()
    {
        Assert.Equal(3, DutyBreaker.MaxConsecutiveFailures);
    }

    [Fact]
    public void Two_failures_in_a_row_pause_nothing_and_the_third_pauses_the_duty_once()
    {
        var first = Fail(new DutyHealth("d1"));
        Assert.False(first.Tripped);
        Assert.Equal(1, first.Health.ConsecutiveFailures);
        var second = Fail(first.Health);
        Assert.False(second.Tripped);
        Assert.False(second.Health.Paused);
        var third = Fail(second.Health, "the sheet moved");
        Assert.True(third.Tripped);
        Assert.True(third.Health.Paused);
        Assert.Equal(3, third.Health.ConsecutiveFailures);
        Assert.Equal(T0, third.Health.PausedAt);
        Assert.Equal(["the site is down", "the site is down", "the sheet moved"], third.Health.Reasons);
    }

    [Fact]
    public void A_success_starts_the_count_again_even_after_two_failures()
    {
        var two = Fail(Fail(new DutyHealth("d1")).Health).Health;
        var healed = Succeed(two);
        Assert.Equal(0, healed.Health.ConsecutiveFailures);
        Assert.Empty(healed.Health.Reasons);
        Assert.False(healed.Tripped);
        var again = Fail(Fail(healed.Health).Health);                // two more: still not three in a row
        Assert.False(again.Tripped);
        Assert.False(again.Health.Paused);
        Assert.True(Fail(again.Health).Tripped);
    }

    [Fact]
    public void A_paused_duty_counts_nothing_more_and_does_not_trip_twice()
    {
        var paused = Fail(Fail(Fail(new DutyHealth("d1")).Health).Health).Health;
        var more = Fail(paused, "a run the owner started by hand");
        Assert.False(more.Tripped);
        Assert.Equal(paused, more.Health);                            // nothing changed, nothing hidden
        var ok = Succeed(paused);
        Assert.True(ok.Health.Paused);                                // a success does not un-pause it: only the owner does
        Assert.Equal(paused, ok.Health);
    }

    [Fact]
    public void Letting_it_go_on_starts_the_count_again()
    {
        var paused = Fail(Fail(Fail(new DutyHealth("d1")).Health).Health).Health;
        var resumed = DutyBreaker.Resume(paused);
        Assert.False(resumed.Paused);
        Assert.Null(resumed.PausedAt);
        Assert.Equal(0, resumed.ConsecutiveFailures);
        Assert.Empty(resumed.Reasons);
        Assert.Equal("d1", resumed.DutyId);
        Assert.False(Fail(resumed).Tripped);
    }

    [Fact]
    public void Only_the_last_three_reasons_are_kept()
    {
        var health = new DutyHealth("d1") { Reasons = new Marrow.Core.Port.ValueList<string>(["a", "b", "c"]) };
        var next = Fail(health with { ConsecutiveFailures = 1 }, "d");
        Assert.Equal(["b", "c", "d"], next.Health.Reasons);
    }

    [Fact]
    public void A_reason_is_one_short_line_of_data()
    {
        Assert.Equal("no reason given", DutyBreaker.OneLine(null));
        Assert.Equal("no reason given", DutyBreaker.OneLine("   \r\n\t "));
        Assert.Equal("line one line two tabbed", DutyBreaker.OneLine("line one\r\nline two\t\ttabbed"));
        Assert.Equal("bell and escape", DutyBreaker.OneLine("bell\u0007 and \u001Bescape"));
        var long200 = new string('x', 200);
        var cut = DutyBreaker.OneLine(long200);
        Assert.Equal(DutyBreaker.ReasonChars, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        // A surrogate pair is never cut in half.
        var emoji = new string('x', DutyBreaker.ReasonChars - 2) + "😀😀";
        var safe = DutyBreaker.OneLine(emoji);
        Assert.True(safe.Length <= DutyBreaker.ReasonChars);
        Assert.False(char.IsHighSurrogate(safe[^2]));
        Assert.Equal("…", safe[^1..]);
        Assert.Equal("fine", DutyBreaker.OneLine("fine"));
    }

    [Fact]
    public void The_cards_words_are_fixed()
    {
        Assert.Equal("Daily digest was paused after 3 failures in a row", DutyWords.Title("Daily digest"));
        Assert.Equal("Try it again", DutyWords.TryAgain);
        Assert.Equal("Keep it paused", DutyWords.KeepPaused);
    }
}
