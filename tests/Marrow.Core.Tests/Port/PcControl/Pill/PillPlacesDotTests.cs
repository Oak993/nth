// PORT-ONLY: the pill's places into the full-screen dot (OD-0145, ED-NEW-pcctl-36; DESIGN.md 10.3 "Auto-dodge" row 4,
// canvas `.sdot`): at every pill size the row and the live glyph keep their places; in the 32 DIP dot the mark is centred
// and the glyph sits at the top right corner (right 1, top 1); between them the mark stays inside the body and centred
// once the body is narrower or lower than 48 DIP, the glyph rides the right edge, and both move continuously.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-36")]
public sealed class PillPlacesDotTests
{
    private static readonly PillParts Parts = new(GroupWidth: 28, StopWidth: 130, CapWidth: 70, ShowsStop: true);

    private static PillPose Pose(double width, double height, double factor = 0.5) =>
        new(new PillVector(600, 8), factor, width, height, 0, 1, 1, 1, 1);

    [Theory]
    [InlineData(200, 48)]
    [InlineData(312, 48)]
    [InlineData(440, 48)]
    [InlineData(440, 300)]
    public void At_every_pill_size_the_row_and_the_glyph_keep_their_places(double width, double height)
    {
        var places = PillPlaces.Of(Pose(width, height), Parts);
        Assert.Equal(places.BodyLeft + PillPlaces.RowInset, places.RowLeft, 9);
        Assert.Equal(0, places.RowTop, 9);
        Assert.Equal(PillPlaces.GlyphHome, places.Glyph);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void In_the_dot_the_mark_is_centred_and_the_glyph_sits_at_the_top_right_corner(double factor)
    {
        var places = PillPlaces.Of(Pose(PillLayout.DotSize, PillLayout.DotSize, factor), Parts);
        // the mark's 32 DIP slot (its 24 DIP glyph 4 DIP in) covers the body exactly: the glyph is centred
        Assert.Equal(places.BodyLeft, places.RowLeft, 9);
        Assert.Equal(-(PillLayout.Height - PillLayout.DotSize) / 2, places.RowTop, 9);
        var markTop = places.RowTop + ((PillLayout.Height - 32) / 2);
        Assert.Equal(0, markTop, 9);
        // the glyph's 6 DIP dot: right 1, top 1 (canvas `.sdot .dt`)
        var centre = new PillVector(places.RowLeft - places.BodyLeft + places.Glyph.X, places.RowTop + places.Glyph.Y);
        Assert.Equal(PillLayout.DotSize - 1 - 3, centre.X, 9);
        Assert.Equal(1 + 3, centre.Y, 9);
    }

    [Fact]
    public void Between_the_pill_and_the_dot_the_mark_and_the_glyph_stay_inside_the_body_and_move_continuously()
    {
        PillPlaces? last = null;
        for (var width = 260.0; width >= PillLayout.DotSize - 1e-9; width -= 0.25)
        {
            // the height closes from 48 to 32 over the same span the width closes its last 16 DIP (the springs differ a little)
            foreach (var height in new[] { PillLayout.Height, Math.Max(PillLayout.DotSize, Math.Min(PillLayout.Height, width)) })
            {
                var places = PillPlaces.Of(Pose(width, height), Parts);
                var what = $"{width} x {height}";
                var bodyLeft = places.BodyLeft;
                var markLeft = places.RowLeft + 4;
                var markTop = places.RowTop + ((PillLayout.Height - 32) / 2) + 4;
                Assert.True(markLeft >= bodyLeft - 1e-9 && markLeft + 24 <= bodyLeft + width + 1e-9, what + ": the mark leaves the body sideways");
                Assert.True(markTop >= -1e-9 && markTop + 24 <= height + 1e-9, what + ": the mark leaves the body");
                if (width < PillLayout.Height) Assert.Equal(bodyLeft + (width / 2), markLeft + 12, 9);
                if (height < PillLayout.Height) Assert.Equal(height / 2, markTop + 12, 9);
                var glyph = new PillVector(places.RowLeft + places.Glyph.X, places.RowTop + places.Glyph.Y);
                Assert.True(glyph.X - 3 >= bodyLeft - 1e-9 && glyph.X + 3 <= bodyLeft + width - 1 + 1e-9, what + ": the glyph leaves the body sideways");
                Assert.True(glyph.Y - 3 >= 1 - 1e-9 && glyph.Y + 3 <= height + 1e-9, what + ": the glyph leaves the body");
            }
            var now = PillPlaces.Of(Pose(width, Math.Max(PillLayout.DotSize, Math.Min(PillLayout.Height, width))), Parts);
            if (last is { } before)
            {
                // a quarter DIP of width never moves the mark or the glyph by more than a DIP (no jump anywhere)
                Assert.True(Math.Abs((now.RowLeft - now.BodyLeft) - (before.RowLeft - before.BodyLeft)) <= 1, $"{width}: the row jumped");
                Assert.True(Math.Abs(now.RowTop - before.RowTop) <= 1, $"{width}: the row jumped up or down");
                Assert.True((now.Glyph - before.Glyph).Length <= 1, $"{width}: the glyph jumped");
            }
            last = now;
        }
    }
}
