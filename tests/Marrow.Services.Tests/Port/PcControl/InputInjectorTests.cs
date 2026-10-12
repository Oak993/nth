using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.3 and 5.2: a click maps to the right normalised absolute values on every
// DPI fixture, text goes as KEYEVENTF_UNICODE chunks; ED-NEW-pcctl-2 (legacy Marrow pc-control.test.ts:144-179): modifiers
// down first and up last, text is text whatever is in it, a newline is Enter. Events go to a recorder, never SendInput.
[Trait("Decision", "ED-NEW-pcctl-1")]
[Trait("Decision", "ED-NEW-pcctl-2")]
public sealed class InputInjectorTests
{
    private const uint Abs = PcInputPlan.MOVE | PcInputPlan.ABSOLUTE | PcInputPlan.VIRTUALDESK;

    private static (InputInjector Injector, PcFakeSendInput Sink, PcFakeClock Clock) Make(PcRect desktop)
    {
        var sink = new PcFakeSendInput();
        var clock = new PcFakeClock();
        return (new InputInjector(sink, () => desktop, clock), sink, clock);
    }

    public static TheoryData<int, int, int, int, int, int> Desktops() => new()
    {
        // left, top, width, height of the virtual desktop; the point clicked.
        { 0, 0, 1920, 1080, 960, 540 },
        { 0, 0, 2560, 1440, 2559, 1439 },
        { -2560, 0, 4480, 1440, -1, 700 },
        { -3840, -400, 5760, 2160, -3840, -400 },
        { 0, -1200, 3840, 3360, 1000, -600 },
        { 0, 0, 7680, 2160, 7000, 10 },
    };

    [Theory]
    [MemberData(nameof(Desktops))]
    public async Task A_click_moves_by_absolute_virtual_desktop_values_that_land_on_the_pixel(int left, int top, int width, int height, int x, int y)
    {
        var desktop = new PcRect(left, top, width, height);
        var (injector, sink, _) = Make(desktop);
        await injector.Click(new PcPoint(x, y), PcMouseButton.Left, 1, [], CancellationToken.None);
        var events = sink.All.ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal(Abs, events[0].Flags);
        Assert.Equal(new PcPoint(x, y), CoordinateMapper.FromAbsolute(events[0].Dx, events[0].Dy, desktop));
        Assert.Equal(PcInputPlan.LEFTDOWN, events[1].Flags);
        Assert.Equal(PcInputPlan.LEFTUP, events[2].Flags);
        Assert.Single(sink.Batches);
    }

    [Fact]
    public async Task A_right_double_click_with_ctrl_holds_ctrl_around_both_clicks()
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Click(new PcPoint(5, 5), PcMouseButton.Right, 2, [KeyChordParser.Control], CancellationToken.None);
        var flags = sink.All.Select(e => (e.Kind, e.Vk, e.Flags)).ToList();
        Assert.Equal((PcInputKind.Key, KeyChordParser.Control, 0u), flags[1]);
        Assert.Equal(new[] { PcInputPlan.RIGHTDOWN, PcInputPlan.RIGHTUP, PcInputPlan.RIGHTDOWN, PcInputPlan.RIGHTUP }, flags.Skip(2).Take(4).Select(f => f.Flags));
        Assert.Equal((PcInputKind.Key, KeyChordParser.Control, PcInputPlan.KEY_UP), flags[6]);
    }

    [Fact]
    public async Task Modifiers_go_down_in_order_before_the_key_and_up_in_reverse_after_it()
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Keys(KeyChordParser.Parse("ctrl+shift+enter").Chord!, 1, CancellationToken.None);
        var keys = sink.All.Select(e => (e.Vk, Up: (e.Flags & PcInputPlan.KEY_UP) != 0)).ToList();
        Assert.Equal(new (ushort, bool)[]
        {
            (KeyChordParser.Control, false), (KeyChordParser.Shift, false), (KeyChordParser.Return, false),
            (KeyChordParser.Return, true), (KeyChordParser.Shift, true), (KeyChordParser.Control, true),
        }, keys);
        Assert.All(sink.All, e => Assert.Equal((ushort)(e.Vk + 0x100), e.Scan));
    }

    [Fact]
    public async Task Repeat_presses_the_key_again_inside_one_hold_of_the_modifiers()
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Keys(KeyChordParser.Parse("shift+tab").Chord!, 3, CancellationToken.None);
        Assert.Equal(1 + 6 + 1, sink.All.Count());
    }

    [Fact]
    public async Task Arrow_keys_carry_the_extended_flag()
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Keys(KeyChordParser.Parse("left").Chord!, 1, CancellationToken.None);
        Assert.All(sink.All, e => Assert.Equal(PcInputPlan.KEY_EXTENDED, e.Flags & PcInputPlan.KEY_EXTENDED));
    }

    [Fact]
    public async Task Text_is_sent_as_unicode_characters_so_no_character_is_a_control_code()
    {
        // Legacy Marrow's SendKeys had to escape % ( + ^ ~; Unicode input has no control characters to escape at all.
        const string text = "50% (a+b)^2 ~ x {ENTER} it's";
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Text(text, CancellationToken.None);
        var events = sink.All.ToList();
        Assert.Equal(text.Length * 2, events.Count);
        Assert.All(events, e => Assert.Equal((ushort)0, e.Vk));
        Assert.Equal(text, new string(events.Where(e => (e.Flags & PcInputPlan.KEY_UP) == 0).Select(e => (char)e.Scan).ToArray()));
        Assert.All(events, e => Assert.Equal(PcInputPlan.KEY_UNICODE, e.Flags & PcInputPlan.KEY_UNICODE));
    }

    [Fact]
    public async Task A_newline_in_any_form_is_one_enter_and_a_tab_is_tab()
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Text("one\ntwo\r\nthree\rfour\tfive", CancellationToken.None);
        var presses = sink.All.Where(e => e.Vk != 0 && (e.Flags & PcInputPlan.KEY_UP) == 0).Select(e => e.Vk).ToList();
        Assert.Equal(new ushort[] { KeyChordParser.Return, KeyChordParser.Return, KeyChordParser.Return, 0x09 }, presses);
    }

    [Fact]
    public async Task Long_text_goes_in_chunks_of_64_characters_with_a_gap_between_chunks()
    {
        var (injector, sink, clock) = Make(new PcRect(0, 0, 100, 100));
        await injector.Text(new string('x', 150), CancellationToken.None);
        Assert.Equal(new[] { 128, 128, 44 }, sink.Batches.Select(b => b.Count));
        Assert.Equal(new[] { InputInjector.TextGapMs, InputInjector.TextGapMs }, clock.Delays);
    }

    [Fact]
    public void A_chunk_never_ends_between_the_halves_of_a_surrogate_pair()
    {
        var text = new string('x', 63) + "\U0001F600" + "y";
        var chunks = PcInputPlan.Text(text, vk => vk);
        Assert.Equal(65 * 2, chunks[0].Count);
        Assert.Equal('\uDE00', (char)chunks[0][^1].Scan);
    }

    [Fact]
    public async Task Cancelling_between_chunks_stops_the_rest_of_the_text()
    {
        var (injector, sink, clock) = Make(new PcRect(0, 0, 100, 100));
        using var stop = new CancellationTokenSource();
        clock.OnDelay = _ => stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => injector.Text(new string('x', 200), stop.Token));
        Assert.Single(sink.Batches);
    }

    [Theory]
    [InlineData(PcScrollDirection.Up, PcInputPlan.WHEEL, 360)]
    [InlineData(PcScrollDirection.Down, PcInputPlan.WHEEL, -360)]
    [InlineData(PcScrollDirection.Right, PcInputPlan.HWHEEL, 360)]
    [InlineData(PcScrollDirection.Left, PcInputPlan.HWHEEL, -360)]
    public async Task The_wheel_moves_to_the_point_then_turns_by_whole_notches(PcScrollDirection direction, uint flag, int data)
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        await injector.Wheel(new PcPoint(50, 50), direction, 3, CancellationToken.None);
        var events = sink.All.ToList();
        Assert.Equal(Abs, events[0].Flags);
        Assert.Equal(flag, events[1].Flags);
        Assert.Equal(data, events[1].MouseData);
    }

    [Fact]
    public async Task A_drag_presses_moves_through_each_point_and_always_releases()
    {
        var desktop = new PcRect(0, 0, 1000, 1000);
        var (injector, sink, clock) = Make(desktop);
        await injector.Drag([new PcPoint(10, 10), new PcPoint(50, 60), new PcPoint(90, 99)], [KeyChordParser.Shift], 100, CancellationToken.None);
        var events = sink.All.ToList();
        Assert.Equal(Abs, events[0].Flags);
        Assert.Equal(KeyChordParser.Shift, events[1].Vk);
        Assert.Equal(PcInputPlan.LEFTDOWN, events[2].Flags);
        Assert.Equal(new PcPoint(90, 99), CoordinateMapper.FromAbsolute(events[4].Dx, events[4].Dy, desktop));
        Assert.Equal(PcInputPlan.LEFTUP, events[5].Flags);
        Assert.Equal(PcInputPlan.KEY_UP, events[6].Flags & PcInputPlan.KEY_UP);
        Assert.Equal(new[] { 100, InputInjector.DragStepMs, InputInjector.DragStepMs }, clock.Delays);
    }

    [Fact]
    public async Task A_stopped_drag_still_releases_the_button_and_the_modifiers()
    {
        var (injector, sink, clock) = Make(new PcRect(0, 0, 100, 100));
        using var stop = new CancellationTokenSource();
        clock.OnDelay = _ => stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => injector.Drag([new PcPoint(1, 1), new PcPoint(2, 2)], [KeyChordParser.Alt], 0, stop.Token));
        var last = sink.Batches[^1];
        Assert.Equal(PcInputPlan.LEFTUP, last[0].Flags);
        Assert.Equal(KeyChordParser.Alt, last[1].Vk);
    }

    [Fact]
    public async Task Input_that_windows_does_not_take_is_reported()
    {
        var (injector, sink, _) = Make(new PcRect(0, 0, 100, 100));
        sink.Accept = 0;
        await Assert.ThrowsAsync<PcInputBlockedException>(() => injector.Click(new PcPoint(1, 1), PcMouseButton.Left, 1, [], CancellationToken.None));
    }
}
