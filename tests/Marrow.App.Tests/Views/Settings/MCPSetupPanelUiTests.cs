using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSetupPanel.swift (B3 52bc7926), :54-262.
[Collection("Headless")]
public sealed class MCPSetupPanelUiTests(HeadlessSession headless)
{
    private static MCPConnectionState NeedsSetup(MCPSetupGuide? guide = null) =>
        new MCPConnectionState.NeedsSetup(guide ?? MCPSetupPanelRig.Guide());

    private void Run(MCPConnectionState state, Action<MCPSetupPanelSession> body, MCPSetupGuide? guide = null, string entryName = "GitLab") =>
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var source = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(source);
            var model = harness.MakeModel();
            using var session = new MCPSetupPanelSession(harness.Main, source, model, state, guide, entryName);
            body(session);
        });

    // The node's layout rectangle in the root's space: bounds added up the parents, so a render transform (the
    // success glyph's scale) never moves it.
    private static Rect Frame(Visual node, Visual root)
    {
        var origin = default(Point);
        for (var current = node; current is not null && !ReferenceEquals(current, root); current = current.GetVisualParent())
            origin += current.Bounds.Position;
        return new Rect(origin, node.Bounds.Size);
    }

    private static Button Close(MCPSetupPanelSession session) =>
        MCPSetupPanelRig.All<Button>(session.View).Single(button =>
            button.GetSelfAndVisualDescendants().OfType<SwiftImage>().Any(image => image.SymbolName == "xmark"));

    private static Grid HeaderRow(MCPSetupPanelSession session) => (Grid)Close(session).Parent!;

    private static Grid ButtonRow(MCPSetupPanelSession session) =>
        (Grid)MCPSetupPanelRig.ButtonLabelled(session.View, "Open my groups").Parent!;

    private static StackPanel StepsBlock(MCPSetupPanelSession session) =>
        (StackPanel)MCPSetupPanelRig.Text(session.View, session.Guide.Steps[0]).Parent!.Parent!;

    private static void ColorIs(Control control, IBrush? brush, SwiftColor expected) =>
        Assert.Equal(expected.Resolve(control.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    private static void TextIs(TextBlock text, double size, FontWeight weight, SwiftColor color)
    {
        Assert.Equal(size, text.FontSize);
        Assert.Equal(weight, text.FontWeight);
        ColorIs(text, text.Foreground, color);
    }

    private static AvaloniaWindow PanelWindow() =>
        (AvaloniaWindow)typeof(FloatingGuidePanel).GetField("panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(MCPSetupPanel.Shared)!;

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:63-86 VStack(.leading, spacing: 14), .padding(18), .frame(width: 400)
    public void Content_is_400_wide_inset_18_with_14_between_its_blocks()
    {
        Run(NeedsSetup(), session =>
        {
            var border = Assert.IsType<Border>(session.View.Child);
            Assert.Equal(400, border.Width);
            Assert.Equal(new Thickness(18), border.Padding);

            var header = Frame(HeaderRow(session), session.View);
            var reason = Frame(MCPSetupPanelRig.Text(session.View, session.Guide.Reason), session.View);
            var steps = Frame(StepsBlock(session), session.View);
            var buttons = Frame(ButtonRow(session), session.View);

            Assert.Equal(18, header.Left, 0.5);
            Assert.Equal(18, header.Top, 0.5);
            Assert.Equal(364, header.Width, 0.5);
            Assert.Equal(14, reason.Top - header.Bottom, 0.5);
            Assert.Equal(14, steps.Top - reason.Bottom, 0.5);
            Assert.Equal(14, buttons.Top - steps.Bottom, 0.5);

            session.View.Measure(new Size(400, double.PositiveInfinity));
            Assert.Equal(buttons.Bottom + 18, session.View.DesiredSize.Height, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:76-86 .clipShape(RoundedRectangle(cornerRadius: Chrome.windowCornerRadius)), WindowDragArea then WindowBackdrop(opacity: 0.5) behind
    public void The_panel_is_clipped_to_the_windows_rectangle_over_a_half_opacity_backdrop_and_a_drag_area()
    {
        Run(NeedsSetup(), session =>
        {
            var border = Assert.IsType<Border>(session.View.Child);
            Assert.True(border.ClipToBounds);
            Assert.Equal(default, border.CornerRadius); // MCPSetupPanel.swift:130 clips to the 26 pt shape; the port clips to the window's rectangle, which DWM rounds (OD-0075, ED-338)

            var layers = Assert.IsType<Grid>(border.Child);
            // Chrome.swift:211-214 scrim(for: 0.5): the stock scrim at the midpoint, 0.18 in light and 0.26 in dark.
            var isDark = session.View.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            SettingsBackdropAssertions.GlassOpacity(Assert.IsType<WindowBackdrop>(layers.Children[0]), isDark ? 0.26 : 0.18);
            Assert.IsType<WindowDragArea>(layers.Children[1]);
            Assert.IsType<StackPanel>(layers.Children[2]);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:76-82 "a press anywhere its controls do not claim drags it; the steps, reason and status line are not controls and so drag with the background"
    public void A_press_on_text_or_a_step_number_reaches_the_drag_area_and_a_press_on_a_button_does_not()
    {
        Run(NeedsSetup(), session =>
        {
            var button = MCPSetupPanelRig.ButtonLabelled(session.View, "Check again");
            session.Click(button);
            session.SetState(NeedsSetup());

            var targets = new List<Visual>
            {
                MCPSetupPanelRig.Text(session.View, session.Guide.Reason),
                MCPSetupPanelRig.Text(session.View, session.Guide.Steps[1]),
                MCPSetupPanelRig.Text(session.View, "2"),
                MCPSetupPanelRig.Text(session.View, session.Entry.Name),
                MCPSetupPanelRig.Text(session.View, session.Guide.Title),
                MCPSetupPanelRig.Text(session.View, "Still off on the server's side. " + session.Guide.Note),
                MCPSetupPanelRig.All<MCPIcon>(session.View).Single(),
            };
            foreach (var target in targets)
            {
                var hit = session.HitAtCentre(target);
                Assert.True(hit is WindowDragArea, $"{target} was hit through {hit}");
            }

            var buttonAt = MCPSetupPanelRig.ButtonLabelled(session.View, "Open my groups");
            Assert.Contains(buttonAt, session.HitAtCentre(buttonAt).GetSelfAndVisualAncestors());
        });
    }

    [Fact]
    [Trait("Decision", "ED-236")] // MCPSetupPanel.swift:117-131 `.padding(18).frame(width: 400)` before `.background { WindowDragArea() }`: the drag area fills the padded frame (OD-0075, the guide panels)
    public void A_press_in_the_18_point_band_around_the_card_reaches_the_drag_area_like_the_card_behind_its_text()
    {
        Run(NeedsSetup(), session =>
        {
            session.Window.UpdateLayout();
            using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(session.Window);
            var height = session.Window.ClientSize.Height;
            foreach (var at in new[] { new Point(8, height / 2), new Point(392, height / 2), new Point(200, 8), new Point(200, height - 9) })
            {
                var hit = session.Window.InputHitTest(at) as Visual;
                Assert.True(hit is WindowDragArea, $"a press at {at} (inside the shape, in the padding) was hit through {hit}");
            }
        });
    }

    // The shared panel, with the browser a recording double: body runs on the main actor's behalf.
    private void RunShared(Action<AppModel, TestRuntime, MCPSetupLauncherScope> body) =>
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var browser = new MCPSetupLauncherScope();
            var model = harness.MakeModel();
            try { body(model, harness.Main, browser); }
            finally
            {
                harness.Main.OnMain(() => { MCPSetupPanel.Shared.Close(); MCPSetupPanelRig.CancelProbes(model); });
                Dispatcher.UIThread.RunJobs();
                // The shared panel outlives a test: hand the next one the state of a panel never presented.
                var field = typeof(FloatingGuidePanel).GetField("panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                (field.GetValue(MCPSetupPanel.Shared) as AvaloniaWindow)?.Close();
                field.SetValue(MCPSetupPanel.Shared, null);
            }
        });

    private static void Present(TestRuntime runtime, AppModel model, string id = "gitlab") =>
        runtime.OnMain(() =>
        {
            MCPSetupPanel.Shared.Present(MCPSetupPanelRig.Entry(id), MCPSetupPanelRig.Guide(), model);
            Dispatcher.UIThread.RunJobs();
        });

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:20-33 present: idempotent per server (a second present for the same entry only brings it forward), the page opens as the panel appears
    public void Presenting_the_same_server_again_only_brings_it_forward_and_another_server_rebuilds_it()
    {
        RunShared((model, runtime, browser) =>
        {
            Present(runtime, model);
            var first = PanelWindow().Content;
            Assert.Equal([new Uri("https://gitlab.com/dashboard/groups")], browser.Launcher.Requests);
            Assert.True(MCPSetupPanel.Shared.IsPresenting(MCPSetupPanelRig.Entry()));

            Present(runtime, model);
            Assert.Same(first, PanelWindow().Content);
            Assert.Single(browser.Launcher.Requests);
            Assert.True(PanelWindow().IsVisible);

            Present(runtime, model, "other");
            Assert.NotSame(first, PanelWindow().Content);
            Assert.Equal(2, browser.Launcher.Requests.Count);
            Assert.True(MCPSetupPanel.Shared.IsPresenting(MCPSetupPanelRig.Entry("other")));
            Assert.False(MCPSetupPanel.Shared.IsPresenting(MCPSetupPanelRig.Entry()));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:35-38 + FloatingGuidePanel.swift:58-69 Escape closes the panel and clears its recorded entry
    public void Escape_closes_the_panel_and_a_later_present_opens_the_page_again()
    {
        RunShared((model, runtime, browser) =>
        {
            Present(runtime, model);
            var elsewhere = new AvaloniaWindow { Content = new Border() };
            elsewhere.Show();
            elsewhere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            elsewhere.Close();

            Assert.False(PanelWindow().IsVisible);
            Assert.False(MCPSetupPanel.Shared.IsPresenting(MCPSetupPanelRig.Entry()));
            Present(runtime, model);
            Assert.True(PanelWindow().IsVisible);
            Assert.Equal(2, browser.Launcher.Requests.Count);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // FloatingGuidePanel.swift:33-56 floating, not hidden on deactivate, no window buttons; MCPSetupPanel.swift:12 width 400, MCPSetupPanel.swift:66 non-activating
    public void The_panel_floats_without_activating_chrome_or_taskbar_entry_at_width_400()
    {
        RunShared((model, runtime, _) =>
        {
            Present(runtime, model);
            var panel = PanelWindow();
            Assert.True(panel.Topmost);
            Assert.False(panel.ShowActivated);
            Assert.False(panel.ShowInTaskbar);
            Assert.Equal(WindowDecorations.None, panel.WindowDecorations);
            Assert.Equal(400, panel.Width);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:42-50 fit(height:): max(ceil, 100), only when more than a point off, the top edge held fixed
    public void Fit_floors_at_100_ignores_a_one_point_change_and_keeps_the_top_edge()
    {
        RunShared((model, runtime, _) =>
        {
            Present(runtime, model);
            var panel = PanelWindow();
            runtime.OnMain(() => MCPSetupPanel.Shared.Fit(300));
            Assert.Equal(300, panel.Height);
            var top = panel.Position;

            runtime.OnMain(() => MCPSetupPanel.Shared.Fit(40));
            Assert.Equal(100, panel.Height);
            Assert.Equal(top, panel.Position);

            runtime.OnMain(() => MCPSetupPanel.Shared.Fit(100.4)); // ceil 101: one point off, ignored
            Assert.Equal(100, panel.Height);

            runtime.OnMain(() => MCPSetupPanel.Shared.Fit(101.2)); // ceil 102: two points off, taken
            Assert.Equal(102, panel.Height);
            Assert.Equal(top, panel.Position);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:84-86 onGeometryChange(height) { fit }: the panel follows its content as the status line or success view grows it
    public void The_panel_grows_to_its_content_when_a_status_line_appears_and_keeps_its_top_edge()
    {
        RunShared((model, runtime, _) =>
        {
            runtime.OnMain(() => MCPSetupPanelRig.SetState(model, "gitlab", NeedsSetup()));
            Present(runtime, model);
            var panel = PanelWindow();
            var before = panel.Height;
            var top = panel.Position;
            var view = Assert.IsType<MCPSetupPanelView>(panel.Content);

            var check = MCPSetupPanelRig.ButtonLabelled(view, "Check again");
            runtime.OnMain(() => { MCPSetupPanelRig.Click(check); MCPSetupPanelRig.CancelProbes(model); });
            runtime.OnMain(() => MCPSetupPanelRig.SetState(model, "gitlab", NeedsSetup()));
            runtime.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();

            view.Measure(new Size(400, double.PositiveInfinity));
            var wanted = Math.Ceiling(view.DesiredSize.Height);
            Assert.Contains(MCPSetupPanelRig.All<TextBlock>(view), block => block.Text?.StartsWith("Still off", StringComparison.Ordinal) == true);
            Assert.True(wanted > before + 20, $"the status line did not make the content taller: {before} -> {wanted}");
            Assert.InRange(panel.Height, wanted - 1, wanted);
            Assert.Equal(top, panel.Position);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // FloatingGuidePanel.swift:21-23 placed at the main screen's visible top right, inset 24 from both edges
    public void The_panel_sits_24_in_from_the_top_right_of_the_main_screens_working_area()
    {
        RunShared((model, runtime, _) =>
        {
            Present(runtime, model);
            var panel = PanelWindow();
            var screen = Assert.IsAssignableFrom<Avalonia.Platform.Screen>(panel.Screens.Primary);
            var scaling = screen.Scaling;
            var area = screen.WorkingArea;
            Assert.Equal(area.Right - (400 + 24) * scaling, panel.Position.X, 1);
            Assert.Equal(area.Y + 24 * scaling, panel.Position.Y, 1);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-079")] // MCPSetupPanel.swift:198-205 name .body semibold Chrome.primaryText over guide.title .subheadline Chrome.secondaryText
    public void The_header_names_the_server_in_body_semibold_over_the_guide_title_in_subheadline()
    {
        Run(NeedsSetup(), session =>
        {
            TextIs(MCPSetupPanelRig.Text(session.View, "GitLab"), 13, FontWeight.SemiBold, Chrome.PrimaryText);
            TextIs(MCPSetupPanelRig.Text(session.View, session.Guide.Title), 11, FontWeight.Normal, Chrome.SecondaryText);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-079")] // MCPSetupPanel.swift:197-203 HStack(alignment: .top, spacing: 10) { MCPIcon(size: 26); VStack(alignment: .leading, spacing: 1) }
    public void The_header_is_top_aligned_with_a_26_point_icon_ten_before_the_texts_which_sit_one_apart()
    {
        Run(NeedsSetup(), session =>
        {
            var header = Frame(HeaderRow(session), session.View);
            var icon = Frame(MCPSetupPanelRig.All<SwiftImage>(MCPSetupPanelRig.All<MCPIcon>(session.View).Single()).Single(), session.View);
            var name = Frame(MCPSetupPanelRig.Text(session.View, "GitLab"), session.View);
            var title = Frame(MCPSetupPanelRig.Text(session.View, session.Guide.Title), session.View);
            var close = Frame(Close(session), session.View);

            Assert.Equal(26, icon.Width, 0.5);
            Assert.Equal(26, icon.Height, 0.5);
            Assert.Equal(header.Top, icon.Top, 0.5);
            Assert.Equal(header.Top, name.Top, 0.5);
            Assert.Equal(header.Top, close.Top, 0.5);
            Assert.Equal(10, name.Left - icon.Right, 0.5);
            Assert.Equal(1, title.Top - name.Bottom, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-079")] // MCPSetupPanel.swift:197-207 spacing 10 on each side of Spacer(minLength: 8): at least 28 points between the texts and the close button
    public void The_header_keeps_twenty_eight_points_between_a_long_name_and_the_close_button()
    {
        Run(NeedsSetup(), session =>
        {
            var texts = Frame((Visual)MCPSetupPanelRig.Text(session.View, session.Entry.Name).Parent!, session.View);
            var close = Frame(Close(session), session.View);
            Assert.Equal(28, close.Left - texts.Right, 0.5);
            Assert.True(close.Right <= 18 + 364 + 0.5, $"the close button left the panel: {close}");
        }, entryName: "A very long connector name that has to wrap inside the narrow panel header");
    }

    [Fact]
    [Trait("Req", "UI-MCP-080")] // MCPSetupPanel.swift:208-215 Image(systemName: "xmark").font(.system(.subheadline, weight: .semibold)).foregroundStyle(Chrome.secondaryText), .buttonStyle(.plain)
    public void The_close_button_draws_only_a_secondary_subheadline_semibold_xmark()
    {
        Run(NeedsSetup(), session =>
        {
            var close = Close(session);
            var image = MCPSetupPanelRig.All<SwiftImage>(close).Single();
            var font = SwiftImage.GetFont(image)!.Resolve();
            Assert.Equal(11, font.FontSize);
            Assert.Equal(FontWeight.SemiBold, font.FontWeight);
            ColorIs(image, image.Foreground, Chrome.SecondaryText);
            Assert.Equal(new Thickness(0), close.BorderThickness);
            Assert.True(close.Background is null || Assert.IsAssignableFrom<ISolidColorBrush>(close.Background).Color.A == 0);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-080")] // MCPSetupPanel.swift:208-210 Button { onDone() }
    public void Pressing_close_runs_the_done_hook_once_and_leaves_the_server_alone()
    {
        Run(NeedsSetup(), session =>
        {
            session.Click(Close(session));
            Assert.Equal(1, session.Done);
            Assert.Null(session.Model.Mcp.Connection("gitlab"));
            Assert.IsType<MCPConnectionState.NeedsSetup>(session.Model.Mcp.State("gitlab"));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-080")] // MCPSetupPanel.swift:29-31, 35-38 the close button is the panel's close(): hides it and clears the recorded entry so a later present reopens the guide page
    public void Pressing_close_on_the_shown_panel_hides_it_and_a_later_present_opens_the_page_again()
    {
        RunShared((model, runtime, browser) =>
        {
            Present(runtime, model);
            var view = Assert.IsType<MCPSetupPanelView>(PanelWindow().Content);
            var close = MCPSetupPanelRig.All<Button>(view).Single(button => MCPSetupPanelRig.All<SwiftImage>(button).Any(image => image.SymbolName == "xmark"));
            runtime.OnMain(() => MCPSetupPanelRig.Click(close));

            Assert.False(PanelWindow().IsVisible);
            Assert.False(MCPSetupPanel.Shared.IsPresenting(MCPSetupPanelRig.Entry()));
            Present(runtime, model);
            Assert.Equal(2, browser.Launcher.Requests.Count);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-081")] // MCPSetupPanel.swift:123-126 Text(guide.reason).font(.system(.callout)).foregroundStyle(Chrome.primaryText).fixedSize(horizontal: false, vertical: true)
    public void The_reason_is_callout_primary_text_that_wraps_across_the_full_width()
    {
        Run(NeedsSetup(), session =>
        {
            var reason = MCPSetupPanelRig.Text(session.View, session.Guide.Reason);
            TextIs(reason, 12, FontWeight.Normal, Chrome.PrimaryText);
            Assert.Equal(TextWrapping.Wrap, reason.TextWrapping);
            var frame = Frame(reason, session.View);
            Assert.Equal(364, frame.Width, 0.5);
            Assert.True(frame.Height > reason.FontSize * 2, $"the reason did not wrap onto more lines: {frame}");
            Assert.Equal(reason.DesiredSize.Height, reason.Bounds.Height, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-081")] // MCPSetupPanel.swift:69-72 the reason belongs to the not-connected branch: the success view replaces it
    public void The_reason_is_replaced_by_the_success_view_once_the_server_connects()
    {
        Run(NeedsSetup(), session =>
        {
            Assert.Contains(MCPSetupPanelRig.All<TextBlock>(session.View), block => block.Text == session.Guide.Reason);
            session.SetState(new MCPConnectionState.Connected(2));
            Assert.DoesNotContain(MCPSetupPanelRig.All<TextBlock>(session.View), block => block.Text == session.Guide.Reason);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-082")] // MCPSetupPanel.swift:127-131 ForEach steps -> stepRow(index + 1): one row per step, numbered from 1 in order, VStack(alignment: .leading, spacing: 6)
    public void Each_step_is_one_row_numbered_from_one_in_order_six_points_apart()
    {
        Run(NeedsSetup(), session =>
        {
            var rows = StepsBlock(session).Children.Cast<Grid>().ToArray();
            Assert.Equal(session.Guide.Steps.Count, rows.Length);
            for (var index = 0; index < rows.Length; index++)
            {
                var number = rows[index].Children.OfType<Border>().Single().GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.Equal((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), number.Text);
                Assert.Equal(session.Guide.Steps[index], rows[index].Children.OfType<TextBlock>().Single().Text);
            }
            for (var index = 1; index < rows.Length; index++)
                Assert.Equal(6, rows[index].Bounds.Top - rows[index - 1].Bounds.Bottom, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-082")] // MCPSetupPanel.swift:170 HStack(spacing: 8), centre alignment by default
    public void A_single_line_step_text_sits_on_the_centre_line_of_its_18_point_number_circle()
    {
        Run(NeedsSetup(), session =>
        {
            var row = (Grid)StepsBlock(session).Children[0];
            var circle = row.Children.OfType<Border>().Single();
            var text = row.Children.OfType<TextBlock>().Single();
            Assert.Equal(text.DesiredSize.Height, text.Bounds.Height, 0.5); // the callout line is 17 points (ED-290 (1)), the circle 18
            Assert.Equal(circle.Bounds.Center.Y, text.Bounds.Center.Y, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-082")] // MCPSetupPanel.swift:220-228 HStack(spacing: 8) { ZStack { Circle().fill(Chrome.accent.opacity(0.18)).frame(18, 18); number .footnote semibold Chrome.accent } }
    public void The_number_sits_centred_in_an_18_point_accent_circle_eight_points_before_the_step_text()
    {
        Run(NeedsSetup(), session =>
        {
            var row = (Grid)StepsBlock(session).Children[0];
            var circle = row.Children.OfType<Border>().Single();
            Assert.Equal(18, circle.Bounds.Width, 0.5);
            Assert.Equal(18, circle.Bounds.Height, 0.5);
            Assert.Equal(new CornerRadius(9), circle.CornerRadius);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(circle.Background);
            Assert.Equal(Chrome.Accent.Opacity(0.18).Resolve(circle.ActualThemeVariant), fill.Color);

            var number = MCPSetupPanelRig.Text(circle, "1");
            TextIs(number, 10, FontWeight.SemiBold, Chrome.Accent);
            var inside = Frame(number, circle);
            Assert.Equal(9, inside.Center.X, 0.75);
            Assert.Equal(9, inside.Center.Y, 0.75);

            var text = row.Children.OfType<TextBlock>().Single();
            Assert.Equal(8, text.Bounds.Left - circle.Bounds.Right, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-082")] // MCPSetupPanel.swift:229-234 step text .font(.system(.callout)) Chrome.primaryText, fixedSize(horizontal: false, vertical: true): wraps, never truncates
    public void Step_text_is_callout_primary_text_that_wraps_and_the_row_centres_its_circle_on_it()
    {
        Run(NeedsSetup(), session =>
        {
            var rows = StepsBlock(session).Children.Cast<Grid>().ToArray();
            var wrapped = rows[1];
            var text = wrapped.Children.OfType<TextBlock>().Single();
            TextIs(text, 12, FontWeight.Normal, Chrome.PrimaryText);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.True(text.Bounds.Height > text.FontSize * 2, $"step 2 did not wrap: {text.Bounds}");
            Assert.Equal(text.DesiredSize.Height, text.Bounds.Height, 0.5);
            var circle = wrapped.Children.OfType<Border>().Single();
            Assert.Equal(text.Bounds.Center.Y, circle.Bounds.Center.Y, 0.5);
        });
    }

    private static GlassEffect GlassOf(Button button) =>
        button.GetSelfAndVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect).First(effect => effect is not null)!;

    // The capsule that carries the glass inside a button, and the label it holds.
    private static Control CapsuleOf(Button button) =>
        button.GetSelfAndVisualDescendants().OfType<Control>().First(node => SwiftGlass.GetEffect(node) is not null);

    private static IReadOnlyList<string?> Texts(Visual root) => [.. MCPSetupPanelRig.All<TextBlock>(root).Select(block => block.Text)];

    private static bool Probing(AppModel model, string id) =>
        ((System.Collections.IDictionary)typeof(MCPStore).GetField("probes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(model.Mcp)!).Contains(id);

    [Fact]
    [Trait("Req", "UI-MCP-083")] // MCPSetupPanel.swift:133-138 Button(guide.pageLabel) { NSWorkspace.shared.open(url) }: reopens the guide's page in the default browser
    public void The_open_page_button_carries_the_guides_label_and_reopens_its_page_in_the_browser()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var source = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(source);
            using var browser = new MCPSetupLauncherScope();
            var model = harness.MakeModel();
            using var session = new MCPSetupPanelSession(harness.Main, source, model, NeedsSetup());
            Assert.Empty(browser.Launcher.Requests);

            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Open my groups"));
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Open my groups"));

            Assert.Equal([new Uri("https://gitlab.com/dashboard/groups"), new Uri("https://gitlab.com/dashboard/groups")], browser.Launcher.Requests);
            Assert.IsType<MCPConnectionState.NeedsSetup>(model.Mcp.State("gitlab"));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-083")] // MCPSetupPanel.swift:133-138, 147-153 HStack { Button(pageLabel).buttonStyle(.glass); Spacer(); ... }: leading, at the control's own size, in glass
    public void The_open_page_button_leads_the_row_as_a_glass_capsule()
    {
        Run(NeedsSetup(), session =>
        {
            var open = MCPSetupPanelRig.ButtonLabelled(session.View, "Open my groups");
            var frame = Frame(open, session.View);
            Assert.Equal(18, frame.Left, 0.5);
            Assert.Equal(Chrome.CapsuleContentHeight, CapsuleOf(open).Bounds.Height, 0.5);
            var label = MCPSetupPanelRig.Text(open, "Open my groups");
            Assert.Equal(label.Bounds.Width + 2 * Chrome.CapsuleHorizontalPadding, frame.Width, 0.5);
            var glass = GlassOf(open);
            Assert.Equal(GlassShape.Capsule(RoundedCornerStyle.Continuous), glass.Shape);
            Assert.Null(glass.TintColor);
        });
    }

    [Theory]
    [InlineData("needs")]
    [InlineData("checking")]
    [InlineData("failed")]
    [InlineData("none")]
    [Trait("Req", "UI-MCP-083")] // MCPSetupPanel.swift:69-72, 133 the open page button sits in the not-connected branch: always there while the server is not connected, gone with the success view
    public void The_open_page_button_is_there_in_every_state_but_connected(string kind)
    {
        MCPConnectionState state = kind switch
        {
            "needs" => NeedsSetup(),
            "checking" => new MCPConnectionState.Connecting(),
            "failed" => new MCPConnectionState.Failed("The server answered 500."),
            _ => new MCPConnectionState.NotConnected(),
        };
        Run(state, session =>
        {
            Assert.Contains("Open my groups", Texts(session.View));
            session.SetState(new MCPConnectionState.Connected(1));
            Assert.DoesNotContain("Open my groups", Texts(session.View));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-084")] // MCPSetupPanel.swift:147-153 Button("Check again") { checkedOnce = true; model.mcp.refresh(entry) }.buttonStyle(.glassProminent), trailing in the row
    public void The_check_again_button_trails_the_row_as_a_tinted_glass_capsule()
    {
        Run(NeedsSetup(), session =>
        {
            var check = MCPSetupPanelRig.ButtonLabelled(session.View, "Check again");
            var open = MCPSetupPanelRig.ButtonLabelled(session.View, "Open my groups");
            var frame = Frame(check, session.View);
            Assert.Equal(18 + 364, frame.Right, 0.5);
            Assert.Equal(Chrome.CapsuleContentHeight, CapsuleOf(check).Bounds.Height, 0.5);
            var label = MCPSetupPanelRig.Text(check, "Check again");
            Assert.Equal(label.Bounds.Width + 2 * Chrome.CapsuleHorizontalPadding, frame.Width, 0.5);
            Assert.Equal(Frame(open, session.View).Top, frame.Top, 0.5);
            var glass = GlassOf(check);
            Assert.Equal(GlassShape.Capsule(RoundedCornerStyle.Continuous), glass.Shape);
            Assert.NotNull(glass.TintColor);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-084")] // MCPSetupPanel.swift:149-151 checkedOnce = true; model.mcp.refresh(entry): re-probes the existing connection, no connect and no change of what is stored
    public void Pressing_check_again_starts_a_refresh_probe_and_swaps_the_button_for_the_checking_indicator()
    {
        Run(NeedsSetup(), session =>
        {
            Assert.False(Probing(session.Model, "gitlab"));
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));

            Assert.True(Probing(session.Model, "gitlab"));
            Assert.IsType<MCPConnectionState.Connecting>(session.Model.Mcp.State("gitlab"));
            Assert.Null(session.Model.Mcp.Connection("gitlab"));
            Assert.Contains("Checking", Texts(session.View));
            Assert.DoesNotContain("Check again", Texts(session.View));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-084")] // MCPSetupPanel.swift:149 checkedOnce = true: pressing it is what enables the status lines, and they never appear before the first check
    public void The_status_lines_wait_for_the_first_press_of_check_again()
    {
        Run(NeedsSetup(), session =>
        {
            Assert.DoesNotContain(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);
            session.SetState(new MCPConnectionState.Failed("could not reach the server"));
            Assert.DoesNotContain("could not reach the server", Texts(session.View));
            session.SetState(NeedsSetup());

            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(NeedsSetup());
            Assert.Contains(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-085")] // MCPSetupPanel.swift:140-146 if state.isConnecting { HStack(spacing: 6) { WorkingSpinner(cellSize: 3); Text("Checking").font(.system(.subheadline)).foregroundStyle(Chrome.secondaryText) } }
    public void While_connecting_a_spinner_and_the_word_checking_take_the_check_buttons_place()
    {
        Run(new MCPConnectionState.Connecting(), session =>
        {
            Assert.DoesNotContain("Check again", Texts(session.View));
            var label = MCPSetupPanelRig.Text(session.View, "Checking");
            TextIs(label, 11, FontWeight.Normal, Chrome.SecondaryText);

            // nth step 11: the 12 px spinner in the 16 px status slot replaces the 3 x 3 working spinner of cell size 3
            var spinner = MCPSetupPanelRig.All<NthStatusMark>(session.View).Single();
            Assert.Equal(NthStatus.Working, spinner.Status);
            Assert.Equal(12, spinner.MarkSize);
            Assert.True(Assert.IsType<NthShineText>(label).IsShining);
            var spinnerFrame = Frame(spinner, session.View);
            var labelFrame = Frame(label, session.View);
            Assert.Equal(6, labelFrame.Left - spinnerFrame.Right, 0.5);
            Assert.Equal(labelFrame.Center.Y, spinnerFrame.Center.Y, 0.5);
            Assert.Equal(18 + 364, labelFrame.Right, 0.5);

            Assert.Equal(16, spinnerFrame.Width, 0.01);
            Assert.Equal(16, spinnerFrame.Height, 0.01);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-085")] // MCPSetupPanel.swift:140-153 the indicator is only a label and spinner: no control to cancel the check, the page button stays
    public void The_checking_indicator_offers_no_control_and_is_gone_when_the_check_ends()
    {
        Run(new MCPConnectionState.Connecting(), session =>
        {
            var buttons = MCPSetupPanelRig.All<Button>(session.View);
            Assert.Equal(2, buttons.Count); // the close button and the page button
            session.SetState(NeedsSetup());
            Assert.DoesNotContain("Checking", Texts(session.View));
            Assert.Empty(MCPSetupPanelRig.All<NthStatusMark>(session.View));
            Assert.Contains("Check again", Texts(session.View));
        });
    }

    private static MCPSetupGuide LongNoteGuide() => MCPSetupPanelRig.Guide(string.Join(' ', Enumerable.Repeat("The change can take a while to reach every server.", 4)));

    [Fact]
    [Trait("Req", "UI-MCP-086")] // MCPSetupPanel.swift:155-160 Text("Still off on the server's side. " + (guide.note ?? "")).font(.system(.subheadline)).foregroundStyle(Chrome.warning)
    public void After_a_check_a_still_needing_setup_server_shows_the_literal_and_the_note_in_subheadline_warning_text()
    {
        Run(NeedsSetup(), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(NeedsSetup());

            var line = MCPSetupPanelRig.Text(session.View, "Still off on the server's side. GitLab can take a minute to apply the change.");
            TextIs(line, 11, FontWeight.Normal, Chrome.Warning);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-086")] // MCPSetupPanel.swift:157 + (guide.note ?? ""): without a note the line is the literal alone, its trailing space kept
    public void Without_a_note_the_still_off_line_is_the_literal_with_its_trailing_space()
    {
        var guide = MCPSetupPanelRig.Guide(null);
        Run(NeedsSetup(guide), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(NeedsSetup(guide));

            Assert.Contains("Still off on the server's side. ", Texts(session.View));
        }, guide);
    }

    [Fact]
    [Trait("Req", "UI-MCP-086")] // MCPSetupPanel.swift:155-160 fixedSize(horizontal: false, vertical: true) full width below the button row in the VStack (spacing 14)
    public void The_still_off_line_wraps_across_the_full_width_fourteen_below_the_button_row()
    {
        var guide = LongNoteGuide();
        Run(NeedsSetup(guide), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(NeedsSetup(guide));

            var line = MCPSetupPanelRig.All<TextBlock>(session.View).Single(block => block.Text?.StartsWith("Still off", StringComparison.Ordinal) == true);
            Assert.Equal(TextWrapping.Wrap, line.TextWrapping);
            var frame = Frame(line, session.View);
            Assert.Equal(364, frame.Width, 0.5);
            Assert.True(frame.Height > line.FontSize * 2, $"the line did not wrap: {frame}");
            Assert.Equal(line.DesiredSize.Height, line.Bounds.Height, 0.5);
            Assert.Equal(14, frame.Top - Frame(ButtonRow(session), session.View).Bottom, 0.5);
        }, guide);
    }

    [Fact]
    [Trait("Req", "UI-MCP-086")] // MCPSetupPanel.swift:155-157 checkedOnce && !state.isConnecting, then case .needsSetup only
    public void The_still_off_line_is_hidden_while_checking_and_for_a_server_in_any_other_state()
    {
        Run(NeedsSetup(), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            Assert.IsType<MCPConnectionState.Connecting>(session.Model.Mcp.State("gitlab"));
            Assert.DoesNotContain(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);

            session.SetState(new MCPConnectionState.NotConnected());
            Assert.DoesNotContain(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);

            session.SetState(NeedsSetup());
            Assert.Contains(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-087")] // MCPSetupPanel.swift:161-166 Text(verbatim: message).font(.system(.subheadline)).foregroundStyle(Chrome.danger).fixedSize(horizontal: false, vertical: true)
    public void After_a_check_a_failed_server_shows_its_message_verbatim_in_subheadline_danger_text()
    {
        const string message = "The server answered 502. Try again in a minute.";
        Run(NeedsSetup(), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(new MCPConnectionState.Failed(message));

            var line = MCPSetupPanelRig.Text(session.View, message);
            TextIs(line, 11, FontWeight.Normal, Chrome.Danger);
            Assert.Equal(TextWrapping.Wrap, line.TextWrapping);
            Assert.DoesNotContain(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-087")] // MCPSetupPanel.swift:161-166 full width, wrapping, below the button row
    public void The_failure_line_wraps_across_the_full_width_fourteen_below_the_button_row()
    {
        var message = string.Join(' ', Enumerable.Repeat("The server quit before it answered.", 6));
        Run(NeedsSetup(), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(new MCPConnectionState.Failed(message));

            var line = MCPSetupPanelRig.Text(session.View, message);
            var frame = Frame(line, session.View);
            Assert.Equal(364, frame.Width, 0.5);
            Assert.True(frame.Height > line.FontSize * 2, $"the line did not wrap: {frame}");
            Assert.Equal(line.DesiredSize.Height, line.Bounds.Height, 0.5);
            Assert.Equal(14, frame.Top - Frame(ButtonRow(session), session.View).Bottom, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-087")] // MCPSetupPanel.swift:155, 161 shown only after a check and not while connecting
    public void The_failure_line_waits_for_a_check_and_is_hidden_while_checking()
    {
        Run(new MCPConnectionState.Failed("could not reach the server"), session =>
        {
            Assert.DoesNotContain("could not reach the server", Texts(session.View));

            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            Assert.IsType<MCPConnectionState.Connecting>(session.Model.Mcp.State("gitlab"));
            Assert.DoesNotContain("could not reach the server", Texts(session.View));

            session.SetState(new MCPConnectionState.Failed("could not reach the server"));
            Assert.Contains("could not reach the server", Texts(session.View));
        });
    }

    // The content column is the top layer of the panel; its first block is the header, its second the success view.
    private static Control SuccessBlock(MCPSetupPanelSession session)
    {
        var column = Assert.IsType<StackPanel>(Assert.IsType<Grid>(Assert.IsType<Border>(session.View.Child).Child).Children[2]);
        return column.Children[1];
    }

    private static SwiftImage Glyph(MCPSetupPanelSession session) =>
        MCPSetupPanelRig.All<SwiftImage>(SuccessBlock(session)).Single(image => image.SymbolName == "checkmark.circle.fill");

    private static double Scale(Control control) => Assert.IsType<ScaleTransform>(control.RenderTransform).ScaleX;

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:240-251 VStack(spacing: 8) { checkmark.circle.fill .font(.system(size: 34)) Chrome.success; Text("Connected · \(count) tools") .font(.system(.callout, weight: .medium)) Chrome.primaryText }
    public void The_success_view_is_a_green_glyph_over_the_connected_count_with_eight_points_between()
    {
        Run(new MCPConnectionState.Connected(7), session =>
        {
            var glyph = Glyph(session);
            var font = SwiftImage.GetFont(glyph)!.Resolve();
            Assert.Equal(34, font.FontSize);
            ColorIs(glyph, glyph.Foreground, Chrome.Success);

            var caption = MCPSetupPanelRig.Text(session.View, "Connected · 7 tools");
            TextIs(caption, 12, FontWeight.Medium, Chrome.PrimaryText);
            Assert.Equal(8, Frame(caption, session.View).Top - Frame(glyph, session.View).Bottom, 0.5);
            Assert.Equal(Frame(glyph, session.View).Center.X, Frame(caption, session.View).Center.X, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:249 Text(verbatim: "Connected · \(count) tools"): the count comes from the connected state
    public void The_success_caption_reads_the_tool_count_of_the_connected_state()
    {
        Run(new MCPConnectionState.Connected(12), session =>
        {
            Assert.Contains("Connected · 12 tools", Texts(session.View));
            session.SetState(new MCPConnectionState.Connected(3));
            Assert.Contains("Connected · 3 tools", Texts(session.View));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:253 .frame(maxWidth: .infinity, minHeight: 120, alignment: .center): full width, at least 120 tall, the stack centred in it
    public void The_success_view_fills_the_width_and_120_points_of_height_with_its_content_centred()
    {
        Run(new MCPConnectionState.Connected(2), session =>
        {
            var block = Frame(SuccessBlock(session), session.View);
            Assert.Equal(364, block.Width, 0.5);
            Assert.Equal(120, block.Height, 0.5);

            var glyph = Frame(Glyph(session), session.View);
            var caption = Frame(MCPSetupPanelRig.Text(session.View, "Connected · 2 tools"), session.View);
            Assert.Equal(glyph.Top - block.Top, block.Bottom - caption.Bottom, 0.5);
            Assert.Equal(block.Center.X, glyph.Center.X, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:69-72 if state.isConnected { successView } else { reason, steps, buttons, status line }
    public void The_success_view_replaces_the_reason_the_steps_the_buttons_and_the_status_line()
    {
        Run(NeedsSetup(), session =>
        {
            session.Click(MCPSetupPanelRig.ButtonLabelled(session.View, "Check again"));
            session.SetState(NeedsSetup());
            Assert.Contains(Texts(session.View), text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);

            session.SetState(new MCPConnectionState.Connected(4));

            var texts = Texts(session.View);
            Assert.Contains("Connected · 4 tools", texts);
            Assert.DoesNotContain(session.Guide.Reason, texts);
            Assert.All(session.Guide.Steps, step => Assert.DoesNotContain(step, texts));
            Assert.DoesNotContain("Open my groups", texts);
            Assert.DoesNotContain("Check again", texts);
            Assert.DoesNotContain(texts, text => text?.StartsWith("Still off", StringComparison.Ordinal) == true);
            Assert.Contains("GitLab", texts); // the header stays
            Assert.Single(MCPSetupPanelRig.All<Button>(session.View)); // only the close button
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:135-141 .onChange(of: state.isConnected) { Task { sleep 1.2s; onDone() } }: the panel closes itself 1.2 seconds after the server connects
    public void The_panel_closes_itself_one_point_two_seconds_after_the_server_connects()
    {
        Run(NeedsSetup(), session =>
        {
            session.SetState(new MCPConnectionState.Connected(2));
            Assert.Equal(0, session.Done);
            session.Advance(1100);
            Assert.Equal(0, session.Done);
            session.Advance(150);
            Assert.Equal(1, session.Done);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:244, 255-259 .scaleEffect(successPopped ? 1 : 0.5); onAppear { withAnimation(.spring(duration: 0.5, bounce: 0.4)) { successPopped = true } }
    public void The_glyph_springs_from_half_size_to_full_size_with_a_bounce_and_stays_there()
    {
        Run(NeedsSetup(), session =>
        {
            session.SetState(new MCPConnectionState.Connected(2));
            Assert.Equal(0.5, Scale(Glyph(session)), 6);

            var spring = Marrow.UI.Port.Animation.Spring(duration: 0.5, bounce: 0.4);
            var peak = 0.0;
            for (var elapsed = 50; elapsed < 500; elapsed += 50)
            {
                session.Frame(50);
                var scale = Scale(Glyph(session));
                Assert.Equal(0.5 + 0.5 * spring.Progress(elapsed / 1000.0), scale, 6);
                peak = Math.Max(peak, scale);
            }
            Assert.True(peak > 1.0, $"the spring never overshot full size (peak {peak}): bounce 0.4 overshoots");

            session.Frame(1000);
            Assert.Equal(1.0, Scale(Glyph(session)), 6);
            session.Frame(1000);
            Assert.Equal(1.0, Scale(Glyph(session)), 6);

            // The view rebuilt for a new count is the same appearance: onAppear's state is already popped, no second spring.
            session.SetState(new MCPConnectionState.Connected(5));
            session.Frame(100);
            Assert.Equal(1.0, Scale(Glyph(session)), 6);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-088")] // MCPSetupPanel.swift:255-257 if reduceMotion { successPopped = true }: set straight to 1, no animation
    public void Under_reduce_motion_the_glyph_is_full_size_at_once()
    {
        Run(NeedsSetup(), session =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(session.View, true);
            session.Pump();
            session.SetState(new MCPConnectionState.Connected(2));

            Assert.Equal(1.0, Scale(Glyph(session)), 6);
            session.Frame(100);
            Assert.Equal(1.0, Scale(Glyph(session)), 6);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // FloatingGuidePanel.swift:17-20 + MCPSetupPanel.swift:84-86 the height follows the content (fittingSize, rounded up, floor 100) from the moment the panel appears
    public void A_presented_panel_is_exactly_as_tall_as_its_content()
    {
        RunShared((model, runtime, _) =>
        {
            Present(runtime, model);
            runtime.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            var panel = PanelWindow();
            var view = Assert.IsType<MCPSetupPanelView>(panel.Content);
            view.Measure(new Size(400, double.PositiveInfinity));
            var wanted = Math.Max(Math.Ceiling(view.DesiredSize.Height), 100);
            Assert.InRange(panel.Height, wanted - 1, wanted);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-078")] // MCPSetupPanel.swift:43 guard let panel, panel.isVisible: a closed panel is not resized
    public void Fit_leaves_a_closed_panel_alone()
    {
        RunShared((model, runtime, _) =>
        {
            Present(runtime, model);
            var panel = PanelWindow();
            runtime.OnMain(() => MCPSetupPanel.Shared.Fit(300));
            runtime.OnMain(() => MCPSetupPanel.Shared.Close());
            Assert.False(panel.IsVisible);

            runtime.OnMain(() => MCPSetupPanel.Shared.Fit(500));
            Assert.Equal(300, panel.Height);
        });
    }
}
