using Marrow.App.Assistant;
using Marrow.App.Assistant.Teach;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Owed native check (never run by the unit agent; the lead runs it alone, with focus, on a quiet desktop and no other typing):
// DESIGN.md rev 5, 16.1 "Native gate", AS-19. The low-level hooks of a teach recording hear what a person does: a click at a point, a double
// click, a burst of typing that ends after a pause (as a count: no key is kept), a shortcut as its chord, and the typing that had not ended when
// the hooks came off. Input another program injected is not the owner's and is not heard unless the test says so. After Stop nothing is heard.
// The typing goes to a real Notepad the test opens and closes.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-9")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class TeachInputNativeTests
{
    private static async Task<T?> Next<T>(List<TeachInput> heard, TimeSpan within) where T : TeachInput
    {
        var until = DateTime.UtcNow + within;
        while (DateTime.UtcNow < until)
        {
            lock (heard)
            {
                if (heard.OfType<T>().FirstOrDefault() is { } found) return found;
            }
            await Task.Delay(25);
        }
        return null;
    }

    [Fact]
    public async Task A_click_a_burst_of_typing_and_a_shortcut_are_heard_as_a_click_a_count_and_a_chord()
    {
        using var notepad = new TeachNativeDesk.Notepad("");
        var heard = new List<TeachInput>();
        using var input = new WindowsTeachInput { AcceptInjected = true };
        input.Start(i => { lock (heard) heard.Add(i); });
        TeachNativeDesk.Click(300, 300);
        var click = await Next<TeachInput.Click>(heard, TimeSpan.FromSeconds(3));
        Assert.Equal(("click", 300, 300), (click!.Verb, click.X, click.Y));
        TeachNativeDesk.Press(0x41, 0x42, 0x43);
        Assert.NotNull(await Next<TeachInput.TypingStarted>(heard, TimeSpan.FromSeconds(3)));
        var typing = await Next<TeachInput.Typing>(heard, WindowsTeachInput.BurstIdle + TimeSpan.FromSeconds(3));
        Assert.Equal(3, typing!.Count);
        TeachNativeDesk.PressWithControl(0x53);
        var chord = await Next<TeachInput.Chord>(heard, TimeSpan.FromSeconds(3));
        Assert.Equal("Ctrl+S", chord!.Text);
        input.Stop();
    }

    [Fact]
    public async Task Two_quick_clicks_in_one_place_are_a_click_and_then_a_double_click()
    {
        using var notepad = new TeachNativeDesk.Notepad("");
        var heard = new List<TeachInput>();
        using var input = new WindowsTeachInput { AcceptInjected = true };
        input.Start(i => { lock (heard) heard.Add(i); });
        TeachNativeDesk.Click(320, 320);
        TeachNativeDesk.Click(320, 320);
        await Task.Delay(500);
        input.Stop();
        lock (heard) Assert.Equal(["click", "double-click"], heard.OfType<TeachInput.Click>().Select(c => c.Verb));
    }

    [Fact]
    public async Task Input_another_program_injected_is_not_heard_by_default()
    {
        using var notepad = new TeachNativeDesk.Notepad("");
        var heard = new List<TeachInput>();
        using var input = new WindowsTeachInput();
        input.Start(i => { lock (heard) heard.Add(i); });
        TeachNativeDesk.Click(300, 300);
        TeachNativeDesk.Press(0x41);
        await Task.Delay(WindowsTeachInput.BurstIdle + TimeSpan.FromSeconds(1));
        input.Stop();
        lock (heard) Assert.Empty(heard);
    }

    [Fact]
    public async Task Typing_that_had_not_ended_when_the_hooks_came_off_is_handed_back_and_nothing_is_heard_after()
    {
        using var notepad = new TeachNativeDesk.Notepad("");
        var heard = new List<TeachInput>();
        using var input = new WindowsTeachInput { AcceptInjected = true };
        input.Start(i => { lock (heard) heard.Add(i); });
        TeachNativeDesk.Press(0x41, 0x42);
        await Next<TeachInput.TypingStarted>(heard, TimeSpan.FromSeconds(3));
        var tail = input.Stop();
        Assert.Equal(2, Assert.IsType<TeachInput.Typing>(tail).Count);
        heard.Clear();
        TeachNativeDesk.Click(300, 300);
        TeachNativeDesk.Press(0x41);
        await Task.Delay(WindowsTeachInput.BurstIdle + TimeSpan.FromSeconds(1));
        lock (heard) Assert.Empty(heard);
    }

    [Fact]
    public void The_hooks_can_be_put_on_and_taken_off_again_and_a_second_start_is_refused()
    {
        using var input = new WindowsTeachInput();
        input.Start(_ => { });
        Assert.Throws<InvalidOperationException>(() => input.Start(_ => { }));
        Assert.Null(input.Stop());
        input.Start(_ => { });
        Assert.Null(input.Stop());
    }
}
