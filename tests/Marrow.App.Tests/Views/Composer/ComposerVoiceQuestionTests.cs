using Marrow.App.Port;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T127 and V-T128 (voice-SPEC V-12.11; OD-0097 D, OD-0095): the language question. A click on the orb while the language default is D and nothing is stored asks: no capture, no pop, the card opens above
// the orb's slot with the tip on the orb's centre (below, tip up, when the window has no room above), 268 wide, padded 12 / 12 / 10, radius 14, the card's edge 13 pt from the slot; its content is the exact strings
// of the page; it opens on 0.18 s ease and 0.24 s ez from 6 px low and 0.96, closes 0.22 s after an answer; Reduce Motion at once.
[Collection("Headless")]
public sealed class ComposerVoiceQuestionTests(HeadlessSession headless)
{
    private static VoiceComposerRig Asking(VoiceOrbSettings? orb = null, VoiceTypingSettings? typing = null)
    {
        var rig = new VoiceComposerRig(orb ?? VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Ask }, typing);
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        rig.QuietRoom();
        return rig;
    }

    private static void Ask(VoiceComposerRig rig, ComposerView view, Avalonia.Controls.Window window)
    {
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
        Run(rig, window, 0.6);
    }

    [Fact] // V-T127: the click asks instead of listening
    [Trait("Decision", "ED-292")]
    public void A_click_on_the_orb_asks_the_question_with_no_capture_and_no_pop()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            Ask(rig, view, window);
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceSessionState.Asking, view.Voice!.Session.State);
                Assert.True(Question(view).IsOpen);
                Assert.False(view.Voice.IsLive);
                Assert.Equal(Marrow.Core.Port.Voice.Orb.OrbMotionMode.Rest, view.Voice.Mode); // the orb stays at rest: no pop
                Assert.Equal(VoiceAnnouncements.LanguageQuestion, view.Voice.Announcement);
                Assert.Equal("Which language will you speak? English, Hindi, Urdu or Auto-detect.", view.Voice.Announcement);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T127: geometry
    [Trait("Decision", "ED-292")]
    public void The_card_hangs_above_the_slot_with_its_tip_on_the_orb_centre_its_edge_13_from_the_slot_its_right_edge_at_the_orb_centre_plus_34()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            Ask(rig, view, window);
            rig.Main.OnMain(() =>
            {
                var question = Question(view);
                var card = question.Card;
                var slot = Slot(view);
                var slotRect = Frame(slot, view);
                var rect = Frame(card, view);
                var centre = OrbCentre(view);
                Assert.Equal(268, rect.Width, 0.6);
                Assert.Equal(PopoverTipEdge.Bottom, card.TipEdge);
                Assert.Equal(slotRect.Top, rect.Bottom, 0.6); // the card's body ends 13 above the slot (its tip's room is the 13)
                Assert.Equal(slotRect.Top - 13, rect.Bottom - 13, 0.6);
                Assert.Equal(centre.X + 34, rect.Right, 1.0); // right edge at the orb's centre + 34
                Assert.Equal(centre.X, rect.Left + card.TipPoint.X, 1.0); // the tip stays on the orb's centre
                Assert.Equal(slotRect.Top - 1, rect.Top + card.TipPoint.Y, 1.0); // the point 1 pt short of the slot
                Assert.False(question.IsBelow);
                // padding 12 / 12 / 10 round the content, radius 14
                var content = (Border)card.Child!;
                Assert.Equal(new Thickness(12, 12, 12, 10), content.Padding);
                Assert.Equal(new CornerRadius(14), question.Glass.CornerRadius);
                var effect = SwiftGlass.GetEffect(question.Glass);
                Assert.NotNull(effect); // the shared glass material under the card
                Assert.Equal(14, effect!.Shape.CornerRadius);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T127: kept inside the composer's edges
    [Trait("Decision", "ED-292")]
    public void The_card_is_kept_inside_the_composers_left_and_right_edges()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(300); // narrower than the card's reach: 300 wide
            Ask(rig, view, window);
            rig.Main.OnMain(() =>
            {
                var rect = Frame(Question(view).Card, view);
                var composer = view.Bounds.Width;
                Assert.True(rect.Left >= -0.6, "not past the left edge: " + rect.Left);
                Assert.True(rect.Right <= composer + 0.6, "not past the right edge: " + rect.Right + " of " + composer);
                Assert.Equal(OrbCentre(view).X, rect.Left + Question(view).Card.TipPoint.X, 1.0); // the tip stays on the orb's centre
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T127: no room above: it flips below with the tip up
    [Trait("Decision", "ED-292")]
    public void With_no_room_above_in_the_window_the_card_flips_below_with_the_tip_up()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var view = rig.MakeView();
            var window = rig.Main.OnMain(() =>
            {
                var host = new Border { Child = view, Width = 600, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                AppEnvironment.SetAppModel(host, rig.App);
                var w = new Avalonia.Controls.Window { Content = host, Width = 1400, Height = 900 };
                w.Show();
                return w;
            });
            rig.PumpWindow(window);
            Ask(rig, view, window);
            rig.Main.OnMain(() =>
            {
                var question = Question(view);
                var card = question.Card;
                var slotRect = Frame(Slot(view), view);
                var rect = Frame(card, view);
                Assert.True(question.IsBelow);
                Assert.Equal(PopoverTipEdge.Top, card.TipEdge);
                Assert.Equal(slotRect.Bottom, rect.Top, 0.6);
                Assert.Equal(OrbCentre(view).X, rect.Left + card.TipPoint.X, 1.0);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T128: the exact strings, the capsules, the dialog name
    [Trait("Decision", "ED-292")]
    public void The_card_has_the_pages_strings_the_capsule_metrics_and_is_a_dialog_named_Voice_language()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            Ask(rig, view, window);
            rig.Main.OnMain(() =>
            {
                var question = Question(view);
                Assert.Equal("Which language will you speak?", question.QuestionText.Text);
                Assert.Equal("Kept in Settings > Voice typing", question.FooterText.Text);
                Assert.Equal(["English", "Hindi", "Urdu", "Auto-detect"], question.Answers.Select(a => a.Text).ToArray());
                Assert.Equal(["en", "hi", "ur", "auto"], question.Answers.Select(a => a.Code).ToArray());
                foreach (var answer in question.Answers)
                {
                    Assert.Equal(26, answer.Bounds.Height, 0.6);
                    Assert.Equal(answer.DesiredSize.Width, answer.Bounds.Width, 0.6);
                    Assert.Equal(Measure(answer.Text, 12.5, semibold: false) + 22, answer.Bounds.Width, 1.0); // padding 0 11
                    Assert.Equal(answer.Text, AutomationProperties.GetName(answer));
                }
                Assert.Equal("Voice language", AutomationProperties.GetName(question));
                Assert.Equal(13, question.QuestionText.FontSize);
                Assert.Equal(Avalonia.Media.FontWeight.SemiBold, question.QuestionText.FontWeight);
                Assert.Equal(11, question.FooterText.FontSize);
                // 9 above the answers, 8 below them; the capsules wrap with a 6 gap
                var first = Frame(question.Answers[0], view);
                var second = Frame(question.Answers[1], view);
                Assert.Equal(first.Right + 6, second.Left, 1.0);
                Assert.Equal(Frame(question.QuestionText, view).Bottom + 9, first.Top, 1.0);
                Assert.Equal(Frame(question.Answers[^1], view).Bottom + 8, Frame(question.FooterText, view).Top, 1.0);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T128: open 0.18 s / 0.24 s ez from the tip
    [Trait("Decision", "ED-292")]
    public void The_card_opens_on_opacity_0_18_ease_and_a_rise_of_6_and_scale_of_0_96_over_0_24_ez()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var seen = Frames(rig, window, 30, () => (Opacity: Question(view).ShownOpacity, Progress: Question(view).Progress));
            var fits = Enumerable.Range(0, 4).Where(slip => seen.Select((s, i) =>
                Math.Abs(s.Opacity - CssCurves.At(CssCurves.Ease, (i - slip) * FrameTime, 0.18)) + Math.Abs(s.Progress - CssCurves.At(CssCurves.Ez, (i - slip) * FrameTime, 0.24))).All(e => e < 2e-3)).ToList();
            Assert.True(fits.Count > 0, "opacity on ease 0.18 s and the motion on ez 0.24 s: " + string.Join(", ", seen.Take(14).Select(s => s.Opacity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
            rig.Main.OnMain(() =>
            {
                var question = Question(view);
                var transform = Assert.IsType<Avalonia.Media.TransformGroup>(question.Shell.RenderTransform);
                Assert.Equal(0, ((Avalonia.Media.TranslateTransform)transform.Children[1]).Y, 1e-6); // settled: no rise
                Assert.Equal(1, ((Avalonia.Media.ScaleTransform)transform.Children[0]).ScaleX, 1e-6);
                // the origin is the tip
                Assert.Equal(question.Card.TipOrigin.Point.X, question.Shell.RenderTransformOrigin.Point.X, 1e-6);
                Assert.Equal(question.Card.TipOrigin.Point.Y, question.Shell.RenderTransformOrigin.Point.Y, 1e-6);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T128: an answer lights first, the card closes 0.22 s later; the picked capsule is in the accent
    [Trait("Decision", "ED-292")]
    public void An_answer_stores_the_language_starts_listening_and_the_card_closes_0_22_s_later_on_the_close_curves()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            Ask(rig, view, window);
            rig.Main.OnMain(() => Click(Question(view).Answers[1])); // Hindi
            rig.Main.OnMain(() =>
            {
                Assert.Equal("hi", rig.App.Settings.VoiceTyping.Lang); // stored at once
                Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State); // listening started at once
                Assert.Equal("hi", view.Voice.Session.ResolvedLanguage);
            });
            var seen = Frames(rig, window, 45, () => (Opacity: Question(view).ShownOpacity, Progress: Question(view).Progress, Open: Question(view).IsOpen, Picked: Question(view).Answers[1].PickedAmount));
            Assert.All(seen.Take(12), s => Assert.True(s.Opacity > 0.99, "still showing for the first 0.22 s")); // 0.22 s = 13 frames
            Assert.True(seen[1].Picked > 0, "the answer lights first");
            Assert.True(seen[^1].Opacity < 1e-6 && !seen[^1].Open, "closed");
            // the close: opacity .18 s ease and the motion back over .24 s ez, begun by the timer 0.22 s after the answer (the manual clock fires it at its due time; a frame either way is allowed)
            var fits = new[] { 0.22, 0.22 - FrameTime, 0.22 + FrameTime }.Where(start => seen.Select((s, i) =>
                Math.Abs(s.Opacity - (1 - CssCurves.At(CssCurves.Ease, i * FrameTime - start, 0.18))) + Math.Abs(s.Progress - (1 - CssCurves.At(CssCurves.Ez, i * FrameTime - start, 0.24)))).All(e => e < 3e-3)).ToList();
            Assert.True(fits.Count > 0, "the close follows its curves from 0.22 s: " + string.Join(", ", seen.Skip(10).Take(14).Select(s => s.Opacity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
            rig.Close(window);
        });
    }

    [Fact] // V-T128: Reduce Motion opens and closes at once
    [Trait("Decision", "ED-292")]
    public void Under_reduce_motion_the_card_opens_and_closes_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, FrameTime);
            rig.Main.OnMain(() =>
            {
                Assert.Equal(1, Question(view).ShownOpacity);
                Assert.Equal(1, Question(view).Progress);
            });
            rig.Main.OnMain(() => Click(Question(view).Answers[0]));
            rig.Elapse(window, FrameTime);
            rig.Main.OnMain(() =>
            {
                Assert.Equal(0, Question(view).ShownOpacity);
                Assert.False(Question(view).IsOpen);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T129: the focus lands on English; Left/Right and Tab wrap; Enter and Space pick
    [Trait("Decision", "ED-292")]
    public void The_focus_is_on_English_at_open_arrows_and_tab_wrap_among_the_four_and_Enter_and_Space_pick()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            Ask(rig, view, window);
            VoiceAnswerButton FocusedNow() => Question(view).Answers.Single(a => a.IsFocused);
            string Name() => rig.Main.OnMain(() => FocusedNow().Text);
            Assert.Equal("English", Name());
            void Press(Key key, KeyModifiers modifiers = KeyModifiers.None) => rig.Main.OnMain(() =>
            {
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
                FocusedNow().RaiseEvent(args);
                Assert.True(args.Handled);
            });
            Press(Key.Right); Assert.Equal("Hindi", Name());
            Press(Key.Right); Assert.Equal("Urdu", Name());
            Press(Key.Tab); Assert.Equal("Auto-detect", Name());
            Press(Key.Right); Assert.Equal("English", Name()); // wraps
            Press(Key.Left); Assert.Equal("Auto-detect", Name()); // and back
            Press(Key.Tab, KeyModifiers.Shift); Assert.Equal("Urdu", Name());
            Press(Key.Left); Assert.Equal("Hindi", Name());
            rig.Main.OnMain(() => Click(FocusedNow())); // Enter and Space on a button click it; the click is what picks
            rig.Main.OnMain(() => Assert.Equal("hi", rig.App.Settings.VoiceTyping.Lang));
            rig.Close(window);
        });
    }

    [Theory] // V-T129: Esc, a click outside, the orb clicked again: closed, nothing stored, nothing started, the focus back on the orb
    [InlineData("escape")]
    [InlineData("outside")]
    [InlineData("orb")]
    [Trait("Decision", "ED-292")]
    public void Esc_an_outside_click_or_the_orb_close_the_card_with_nothing_stored_and_the_next_click_asks_again(string how)
    {
        headless.OnUiThread(() =>
        {
            using var rig = Asking();
            var (view, window) = rig.Open(900);
            Ask(rig, view, window);
            rig.Main.OnMain(() =>
            {
                switch (how)
                {
                    case "escape":
                        ((VoiceAnswerButton)Question(view).Answers[0]).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                        break;
                    case "outside":
                        window.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(window, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true), window, new Point(5, 5),
                            0, new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed), KeyModifiers.None) { RoutedEvent = InputElement.PointerPressedEvent });
                        break;
                    default:
                        Click(Slot(view).HitTarget);
                        break;
                }
            });
            Run(rig, window, 0.6);
            rig.Main.OnMain(() =>
            {
                Assert.False(Question(view).IsOpen);
                Assert.Null(rig.App.Settings.VoiceTyping.Lang); // nothing stored
                Assert.NotEqual(VoiceSessionState.Listening, view.Voice!.Session.State); // nothing started
                Assert.True(Slot(view).HitTarget.IsFocused, "the focus is back on the orb");
            });
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the next click asks again
            Run(rig, window, 0.6);
            rig.Main.OnMain(() => Assert.True(Question(view).IsOpen));
            rig.Close(window);
        });
    }
}
