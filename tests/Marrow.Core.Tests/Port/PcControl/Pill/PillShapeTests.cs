// PORT-ONLY: the pill's shape over time (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Grab in flight", 10.6 "Hit testing"):
// a resting shape, a moving one that follows the travel and the size springs exactly, the rounded hit test with its 2 DIP
// inflation, the settle, and the final rect.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PillShapeTests
{
    [Fact]
    public void A_resting_shape_is_its_rect_at_every_time_and_is_at_rest()
    {
        var shape = PillShape.Resting(new PillRect(740, 8, 440, 48));

        Assert.True(shape.AtRest);
        Assert.True(shape.Settled(0));
        Assert.Equal(new PillRect(740, 8, 440, 48), shape.At(0));
        Assert.Equal(new PillRect(740, 8, 440, 48), shape.At(99));
        Assert.Equal(shape.At(5), shape.Final);
    }

    [Fact]
    public void A_moving_shape_follows_the_travel_and_the_width_spring_exactly()
    {
        var travel = PillTravel.Start(10, new PillVector(16, 8), new PillVector(740, 8), PillVector.Zero, reduceMotion: false);
        var width = PillCurve.Spring(320, 440, 0, PillCurve.WidthResponse, 10);
        var shape = PillShape.Moving(travel, width, 0, null, 48);

        Assert.False(shape.AtRest);
        foreach (var t in new[] { 10.0, 10.05, 10.2, 10.4 })
        {
            var at = shape.At(t);
            Assert.Equal(travel.Value(t).X, at.X, 9);
            Assert.Equal(travel.Value(t).Y, at.Y, 9);
            Assert.Equal(width.Value(t), at.Width, 9);
            Assert.Equal(48, at.Height);
        }
        Assert.Equal(new PillRect(740, 8, 440, 48), shape.Final);
        Assert.False(shape.Settled(10.1));
        Assert.True(shape.Settled(11));
    }

    [Fact]
    public void Contains_follows_the_rounded_corners_and_the_inflation()
    {
        var shape = PillShape.Resting(new PillRect(100, 10, 300, 48));

        Assert.True(shape.Contains(new PillVector(250, 34), 0));
        Assert.True(shape.Contains(new PillVector(124, 11), 0));
        // The corner's cut: inside the rect, outside its 24 DIP arc.
        Assert.False(shape.Contains(new PillVector(101, 11), 0));
        Assert.False(shape.Contains(new PillVector(99, 34), 0));
        Assert.True(shape.Contains(new PillVector(99, 34), 0, PillShape.HitInflate));
        Assert.False(shape.Contains(new PillVector(97.5, 34), 0, PillShape.HitInflate));
    }

    [Fact]
    public void A_moving_shape_is_hit_where_it_is_at_that_moment_not_where_it_ends()
    {
        var travel = PillTravel.Start(0, new PillVector(16, 8), new PillVector(1464, 8), PillVector.Zero, reduceMotion: false);
        var shape = PillShape.Moving(travel, null, 440, null, 48);
        var early = shape.At(0.02);
        var middle = new PillVector(early.X + 220, 32);

        Assert.True(shape.Contains(middle, 0.02));
        Assert.False(shape.Contains(middle, 0.4));
        Assert.True(shape.Contains(new PillVector(1464 + 220, 32), 0.4));
    }

    [Fact]
    public void A_sizing_shape_holds_its_place_while_its_height_grows_into_a_card()
    {
        var height = PillCurve.Spring(48, 300, 0, PillCurve.ExpandResponse, 0);
        var shape = PillShape.Sizing(new PillVector(740, 8), null, 440, height, 48);

        Assert.Equal(740, shape.At(0.1).X);
        Assert.InRange(shape.At(0.1).Height, 48.0001, 299.9);
        Assert.Equal(new PillRect(740, 8, 440, 300), shape.Final);
    }
}
