using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing.MultiAccountSettings;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Views.MultiAccount.Opinions;

[Collection("Headless")]
public sealed class OpinionTimelinePlacementTests(HeadlessSession headless)
{
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Actual_timeline_keeps_opinions_after_their_prompt_and_combined_byline_and_note_before_later_messages(bool reduced)
    {
            var f = new OpinionViewRig(); var port = new FakeOpinionsPort();
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduced });
            using var ports = ThreadPortSeams.Offer.Override(new((_, service) => service == ProviderKind.Claude ? f.Current : service == ProviderKind.Codex ? f.Codex : null, null, null, port));
            using var placement = OpinionTimelineSeams.Offer.Override(_ => new(_ => "prompt", _ => "combined", (_, _) => null));
            ThreadTimelineRig.Run(headless, h =>
            {
                var prompt = TimelineMount.User("prompt", text: "Review"); h.SetEntries([prompt]); h.Settle();
                EnvironmentValues.SetAccessibilityReduceMotion(h.Window, reduced);
                var round = port.AskAsync(h.Runtime.ThreadID, "Review", [f.Lead, f.Other]).Result;
                var arrived = Assert.Single(h.View.GetVisualDescendants().OfType<OpinionRoundView>());
                Assert.Equal(0, arrived.Opacity);
                // The timeline fixture delivers the frame before advancing its clock. Move the
                // monotonic clock first, as a real frame observes the time elapsed since arrival.
                h.Store.Main.Time.Advance(TimeSpan.FromMilliseconds(50)); h.Advance(50);
                Assert.InRange(arrived.Opacity, .01, .99);
                if (reduced) Assert.Null(arrived.RenderTransform); else Assert.NotNull(arrived.RenderTransform);
                foreach (var id in new[] { f.Lead, f.Other }) { port.Stream(round, id, "Answer"); port.Finish(round, id); }
                h.Settle();
                var view = Assert.Single(h.View.GetVisualDescendants().OfType<OpinionRoundView>());
                var combine = view.Combine;
                combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(port.Combined);
                // E1/W owns posting the actual combined text; the view consumes its explicitly associated entry ID.
                h.SetEntries([prompt, TimelineMount.Reply("combined", text: "Combined response"), TimelineMount.User("later", text: "Next message")]); h.Settle();
                var byline = h.View.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Claude Max 1 · combined 2 answers");
                var note = h.View.GetVisualDescendants().OfType<Control>().Single(c => AutomationProperties.GetAutomationId(c) == "opinion-closing-" + round.ToString("N"));
                var users = h.View.GetVisualDescendants().OfType<UserMessageRow>().ToArray();
                Assert.True(Y(combine) < Y(byline)); Assert.True(Y(byline) < Y(note)); Assert.True(Y(note) < Y(users[1]));
                Assert.Contains(view, users[0].GetVisualDescendants()); Assert.DoesNotContain(view, users[1].GetVisualDescendants());
                // A subsequent ordinary redraw preserves the same cards and their position.
                h.SetEntries([prompt, TimelineMount.Reply("combined", text: "Combined response"), TimelineMount.User("later", text: "Next message"), TimelineMount.Reply("later-reply")]); h.Settle();
                Assert.Same(view, Assert.Single(h.View.GetVisualDescendants().OfType<OpinionRoundView>()));
                double Y(Control c) => c.TranslatePoint(default, h.View)!.Value.Y;
            });
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Engine_error_projection_displays_failed_partial_head_and_keeps_Combine_unavailable()
    {
        var f = new OpinionViewRig(); var round = await f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other]);
        foreach (var id in new[] { f.Lead, f.Other }) { f.Port.Stream(round, id, "Partial"); f.Port.Finish(round, id); }
        MultiAccountHarness.Run(headless, f.Accounts,
            () => new OpinionRoundView(f.Port.Rounds(f.Thread).Single(), f.Port, f.Current.Accounts, () => new("Claude Max 1"),
                id => id == f.Other ? "Opinion cancelled." : null), h =>
            {
                Assert.Contains("Opinion cancelled.", h.Words());
                var view = (OpinionRoundView)h.Page; Assert.False(view.Combine.IsVisible); Assert.False(view.Combine.IsEnabled); Assert.Empty(f.Port.Combined);
            });
    }
}
