using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/LinkHover.swift (B3 52bc792), :1-51. New at B3 (N9): the
// continuous-hover cursor and head-mention hover card, shared by InlineText (MarkdownView.swift) and
// ProseRunView.swift. Head-mention cases render a persona through LinkParagraphView, which scans for mentions
// through UI.Markdown.MentionScanner (WINDOWS-ONLY: SwiftCharacter.IsLetter/IsNumber needs icu.dll, unavailable
// on this dev machine); the plain link/cursor cases render no mention and so avoid it.
[Collection("Headless")]
public sealed class LinkHoverTests(HeadlessSession headless)
{
    private static readonly IReadOnlyDictionary<string, HydraMentionTarget> NoTargets = new Dictionary<string, HydraMentionTarget>();

    // Mirrors MarkdownBlockViewBodyTests.Show: a real layout pass needs a hosting window.
    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-THEME-144")] // DroppyCode/UI/Markdown/LinkHover.swift:21-24
    public void Moving_over_a_link_sets_the_hand_cursor() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("[link](https://example.com)");
        var view = (LinkTextView)paragraph.Child!;
        var window = Show(paragraph);
        var state = new LinkHover.HoverState();

        LinkHover.Moved(paragraph, view, new Point(2, 2), [], NoTargets, state);

        Assert.True(state.ShowsHand);
        Assert.NotNull(paragraph.Cursor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-144")] // DroppyCode/UI/Markdown/LinkHover.swift:22-24: only pushes/pops on change
    public void Moving_over_prose_does_not_show_the_hand_cursor() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("plain prose with no link");
        var view = (LinkTextView)paragraph.Child!;
        var window = Show(paragraph);
        var state = new LinkHover.HoverState();

        LinkHover.Moved(paragraph, view, new Point(2, 2), [], NoTargets, state);

        Assert.False(state.ShowsHand);
        Assert.Null(paragraph.Cursor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-144")] // no host text view yet: the hover does nothing (:18 guard)
    public void With_no_host_text_view_yet_moving_does_nothing() => headless.OnUiThread(() =>
    {
        var paragraph = new Decorator();
        var window = Show(paragraph);
        var state = new LinkHover.HoverState();

        LinkHover.Moved(paragraph, null, new Point(2, 2), [], NoTargets, state);

        Assert.False(state.ShowsHand);
        Assert.Null(paragraph.Cursor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-144")] // DroppyCode/UI/Markdown/LinkHover.swift:46-50
    public void Ending_hover_after_showing_the_hand_reverts_the_cursor_and_clears_state() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("[link](https://example.com)");
        var view = (LinkTextView)paragraph.Child!;
        var window = Show(paragraph);
        var state = new LinkHover.HoverState();
        LinkHover.Moved(paragraph, view, new Point(2, 2), [], NoTargets, state);
        Assert.True(state.ShowsHand); // set up: the hand is showing before End runs
        state.HoveredHeadID = Guid.NewGuid();

        LinkHover.End(paragraph, state);

        Assert.False(state.ShowsHand);
        Assert.Null(state.HoveredHeadID);
        Assert.Equal("Arrow", paragraph.Cursor?.ToString()); // :47 NSCursor.pop(): reverts to the default cursor
    });

    [Fact]
    [Trait("Req", "PR-THEME-144")] // ending hover when the hand was never shown leaves the cursor untouched
    public void Ending_hover_without_having_shown_the_hand_leaves_the_cursor_alone() => headless.OnUiThread(() =>
    {
        var paragraph = new Decorator();
        var window = Show(paragraph);
        var state = new LinkHover.HoverState();

        LinkHover.End(paragraph, state);

        Assert.Null(paragraph.Cursor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-144")] // WINDOWS-ONLY: rendering a mention needs MentionScanner, which needs icu.dll
    public void Hovering_a_head_mention_with_a_matching_target_and_persona_tracks_it() => headless.OnUiThread(() =>
    {
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        // The mention must be the first thing rendered: unlike "Ping Hank now" (whose "Ping "
        // prefix occupies real pixel width before the glyph), a leading mention lands at the
        // paragraph's own origin so (2, 2) is guaranteed to fall inside its hit rect regardless
        // of font metrics.
        var paragraph = new LinkParagraphView("Hank now", mentions: [persona], targets: targets);
        var view = (LinkTextView)paragraph.Child!;
        var window = Show(paragraph);
        var state = new LinkHover.HoverState();

        var rect = view.HeadMentionRect(new Point(2, 2)) is { } found ? found.Rect
            : throw new InvalidOperationException("expected a head mention near the start of the rendered text");
        var point = new Point(rect.X + 1, rect.Y + 1);

        LinkHover.Moved(paragraph, view, point, [persona], targets, state);

        Assert.Equal(target.ThreadID, state.HoveredHeadID);
    });

    // :7-11, :14 the modifier is handed the personas and targets of the body that ran last, so a head that joins the thread after the
    // paragraph was built is tracked from then on (InlineText, MarkdownView.swift:1159-1160 at B3, body runs again on a change of
    // hydraMentionPersonas or hydraMentionTargets, :1074-1075). The port attaches the hover once and reads the mentions it is to use
    // when the pointer moves (ED-256 (9), review round 6). The getters hand back what the host holds when they are
    // called, and the head joining puts new objects there rather than changing the old ones, as MarkdownView.SetHydraMentionPersonas
    // and SetHydraMentionTargets do (a new ValueList and ValueMap): a hover that called them once, when it was attached, keeps the
    // empty ones and fails here (scoped check of review round 6, mutant H1).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-144 (the head card of a mention that arrived after the hover was attached): DroppyCode/UI/Markdown/LinkHover.swift:7-14, :26-36; MarkdownView.swift:1074-1075
    public void A_hover_attached_before_a_head_joined_tracks_it_once_it_has() => headless.OnUiThread(() =>
    {
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        // The paragraph is drawn with the head from the start (as in the case above), so the mention is there to be found; what changes
        // is the mentions the hover is handed.
        var paragraph = new LinkParagraphView("Hank now", mentions: [persona], targets: targets);
        var view = (LinkTextView)paragraph.Child!;
        var window = Show(paragraph);
        IReadOnlyList<HydraPersona> personas = [];
        IReadOnlyDictionary<string, HydraMentionTarget> held = new Dictionary<string, HydraMentionTarget>();
        var state = LinkHover.Track(paragraph, () => view, () => personas, () => held);
        var rect = view.HeadMentionRect(new Point(2, 2)) is { } found ? found.Rect
            : throw new InvalidOperationException("expected a head mention near the start of the rendered text");
        var point = paragraph.TranslatePoint(rect.Center, window)!.Value;

        var moves = 0;
        paragraph.PointerMoved += (_, _) => moves++;
        window.MouseMove(new Point(1, 1), Avalonia.Input.RawInputModifiers.None); // the pointer enters the window
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(point, Avalonia.Input.RawInputModifiers.None); // and moves over the mention
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, moves); // set up: the hover saw the pointer enter and move over the mention ...
        Assert.Null(state.HoveredHeadID); // ... and no head is known to it yet

        var emptyPersonas = personas;
        var emptyTargets = held;
        personas = [persona]; // the environment changed under the paragraph: new objects, the old ones untouched; nothing was attached again
        held = new Dictionary<string, HydraMentionTarget> { [persona.Name] = target };
        Assert.Empty(emptyPersonas); // set up: what the hover was attached with is still empty
        Assert.Empty(emptyTargets);
        window.MouseMove(new Point(point.X + 1, point.Y), Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(target.ThreadID, state.HoveredHeadID);
        LinkHover.End(paragraph, state); // hides the card the move scheduled
    });

    [Fact]
    [Trait("Req", "PR-THEME-144")] // WINDOWS-ONLY: same rendering dependency as above
    public void Moving_away_from_a_tracked_head_mention_clears_it_and_hides_the_popover() => headless.OnUiThread(() =>
    {
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        var paragraph = new LinkParagraphView("Ping Hank now and then some plain prose after it", mentions: [persona], targets: targets);
        var view = (LinkTextView)paragraph.Child!;
        var window = Show(paragraph);
        var state = new LinkHover.HoverState { HoveredHeadID = target.ThreadID };

        // Somewhere past the mention, in the plain prose tail.
        var farPoint = new Point(300, 2);
        LinkHover.Moved(paragraph, view, farPoint, [persona], targets, state);

        Assert.Null(state.HoveredHeadID);
    });
}
