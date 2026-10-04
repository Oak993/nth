using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// SidebarView.swift:1466-1509 (glyph identity: ForEach(rows, id: \.first?.id) { ForEach(row, id: \.id) }), :1535 `.animation(.snappy(duration: 0.18),
// value: cardRows)`, :1541 `.animation(Chrome.panelSlide, value: heads.map(\.id))`. A glyph (or a whole row) that survives a change of the
// head set keeps its identity, so SwiftUI moves it from its old place to its new one on the animation in force; OD-0076 asks the same of the
// port. A glyph that moves to another row has another identity (its row's id is part of it) and leaves and joins instead (UI-SIDEBAR-038's fade).
[Collection("Headless")]
public sealed class HeadCardGlideTests(HeadlessSession headless)
{
    // The place a glyph's button is drawn at inside the card: its layout slot plus the shift it is riding, if any.
    private static (double X, double Y) Drawn(Control card, Control button)
    {
        var (x, y) = Settled(card, button);
        var shift = button.RenderTransform as TranslateTransform;
        return (x + (shift?.X ?? 0), y + (shift?.Y ?? 0));
    }

    // The place its layout puts it: the sum of the slots of the button and every ancestor up to the card.
    private static (double X, double Y) Settled(Control card, Control button)
    {
        double x = 0, y = 0;
        for (Visual? visual = button; visual is not null && !ReferenceEquals(visual, card); visual = visual.GetVisualParent())
        {
            x += visual.Bounds.X;
            y += visual.Bounds.Y;
        }
        return (x, y);
    }

    private static Dictionary<Guid, Button> Buttons(Control card, IEnumerable<ChatThread> heads)
    {
        var all = LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        return all.Zip(heads).ToDictionary(pair => pair.Second.Id, pair => pair.First);
    }

    private static (Control Card, AvaloniaWindow Window) Mount(AppModelHarness harness, AppModel model, ChatThread parent, List<ChatThread> heads)
    {
        Control card = null!; AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        return (card, window);
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // :1541 .animation(Chrome.panelSlide, value: heads.map(\.id)): the glyphs after a removed head slide into its place
    public void When_a_head_is_removed_from_the_middle_of_a_row_the_glyphs_after_it_glide_into_place_on_panelSlide_with_no_jump() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 4).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        var (card, window) = Mount(harness, model, parent, heads);
        try
        {
            var shown = SidebarView.OrderedHeads(heads).ToList(); // the order the card draws them in (:465)
            var before = new Dictionary<Guid, (double X, double Y)>();
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                foreach (var (id, button) in Buttons(card, shown)) before[id] = Drawn(card, button);
            });
            var kept = new List<ChatThread> { shown[0], shown[2], shown[3] };
            harness.Main.OnMain(() => CallOn(card, "Update", kept)); // no transaction: the head set changed, so panelSlide is the animation in force (:1541)
            harness.Main.OnMain(() => window.UpdateLayout());
            var settled = new Dictionary<Guid, (double X, double Y)>();
            harness.Main.OnMain(() =>
            {
                var now = Buttons(card, kept);
                foreach (var (id, button) in now) settled[id] = Settled(card, button);
                foreach (var head in kept) Assert.Equal(before[head.Id].X, Drawn(card, now[head.Id]).X, 0.5); // frame 0: still where they stood (the slots are exact, layout rounds to whole pixels)
                Assert.Equal(settled[shown[0].Id], before[shown[0].Id]); // the first glyph does not move
                Assert.True(settled[shown[2].Id].X < before[shown[2].Id].X); // the ones behind the gap move left
                Assert.True(settled[shown[3].Id].X < before[shown[3].Id].X);
            });
            var elapsed = 0d;
            var last = new Dictionary<Guid, double>();
            var distance = before[shown[2].Id].X - settled[shown[2].Id].X;
            foreach (var step in Enumerable.Repeat(0.016, 10))
            {
                harness.Main.AdvanceMilliseconds(16);
                harness.Main.OnMain(() => manual.Advance(TimeSpan.FromSeconds(step)));
                elapsed += step;
                harness.Main.OnMain(() =>
                {
                    var now = Buttons(card, kept);
                    var travel = Chrome.PanelSlide.Progress(elapsed);
                    foreach (var head in kept)
                    {
                        var at = Drawn(card, now[head.Id]);
                        Assert.Equal(before[head.Id].X + (settled[head.Id].X - before[head.Id].X) * travel, at.X, 0.5);
                        Assert.Equal(before[head.Id].Y, at.Y, 0.01); // within the row: no vertical drift
                        if (last.TryGetValue(head.Id, out var was)) Assert.True(Math.Abs(at.X - was) < 0.6 * distance, "no single-frame jump");
                        last[head.Id] = at.X;
                    }
                    if (elapsed < 0.1) Assert.NotEqual(settled[shown[2].Id].X, Drawn(card, now[shown[2].Id]).X, 0.2); // under way, not there
                });
            }
            harness.Main.AdvanceMilliseconds(1500);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromSeconds(1.5)));
            harness.Main.OnMain(() =>
            {
                var now = Buttons(card, kept);
                foreach (var head in kept) Assert.Equal(settled[head.Id].X, Drawn(card, now[head.Id]).X, 0.01); // arrived
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // :1466 ForEach(rows, id: \.first?.id): a row that keeps its first head keeps its identity and moves up on :1535 snappy when the row above it goes
    public void A_surviving_row_of_glyphs_glides_up_on_the_snappy_curve_when_the_row_above_it_is_removed() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var perRow = (int)HeadCardsRow.PerRow(200);
        var heads = Enumerable.Range(0, perRow + 3).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        var (card, window) = Mount(harness, model, parent, heads);
        try
        {
            var shown = SidebarView.OrderedHeads(heads).ToList(); // the order the card draws them in (:465)
            var second = shown.Skip(perRow).ToList(); // the second row's three heads; the first row's go
            var before = new Dictionary<Guid, (double X, double Y)>();
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                foreach (var (id, button) in Buttons(card, shown)) before[id] = Drawn(card, button);
            });
            harness.Main.OnMain(() => CallOn(card, "Update", second));
            harness.Main.OnMain(() => window.UpdateLayout());
            var snappy = Animation.Snappy(duration: 0.18);
            harness.Main.OnMain(() =>
            {
                var now = Buttons(card, second);
                foreach (var head in second)
                {
                    Assert.Equal(before[head.Id].Y, Drawn(card, now[head.Id]).Y, 0.01); // frame 0: where they stood, one row down
                    Assert.True(Settled(card, now[head.Id]).Y < before[head.Id].Y);
                }
            });
            var elapsed = 0d;
            foreach (var step in Enumerable.Repeat(0.016, 6))
            {
                harness.Main.AdvanceMilliseconds(16);
                harness.Main.OnMain(() => manual.Advance(TimeSpan.FromSeconds(step)));
                elapsed += step;
                harness.Main.OnMain(() =>
                {
                    var now = Buttons(card, second);
                    foreach (var head in second)
                    {
                        var settledY = Settled(card, now[head.Id]).Y;
                        var at = Drawn(card, now[head.Id]);
                        Assert.Equal(before[head.Id].Y + (settledY - before[head.Id].Y) * snappy.Progress(elapsed), at.Y, 0.5);
                        Assert.Equal(before[head.Id].X, at.X, 0.01); // straight up: it keeps its column
                    }
                });
            }
            harness.Main.AdvanceMilliseconds(1000);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromSeconds(1)));
            harness.Main.OnMain(() =>
            {
                var now = Buttons(card, second);
                foreach (var head in second) Assert.Equal(Settled(card, now[head.Id]).Y, Drawn(card, now[head.Id]).Y, 0.01); // arrived
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });
}
