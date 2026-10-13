using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-perf-idle-7: a pointer moving over the window sends a thousand inputs a second, and each used to wake every ambient source
// (a message to the render thread and a commit per source). The time of the input is always kept; the wake goes out only
// when the window is available and the input before it is at least a second old (a restart from rest, since the idle ease
// starts at 20 s), and an unavailable window raises it itself on becoming available.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-framepacing-1")]
public sealed class AmbientWindowStateInputTests(HeadlessSession headless)
{
    [Fact]
    public void A_thousand_inputs_inside_a_second_wake_the_sources_once() => headless.OnUiThread(() =>
    {
        var state = new AmbientWindowState(new Window(), true);
        var wakes = 0;
        state.Changed += () => wakes++;
        state.Touch(100);
        Assert.Equal(1, wakes); // the first input after a long rest wakes them
        for (var i = 1; i <= 1000; i++) state.Touch(100 + i / 1000.0 * .99);
        Assert.Equal(1, wakes);
        Assert.Equal(100.99, state.LastInput, 6); // the time is kept for the sources that re-read it
    });

    [Fact]
    public void An_input_a_second_after_the_last_one_wakes_them_again_and_a_run_of_inputs_wakes_them_once_a_second() => headless.OnUiThread(() =>
    {
        var state = new AmbientWindowState(new Window(), true);
        var wakes = 0;
        state.Changed += () => wakes++;
        state.Touch(0);
        state.Touch(.5);
        Assert.Equal(1, wakes);
        state.Touch(1.5); // a second after the one before
        Assert.Equal(2, wakes);
        state.Touch(30); // out of the idle ease: a restart
        Assert.Equal(3, wakes);
        state.Touch(5); // the clock went backwards (another domain): never swallowed
        Assert.Equal(4, wakes);
    });

    [Fact]
    public void An_unavailable_window_wakes_nothing_for_input_and_wakes_the_sources_when_it_becomes_available() => headless.OnUiThread(() =>
    {
        var state = new AmbientWindowState(new Window(), true);
        var wakes = 0;
        state.Changed += () => wakes++;
        state.SetAvailable(false);
        Assert.Equal(1, wakes);
        for (var i = 0; i < 100; i++) state.Touch(100 + i * 2);
        Assert.Equal(1, wakes);
        Assert.Equal(298, state.LastInput, 6);
        state.SetAvailable(true);
        Assert.Equal(2, wakes);
    });
}
