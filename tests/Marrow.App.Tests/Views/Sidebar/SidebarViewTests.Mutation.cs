using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792), :69-205, :117-119, :2316-2427.
// Supplement the inherited assertions with real hit-tested pointer input and independent literal expectations.
public sealed partial class SidebarViewTests
{
    private static void PointerClick(Button button)
    {
        var window = WindowOf(button);
        window.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-004")] // App/Views/Sidebar/SidebarView.swift:82,2322-2341; UI/Chrome/Chrome.swift:28,98
    public void Bell_hits_only_its_circle_and_uses_the_source_font_accessibility_and_hover_curve() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var model = harness.MakeModel();
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        var button = ButtonNamed(view, "Activity view");
        var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(button).Single();
        var circle = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(button).Single();
        harness.Main.OnMain(() =>
        {
            Assert.Equal(new Size(30, 30), button.Bounds.Size); // :2328
            Assert.Equal(new Thickness(6, 0, 0, 0), button.GetLogicalAncestors().OfType<Control>().First(c => c.GetType() == Private("ActivityViewToggle")).Margin); // :82
            Assert.Equal(12, SwiftImage.GetFont(glyph)!.Resolve().FontSize); // Chrome.swift:28 callout semibold, ED-059
            Assert.Equal(FontWeight.SemiBold, SwiftImage.GetFont(glyph)!.Resolve().FontWeight);
            Assert.Equal("Off", AutomationProperties.GetItemStatus(button)); // :2341
            var corner = button.TranslatePoint(new Point(1, 1), window)!.Value;
            window.MouseDown(corner, MouseButton.Left); window.MouseUp(corner, MouseButton.Left);
            Assert.False(model.Settings.SidebarActivityView); // :2333 contentShape(Circle())
        });
        harness.Main.OnMain(() => window.MouseMove(button.TranslatePoint(new Point(15, 15), window)!.Value));
        harness.Main.AdvanceMilliseconds(50);
        harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(50)));
        harness.Main.OnMain(() =>
        {
            // :2336-2338 + Chrome.swift:98 easeOut(0.1); at halfway the alpha is neither endpoint.
            var actual = ((ISolidColorBrush)circle.Fill!).Color;
            var end = Chrome.Overlay(0.08).Resolve(circle.ActualThemeVariant);
            Assert.Equal((byte)Math.Round(end.A * Animation.EaseOut(0.1).Progress(0.05)), actual.A);
        });
        Animation? during = null;
        using var probe = new OnChange<bool>(() => model.Settings.SidebarActivityView, (_, _) => during = Transaction.Current);
        harness.Main.OnMain(() => PointerClick(button));
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.SidebarActivityView);
            Assert.Equal(Chrome.PanelSlide, during); // :2323
            Assert.Equal("On", AutomationProperties.GetItemStatus(button));
            Close(view);
        });
    });

    // PR-SIDEBAR-019: the ED-218 foundation has landed; this caller proof must go red before wiring it.
    // The record's pinned capture comparison is still STOP #10, so this test has no Req trait.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_expansion_moves_a_surviving_project_from_its_old_position_on_panelSlide(bool reduceMotion) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        var first = NewProject(harness, "First");
        var next = NewProject(harness, "Next");
        var thread = NewThread(first, "Open", 0);
        var model = Model(harness, [first, next], [thread]);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        try
        {
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Tick(1600);
        var row = RowNamed(view, "Next");
        var container = ListPanel(view).Children.Single(c => c.GetLogicalDescendants().Contains(row));
        var oldY = container.Bounds.Y;
        harness.Main.OnMain(() => PointerClick(ButtonNamed(view, "First")));
        Tick(0);
        var displacement = oldY - container.Bounds.Y;
        Assert.NotEqual(0, displacement); // :117-119 space closes when First collapses
        Assert.Equal(displacement, container.RenderTransform!.Value.M32, 5); // starts at its old visible position
        Tick(90);
        var curve = reduceMotion ? Animation.EaseOut(0.18) : Animation.Spring(0.32, 0.9); // Chrome.swift:100-104 panelSlide
        Assert.Equal(displacement * (1 - curve.Progress(0.09)), container.RenderTransform!.Value.M32, 5);
        Tick(1600);
        Assert.Equal(0, container.RenderTransform?.Value.M32 ?? 0, 5);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift:2349-2357
    public void Activity_headers_keep_their_verbatim_text_font_colour_leading_alignment_and_non_button_role() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        foreach (var title in new[] { "Needs attention", "Pinned", "Today", "Yesterday", "Wednesday", "September 2", "September 2, 2025" })
        {
            var header = (Control)Activator.CreateInstance(Private("ActivityHeader"), title, true)!;
            Shown(header);
            var text = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(header));
            Assert.Equal(title, text.Text); // :2350 Text(verbatim: title), caller supplies literal or localized day string
            Assert.Equal(12, text.FontSize); Assert.Equal(FontWeight.SemiBold, text.FontWeight); // :2351 .callout semibold
            Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), ((ISolidColorBrush)text.Foreground!).Color); // :2352
            Assert.Equal(HorizontalAlignment.Stretch, text.HorizontalAlignment); // :2356 full width
            Assert.Equal(TextAlignment.Start, text.TextAlignment); // :2356 leading follows the writing direction
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(header));
            WindowOf(header).Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:693-703
    public void No_results_is_mounted_only_when_the_search_has_no_groups() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Found", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        harness.Main.OnMain(() => Assert.DoesNotContain("No results", Texts(view)));
        harness.Main.OnMain(() => SearchBox(view).Text = "missing");
        harness.Main.OnMain(() => Assert.Contains("No results", Texts(view)));
        harness.Main.OnMain(() => SearchBox(view).Text = "Found");
        harness.Main.AdvanceMilliseconds(1600);
        harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(1600)); WindowOf(view).UpdateLayout(); });
        harness.Main.OnMain(() => { Assert.DoesNotContain("No results", Texts(view)); Close(view); });
    });
}
