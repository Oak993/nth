// PORT-ONLY: the card's misclick protection (OD-0144, OD-0145, ED-NEW-pcctl-20; DESIGN.md 4.1, 4.3, 10.4 "Safety", 10.5;
// critiques C12, S7): the 400 ms shield and its 35 % to full fade, the 150 ms rest before a mouse click counts (touch and
// pen at once), controls that landed under the pointer staying inert until it leaves them, the two separate clicks of
// "Always allow", free controls, a new layout starting it all again, and the card's keys (Enter never answers, Space
// presses under the same rules, Esc folds or only cancels a composition).
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-20")]
public sealed class CardShieldTests
{
    private static readonly CardControl No = new("no", new PillRect(16, 200, 120, 36), CardControlKind.Answer);
    private static readonly CardControl Chat = new("chat", new PillRect(144, 200, 140, 36), CardControlKind.Answer);
    private static readonly CardControl Always = new("always", new PillRect(16, 244, 268, 36), CardControlKind.Always);
    private static readonly CardControl Later = new("later", new PillRect(360, 12, 64, 28), CardControlKind.Free);
    private static readonly CardControl[] Layout = [No, Chat, Always, Later];

    private static PillVector Centre(CardControl control) =>
        new(control.Bounds.X + (control.Bounds.Width / 2), control.Bounds.Y + (control.Bounds.Height / 2));

    [Fact]
    public void Nothing_answers_inside_the_400_ms_shield_and_the_answers_fade_from_35_percent_to_full()
    {
        var shield = new CardShield();
        shield.Show(1000, Layout, pointer: null);
        shield.Moved(1000, Centre(No));

        Assert.Equal(CardClick.Shielded, shield.Click(1399, "no", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Shielded, shield.Click(1399, "no", PillPointerKind.Touch));
        Assert.Equal(CardClick.Fires, shield.Click(1400, "no", PillPointerKind.Mouse));
        Assert.Equal(1400, shield.ShieldEndsAt);
        Assert.Equal(CardShield.ShieldFrom, shield.Opacity(1000, "no"), 6);
        Assert.Equal(0.675, shield.Opacity(1200, "no"), 6);
        Assert.Equal(1, shield.Opacity(1400, "no"), 6);
        Assert.Equal(1, shield.Opacity(1000, "later"), 6);
    }

    [Fact]
    public void A_mouse_click_counts_only_after_the_pointer_rested_150_ms_on_that_control_and_touch_and_pen_at_once()
    {
        var shield = new CardShield();
        shield.Show(0, Layout, pointer: null);
        shield.Moved(500, Centre(No));
        shield.Moved(560, Centre(No) + new PillVector(30, 2));

        Assert.Equal(CardClick.NotRested, shield.Click(649, "no", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Fires, shield.Click(650, "no", PillPointerKind.Mouse));
        Assert.Equal(CardClick.NotRested, shield.Click(650, "chat", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Fires, shield.Click(650, "chat", PillPointerKind.Touch));
        Assert.Equal(CardClick.Fires, shield.Click(650, "chat", PillPointerKind.Pen));

        // Leaving the control starts its rest again.
        shield.Moved(700, Centre(Chat));
        shield.Moved(720, Centre(No));
        Assert.Equal(CardClick.NotRested, shield.Click(800, "no", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Fires, shield.Click(870, "no", PillPointerKind.Mouse));
    }

    [Fact]
    public void A_control_that_lands_under_the_pointer_stays_inert_at_35_percent_until_the_pointer_leaves_it_once()
    {
        var shield = new CardShield();
        var resting = Centre(No);
        shield.Show(0, Layout, resting);

        Assert.True(shield.IsInert("no"));
        Assert.False(shield.IsInert("chat"));
        Assert.Equal(CardClick.Inert, shield.Click(2000, "no", PillPointerKind.Mouse));
        Assert.Equal(CardShield.ShieldFrom, shield.Opacity(2000, "no"), 6);

        // Within 4 DIP of its edge is still on it.
        shield.Moved(2100, new PillVector(No.Bounds.Right + 3, resting.Y));
        Assert.True(shield.IsInert("no"));
        // A touch is never a stray hover, so it is not held back.
        Assert.Equal(CardClick.Fires, shield.Click(2100, "no", PillPointerKind.Touch));

        shield.Moved(2200, new PillVector(No.Bounds.Right + 5, resting.Y));
        Assert.False(shield.IsInert("no"));
        shield.Moved(2300, resting);
        Assert.Equal(CardClick.NotRested, shield.Click(2400, "no", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Fires, shield.Click(2450, "no", PillPointerKind.Mouse));
    }

    [Fact]
    public void Always_allow_takes_a_second_separate_click_600_ms_to_3_s_after_the_first()
    {
        var shield = Rested(Always, at: 1000);

        Assert.Equal(CardClick.Armed, shield.Click(1000, "always", PillPointerKind.Mouse));
        Assert.True(shield.IsArmed(1000, "always"));
        Assert.Equal(CardClick.TooSoon, shield.Click(1300, "always", PillPointerKind.Mouse, clickCount: 2));
        Assert.Equal(CardClick.TooSoon, shield.Click(1599, "always", PillPointerKind.Mouse));
        Assert.Equal(CardClick.TooSoon, shield.Click(1650, "always", PillPointerKind.Mouse, clickCount: 2));
        Assert.Equal(CardClick.Fires, shield.Click(1700, "always", PillPointerKind.Mouse));
        Assert.False(shield.IsArmed(1700, "always"));

        // Armed for 3 s; after that the next click arms it again.
        Assert.Equal(CardClick.Armed, shield.Click(5000, "always", PillPointerKind.Mouse));
        Assert.False(shield.IsArmed(8000, "always"));
        Assert.Equal(CardClick.Armed, shield.Click(8000, "always", PillPointerKind.Mouse));
        // A double-click's second click never arms.
        var fresh = Rested(Always, at: 1000);
        Assert.Equal(CardClick.TooSoon, fresh.Click(1000, "always", PillPointerKind.Mouse, clickCount: 2));
        Assert.False(fresh.IsArmed(1000, "always"));
    }

    [Fact]
    public void Later_the_pager_and_the_composer_are_free_of_the_shield_the_rest_and_the_inert_rule()
    {
        var shield = new CardShield();
        shield.Show(0, Layout, Centre(Later));

        Assert.False(shield.IsInert("later"));
        Assert.Equal(CardClick.Fires, shield.Click(10, "later", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Unknown, shield.Click(10, "nothing", PillPointerKind.Mouse));
    }

    [Fact]
    public void A_new_layout_starts_the_shield_and_every_rest_again_and_a_new_body_disarms_always_allow()
    {
        var shield = Rested(Always, at: 1000);
        Assert.Equal(CardClick.Armed, shield.Click(1000, "always", PillPointerKind.Mouse));

        // The composer grew: the same body, a new layout under the resting pointer.
        shield.Show(1100, Layout, Centre(Always), newBody: false);
        Assert.True(shield.IsArmed(1100, "always"));
        Assert.True(shield.IsInert("always"));
        Assert.Equal(CardClick.Shielded, shield.Click(1200, "chat", PillPointerKind.Touch));

        // The next ask: a new body.
        shield.Show(1200, Layout, null);
        Assert.False(shield.IsArmed(1200, "always"));
        shield.Moved(1700, Centre(Chat));
        Assert.Equal(CardClick.NotRested, shield.Click(1800, "chat", PillPointerKind.Mouse));
        Assert.Equal(CardClick.Fires, shield.Click(1850, "chat", PillPointerKind.Mouse));

        // A control the placement could not clear waits like one that landed under the pointer.
        shield.MarkInert(["no", "later"]);
        Assert.True(shield.IsInert("no"));
        Assert.False(shield.IsInert("later"));
    }

    [Fact]
    public void Enter_never_answers_outside_the_composer_and_in_it_sends_text_commits_a_composition_or_breaks_a_line()
    {
        var shield = Rested(No, at: 1000);

        Assert.Equal(CardKeyAction.FlashEnterHint, shield.Key(1000, CardKey.Enter, false, false, "no", false, false, false).Action);
        Assert.Equal(CardKeyAction.FlashEnterHint, shield.Key(1000, CardKey.Enter, false, false, null, false, false, false).Action);
        Assert.Equal(CardKeyAction.SendText, shield.Key(1000, CardKey.Enter, false, false, null, true, false, true).Action);
        Assert.Equal(CardKeyAction.None, shield.Key(1000, CardKey.Enter, false, false, null, true, false, false).Action);
        Assert.Equal(CardKeyAction.CommitComposition, shield.Key(1000, CardKey.Enter, false, false, null, true, true, true).Action);
        Assert.Equal(CardKeyAction.NewLine, shield.Key(1000, CardKey.Enter, true, false, null, true, false, true).Action);
        Assert.Equal("Enter does nothing here", CardShield.EnterHint);
    }

    [Fact]
    public void Esc_folds_the_card_and_never_answers_but_with_a_composition_open_only_cancels_it()
    {
        var shield = Rested(No, at: 1000);

        Assert.Equal(CardKeyAction.Fold, shield.Key(1000, CardKey.Escape, false, false, "no", false, false, false).Action);
        Assert.Equal(CardKeyAction.Fold, shield.Key(1000, CardKey.Escape, false, false, null, true, false, true).Action);
        Assert.Equal(CardKeyAction.CancelComposition, shield.Key(1000, CardKey.Escape, false, false, null, true, true, true).Action);
    }

    [Fact]
    public void Space_presses_the_focused_control_under_the_shield_and_the_two_step_rule_and_never_on_a_repeat()
    {
        var shield = new CardShield();
        shield.Show(0, Layout, pointer: null);

        Assert.Equal(new CardKeyResult(CardKeyAction.Press, CardClick.Shielded), shield.Key(399, CardKey.Space, false, false, "no", false, false, false));
        Assert.Equal(new CardKeyResult(CardKeyAction.Press, CardClick.Fires), shield.Key(400, CardKey.Space, false, false, "no", false, false, false));
        Assert.Equal(new CardKeyResult(CardKeyAction.Press, CardClick.Fires), shield.Key(10, CardKey.Space, false, false, "later", false, false, false));
        Assert.Equal(new CardKeyResult(CardKeyAction.None), shield.Key(400, CardKey.Space, false, true, "no", false, false, false));
        Assert.Equal(new CardKeyResult(CardKeyAction.None), shield.Key(400, CardKey.Space, false, false, null, false, false, false));
        Assert.Equal(new CardKeyResult(CardKeyAction.None), shield.Key(400, CardKey.Space, false, false, null, true, false, true));

        Assert.Equal(CardClick.Armed, shield.Key(1000, CardKey.Space, false, false, "always", false, false, false).Click);
        Assert.Equal(CardClick.TooSoon, shield.Key(1500, CardKey.Space, false, false, "always", false, false, false).Click);
        Assert.Equal(CardClick.Fires, shield.Key(1600, CardKey.Space, false, false, "always", false, false, false).Click);
    }

    // A card shown long ago whose pointer has rested on the control since well before at.
    private static CardShield Rested(CardControl control, double at)
    {
        var shield = new CardShield();
        shield.Show(at - 2000, Layout, pointer: null);
        shield.Moved(at - 1000, Centre(control));
        return shield;
    }
}
