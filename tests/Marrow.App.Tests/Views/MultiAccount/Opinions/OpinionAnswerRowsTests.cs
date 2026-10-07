using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Opinions;

// Locked page #opinions, :402-405, :890-898, :4857-4871; OD-0115 opinions=B.
[Collection("Headless")]
public sealed class OpinionAnswerRowsTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Cards_keep_round_order_labels_and_independent_streams_even_when_account_order_changes()
    {
        var f = new OpinionViewRig(); var round = await f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Cross, f.Other]);
        OpinionAnswerRows? view = null;
        MultiAccountHarness.Run(headless, f.Accounts, () => view = Make(f), h =>
        {
            var row = Assert.Single(view!.Rounds);
            Assert.Equal(new[] { "M1 Claude Max 1 Answer", "C1 Codex 1 Opinion", "M2 Claude Max 2 Opinion" }, row.Cards.Select(c => c.Label));
            var first = row.Cards[0]; var cross = row.Cards[1];
            Assert.Equal(first.Label, AutomationProperties.GetName(first));
            f.Port.Stream(round, f.Cross, "Risk"); f.Port.Stream(round, f.Lead, "Mostly"); h.Settle();
            Assert.Equal("Mostly", first.Text); Assert.Equal("Risk", cross.Text); Assert.Equal("", row.Cards[2].Text);
            f.Port.Finish(round, f.Cross); f.Port.Stream(round, f.Other, "Agrees"); h.Settle();
            f.Accounts.Accounts.Reorder(Marrow.Core.Models.ProviderKind.Claude, [f.Other, f.Lead]); f.Current.Refresh(); h.Settle();
            Assert.Same(first, row.Cards[0]); Assert.Same(cross, row.Cards[1]);
            Assert.False(row.Combine.IsVisible); Assert.Empty(f.Port.Combined);
            Assert.True(first.Bounds.Width >= 200); Assert.True(row.Cards[2].Bounds.Y > first.Bounds.Y); // 572px fits two columns, third wraps
        });
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Combine_is_enabled_only_after_all_answers_and_calls_port_once_then_shows_exact_note()
    {
        var f = new OpinionViewRig(); var local = f.Accounts.Connect(Marrow.Core.Models.ProviderKind.Local).Id;
        var round = await f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other, f.Cross, local]);
        OpinionAnswerRows? view = null;
        MultiAccountHarness.Run(headless, f.Accounts, () => view = Make(f), h =>
        {
            var row = Assert.Single(view!.Rounds);
            foreach (var id in new[] { f.Lead, f.Other, f.Cross }) { f.Port.Stream(round, id, "Answer"); f.Port.Finish(round, id); }
            h.Settle(); Assert.False(row.Combine.IsVisible);
            f.Port.Stream(round, local, "Checksum"); f.Port.Finish(round, local); h.Settle();
            Assert.True(row.Combine.IsVisible); Assert.True(row.Combine.IsEnabled);
            Assert.Equal("Combine", AutomationProperties.GetName(row.Combine));
            Assert.Equal(26, row.Combine.Height); Assert.Equal(new Thickness(11, 0), row.Combine.Padding);
            row.Combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            row.Combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            Assert.Equal(round, Assert.Single(f.Port.Combined)); Assert.False(row.Combine.IsEnabled); Assert.Equal(.5, row.Combine.Opacity);
            Assert.Equal("Claude Max 1 · combined 4 answers", row.CombinedByline);
            Assert.Contains("That was for this message only · the thread carries on with Deep work, step 1, Claude Max 1", h.Words());
        });
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Pending_combine_is_guarded_and_a_failed_post_never_claims_completion_or_retries()
    {
        var f = new OpinionViewRig(); var round = await f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other]);
        Finish(f.Port, round, f.Lead, f.Other); var port = new HeldCombine(f.Port); OpinionAnswerRows? view = null;
        MultiAccountHarness.Run(headless, f.Accounts, () => view = Make(f, port), h =>
        {
            var row = Assert.Single(view!.Rounds); row.Combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            row.Combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            Assert.Equal(1, port.Calls); Assert.False(row.Combine.IsEnabled); Assert.DoesNotContain(h.Words(), s => s.StartsWith("That was", StringComparison.Ordinal));
            port.Pending.SetException(new InvalidOperationException("The combined answer could not be posted."));
            h.Until(() => h.Words().Contains("The combined answer could not be posted."));
            row.Combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, port.Calls);
            Assert.DoesNotContain(h.Words(), s => s.StartsWith("That was", StringComparison.Ordinal));
        });
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Timeline_slot_is_empty_until_rounds_arrive_filters_threads_and_restores_after_reattach(bool reduced)
    {
        var f = new OpinionViewRig(); OpinionAnswerRows? view = null; StackPanel body = null!; Border rows = null!; Border sentinel = null!;
        MultiAccountHarness.Run(headless, f.Accounts, () =>
        {
            body = new StackPanel(); rows = new Border(); sentinel = new Border { Height = 1 }; body.Children.Add(rows);
            view = OpinionAnswerRows.Append(body, f.Thread, f.Port, () => Accounts(f), () => new("Claude Max 1", "Deep work", 1), 12);
            body.Children.Add(sentinel); return body;
        }, h =>
        {
            Assert.Equal(new Control[] { rows, view!, sentinel }, body.Children); Assert.Equal(0, view!.Bounds.Height);
            f.Port.AskAsync(Guid.NewGuid(), "Elsewhere", [f.Lead]).GetAwaiter().GetResult(); h.Settle(); Assert.Empty(view.Rounds);
            var round = f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other]).Result; h.Settle(); Assert.True(view.Bounds.Height > 0);
            Assert.Equal(12, view.Margin.Top); Assert.Single(view.Rounds);
            body.Children.Remove(view); f.Port.Stream(round, f.Other, "While detached"); body.Children.Insert(1, view); h.Settle();
            Assert.Equal("While detached", Assert.Single(view.Rounds).Cards[1].Text);
            var second = f.Port.AskAsync(f.Thread, "Again", [f.Lead, f.Cross]).Result; h.Settle();
            Assert.Equal(new[] { round, second }, view.Rounds.Select(r => r.RoundId));
        }, reduceMotion: reduced);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Reduce_motion_arrivals_crossfade_without_translation_scale_or_stream_restarts()
    {
        var f = new OpinionViewRig(); OpinionAnswerRows? view = null;
        MultiAccountHarness.Run(headless, f.Accounts, () => view = Make(f), h =>
        {
            var round = f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other]).Result;
            h.Step(0); var row = Assert.Single(view!.Rounds);
            Assert.Equal(0, row.Opacity); h.Step(100); Assert.InRange(row.Opacity, .01, .99);
            Assert.All(row.GetVisualDescendants().Prepend(row), c => Assert.Null(c.RenderTransform));
            f.Port.Stream(round, f.Other, "Now"); h.Step(50); Assert.Equal("Now", row.Cards[1].Text);
            h.Step(110); Assert.Equal(1, row.Opacity); // enter(...), RM default fadeIn 260ms
            Finish(f.Port, round, f.Lead, f.Other); h.Step(200);
            row.Combine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Step(100);
            Assert.Contains("That was for this message only · the thread carries on with Deep work, step 1, Claude Max 1", h.Words());
            Assert.All(row.GetVisualDescendants().Prepend(row), c => Assert.Null(c.RenderTransform));
        }, motion: true, reduceMotion: true);
    }

    private static OpinionAnswerRows Make(OpinionViewRig f, IOpinionsPort? port = null) => new(f.Thread, port ?? f.Port, () => Accounts(f), () => new("Claude Max 1", "Deep work", 1), 0);
    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Complete_backtick_spans_stream_as_monospace_and_incomplete_spans_keep_their_text()
    {
        var f = new OpinionViewRig(); var round = await f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other]);
        MultiAccountHarness.Run(headless, f.Accounts, () => Make(f), h =>
        {
            f.Port.Stream(round, f.Other, "Use `send()"); h.Settle(); Assert.Contains("Use `send()", h.Words());
            f.Port.Stream(round, f.Other, "` safely"); h.Settle(); Assert.Contains("Use send() safely", h.Words());
            var mono = h.Page.GetVisualDescendants().OfType<TextBlock>().SelectMany(t => t.Inlines?.OfType<Run>() ?? []).Single(r => r.Text == "send()");
            Assert.Equal(Marrow.UI.Port.SwiftFont.MonospacedSystemFont(12, Marrow.UI.Port.SwiftFont.Weight.Regular).Resolve().FontFamily, mono.FontFamily);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Enabling_reduce_motion_while_Combine_is_held_clears_its_settled_scale()
    {
        var f = new OpinionViewRig(); var round = await f.Port.AskAsync(f.Thread, "Review", [f.Lead, f.Other]); Finish(f.Port, round, f.Lead, f.Other);
        MultiAccountHarness.Run(headless, f.Accounts, () => Make(f), h =>
        {
            var row = Assert.Single(((OpinionAnswerRows)h.Page).Rounds); row.Combine.Focus(NavigationMethod.Tab);
            row.Combine.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); h.Step(220);
            Assert.True(row.Combine.IsPressed); Assert.Contains(row.Combine.GetVisualDescendants(), c => c.RenderTransform is not null);
            Marrow.UI.Port.EnvironmentValues.SetAccessibilityReduceMotion(h.Window, true);
            Assert.All(row.Combine.GetVisualDescendants(), c => Assert.Null(c.RenderTransform));
        }, motion: true);
    }
    private static IReadOnlyList<ThreadAccount> Accounts(OpinionViewRig f) => f.Current.Accounts.Concat(f.Codex.Accounts).Concat(new FakeThreadAccountPort(f.Accounts, Marrow.Core.Models.ProviderKind.Local).Accounts).ToArray();
    private static void Finish(FakeOpinionsPort port, Guid round, params Guid[] ids) { foreach (var id in ids) { port.Stream(round, id, "Done"); port.Finish(round, id); } }
    private sealed class HeldCombine(FakeOpinionsPort inner) : IOpinionsPort
    {
        public TaskCompletionSource Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public event Action<Guid>? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default) => inner.AskAsync(thread, message, accounts, ct);
        public IReadOnlyList<OpinionRound> Rounds(Guid thread) => inner.Rounds(thread);
        public Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default) { Calls++; return Pending.Task; }
    }
}
