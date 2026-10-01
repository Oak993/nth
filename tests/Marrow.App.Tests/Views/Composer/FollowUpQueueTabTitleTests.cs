using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.FollowUpQueueTabRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// The queue tab itself: its title line, the fold, the shape of the tab, the focus rules.
// Source: DroppyCode/App/Views/Composer/FollowUpQueueTab.swift (B3 52bc7926).
[Collection("Headless")]
public sealed class FollowUpQueueTabTitleTests(HeadlessSession headless)
{
    // The main loop runs posted observation work (Tracked, .onChange) only when a store.Main.OnMain body returns, so a
    // test is a series of steps: each step's writes have been observed by the views when the next step starts.
    private void WithTab(FollowUpPrompt[] prompts, Action<FollowUpQueueTab, AvaloniaWindow, ThreadRuntime, Action<Action>> body, double width = 600)
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = Make();
            using var storeDisposable = store;
            void Step(Action action) => store.Main.OnMain(action);
            FollowUpQueueTab? tab = null;
            AvaloniaWindow? window = null;
            Step(() =>
            {
                Seed(runtime, prompts);
                tab = new FollowUpQueueTab(runtime);
                window = Show(tab, width);
            });
            body(tab!, window!, runtime, Step);
            Step(() => window!.Close());
            _ = app;
        });
    }

    private static FollowUpPrompt[] Prompts(int count) =>
        Enumerable.Range(1, count).Select(i => Prompt("prompt " + i)).ToArray();

    private static (SwiftImage Icon, TextBlock Count, TextBlock Queued, SwiftImage Chevron) TitleParts(FollowUpQueueTab tab)
    {
        var title = TitleButton(tab);
        var images = title.GetVisualDescendants().OfType<SwiftImage>().ToList();
        var texts = title.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Equal(2, images.Count);
        Assert.Equal(2, texts.Count);
        return (images[0], texts[0], texts[1], images[1]);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-151")] // FollowUpQueueTab.swift:55-70,81
    [Trait("Req", "UI-COMPOSER-048")]
    public void The_title_reads_the_count_then_queued_and_has_the_other_phrasing_for_assistive_technology()
    {
        WithTab(Prompts(1), (tab, window, runtime, step) =>
        {
            var (icon, count, queued, chevron) = TitleParts(tab);
            var title = TitleButton(tab);

            Assert.Equal("1 follow-up", count.Text);
            Assert.Equal("queued", queued.Text);
            Assert.Equal("1 queued follow-up", AutomationProperties.GetName(title));

            Assert.Equal("arrow.turn.down.right", icon.SymbolName);
            Assert.Equal("chevron.down", chevron.SymbolName);

            // The count phrase in the primary colour at 0.9, "queued" and both symbols in the secondary colour.
            Assert.Equal(Effective(Chrome.PrimaryText.Opacity(0.9).Brush(tab)), Effective(count.Foreground));
            Assert.Equal(Effective(Chrome.SecondaryText.Brush(tab)), Effective(queued.Foreground));
            Assert.Equal(Effective(Chrome.SecondaryText.Brush(tab)), Effective(icon.Foreground));
            Assert.Equal(Effective(Chrome.SecondaryText.Brush(tab)), Effective(chevron.Foreground));
            Assert.NotEqual(Effective(count.Foreground), Effective(queued.Foreground));

            // .callout at medium (12 point) with monospaced digits; the symbols take Chrome.inlineIconFont.
            foreach (var text in new[] { count, queued })
            {
                Assert.Equal(12, text.FontSize);
                Assert.Equal(FontWeight.Medium, text.FontWeight);
                Assert.Contains(text.FontFeatures!, feature => feature.Tag == "tnum" && feature.Value == 1);
            }
            Assert.Equal(Chrome.InlineIconFont, SwiftImage.GetFont(icon));
            Assert.Equal(Chrome.InlineIconFont, SwiftImage.GetFont(chevron));

            // :55 HStack(spacing: 7), :67 the chevron in a 22 by 22 frame.
            Assert.Equal(7, count.Bounds.X - icon.Bounds.Right);
            Assert.Equal(7, queued.Bounds.X - count.Bounds.Right);
            Assert.Equal(22, chevron.Bounds.Width);
            Assert.Equal(22, chevron.Bounds.Height);

            // The count follows the queue.
            step(() => Seed(runtime, Prompts(3)));
            step(() => Settle(window));
            Assert.Equal("3 follow-ups", count.Text);
            Assert.Equal("3 queued follow-ups", AutomationProperties.GetName(title));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-152")] // FollowUpQueueTab.swift:52-53,68,80,82
    [Trait("Req", "UI-COMPOSER-048")]
    public void Clicking_anywhere_on_the_title_line_folds_the_queue_under_panelSlide_and_flips_the_chevron()
    {
        WithTab(Prompts(2), (tab, window, runtime, step) =>
        {
            var (_, _, queued, chevron) = TitleParts(tab);
            var title = TitleButton(tab);
            var seen = new List<(bool Collapsed, Animation? Animation)>();
            Tracked<bool>? probe = null;
            step(() => probe = new Tracked<bool>(() => runtime.FollowUpsCollapsed, value => seen.Add((value, Transaction.Current))));

            Assert.False(runtime.FollowUpsCollapsed);
            Assert.Equal("Collapse queued follow-ups", ToolTip.GetTip(title));
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(title)); // :82 .accessibilityValue
            Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);

            // On the word "queued".
            var onText = Center(queued, window);
            step(() =>
            {
                window.MouseDown(onText, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(onText, MouseButton.Left, RawInputModifiers.None);
            });
            Assert.True(runtime.FollowUpsCollapsed);
            Assert.Equal(Chrome.PanelSlide, seen.Last().Animation);
            Assert.Equal("Expand queued follow-ups", ToolTip.GetTip(title));
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(title));
            Assert.Equal(180, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);

            // In the empty gap between "queued" and the chevron (.contentShape(.rect) makes the whole line the button).
            var queuedRight = queued.TranslatePoint(new Point(queued.Bounds.Width, 0), window)!.Value.X;
            var chevronLeft = chevron.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var gap = new Point((queuedRight + chevronLeft) / 2, onText.Y);
            Assert.True(chevronLeft - queuedRight > 20, "there is an empty stretch to click");
            step(() =>
            {
                window.MouseDown(gap, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(gap, MouseButton.Left, RawInputModifiers.None);
            });
            Assert.False(runtime.FollowUpsCollapsed);
            Assert.Equal("Collapse queued follow-ups", ToolTip.GetTip(title));
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(title));
            Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
            step(() => probe!.Dispose());
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-152")] // FollowUpQueueTab.swift:29-31 the fold is the thread's, so it survives the queue emptying
    public void The_fold_belongs_to_the_thread_so_a_queue_that_empties_and_refills_comes_back_folded()
    {
        WithTab(Prompts(2), (tab, window, runtime, step) =>
        {
            step(() =>
            {
                var point = Center(TitleButton(tab), window);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            });
            Assert.True(runtime.FollowUpsCollapsed);

            step(() => Seed(runtime));
            step(() => Seed(runtime, Prompts(3)));
            step(() => Settle(window));

            Assert.True(runtime.FollowUpsCollapsed);
            var list = tab.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.Equal(0, list.Opacity);
            Assert.Equal(0, list.Height);

            // A tab built fresh for the same thread starts folded as well.
            FollowUpQueueTab? again = null;
            step(() =>
            {
                again = new FollowUpQueueTab(runtime);
                window.Content = again;
                Settle(window);
            });
            step(() => Settle(window));
            Assert.Equal(0, again!.GetVisualDescendants().OfType<ScrollViewer>().First().Opacity);
            Assert.Equal("Expand queued follow-ups", ToolTip.GetTip(TitleButton(again!)));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-153")] // FollowUpQueueTab.swift:74-79,416-418,397-399
    public void Nothing_in_the_tab_takes_keyboard_focus_and_row_text_cannot_be_selected()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = Make();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var bundle = Guid.NewGuid();
                Seed(runtime, Prompt("first", bundle, Image("a.png")), Prompt("second", bundle), Prompt("third", null, File("b.txt")));
                var tab = new FollowUpQueueTab(runtime);
                // A control outside the tab that does take focus: tabbing must reach it, never the tab.
                var outside = new Button { Content = "outside", Focusable = true };
                var panel = new StackPanel { Children = { tab, outside } };
                var window = Show(panel);

                var inTab = tab.GetVisualDescendants().OfType<Control>().ToList();
                Assert.NotEmpty(inTab.OfType<Button>());
                Assert.All(inTab, control => Assert.False(control.Focusable && control.IsTabStop,
                    control.GetType().Name + " would take focus in the tab loop"));
                Assert.Empty(inTab.OfType<SelectableTextBlock>());
                Assert.Empty(inTab.OfType<TextBox>());

                window.Activate();
                var reached = new List<object?>();
                for (var press = 0; press < 12; press++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, keySymbol: null);
                    window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, keySymbol: null);
                    Settle(window);
                    reached.Add(window.FocusManager!.GetFocusedElement());
                }
                Assert.Contains(outside, reached);
                Assert.All(reached, element => Assert.False(element is Visual visual && tab.IsVisualAncestorOf(visual),
                    "focus entered the tab"));
                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-155")] // FollowUpQueueTab.swift:84-90,136-148
    [Trait("Req", "UI-COMPOSER-047")]
    public void Folding_keeps_the_rows_in_place_while_the_list_clips_to_zero_fades_and_leaves_hit_testing_and_accessibility()
    {
        WithTab(Prompts(3), (tab, window, runtime, step) =>
        {
            var list = tab.GetVisualDescendants().OfType<ScrollViewer>().First();

            // Open: the list is as tall as its rows, takes clicks, is seen; the mask runs 32 past the sides and 16 past top and bottom.
            Assert.True(list.Height > 60, "the open list is as tall as its rows, not zero (" + list.Height + ")");
            Assert.Equal(1, list.Opacity);
            Assert.True(list.IsHitTestVisible);
            Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(list));
            var open = Assert.IsType<RectangleGeometry>(list.Clip).Rect;
            Assert.Equal(-32, open.X);
            Assert.Equal(-16, open.Y);
            Assert.Equal(list.Bounds.Width + 64, open.Width);
            Assert.Equal(list.Height + 32, open.Height);
            var rowsBefore = Rows(tab);
            Assert.Equal(3, rowsBefore.Count);

            step(() => runtime.FollowUpsCollapsed = true);
            step(() => Settle(window));

            // Folded: nothing was removed (no ghost mid-animation), the height is zero, the list is clear, deaf and hidden from screen readers.
            Assert.Equal(0, list.Height);
            Assert.Equal(0, list.Opacity);
            Assert.False(list.IsHitTestVisible);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(list));
            Assert.Equal(rowsBefore, Rows(tab));
            var closed = Assert.IsType<RectangleGeometry>(list.Clip).Rect;
            Assert.Equal(-32, closed.X);
            Assert.Equal(0, closed.Y); // the extra vertical room closes with the fold
            Assert.Equal(0, closed.Height);

            step(() => runtime.FollowUpsCollapsed = false);
            step(() => Settle(window));
            Assert.True(list.Height > 60);
            Assert.Equal(1, list.Opacity);
            Assert.True(list.IsHitTestVisible);
            Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(list));
            Assert.Equal(-16, Assert.IsType<RectangleGeometry>(list.Clip).Rect.Y);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-047")] // FollowUpQueueTab.swift:13-25,87,132-136: past seven rows the list scrolls inside the tab
    public void Past_seven_rows_the_list_is_cut_on_a_whole_row_and_scrolls()
    {
        WithTab(Prompts(9), (tab, window, runtime, step) =>
        {
            var list = tab.GetVisualDescendants().OfType<ScrollViewer>().First();
            var rows = Rows(tab);
            Assert.Equal(9, rows.Count);
            var rowHeight = rows.Average(row => row.Bounds.Height);
            Assert.Equal(32, rowHeight);

            // The cut is the top of the content (padding 6, rule 1, gap 4) plus seven whole rows.
            Assert.Equal(6 + 1 + 4 + 9 * rowHeight, list.Extent.Height, 0.5);
            Assert.Equal(6 + 1 + 4 + 7 * rowHeight, list.Height, 0.5);
            Assert.True(list.Extent.Height > list.Viewport.Height + 1, "the list scrolls inside the tab");

            // Seven rows and fewer: the whole list.
            step(() => Seed(runtime, Prompts(7)));
            step(() => Settle(window));
            Assert.Equal(6 + 1 + 4 + 7 * rowHeight, list.Extent.Height, 0.5);
            Assert.Equal(list.Extent.Height, list.Height, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-156")] // FollowUpQueueTab.swift:47,150-155
    [Trait("Req", "UI-COMPOSER-047")]
    public void The_tab_is_a_glass_tab_at_most_560_wide_rounded_at_the_top_and_padded_12_7_12_21()
    {
        WithTab([Prompt("short")], (tab, window, runtime, step) =>
        {
            var shape = tab.GetVisualDescendants().OfType<Border>()
                .First(border => SwiftGlass.GetEffect(border) is not null);

            Assert.Equal(new Thickness(12, 7, 12, 21), shape.Padding);
            Assert.Equal(560, shape.MaxWidth);
            Assert.Equal(new CornerRadius(12, 12, 0, 0), shape.CornerRadius);

            var effect = SwiftGlass.GetEffect(shape)!;
            Assert.Equal(GlassShapeKind.RoundedRectangle, effect.Shape.Kind);
            Assert.Equal(12, effect.Shape.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style);
            Assert.False(effect.IsInteractive); // .glassEffect(.regular, ...) without .interactive()
            Assert.Null(effect.TintColor);

            // The row text is .frame(maxWidth: .infinity), so the tab takes the width it is offered, up to 560.
            Assert.Equal(560, shape.Bounds.Width);
            step(() => window.Width = 400);
            step(() => Settle(window));
            Assert.Equal(400, shape.Bounds.Width);
        }, width: 1000);
    }
}
