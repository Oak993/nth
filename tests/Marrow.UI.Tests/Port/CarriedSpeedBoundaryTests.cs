using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

public sealed class CarriedSpeedBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-motion-drawer-2")]
    public void A_second_retarget_before_the_next_frame_keeps_the_carried_start_speed(bool nested)
    {
        var spring = Animation.Smooth(.35).WithStartSpeed(7);
        if (nested) spring = spring.WithStartSpeed(-2);
        Assert.Equal(nested ? 5 : 7, spring.Speed(0), 9);
        Assert.Equal(0, spring.Speed(spring.Duration));
    }
}
