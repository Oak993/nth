using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the card and the keyboard (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "It never steals focus",
// 2.7). The window the keyboard came from is saved when the reply field's click is answered with MA_ACTIVATE; 100 ms later
// the overlay holds the keyboard or the card says "Type your answer in the chat"; at a hand-back the keyboard goes back
// only when the overlay is still in front, the saved window is the same window, not minimized or cloaked, and no borrow
// runs for it; a click elsewhere only takes the keyboard away; no borrow starts while the card holds the keyboard, and a
// queued one waits 300 ms after the hand-back.
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class CardFocusTests
{
    private const nint Overlay = 0x50;
    private const nint Word = 0x10;

    private static (CardFocus Focus, PcFakeCardFocusNative Native, ManualTimeProvider Clock, List<(double Seconds, Action Run)> Later, HashSet<nint> Borrowing) Make()
    {
        var native = new PcFakeCardFocusNative { Front = Word };
        native.Windows[Word] = (40, "OpusApp");
        native.Windows[Overlay] = (7, "Tactus");
        native.FocusedIn[Word] = "body";
        var clock = new ManualTimeProvider();
        var later = new List<(double, Action)>();
        var borrowing = new HashSet<nint>();
        var focus = new CardFocus(native, () => Overlay, borrowing.Contains, clock, (seconds, run) => later.Add((seconds, run)));
        return (focus, native, clock, later, borrowing);
    }

    // the click is answered with MA_ACTIVATE, Windows activates the overlay, and the 100 ms check runs
    private static void Take(CardFocus focus, PcFakeCardFocusNative native, List<(double Seconds, Action Run)> later)
    {
        focus.Activating();
        native.Front = Overlay;
        focus.Activated();
        var (seconds, run) = later[^1];
        Assert.Equal(CardFocus.ActivationCheckSeconds, seconds, 9);
        run();
    }

    [Fact]
    public void The_keyboard_goes_back_to_the_saved_window_and_its_element()
    {
        var (focus, native, _, later, _) = Make();

        Take(focus, native, later);
        Assert.True(focus.HasKeyboard);
        Assert.Equal(new CardFocusTarget(Word, 40, "OpusApp", "body"), focus.Saved);
        native.LandsOn = "ribbon";

        Assert.Equal(CardHandBack.Done, focus.HandBack());
        Assert.Equal(new[] { Word }, native.Activated);
        Assert.Equal(new[] { "body" }, native.Focused);
        Assert.False(focus.HasKeyboard);
        Assert.Equal(CardHandBack.NotHeld, focus.HandBack());
        Assert.Single(native.Activated);
    }

    [Fact]
    public void A_locked_foreground_says_type_in_the_chat()
    {
        var (focus, native, _, later, _) = Make();
        var failed = 0;
        focus.ActivationFailed += () => failed++;

        focus.Activating();
        later[^1].Run();

        Assert.Equal(1, failed);
        Assert.False(focus.HasKeyboard);
        Assert.Equal(CardHandBack.NotHeld, focus.HandBack());
        Assert.Empty(native.Activated);
    }

    [Fact]
    public void A_click_on_another_app_only_takes_the_keyboard_away()
    {
        var (focus, native, _, later, _) = Make();
        Take(focus, native, later);

        native.Front = 0x20;
        focus.Deactivated();

        Assert.Equal(CardHandBack.NotHeld, focus.HandBack());
        Assert.Empty(native.Activated);
    }

    [Fact]
    public void Nothing_is_activated_when_a_rule_fails()
    {
        // the user clicked elsewhere and the overlay lost the front without a deactivation yet
        var (focus, native, _, later, borrowing) = Make();
        Take(focus, native, later);
        native.Front = 0x20;
        Assert.Equal(CardHandBack.Moved, focus.HandBack());

        // the saved window closed and its handle was reused by another process
        native.Front = Word;
        Take(focus, native, later);
        native.Windows[Word] = (41, "OpusApp");
        Assert.Equal(CardHandBack.Gone, focus.HandBack());
        native.Windows[Word] = (40, "OpusApp");

        // minimized or cloaked
        native.Front = Word;
        Take(focus, native, later);
        native.Minimized.Add(Word);
        Assert.Equal(CardHandBack.Hidden, focus.HandBack());
        native.Minimized.Clear();

        // a borrow runs for it
        native.Front = Word;
        Take(focus, native, later);
        borrowing.Add(Word);
        Assert.Equal(CardHandBack.Borrowing, focus.HandBack());

        Assert.Empty(native.Activated);
    }

    [Fact]
    public void No_borrow_starts_while_the_card_holds_the_keyboard_and_one_waits_300_ms_after()
    {
        var (focus, native, clock, later, _) = Make();
        Assert.Equal(TimeSpan.Zero, focus.BorrowWait());

        Take(focus, native, later);
        Assert.Null(focus.BorrowWait());
        focus.HandBack();
        Assert.Equal(CardFocus.BorrowWaitSeconds, focus.BorrowWait()!.Value.TotalSeconds, 6);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0.1, focus.BorrowWait()!.Value.TotalSeconds, 6);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(TimeSpan.Zero, focus.BorrowWait());
    }

    [Fact]
    public void Typing_lasts_two_seconds_after_a_key_or_while_a_draft_is_open()
    {
        var (focus, _, clock, _, _) = Make();
        Assert.False(focus.Typing(draftOpen: false));
        Assert.True(focus.Typing(draftOpen: true));

        focus.Typed();
        clock.Advance(TimeSpan.FromSeconds(1.9));
        Assert.True(focus.Typing(draftOpen: false));
        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.False(focus.Typing(draftOpen: false));
    }

    [Fact]
    public void A_second_click_while_the_card_holds_the_keyboard_keeps_the_first_saved_window()
    {
        var (focus, native, _, later, _) = Make();
        Take(focus, native, later);

        focus.Activating();
        later[^1].Run();

        Assert.Equal(Word, focus.Saved!.Window);
        Assert.True(focus.HasKeyboard);
    }
}
