using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Port.MultiAccount.Usage;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Views.MultiAccount.Cards;
using Marrow.App.Views.Settings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Cards;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Usage;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.MultiAccount;

// T26 REVIEW ONLY. Expected values come from the LOCKED HTML's final cascade or the cited OD,
// never from the implementation. These regressions deliberately stay red until a separate fix.
// Each data row is one finding in porting/first-release/reviews/ma-ui-review.md.
[Collection("Headless")]
public sealed class MaUiReviewTests(HeadlessSession headless)
{
    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    [InlineData("MAUI-001")] // HTML 2687,2715; checklist 4 Link; OD-0115 link=A / OD-0120
    [InlineData("MAUI-003")] // HTML 2734; checklist 4 row menu
    // MAUI-004 withdrawn by T15-review-rulings.md #1 / ED-500: plan C7; HTML2693-2697/2706.
    [InlineData("MAUI-005")] // HTML 215: apop padding 6
    [InlineData("MAUI-006")] // HTML 1347 (supersedes 826): auto height, min-height 68
    [InlineData("MAUI-007")] // HTML 2686,2716: account monogram
    [InlineData("MAUI-008")] // HTML 147: grip 16 x 24
    [InlineData("MAUI-009")] // HTML 1447: number capsule, height 18, radius 9
    [InlineData("MAUI-010")] // HTML 241: 7px green active dot with 3px halo
    [InlineData("MAUI-011")] // HTML 1361 supersedes 240: percent uses var(--app), not mono
    [InlineData("MAUI-012")] // HTML 1362: disabled content opacity .42
    [InlineData("MAUI-013")] // HTML 230: pinned row background --pacSoft
    [InlineData("MAUI-014")] // HTML 2692,1303: config chevron
    [InlineData("MAUI-015")] // HTML 247: footer padding 8/8/6 and top divider
    [InlineData("MAUI-016")] // HTML 2020-2025: displaced neighbours glide during drag
    [InlineData("MAUI-017")] // HTML 2008: 5px drag threshold
    [InlineData("MAUI-019")] // HTML 2049-2050: unmodified arrows navigate rows
    [InlineData("MAUI-020")] // HTML 2052: Shift+F10 opens row context menu
    [InlineData("MAUI-050")] // HTML 125 + 3: .6s cubic-bezier(.32,.72,0,1)
    [InlineData("MAUI-057")] // HTML 229: inset 2px --pac keyboard focus ring
    [InlineData("MAUI-058")] // HTML 228 + 3: hover --hov rgba(0,0,0,.05)
    [InlineData("MAUI-059")] // HTML 3-4,218,234,239: exact primary/secondary/faint text palette
    [InlineData("MAUI-062")] // HTML 2018: carried row scale 1.02
    [InlineData("MAUI-063")] // HTML 2016-2017: edge rubber band capped at .35 of pitch
    [InlineData("MAUI-064")] // HTML 1983,2029-2045: drop settles with SP.settle
    public async Task Popover_pins_locked_design(string finding)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max");
        var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Usage[a.Id] = new(.5, null, .43, null, false, false);
        if (finding == "MAUI-012") rig.Accounts.SetEnabled(a.Id, false);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        if (finding == "MAUI-013") await port.PinAsync(a.Id);
        using var model = new AccountPopoverModel(port, rig.Accounts);
        AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model, new(rig.Accounts, _ => [])), h =>
        {
            var row = view!.Rows[0]; var pin = h.Find<Button>("popover-pin-" + a.Id.ToString("N"));
            switch (finding)
            {
                case "MAUI-001":
                    Assert.Contains(h.All().OfType<Button>(), c => AutomationProperties.GetName(c) == "Link Claude Max 2 to check this thread’s work"); break;
                case "MAUI-003": Assert.Contains(row.Menu, e => e.Label == "Keep this thread on it"); break;
                case "MAUI-005": Assert.Equal(new Thickness(6), Assert.IsType<StackPanel>(view.Child).Margin); break;
                case "MAUI-006":
                    Assert.True(double.IsNaN(row.Height), "HTML 1347 requires auto row height."); Assert.Equal(68, row.MinHeight); break;
                case "MAUI-007": Assert.Contains("M1", h.Words(row)); break;
                case "MAUI-008": Assert.Equal(new Size(16, 24), h.Find<Control>("popover-grip-" + a.Id.ToString("N")).Bounds.Size); break;
                case "MAUI-009":
                    var number = Text(row, "1"); Assert.Equal(18, number.GetVisualParent()!.Bounds.Height); break;
                case "MAUI-010":
                    Assert.Contains(row.GetVisualDescendants().OfType<Border>(), c => c.Width == 7 && c.Height == 7 && c.CornerRadius.TopLeft == 3.5 && c.Background is ISolidColorBrush brush && brush.Color == Color.Parse("#0F7B3F")); break;
                case "MAUI-011": Assert.Equal(Text(row, "Claude Max 1").FontFamily.Name, Text(row, "43%").FontFamily.Name); break;
                case "MAUI-012": Assert.Equal(.42, Field<StackPanel>(row, "main").Opacity); break;
                case "MAUI-013": Assert.Equal(Color.Parse("#1A0067C0"), BrushColor(pin.Background)); break;
                case "MAUI-014":
                    var config = h.Find<Button>("popover-config-" + a.Id.ToString("N"));
                    Assert.Contains(config.GetVisualDescendants().OfType<SwiftImage>(), c => c.SymbolName == "chevron.down"); break;
                case "MAUI-015":
                    var footer = Text(view, model.Footer); Assert.IsType<Border>(footer.GetVisualParent()); break;
                case "MAUI-016":
                case "MAUI-017":
                case "MAUI-062":
                case "MAUI-063":
                case "MAUI-064":
                    var grip = h.Find<Control>("popover-grip-" + a.Id.ToString("N"));
                    var point = grip.TranslatePoint(new Point(11, 11), h.Window)!.Value;
                    h.Window.MouseDown(point, MouseButton.Left);
                    var released = false;
                    try
                    {
                        var distance = finding == "MAUI-017" ? 4 : finding == "MAUI-063" ? -200 : 80;
                        h.Window.MouseMove(point + new Vector(0, distance)); h.Step(100);
                        if (finding == "MAUI-016") Assert.NotNull(view.Rows[1].RenderTransform);
                        if (finding == "MAUI-017") Assert.Null(row.RenderTransform);
                        if (finding == "MAUI-062") Assert.Equal(1.02, row.RenderTransform!.Value.M11);
                        if (finding == "MAUI-063") Assert.InRange(row.RenderTransform!.Value.M32, -(row.Bounds.Height + 2) * .35, 0);
                        if (finding == "MAUI-064")
                        {
                            h.Window.MouseUp(point + new Vector(0, distance), MouseButton.Left); released = true; h.Step(16);
                            Assert.NotNull(row.RenderTransform);
                        }
                    }
                    finally { if (!released) h.Window.MouseUp(point, MouseButton.Left); }
                    break;
                case "MAUI-019":
                    pin.Focus(); pin.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down }); h.Settle();
                    Assert.True(h.Find<Button>("popover-pin-" + b.Id.ToString("N")).IsFocused, "HTML 2050 moves focus to the next row."); break;
                case "MAUI-020":
                    SettingsProviderPopupLayers.Install(h.Window); pin.Focus();
                    pin.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F10, KeyModifiers = KeyModifiers.Shift }); h.Settle();
                    Assert.Contains("Move to top", h.Words()); break;
                case "MAUI-050":
                    h.Step(300); Assert.Equal(.5 * CssEase(.5, .32, .72, 0, 1), Field<AnimatedScalar>(row.FiveHour, "shown").Value, 6); break;
                case "MAUI-057":
                    pin.Focus(NavigationMethod.Tab); h.Settle(); Assert.True(pin.IsFocused);
                    Assert.Contains(pin.GetVisualDescendants().OfType<Border>(), c => c.BorderThickness == new Thickness(2) && c.BorderBrush is ISolidColorBrush ink && ink.Color == Color.Parse("#0067C0")); break;
                case "MAUI-058":
                    pin.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, pin, new Avalonia.Input.Pointer(58, PointerType.Mouse, true), h.Window, default, 0, new PointerPointProperties(), KeyModifiers.None));
                    ((IPseudoClasses)pin.Classes).Set(":pointerover", true); h.Settle();
                    Assert.Equal(Color.Parse("#0D000000"), BrushColor(pin.Background)); break;
                case "MAUI-059":
                    Assert.Equal(Color.Parse("#A81C1C1E"), BrushColor(Text(row, "5 h").Foreground));
                    Assert.Equal(Color.Parse("#1C1C1E"), BrushColor(Text(row, "Claude Max 1").Foreground));
                    Assert.Equal(Color.Parse("#6B1C1C1E"), BrushColor(Text(view!, model.OrderHint).Foreground));
                    h.Window.RequestedThemeVariant = ThemeVariant.Dark; h.Settle();
                    Assert.Equal(Color.Parse("#A8ECECEC"), BrushColor(Text(row, "5 h").Foreground));
                    Assert.Equal(Color.Parse("#ECECEC"), BrushColor(Text(row, "Claude Max 1").Foreground));
                    Assert.Equal(Color.Parse("#6BECECEC"), BrushColor(Text(view!, model.OrderHint).Foreground)); break;
                default: throw new ArgumentOutOfRangeException(nameof(finding));
            }
        }, motion: finding is "MAUI-016" or "MAUI-017" or "MAUI-050" or "MAUI-062" or "MAUI-063" or "MAUI-064");
    }

    [Fact, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    public void MAUI_002_Ask_others_B_is_an_accessible_composer_action() => headless.OnUiThread(() =>
    {
        // HTML 4804-4811,4855-4871; checklist 5; OD-0115 opinions=B / OD-0120.
        // RR-49 / ED-NEW-ma-gaps-5 (lead fixture ruling, ED-NEW-ma-wire-lead-1): opinions go only to verified heads, so the action is
        // proved on a Claude thread (a verified head); the review's Codex fixture now proves the fail-closed side instead.
        foreach (var (service, offered) in new[] { (ProviderKind.Claude, true), (ProviderKind.Codex, false) })
        {
            var accounts = new AccountsRig(); accounts.Connect(service); accounts.ConnectAnother(service);
            var port = new FakeThreadAccountPort(accounts, service);
            using var ports = ThreadPortSeams.Offer.Override(new((_, _) => port, new FakeCardPort(), new FakeLinkPort(), new FakeOpinionsPort()));
            using var composer = ComposerChipRig.Start(provider: service);
            composer.OnMain(() =>
            {
                var buttons = composer.View.GetVisualDescendants().OfType<Button>().ToList();
                if (offered) Assert.Contains(buttons, b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Ask others for this message");
                else Assert.DoesNotContain(buttons, b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Ask others for this message");
            });
        }
    });

    [Fact, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    public void MAUI_053_Loaded_Codex_history_shows_the_in_sync_composer_chip() => headless.OnUiThread(() =>
    {
        // HTML 2481,324; checklist 3 "During Handshake"; OD-0122 same tick as Claude.
        var accounts = new AccountsRig(); var a = accounts.Connect(ProviderKind.Codex); var b = accounts.ConnectAnother(ProviderKind.Codex);
        var port = new FakeThreadAccountPort(accounts, ProviderKind.Codex); var cards = new FakeCardPort();
        var capsuleModel = new ComposerCapsuleModel(accounts.Accounts, accounts.Pills, accounts.IsConnected, _ => null);
        using var capsuleOffer = ComposerCapsuleSeams.Offer.Override(_ => new ComposerCapsuleSlot(capsuleModel,
            new CapsuleStyleSetting(accounts.Settings), () => new CapsuleSubject(ProviderKind.Codex, a.Id, null, false, null)));
        using var ports = ThreadPortSeams.Offer.Override(new((_, _) => port, cards, new FakeLinkPort(), new FakeOpinionsPort()));
        using var composer = ComposerChipRig.Start();
        composer.OnMain(() => Assert.Contains(composer.View.GetVisualDescendants().OfType<AccountCapsule>(), c => c.IsEffectivelyVisible));
        cards.Raise(new(composer.ThreadID, HandshakeEventKind.InSyncReady, HandshakePhase.Linking, HandshakePhase.InSync,
            a.Id.ToString(), b.Id.ToString(), HandshakeReason.NearLimit, composer.Time.GetUtcNow()));
        composer.Pump();
        composer.OnMain(() => Assert.Contains(composer.View.GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == "In sync"));
    });

    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    [InlineData("MAUI-021")] // HTML 834: cfgp width 330
    [InlineData("MAUI-022")] // HTML 1368: hsp width 248
    [InlineData("MAUI-023")] // HTML 2642: reset hidden when no own override
    [InlineData("MAUI-024")] // HTML 2635: account-specific slider aria
    [InlineData("MAUI-025")] // HTML 2613-2617: arrows, Home and End
    [InlineData("MAUI-026")] // HTML 1071-1082: compact 18px control / 12px track / 17px knob
    public void Dialogs_pin_locked_design(string finding)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); rig.ConnectAnother(ProviderKind.Claude);
        AccountHandshakePanel? handshake = null;
        MultiAccountHarness.Run(headless, rig, () => finding == "MAUI-021"
            ? new AccountDefaultsPanel(rig.Accounts, a.Id, "Claude Max 1", [])
            : handshake = new AccountHandshakePanel(rig.Accounts, a.Id, "Claude Max 1", ProviderKind.Claude), h =>
        {
            switch (finding)
            {
                case "MAUI-021": Assert.Equal(330, h.Page.Width); break;
                case "MAUI-022": Assert.Equal(248, h.Page.Width); break;
                case "MAUI-023": Assert.False(h.FindIncludingHidden<Button>("account-handshake-default").IsEffectivelyVisible); break;
                case "MAUI-024": Assert.Equal("Handshake point for this account", AutomationProperties.GetName(handshake!.Slider.Slider)); break;
                case "MAUI-025":
                    handshake!.Slider.Slider.Focus(); handshake.Slider.Slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End }); h.Settle();
                    Assert.Equal(99, rig.Accounts.Get(a.Id).Defaults.HandshakePoint?.Percentage); break;
                case "MAUI-026": Assert.Equal(18, handshake!.Slider.Slider.Height); break;
                default: throw new ArgumentOutOfRangeException(nameof(finding));
            }
        });
    }

    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    [InlineData("MAUI-027")] // HTML 436: Usage header 12px
    [InlineData("MAUI-028")] // HTML 447: current account 15px
    [InlineData("MAUI-029")] // HTML 4343: no-5h state retains "5-hour window"
    [InlineData("MAUI-030")] // HTML 461,463: incoming page starts at 100% width
    [InlineData("MAUI-031")] // HTML 460: .45s --ez slide
    [InlineData("MAUI-032")] // HTML 469: Reduce Motion .3s CSS ease opacity
    public void Usage_B_pins_locked_design(string finding)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Usage[a.Id] = new(.2, null, .43, null, finding == "MAUI-029", false);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new UsagePanelModel(port, [port]);
        MultiAccountUsageView? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new MultiAccountUsageView(model), h =>
        {
            switch (finding)
            {
                case "MAUI-027": Assert.Equal(12, Text(view!, "Usage").FontSize); break;
                case "MAUI-028": Assert.Equal(15, Text(view!, "Claude Max 1").FontSize); break;
                case "MAUI-029": Assert.Contains("5-hour window", h.Words()); break;
                default:
                    h.Press("usage-all-accounts"); var page = Field<Control>(view!, "page");
                    if (finding == "MAUI-030") Assert.Equal(view!.Bounds.Width, Assert.IsType<TranslateTransform>(page.RenderTransform).X, 6);
                    if (finding == "MAUI-031")
                    {
                        h.Step(175); Assert.Equal(view!.Bounds.Width * (1 - CssEase(175.0 / 450, .32, .72, 0, 1)), Assert.IsType<TranslateTransform>(page.RenderTransform).X, 6);
                    }
                    if (finding == "MAUI-032")
                    {
                        h.Step(150); Assert.Equal(CssEase(.5, .25, .1, .25, 1), page.Opacity, 6);
                    }
                    break;
            }
        }, motion: finding is "MAUI-030" or "MAUI-031" or "MAUI-032", reduceMotion: finding == "MAUI-032");
    }

    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    // MAUI-033 withdrawn by T15-review-rulings.md #2 / ED-501: plan C4; OD-0116/0119.
    [InlineData("MAUI-034")] // HTML 4225: bold Finish now and Stop
    [InlineData("MAUI-035")] // HTML 380: 1px divider
    [InlineData("MAUI-036")] // HTML 378 + dark token 4: --pacInk #06121C
    [InlineData("MAUI-037")] // HTML 371 + light token 3: --hot #B25E00
    [InlineData("MAUI-038")] // HTML 346: note rounded background
    [InlineData("MAUI-065")] // HTML 375-378 + 3: the primary action has a painted --pac face
    public void Cards_pin_locked_design(string finding)
    {
        var f = FirstTimeCardTests.Make(finding == "MAUI-035", finding == "MAUI-034" ? DecisionKind.MidReply : DecisionKind.OnLimit);
        var model = new LimitCardModel(f.State, f.Accounts.Accounts, f.Cards, TimeZoneInfo.Utc);
        LimitCardView? view = null;
        MultiAccountHarness.Run(headless, f.Rig, () => finding == "MAUI-038"
            ? new LimitCardNote("Continued on Claude Max 2 · Marrow’s summary · What was handed over", null)
            : view = new LimitCardView(model, ["Claude Max 1", "Claude Max 2"]), h =>
        {
            switch (finding)
            {
                case "MAUI-034":
                    var explanation = Text(view!, model.Explanation!);
                    Assert.Contains(explanation.Inlines?.OfType<Run>() ?? [], r => r.Text == "Finish now" && r.FontWeight == FontWeight.SemiBold); break;
                case "MAUI-035":
                    Assert.Contains(view!.GetVisualDescendants().OfType<Border>(), b => b.BorderThickness.Top == 1 && b.BorderThickness.Bottom == 0); break;
                case "MAUI-036":
                    h.Window.RequestedThemeVariant = ThemeVariant.Dark; h.Settle();
                    Assert.Equal(Color.Parse("#06121C"), BrushColor(Assert.IsType<TextBlock>(view!.Actions[0].Content).Foreground)); break;
                case "MAUI-037":
                    Assert.Equal(Color.Parse("#B25E00"), BrushColor(view!.GetVisualDescendants().OfType<SwiftImage>().Single(i => i.SymbolName == "gauge.with.needle").Foreground)); break;
                case "MAUI-038": Assert.Contains(h.Page.GetVisualDescendants().OfType<Border>(), b => b.CornerRadius.TopLeft > 0 && b.Background is not null); break;
                case "MAUI-065":
                    Assert.Contains(view!.Actions[0].GetVisualDescendants().OfType<Control>(), c =>
                        (c is Border { Background: ISolidColorBrush b } && b.Color == Color.Parse("#0067C0")) ||
                        (c is ContentPresenter { Background: ISolidColorBrush p } && p.Color == Color.Parse("#0067C0"))); break;
                default: throw new ArgumentOutOfRangeException(nameof(finding));
            }
        });
    }

    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    [InlineData("MAUI-018")] // HTML 467: every movement is a crossfade under Reduce Motion
    [InlineData("MAUI-039")] // HTML 142: Settings account row height 46
    [InlineData("MAUI-040")] // HTML 1459: off words .45, switch unchanged
    [InlineData("MAUI-041")] // OD-0119: equal-to-default override has no note
    [InlineData("MAUI-042")] // HTML 3846: switch aria "Use <name>"
    [InlineData("MAUI-043")] // HTML 3867: full counter --hot
    [InlineData("MAUI-061")] // HTML 1993: dragList accepts the primary button only
    public void Settings_accounts_pin_locked_design(string finding)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); rig.ConnectAnother(ProviderKind.Claude, "Max");
        if (finding == "MAUI-040") rig.Accounts.SetEnabled(a.Id, false);
        if (finding == "MAUI-041") rig.Accounts.SetDefaults(a.Id, rig.Accounts.Get(a.Id).Defaults with { HandshakePoint = rig.Accounts.GeneralHandshakePoint });
        AccountRowControl? row = null;
        MultiAccountHarness.Run(headless, rig, () => row = new AccountRowControl(rig.Model, rig.Model.Blocks.Single(b => b.Service == ProviderKind.Claude).Rows[0], new ModelPulse(_ => { }), (_, _, _) => { }), h =>
        {
            switch (finding)
            {
                case "MAUI-018":
                case "MAUI-061":
                    var grip = h.Find<Control>("account-grip-" + a.Id.ToString("N")); var p = grip.TranslatePoint(new Point(11, 11), h.Window)!.Value;
                    var mouseButton = finding == "MAUI-061" ? MouseButton.Right : MouseButton.Left;
                    h.Window.MouseDown(p, mouseButton);
                    try { h.Window.MouseMove(p + new Vector(0, 40)); Assert.Null(row!.RenderTransform); }
                    finally { h.Window.MouseUp(p, mouseButton); }
                    break;
                case "MAUI-039": Assert.Equal(46, Assert.IsType<Grid>(Assert.IsType<Border>(row!.Child).Child).Height); break;
                case "MAUI-040":
                    var text = Text(row!, "Claude Max 1"); Assert.Equal(.45, EffectiveOpacity(text, row!), 6); break;
                case "MAUI-041": Assert.DoesNotContain("Handshake", row!.SubtitleShown, StringComparison.Ordinal); break;
                case "MAUI-042": Assert.Equal("Use Claude Max 1", AutomationProperties.GetName(h.Find<SettingsSwitch>("account-switch-" + a.Id.ToString("N")))); break;
                case "MAUI-043":
                    row!.Pick("rename"); h.Settle(); h.Find<TextBox>("account-rename-" + a.Id.ToString("N")).Text = new string('A', 24); h.Settle();
                    Assert.Equal(Color.Parse("#B25E00"), BrushColor(Text(row, "24/24").Foreground)); break;
                default: throw new ArgumentOutOfRangeException(nameof(finding));
            }
        }, reduceMotion: finding == "MAUI-018");
    }

    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    [InlineData("MAUI-044")] // HTML 4457,4472: account mono, not provider icon
    [InlineData("MAUI-045")] // HTML 1791,4457,4473: Fast bolt before model + aria Fast mode
    [InlineData("MAUI-046")] // HTML 4462-4465: floating defaults dialog with Same-as-account switch
    [InlineData("MAUI-047")] // HTML 4466: blocked removal gives 300ms feedback
    [InlineData("MAUI-048")] // HTML 1468: off step words opacity .45
    [InlineData("MAUI-055")] // HTML 2622: locked slider explains the control at the top
    [InlineData("MAUI-060")] // HTML 467,2024: pill reorder uses a crossfade under Reduce Motion
    public void Pill_editor_pins_locked_design(string finding)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Accounts.SetDefaults(a.Id, rig.Accounts.Get(a.Id).Defaults with { Model = "opus", Effort = "high", FastMode = true });
        if (finding == "MAUI-048") rig.Accounts.SetEnabled(a.Id, false);
        var pill = rig.MakePill("Work", a, b);
        if (finding == "MAUI-055") rig.Pills.SetHandshakeMode(pill.Id, PillHandshakeMode.SameForEvery, Marrow.Core.Accounts.HandshakePoint.Smart);
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<PillEditorView>(context => new PillEditorView(context, context.Editor(pill.Id), () => { })), h =>
        {
            // T15-review-rulings.md #4 / ED-503: headless floating popup needs its overlay.
            if (finding == "MAUI-046") { SettingsProviderPopupLayers.Install(h.Window); h.Settle(); }
            var view = ((MultiAccountHost<PillEditorView>)h.Page).View!; var row = view.StepList[0];
            switch (finding)
            {
                case "MAUI-044": Assert.Contains("M1", h.Words(row)); break;
                case "MAUI-045":
                    var content = Assert.IsType<StackPanel>(row.ConfigButton.Content);
                    Assert.IsType<SwiftImage>(content.Children[0]); break;
                case "MAUI-046":
                    h.Press("step-config-" + a.Id.ToString("D")); Assert.False(row.SettingsOpen, "HTML 4462 opens a floating dialog; it does not expand the row."); break;
                case "MAUI-047":
                    h.Press("step-remove-" + a.Id.ToString("D")); h.Step(100); Assert.NotNull(row.RenderTransform); break;
                case "MAUI-048": Assert.Equal(.45, EffectiveOpacity(Text(row, "Claude Max 1"), row), 6); break;
                case "MAUI-055": Assert.Equal("Same for every step: set it with the slider at the top", ToolTip.GetTip(row.Slider!)); break;
                case "MAUI-060":
                    var grip = h.Find<Control>("step-grip-" + a.Id.ToString("D")); var p = grip.TranslatePoint(new Point(11, 11), h.Window)!.Value;
                    h.Window.MouseDown(p, MouseButton.Left);
                    try { h.Window.MouseMove(p + new Vector(0, 40)); Assert.Null(row.RenderTransform); }
                    finally { h.Window.MouseUp(p, MouseButton.Left); }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(finding));
            }
        }, motion: finding == "MAUI-047", reduceMotion: finding == "MAUI-060", models: _ => [new("opus", "Opus 5.5", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "high", fastTier: "fast")]);
    }

    // MAUI-049 withdrawn by T15-review-rulings.md #2 / ED-501.
    // HTML3295-3303/3346-3348/3078-3080 composes the shared capsule outside the painter.
    // HandshakeComposedGhostReviewProofTests pins the actual composed look in 52 cases.

    private static TextBlock Text(Control root, string words) => root.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == words);
    private static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!);
    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static double EffectiveOpacity(Control text, Control root)
    {
        double opacity = 1;
        foreach (var visual in text.GetSelfAndVisualAncestors()) { opacity *= visual.Opacity; if (ReferenceEquals(visual, root)) break; }
        return opacity;
    }
    private static double CssEase(double x, double x1, double y1, double x2, double y2)
    {
        static double B(double t, double a, double b) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        double low = 0, high = 1;
        for (var i = 0; i < 60; i++) { var mid = (low + high) / 2; if (B(mid, x1, x2) < x) low = mid; else high = mid; }
        return B((low + high) / 2, y1, y2);
    }
}
