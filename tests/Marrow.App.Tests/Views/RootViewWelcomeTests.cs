using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :299-350 struct WelcomeView, with the Chrome parts it
// uses (UI/Chrome/Chrome.swift:1006-1080). The page-level records UI-WIN-056 to UI-WIN-063 as drawn in a laid-out window.
[Collection("Headless")]
public sealed class RootViewWelcomeTests(HeadlessSession headless)
{
    private void OnWelcome(Action<Control> check, double width = 900)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = RootViewRig.Show(model, new WelcomeView(), width);
                try { check(host); }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    // :306 the identity VStack(spacing: 14), the first child of the page's VStack(spacing: 30) (:305).
    private static StackPanel Identity(Control host) => Assert.IsType<StackPanel>(RootViewRig.Text(host, "nth").Parent);

    private static StackPanel Column(Control host) => Assert.IsType<StackPanel>(Identity(host).Parent);

    private static Button AddProject(Control host) =>
        Assert.Single(RootViewRig.All<Button>(host), button => RootViewRig.AllText(button) == "Add a project");

    [Fact]
    [Trait("Req", "UI-WIN-056")] // App/Views/RootView.swift:304-309
    public void The_welcome_page_leads_with_a_104_point_application_icon_inside_a_column_padded_48()
    {
        OnWelcome(host =>
        {
            var identity = Identity(host);
            var column = Column(host);
            var icon = Assert.IsType<Image>(identity.Children[0]); // :307 Image(nsImage: NSApp.applicationIconImage), first of the stack
            MarrowLogoCheck.AssertIsTheAppIconImage(icon, 104); // the running application's own icon (OD-0015): the packaged bitmap, as the About pill draws it
            Assert.Equal(new Size(104, 104), icon.Bounds.Size); // :309 .frame(width: 104, height: 104)
            Assert.Equal(0, icon.Bounds.Top); // top of the 14-point stack
            Assert.Equal(identity.Bounds.Width / 2, icon.Bounds.Center.X, 0.5); // a VStack centres its children
            Assert.Equal(host.Bounds.Width / 2, RootViewRig.In(icon, host).Center.X, 0.5); // and the page centres the stack

            // :344 .padding(48) around the 30-point column (:305)
            var padded = Assert.IsType<Border>(column.Parent);
            Assert.Equal(48, column.Bounds.Top);
            Assert.Equal(48, padded.Bounds.Bottom - column.Bounds.Bottom, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-056")] // App/Views/RootView.swift:344-345 .padding(48) then .frame(maxWidth: .infinity)
    public void The_welcome_column_fills_the_page_inside_48_points_of_padding_on_every_side()
    {
        OnWelcome(host =>
        {
            var column = Column(host);
            var padded = Assert.IsType<Border>(column.Parent);
            Assert.Equal(48, column.Bounds.Left, 0.5);
            Assert.Equal(48, padded.Bounds.Width - column.Bounds.Right, 0.5);
            Assert.Equal(48, column.Bounds.Top, 0.5);
            Assert.Equal(48, padded.Bounds.Height - column.Bounds.Bottom, 0.5);
        }, 500);
    }

    [Fact]
    [Trait("Req", "UI-WIN-056")] // App/Views/RootView.swift:305-306, :344 the 30-point column with the identity stack first, centred
    public void The_welcome_column_stacks_its_four_blocks_30_points_apart_centred_in_the_page()
    {
        OnWelcome(host =>
        {
            var column = Column(host);
            Assert.Equal(4, column.Children.Count); // identity, add-a-project stack, Activity list, Providers
            for (var index = 1; index < column.Children.Count; index++)
                Assert.Equal(30, column.Children[index].Bounds.Top - column.Children[index - 1].Bounds.Bottom, 0.5); // :305 spacing: 30
            Assert.Equal(host.Bounds.Width / 2, RootViewRig.In(column, host).Center.X, 0.5); // :345 .frame(maxWidth: .infinity)
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-057")] // App/Views/RootView.swift:310-311, OD-0002 (the literal is "Marrow")
    public void The_product_name_is_34_point_semibold_default_colour_second_in_the_stack()
    {
        OnWelcome(host =>
        {
            var identity = Identity(host);
            var title = RootViewRig.Text(host, "nth");
            Assert.Same(title, identity.Children[1]); // second element of the stack
            RootViewRig.AssertStyle(title, 34, FontWeight.SemiBold); // :311 .font(.system(size: 34, weight: .semibold))
            Assert.False(title.IsSet(TextBlock.ForegroundProperty)); // no foregroundStyle: the default text colour
            var icon = identity.Children[0];
            Assert.Equal(14, title.Bounds.Top - icon.Bounds.Bottom, 0.5); // :306 VStack(spacing: 14)
            Assert.DoesNotContain("Droppy", RootViewRig.AllText(host), StringComparison.Ordinal); // OD-0002 and OD-0012 F2
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-057")] // App/Views/RootView.swift:306 VStack(spacing: 14), centre alignment by default
    [Trait("Req", "UI-WIN-058")] // App/Views/RootView.swift:306, :312-314
    public void The_product_name_and_the_tagline_are_each_centred_on_the_page_like_the_icon()
    {
        OnWelcome(host =>
        {
            // A VStack centres each child at its own width: the name's and the tagline's text sit under the icon's centre,
            // not left-aligned at the widest line's edge (the native gallery showed "Marrow" flush left under the tagline).
            foreach (var text in new[] { RootViewRig.Text(host, "nth"), RootViewRig.Text(host, "A calm, native home for your coding agents.") })
            {
                Assert.Equal(text.DesiredSize.Width, text.Bounds.Width, 0.5); // at its own width
                Assert.Equal(host.Bounds.Width / 2, RootViewRig.In(text, host).Center.X, 0.5);
            }
        }, width: 1320);
    }

    [Fact]
    [Trait("Req", "UI-WIN-059")] // App/Views/RootView.swift:316 VStack(spacing: 10), centre alignment by default
    [Trait("Req", "UI-WIN-060")] // App/Views/RootView.swift:325-327 the drop hint
    public void Add_a_project_and_the_drop_hint_are_each_centred_at_their_own_width_on_the_page()
    {
        OnWelcome(host =>
        {
            // The hint is the wider line; the capsule is its own width under it, not stretched to the hint's.
            foreach (Control child in new Control[] { AddProject(host), RootViewRig.Text(host, "Or drop a folder anywhere in this window.") })
            {
                Assert.Equal(child.DesiredSize.Width, child.Bounds.Width, 0.5);
                Assert.Equal(host.Bounds.Width / 2, RootViewRig.In(child, host).Center.X, 0.5);
            }
        }, width: 1320);
    }

    [Fact]
    [Trait("Req", "UI-WIN-058")] // App/Views/RootView.swift:312-314, OD-0012 F2
    public void The_tagline_is_the_title3_style_in_secondary_text_third_in_the_stack()
    {
        OnWelcome(host =>
        {
            var identity = Identity(host);
            var tagline = RootViewRig.Text(host, "A calm, native home for your coding agents."); // OD-0012 F2 drops the Droppy sentence
            Assert.Same(tagline, identity.Children[2]);
            RootViewRig.AssertStyle(tagline, 15, FontWeight.Normal); // :313 .font(.system(.title3)), ChatZoom.swift:50
            RootViewRig.AssertForeground(tagline, RootViewRig.Secondary); // :314 Chrome.secondaryText
            Assert.Equal(14, tagline.Bounds.Top - identity.Children[1].Bounds.Bottom, 0.5); // :306 spacing 14
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-059")] // App/Views/RootView.swift:316-324
    public void Add_a_project_is_a_prominent_accent_tinted_glass_button_with_its_folder_symbol()
    {
        OnWelcome(host =>
        {
            var button = AddProject(host);
            var symbol = Assert.Single(RootViewRig.All<SwiftImage>(button));
            Assert.Equal("folder.badge.plus", symbol.SymbolName); // :320 Label(..., systemImage: "folder.badge.plus")
            Assert.True(button.IsEnabled); // no disabled state
            var stack = Assert.IsType<StackPanel>(button.Parent);
            Assert.Same(button, stack.Children[0]); // first element of the 10-point stack (:316)
            // :323 .buttonStyle(.glassProminent): glass in a capsule, tinted by the theme accent.
            var glass = Assert.Single(RootViewRig.All<Border>(button), border => SwiftGlass.GetEffect(border) is not null);
            var effect = SwiftGlass.GetEffect(glass)!;
            Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
            Assert.NotNull(effect.TintColor);
            Assert.Equal(Chrome.Accent.Resolve(button.ActualThemeVariant), effect.TintColor.Resolve(button.ActualThemeVariant));
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-059")] // App/Views/RootView.swift:317-324 `.padding(.horizontal, 6)` on the welcome label only, `.extraLarge` against NoThreadView's `.large` (:440)
    public void Add_a_project_carries_6_extra_points_beside_its_label_and_is_larger_than_the_new_thread_button()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            harness.Main.OnMain(() =>
            {
                var welcome = RootViewRig.Show(model, new WelcomeView());
                var add = AddProject(welcome);
                var addInset = RootViewRig.In(Assert.Single(RootViewRig.All<SwiftImage>(add)), welcome).X - RootViewRig.In(add, welcome).X;
                var addHeight = add.Bounds.Height;
                RootViewRig.Close(welcome);

                model.AddProject(new Project("Project", directory).Url);
                var pane = RootViewRig.Show(model, new NoThreadView());
                var newThread = Assert.Single(RootViewRig.All<Button>(pane));
                var newInset = RootViewRig.In(Assert.Single(RootViewRig.All<SwiftImage>(newThread)), pane).X - RootViewRig.In(newThread, pane).X;
                var newHeight = newThread.Bounds.Height;
                RootViewRig.Close(pane);

                Assert.Equal(newInset + 6, addInset, 0.5); // the only padding the two labels differ by
                Assert.True(addHeight > newHeight, "extra-large must be taller than large"); // :324 against :440
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-059")] // App/Views/RootView.swift:317-319 model.chooseProjectFolder() -> AppModel.swift:535-547, a modal folder chooser
    public void Clicking_add_a_project_opens_the_modal_folder_chooser()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var runner = new RecordingOpenPanelRunner(null);
            using var panel = OpenPanel.Runner.Override(runner);
            harness.Main.OnMain(() =>
            {
                var host = RootViewRig.Show(model, new WelcomeView());
                try
                {
                    Assert.Empty(runner.Shown);
                    AddProject(host).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var shown = Assert.Single(runner.Shown);
                    Assert.True(shown.Directories); // AppModel.swift:537-538: folders only
                    Assert.False(shown.Files);
                    Assert.True(shown.Multiple); // :539
                }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-060")] // App/Views/RootView.swift:325-327
    public void The_drop_hint_is_callout_secondary_text_10_points_under_the_button()
    {
        OnWelcome(host =>
        {
            var button = AddProject(host);
            var hint = RootViewRig.Text(host, "Or drop a folder anywhere in this window.");
            var stack = Assert.IsType<StackPanel>(button.Parent);
            Assert.Same(hint, stack.Children[1]);
            RootViewRig.AssertStyle(hint, 12, FontWeight.Normal); // :326 .font(.system(.callout)), ChatZoom.swift:52
            RootViewRig.AssertForeground(hint, RootViewRig.Secondary); // :327
            Assert.Equal(10, hint.Bounds.Top - button.Bounds.Bottom, 0.5); // :316 VStack(spacing: 10)
            Assert.Equal(RootViewRig.In(button, host).Center.X, RootViewRig.In(hint, host).Center.X, 0.5);
        });
    }

    [Theory]
    [Trait("Req", "UI-WIN-061")] // App/Views/RootView.swift:329-338; UI/Chrome/Chrome.swift:1034-1050, sectionHeaderSpacing 10 (:60)
    [InlineData(900.0, 480.0)] // capped at 480: .frame(maxWidth: 480) around a section that fills what it is offered
    [InlineData(400.0, 304.0)] // narrower than the cap: the window less the 48-point padding on both sides
    public void The_activity_list_section_is_a_secondary_semibold_header_with_its_card_10_points_below_at_most_480_wide(double window, double expected)
    {
        OnWelcome(host =>
        {
            var header = RootViewRig.Text(host, "Activity list");
            RootViewRig.AssertStyle(header, 13, FontWeight.SemiBold); // :1040 .font(.system(.body, weight: .semibold)), ChatZoom.swift:51
            RootViewRig.AssertForeground(header, RootViewRig.Secondary); // :1042
            var section = Assert.Single(RootViewRig.All<ChromeSection>(host), s => RootViewRig.AllText(s).StartsWith("Activity list", StringComparison.Ordinal));
            var card = Assert.Single(RootViewRig.All<ChromeCard>(section));
            Assert.Equal(10, RootViewRig.In(card, host).Top - RootViewRig.In(header, host).Bottom, 0.5);
            Assert.Equal(expected, section.Bounds.Width, 0.5);
            Assert.Equal(expected, card.Bounds.Width, 0.5);
        }, window);
    }

    [Fact]
    [Trait("Req", "UI-WIN-062")] // App/Views/RootView.swift:331-336; UI/Chrome/Chrome.swift:1060-1079 ChromeRow
    public void The_project_mark_row_pads_16_10_11_with_a_body_title_and_a_subheadline_detail()
    {
        OnWelcome(host =>
        {
            var title = RootViewRig.Text(host, "Project mark");
            RootViewRig.AssertStyle(title, 13, FontWeight.Normal); // :1064 .font(.system(.body))
            RootViewRig.AssertForeground(title, RootViewRig.Primary); // :1065
            var detail = RootViewRig.Text(host, "Each project's emoji or symbol before the thread's name, one line per thread"); // :88-92 Icon, the default
            RootViewRig.AssertStyle(detail, 11, FontWeight.Normal); // :1068 .font(.system(.subheadline)), ChatZoom.swift:53
            RootViewRig.AssertForeground(detail, RootViewRig.Secondary); // :1069

            var row = Assert.Single(RootViewRig.All<ChromeRow>(host));
            var card = Assert.Single(RootViewRig.All<ChromeCard>(host), c => RootViewRig.All<ChromeRow>(c).Length == 1);
            var picker = RootViewRig.All<Control>(row).First(c => c.GetType().Name.StartsWith("ChromeVisualPicker", StringComparison.Ordinal));
            var textStack = Assert.IsType<StackPanel>(title.Parent);
            Assert.Equal(2, detail.Bounds.Top - title.Bounds.Bottom, 0.5); // :1063 VStack(spacing: 2)
            Assert.Equal(16, RootViewRig.In(title, card).Left, 0.5); // :1076 .padding(.leading, 16)
            Assert.Equal(10, card.Bounds.Width - RootViewRig.In(picker, card).Right, 0.5); // :1077 Chrome.rowControlTrailingPadding, 10
            Assert.Equal(11, RootViewRig.In(textStack, card).Top, 0.5); // :1078 .padding(.vertical, 11)
            Assert.Equal(Math.Max(textStack.Bounds.Height, picker.Bounds.Height) + 2 * 11, card.Bounds.Height, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-062")] // App/Views/RootView.swift:332-334 the preview symbols; ChromeVisualPicker.swift:22-50 one tile per option
    public void The_project_mark_picker_offers_the_two_styles_with_their_symbols()
    {
        OnWelcome(host =>
        {
            var tiles = RootViewRig.All<ChromeVisualTile>(host);
            Assert.Equal(2, tiles.Length); // ActivityThreadStyle.allCases, AppSettings.swift:72-93
            Assert.Equal(["Icon", "Project name"], tiles.Select(t => Assert.Single(RootViewRig.All<TextBlock>(t)).Text));
            Assert.Equal("list.bullet", Assert.Single(RootViewRig.All<SwiftImage>(tiles[0])).SymbolName); // :333 style == .icon
            Assert.Equal("text.alignleft", Assert.Single(RootViewRig.All<SwiftImage>(tiles[1])).SymbolName);
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-062")] // App/Views/RootView.swift:331-335; AppSettings.swift:277-279 didSet persists activityThreadStyle
    public void Clicking_an_option_assigns_the_style_persists_it_and_changes_the_detail_line()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            Control host = null!;
            harness.Main.OnMain(() =>
            {
                host = RootViewRig.Show(model, new WelcomeView());
                Assert.Equal(ActivityThreadStyle.Icon, model.Settings.ActivityThreadStyle);
                var tile = RootViewRig.All<ChromeVisualTile>(host).Single(t => RootViewRig.AllText(t) == "Project name");
                Assert.Single(RootViewRig.All<Button>(tile)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(ActivityThreadStyle.ProjectName, model.Settings.ActivityThreadStyle);
                Assert.Equal("projectName", harness.Defaults.Object("activityThreadStyle")); // :278 defaults.set(rawValue, forKey:)
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                RootViewRig.Layout(host);
                var text = RootViewRig.AllText(host);
                Assert.Contains("The project's name, with a folder mark, on a line under the thread's title", text, StringComparison.Ordinal); // :90
                Assert.DoesNotContain("Each project's emoji or symbol", text, StringComparison.Ordinal);
                RootViewRig.Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-063")] // App/Views/RootView.swift:339-342, :352-363; UI/Chrome/Chrome.swift:1034-1050
    public void The_providers_section_is_a_secondary_semibold_header_over_one_row_per_provider_in_declaration_order_with_dividers_between()
    {
        OnWelcome(host =>
        {
            var header = RootViewRig.Text(host, "Providers");
            RootViewRig.AssertStyle(header, 13, FontWeight.SemiBold);
            RootViewRig.AssertForeground(header, RootViewRig.Secondary);
            var section = Assert.Single(RootViewRig.All<ChromeSection>(host), s => RootViewRig.AllText(s).StartsWith("Providers", StringComparison.Ordinal));
            Assert.Equal(480, section.Bounds.Width, 0.5); // :342 .frame(maxWidth: 480)
            var checklist = Assert.Single(RootViewRig.All<ProviderChecklist>(section));
            Assert.Equal(10, RootViewRig.In(checklist, host).Top - RootViewRig.In(header, host).Bottom, 0.5);

            // Provider.swift:4-17 case order, :22-37 display names; then OpenRouter, the port's API-key provider declared after
            // Pi (OD-0049, ED-234), which the checklist lists like DeepSeek, Meta and Z.ai.
            string[] names = ["Codex", "Claude", "Cursor", "OpenCode Zen", "OpenCode Go", "Grok", "DeepSeek", "Meta", "Z.ai", "Devin", "Antigravity", "Copilot", "Command Code", "Pi", "OpenRouter", "Local"]; // ED-410
            var rows = RootViewRig.All<ProviderStatusRow>(checklist);
            var dividers = RootViewRig.All<ChromeRowDivider>(checklist);
            Assert.Equal(names.Length, rows.Length);
            Assert.Equal(names, rows.Select(row => RootViewRig.All<TextBlock>(row).First().Text));
            Assert.Equal(names.Length - 1, dividers.Length); // :358 if index > 0 { ChromeRowDivider() }
            for (var index = 0; index < dividers.Length; index++)
            {
                var above = RootViewRig.In(rows[index], host);
                var line = RootViewRig.In(dividers[index], host);
                var below = RootViewRig.In(rows[index + 1], host);
                Assert.Equal(above.Bottom, line.Top, 0.5); // a divider sits between consecutive rows only
                Assert.Equal(line.Bottom, below.Top, 0.5);
            }
        });
    }
}
