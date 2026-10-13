using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// PORT-ONLY: final security gate N12 (ED-NEW-secfinal-25). A duty id is a short token of a-z, 0-9 and dashes that reaches a command line
// (--duty <id>) and a Task Scheduler name. The shape ended in $, which .NET lets match before a final line feed, so "d1\n" passed as an id. The shape now
// uses \A and \z: the whole text, nothing after it.
[Trait("Decision", "ED-NEW-secfinal-25")]
public sealed class DutyIdShapeTests
{
    [Theory]
    [InlineData("d1\n")]
    [InlineData("morning-digest\n")]
    [InlineData("a\n")]
    public void An_id_with_a_trailing_line_feed_is_not_an_id(string id)
    {
        Assert.DoesNotMatch(Duty.IdShape, id);
        Assert.Equal("That id is not one nth can use.", new Duty(id, "Morning digest", "dextra", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 0)), "UTC"), "Compose the digest.").Problem());
    }

    [Theory]
    [InlineData("--duty", "d1\n")]
    [InlineData("--duty=d1\n", null)]
    public void The_duty_flag_refuses_an_id_with_a_trailing_line_feed(string first, string? second)
    {
        var mode = StartupArguments.Parse(second is null ? [first] : [first, second]);

        Assert.Null(mode.DutyId);
        Assert.NotNull(mode.Problem);
    }

    [Theory]
    [InlineData("d1")]
    [InlineData("d0123456789ab")]
    [InlineData("a-b-c")]
    [InlineData("0")]
    public void A_plain_id_is_still_an_id(string id) => Assert.Matches(Duty.IdShape, id);

    [Theory]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("A1")]
    [InlineData("a b")]
    [InlineData("d1234567890123456789012345678901234567890")]
    public void Other_shapes_stay_refused(string id) => Assert.DoesNotMatch(Duty.IdShape, id);
}
