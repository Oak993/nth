using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/RequestCards.swift (B2 1fbf81e), :1-66.

[Collection("Headless")]
public sealed class RequestCardsTests(HeadlessSession headless)
{
    // Same shape as FollowUpEditorTests.RuntimeHarness: a real offline ThreadRuntime inside a headless UI test.
    // LoginEnvironment.Load() is awaited once first on a pool thread (no synchronization context), so the harness's
    // own Load().Wait() inside PrepareBootstrap returns at once instead of deadlocking the Avalonia dispatcher.
    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }

    private static ApprovalRequest Sample(
        ApprovalRequest.Kind kind = ApprovalRequest.Kind.Command,
        string title = "echo hello",
        ValueList<ApprovalRequest.Option>? options = null,
        string? detail = null,
        string? reason = null,
        string id = "req-1") =>
        new(id, kind, title,
            options ?? new ValueList<ApprovalRequest.Option>([
                new ApprovalRequest.Option("decline", "Decline", ApprovalRequest.Option.Role.Decline),
                new ApprovalRequest.Option("approve", "Approve", ApprovalRequest.Option.Role.Approve),
            ]),
            detail, reason);

    private static Border Build(ApprovalRequest request, ThreadRuntime runtime)
    {
        var host = new Border { Child = new ApprovalCard(request, runtime) };
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        Settle(window);
        return host;
    }

    private static void Settle(Avalonia.Controls.TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Close(Control host)
    {
        if (Avalonia.Controls.TopLevel.GetTopLevel(host) is AvaloniaWindow window)
        {
            Settle(window);
            window.Close();
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Color Of(SwiftColor color) => color.Resolve(ThemeVariant.Default);

    private static Color? SolidColor(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    [Fact]
    [Trait("Req", "PR-COMPOSER-183")] // App/Views/Composer/RequestCards.swift:8-15
    [Trait("Req", "UI-RUNTIME-005")] // App/Views/Composer/RequestCards.swift:3-65
    [Trait("Req", "UI-COMPOSER-063")] // App/Views/Composer/RequestCards.swift:3-65
    public void The_header_shows_the_requests_symbol_in_warning_beside_its_headline()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(kind: ApprovalRequest.Kind.Command))));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);

                var icon = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single();
                Assert.Equal("terminal", icon.SymbolName); // :10, Core/Models/Requests.swift Symbol for .command
                Assert.Equal(Of(Chrome.Warning), SolidColor(icon.Foreground)); // :11

                var headline = LogicalQuery.SelfAndDescendants<TextBlock>(host)
                    .First(block => block.Text == "Run this command?"); // :12, Requests.swift Headline for .command
                Assert.Equal(13, headline.FontSize); // :13 .font(.headline), ED-059

                Close(host);
            });
        });
    }

    // R-TEST-6: an expectation resting on open oracle item O-64 cannot also carry PR-COMPOSER-183 / UI-RUNTIME-005 /
    // UI-COMPOSER-063's Req traits, so the headline's weight is pinned here, apart from the structural facts above.
    // Mirrors TourViewTests.The_empty_state_titles_plain_headline_stays_bold.
    [Fact]
    [Trait("Decision", "ED-186")]
    [Trait("Oracle", "O-64")]
    public void The_headers_plain_headline_stays_bold()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(kind: ApprovalRequest.Kind.Command))));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);

                var headline = LogicalQuery.SelfAndDescendants<TextBlock>(host)
                    .First(block => block.Text == "Run this command?"); // :12, Requests.swift Headline for .command
                Assert.Equal(FontWeight.Bold, headline.FontWeight); // ED-186: plain .headline is bold, not chat-semibold; O-64 unresolved

                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-183")] // App/Views/Composer/RequestCards.swift:62-64
    [Trait("Req", "UI-COMPOSER-063")] // App/Views/Composer/RequestCards.swift:62-64
    public void The_card_is_a_20_radius_glass_tinted_with_warning_at_0_1_padded_16()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample())));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);
                var card = LogicalQuery.SelfAndDescendants<Border>(host).First(border => border.CornerRadius == new CornerRadius(20));
                Assert.Equal(new Thickness(16), card.Padding); // :62 .padding(16)
                var effectOrNull = SwiftGlass.GetEffect(card);
                Assert.NotNull(effectOrNull);
                var effect = effectOrNull!;
                Assert.Equal(GlassShapeKind.RoundedRectangle, effect.Shape.Kind); // :64 in: .rect(cornerRadius: 20, style: .continuous)
                Assert.Equal(20, effect.Shape.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style);
                Assert.Equal(0.1, effect.TintOpacity); // :64 .tint(Chrome.warning.opacity(0.1))
                Assert.Equal(Of(Chrome.Warning), Of(effect.TintColor!));
                Assert.Equal(Of(Chrome.Warning.Opacity(0.1)), SolidColor(card.Background));

                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-184")] // App/Views/Composer/RequestCards.swift:17-24
    [Trait("Req", "UI-RUNTIME-008")] // App/Views/Composer/RequestCards.swift:17-24
    public void A_command_approval_shows_the_command_in_a_monospaced_selectable_block_capped_at_8_lines()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(kind: ApprovalRequest.Kind.Command, title: "echo hello"))));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);
                var text = LogicalQuery.SelfAndDescendants<SelectableTextBlock>(host).Single(block => block.Text == "echo hello"); // :18, :20 textSelection(.enabled)
                Assert.Equal(8, text.MaxLines); // :21
                Assert.Equal(12, text.FontSize); // :19 .callout, ED-059
                Assert.Contains("Cascadia Mono", text.FontFamily.Name, System.StringComparison.Ordinal); // :19 design: .monospaced, OD-0009

                var box = Assert.IsType<Border>(text.Parent);
                Assert.Equal(new Thickness(10), box.Padding); // :22
                Assert.Equal(new CornerRadius(10), box.CornerRadius); // :24
                Assert.Equal(Of(SwiftColor.Quaternary.Opacity(0.5)), SolidColor(box.Background)); // :24 .quaternary.opacity(0.5)

                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-185")] // App/Views/Composer/RequestCards.swift:25-33
    [Trait("Req", "UI-RUNTIME-007")] // App/Views/Composer/RequestCards.swift:25-28
    [Trait("Req", "UI-COMPOSER-064")] // App/Views/Composer/RequestCards.swift:25-28
    public void A_plan_approval_shows_the_fixed_sentence_and_every_other_kind_shows_the_requests_own_title()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(id: "plan-1", kind: ApprovalRequest.Kind.Plan, title: "Build the plan")));
                h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(id: "tool-1", kind: ApprovalRequest.Kind.Tool, title: "Run the tool")));
            });
            var (planRequest, toolRequest) = h.Main.OnMain(() =>
                (h.Runtime.Approvals.Single(r => r.Id == "plan-1"), h.Runtime.Approvals.Single(r => r.Id == "tool-1")));
            h.Main.OnMain(() =>
            {
                var planHost = Build(planRequest, h.Runtime);
                var planText = LogicalQuery.SelfAndDescendants<TextBlock>(planHost)
                    .First(block => block.Text == "Approve the plan above to let the agent start building."); // :26 verbatim
                Assert.IsNotType<SelectableTextBlock>(planText); // :26-28: unlike every other kind's body, not selectable
                Assert.Equal(12, planText.FontSize); // :27 .callout
                Assert.Equal(Of(Chrome.SecondaryText), SolidColor(planText.Foreground)); // :28
                Assert.DoesNotContain("Build the plan", LogicalQuery.SelfAndDescendants<TextBlock>(planHost).Select(block => block.Text));
                Close(planHost);

                var toolHost = Build(toolRequest, h.Runtime);
                var toolText = LogicalQuery.SelfAndDescendants<SelectableTextBlock>(toolHost).Single(block => block.Text == "Run the tool"); // :29-32
                Assert.Equal(12, toolText.FontSize); // :31 .callout
                Close(toolHost);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-186")] // App/Views/Composer/RequestCards.swift:34-44
    public void Detail_is_capped_at_3_lines_and_reason_is_shown_in_full_both_in_secondaryText()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(
                Sample(detail: "line one", reason: "because the tests said so"))));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);
                var detail = LogicalQuery.SelfAndDescendants<TextBlock>(host).First(block => block.Text == "line one");
                Assert.Equal(3, detail.MaxLines); // :38 .lineLimit(3)
                Assert.Equal(10, detail.FontSize); // :36 .caption
                Assert.Equal(Of(Chrome.SecondaryText), SolidColor(detail.Foreground)); // :37

                var reason = LogicalQuery.SelfAndDescendants<TextBlock>(host).First(block => block.Text == "because the tests said so");
                Assert.Equal(12, reason.FontSize); // :42 .callout
                Assert.Equal(Of(Chrome.SecondaryText), SolidColor(reason.Foreground)); // :43

                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-186")] // App/Views/Composer/RequestCards.swift:34, :40
    public void Empty_detail_and_absent_reason_render_nothing_at_all()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(detail: "", reason: null))));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);
                var card = LogicalQuery.SelfAndDescendants<Border>(host).First(border => border.CornerRadius == new CornerRadius(20));
                var stack = Assert.IsType<StackPanel>(card.Child);
                Assert.Equal(3, stack.Children.Count); // header, body, options row only: :34 and :40 both guard on !isEmpty
                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-187")] // App/Views/Composer/RequestCards.swift:45-59
    [Trait("Req", "UI-COMPOSER-065")] // App/Views/Composer/RequestCards.swift:45-59
    [Trait("Req", "UI-RUNTIME-006")] // App/Views/Composer/RequestCards.swift:45-59
    public void Options_render_reversed_approve_prominent_others_plain_glass()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var options = new ValueList<ApprovalRequest.Option>([
                new ApprovalRequest.Option("decline", "Decline", ApprovalRequest.Option.Role.Decline),
                new ApprovalRequest.Option("approve", "Approve", ApprovalRequest.Option.Role.Approve),
                new ApprovalRequest.Option("always", "Always allow", ApprovalRequest.Option.Role.ApproveAlways),
            ]);
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(options: options))));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);
                var buttons = LogicalQuery.SelfAndDescendants<Button>(host).ToArray();
                var titles = buttons.Select(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Single().Text ?? "").ToArray();
                Assert.Equal(["Always allow", "Approve", "Decline"], titles); // :50 request.options.reversed()

                foreach (var button in buttons)
                {
                    var frame = Assert.IsType<Border>(button.Content);
                    var effectOrNull = SwiftGlass.GetEffect(frame);
                    Assert.NotNull(effectOrNull);
                    var effect = effectOrNull!;
                    Assert.True(effect.IsInteractive);
                    var label = LogicalQuery.SelfAndDescendants<TextBlock>(button).Single().Text;
                    if (label == "Approve") // :51-53 .buttonStyle(.glassProminent)
                    {
                        Assert.Equal(Of(Chrome.Accent), Of(effect.TintColor!));
                        Assert.Equal(0.3, effect.TintOpacity);
                    }
                    else // :55-57 .buttonStyle(.glass): no tint
                    {
                        Assert.Null(effect.TintColor);
                    }
                }

                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-187")] // App/Views/Composer/RequestCards.swift:47-49, :54
    [Trait("Req", "UI-RUNTIME-006")] // App/Views/Composer/RequestCards.swift:47-49, :54
    public void Only_the_top_cards_approve_option_claims_Ctrl_Return_and_it_follows_the_stack()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(id: "first")));
                h.Runtime.Rehearse(new ProviderEvent.Approval(Sample(id: "second")));
            });
            var (first, second) = h.Main.OnMain(() =>
                (h.Runtime.Approvals.Single(r => r.Id == "first"), h.Runtime.Approvals.Single(r => r.Id == "second")));

            var (window, approveA, approveB) = h.Main.OnMain(() =>
            {
                var hostA = new Border { Child = new ApprovalCard(first, h.Runtime) };
                var hostB = new Border { Child = new ApprovalCard(second, h.Runtime) };
                var stackHost = new StackPanel();
                stackHost.Children.Add(hostA);
                stackHost.Children.Add(hostB);
                var win = new AvaloniaWindow { Content = stackHost };
                win.Show();
                Settle(win);

                Button ApproveOf(Control host) => LogicalQuery.SelfAndDescendants<Button>(host)
                    .Single(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(block => block.Text == "Approve"));

                return (win, ApproveOf(hostA), ApproveOf(hostB));
            });

            var chord = new KeyGesture(Key.Enter, KeyModifiers.Control); // :49 .command -> Ctrl (ED-099 (3))
            h.Main.OnMain(() =>
            {
                Assert.Equal(chord, approveA.HotKey); // :49 "first" is runtime.approvals.first
                Assert.Null(approveB.HotKey); // :47-48 two cards both claiming it left neither reliably approving
            });

            // A separate OnMain, not appended to the block above: Track's Schedule posts its Update onto the same
            // manual main loop (App/Views/RootView.cs and UI/Port/ViewSupport.cs Tracked.Schedule), which only runs
            // once the loop's current work item returns control to RunUntilIdle's own while loop.
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.RequestResolved("first")));

            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Null(approveA.HotKey);
                Assert.Equal(chord, approveB.HotKey); // "second" is now the head of runtime.approvals
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-187")] // App/Views/Composer/RequestCards.swift:52-53, :56-57
    public void Clicking_an_option_resolves_the_request_with_that_option()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Approval(Sample())));
            var request = h.Main.OnMain(() => h.Runtime.Approvals.Single());
            h.Main.OnMain(() =>
            {
                var host = Build(request, h.Runtime);
                var decline = LogicalQuery.SelfAndDescendants<Button>(host)
                    .Single(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(block => block.Text == "Decline"));
                decline.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(h.Runtime.Approvals); // :56 runtime.resolve(request, option:) removes it (ThreadRuntime.Resolve)

                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-186")] // App/Views/Composer/RequestCards.swift:9-14 HStack(spacing: 8) { icon; headline; Spacer() }: the headline wraps once icon, 8, headline and 8 no longer fit
    public void The_header_headline_wraps_when_both_hstack_gaps_no_longer_fit()
    {
        headless.OnUiThread(() =>
        {
            var card = new ApprovalCard(Sample(), null!);
            card.Measure(Size.Infinity);
            var headline = LogicalQuery.SelfAndDescendants<TextBlock>(card).First(block => block.FontWeight == FontWeight.Bold);
            var natural = headline.DesiredSize.Width;
            var icon = LogicalQuery.SelfAndDescendants<Control>(card).First(control => control.Parent is Grid grid && Grid.GetColumn(control) == 0 && grid.Children.Contains(headline));
            var width = 32 + icon.DesiredSize.Width + 8 + natural + 4; // 4 points short of icon + 8 + headline + 8
            card.Measure(new Size(width, double.PositiveInfinity));
            Assert.True(headline.TextLayout.TextLines.Count > 1, "the headline stayed on one line at width " + width);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-186")] // App/Views/Composer/RequestCards.swift:35-38 .lineLimit(3) truncates with a trailing ellipsis
    public void A_detail_longer_than_three_lines_is_cut_with_an_ellipsis()
    {
        headless.OnUiThread(() =>
        {
            var card = new ApprovalCard(Sample(detail: "detail"), null!);
            var detail = LogicalQuery.SelfAndDescendants<TextBlock>(card).First(block => block.Text == "detail");
            Assert.Equal(TextTrimming.CharacterEllipsis, detail.TextTrimming);
        });
    }
}
