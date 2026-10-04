using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :36-150 ModelEffortButton, the chip's label
// (PR-COMPOSER-106), its compact form and its tooltip (PR-COMPOSER-107), and the width it holds while its popover is
// open (PR-COMPOSER-108). Every scenario mounts the real chip in a real (headless) window over a real AppModel.
[Collection("Headless")]
public sealed class EffortSliderChipRecordsTests(HeadlessSession headless)
{
    private const string LongName = "Claude Opus 5 (1M context)"; // chipName: "Opus 5" (Provider.swift:268-282)
    private static readonly string[] Levels = ["low", "medium", "high", "xhigh"];

    // A Codex thread (no seeded catalogue) on a model this scenario defines; the provider CLIs cannot resolve, so nothing
    // real is launched (fail closed).
    private static EffortChipRig Make(Func<string, ChatThread> thread, bool compact = false, Action<AppModel, ChatThread>? configure = null,
        ModelOption[]? models = null, double width = 360)
    {
        var owned = new List<IDisposable>();
        owned.Add(FrameClock.Source.Override(new ManualFrameSource()));
        var harness = new AppModelHarness(withRuntime: false); owned.Add(harness);
        var chat = thread(harness.Scratch);
        harness.WriteLibrary([], [chat]);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Codex, models ??
        [
            new ModelOption("m1", LongName, efforts: new ValueList<string>(Levels), defaultEffort: "medium", fastTier: "fast"),
            new ModelOption("m2", "Short", efforts: new ValueList<string>(Levels), defaultEffort: "medium"),
            new ModelOption("m3", "No levels"),
            new ModelOption("m4", "Claude Sonnet 4.5 Extended Thinking", efforts: new ValueList<string>(Levels), defaultEffort: "medium"), // chipName "Sonnet 4.5 Extended Thinking"
        ]);
        configure?.Invoke(model, chat);
        var chip = new ModelEffortButton(chat, false, compact);
        var presenter = new RecordingPresenter(); owned.Add(Presentation.Presenter.Override(presenter));
        var host = new Border { Child = chip, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = width, Height = 120 };
        window.Show(); EffortChipRig.Settle();
        var rig = new EffortChipRig { Model = model, Thread = chat, Chip = chip, Window = window, Presenter = presenter };
        foreach (var value in owned) rig.Own(value);
        return rig;
    }

    private static ChatThread Chat(string model, string? effort, bool fast = false) =>
        new(Guid.NewGuid(), ProviderKind.Codex, model, effort, RuntimeMode.Supervised, fast);

    private static void AssertInk(SwiftColor expected, Control control, IBrush? brush) =>
        Assert.Equal(expected.Resolve(control.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    // ---- PR-COMPOSER-106: the chip's label ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:65-104; Core/Models/Provider.swift:268-282 chipName
    public void The_full_chip_orders_mark_name_effort_chevron_five_points_apart() => headless.OnUiThread(() =>
    {
        using var rig = Make(_ => Chat("m1", "high"));
        var icon = Assert.Single(rig.Chip.GetVisualDescendants().OfType<ProviderIcon>());
        Assert.Equal((14, 14, 14), (icon.Size, icon.Width, icon.Height)); // :74 ProviderIcon(size: 14)
        var name = rig.Text("Opus 5"); // :90 chipName, not the catalogue's name "Claude Opus 5 (1M context)"
        Assert.Equal(1, name.MaxLines); // :92 .lineLimit(1)
        Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming); // :93 .truncationMode(.tail)
        AssertInk(Chrome.PrimaryText, name, name.Foreground); // :91
        var effort = rig.Text("High"); // :95 effortTitle(thread.effort)
        AssertInk(Chrome.PrimaryText.Opacity(0.72), effort, effort.Foreground); // :96 primaryText.opacity(0.72)
        Assert.Equal(1, effort.MaxLines); // :97 .lineLimit(1)
        var chevron = Assert.IsType<SwiftImage>(rig.Symbol("chevron.down")); // :101
        Assert.Equal(Chrome.ChevronFont, SwiftImage.GetFont(chevron)); // :102 Chrome.chevronFont: 8 point bold
        Assert.Equal(SwiftFont.System(8, SwiftFont.Weight.Bold), SwiftImage.GetFont(chevron));
        AssertInk(Chrome.SecondaryText, chevron, chevron.Foreground); // :103
        Assert.Null(rig.Symbol("bolt.fill")); // fast mode is off
        // :65 HStack(spacing: 5): the order mark, name, effort, chevron; each gap exactly 5 points
        var order = new Control[] { icon, name, effort, chevron };
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(rig.Left(order[i]) > rig.Left(order[i - 1]), $"{order[i]} is not after {order[i - 1]}");
            Assert.Equal(5, rig.Left(order[i]) - rig.Right(order[i - 1]), 0.5);
        }
        Assert.Equal(0, rig.Left(icon), 0.5); // the mark opens the row
        Assert.Equal(rig.Right(chevron), rig.Frame.Bounds.Width, 0.5); // the chevron closes it: nothing trails the last item
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:77-80 `if thread.fastMode, current?.supportsFast == true`
    public void A_yellow_bolt_sits_between_the_mark_and_the_name_only_when_fast_mode_is_on_and_supported() => headless.OnUiThread(() =>
    {
        using (var rig = Make(_ => Chat("m1", "high", fast: true)))
        {
            var icon = Assert.Single(rig.Chip.GetVisualDescendants().OfType<ProviderIcon>());
            var bolt = Assert.IsType<SwiftImage>(rig.Symbol("bolt.fill"));
            Assert.Equal(Chrome.InlineIconFont, SwiftImage.GetFont(bolt)); // :79 Chrome.inlineIconFont
            AssertInk(SwiftColor.Yellow, bolt, bolt.Foreground); // :80 .yellow
            var name = rig.Text("Opus 5");
            Assert.Equal(5, rig.Left(bolt) - rig.Right(icon), 0.5); // :65 spacing 5 either side of the bolt
            Assert.Equal(5, rig.Left(name) - rig.Right(bolt), 0.5);
        }
        using (var rig = Make(_ => Chat("m2", "high", fast: true))) // the model has no fast tier
            Assert.Null(rig.Symbol("bolt.fill"));
        using (var rig = Make(_ => Chat("m1", "high", fast: false))) // supported, but off
            Assert.Null(rig.Symbol("bolt.fill"));
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:94-99 `if let current, !current.efforts.isEmpty`
    public void A_model_without_efforts_shows_no_effort_text() => headless.OnUiThread(() =>
    {
        using var rig = Make(_ => Chat("m3", null));
        var name = Assert.Single(rig.Texts); // the name alone: no effort text at all
        Assert.Equal("No levels", name.Text);
        var chevron = Assert.IsType<SwiftImage>(rig.Symbol("chevron.down"));
        Assert.Equal(5, rig.Left(chevron) - rig.Right(name), 0.5); // the chevron follows the name directly
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:66-75,90 the pair's fused mark at 15 and the pair's own name
    public void A_pair_chat_wears_the_fused_mark_at_15_and_the_pairs_custom_name() => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Codex) { Name = "Duo Team" };
        using var rig = Make(_ => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "m1", "high", RuntimeMode.Supervised) { HydraEnabled = true, HydraPairID = pair.Id },
            configure: (model, _) => { model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair); });
        var fused = Assert.Single(rig.Chip.GetVisualDescendants().OfType<HydraFusedPairIcon>());
        Assert.Equal((15, 15), (fused.Width, fused.Height)); // :70-72 size: 15
        Assert.All(rig.Chip.GetVisualDescendants().OfType<ProviderIcon>(), icon => Assert.True(fused.IsVisualAncestorOf(icon))); // the pair's mark replaces the provider's
        var name = rig.Text("Duo Team"); // :90 pair?.customName first
        Assert.DoesNotContain(rig.Texts, t => t.Text == "Opus 5");
        Assert.Equal(5, rig.Left(name) - rig.Right(fused), 0.5);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:90 `current?.chipName ?? thread.model ?? thread.provider.displayName`
    public void Without_a_catalogue_entry_the_chip_names_the_raw_model_id_then_the_provider() => headless.OnUiThread(() =>
    {
        using (var rig = Make(_ => Chat("an-unlisted-id", null)))
        {
            rig.Text("an-unlisted-id");
            Assert.DoesNotContain(rig.Texts, t => t.Text is "Default" or "Medium"); // :94 no entry, no effort
        }
        using (var rig = Make(_ => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised)))
            rig.Text(ProviderKind.Codex.DisplayName);
    });

    // ---- PR-COMPOSER-107: compact (icon only) and the tooltip ----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-107")] // App/Views/Composer/EffortSlider.swift:82-103 `if !compact`; :65 HStack(spacing: 5): no gap around a lone child
    public void The_compact_chip_is_the_mark_alone_14_wide_and_the_bolt_five_points_after_it() => headless.OnUiThread(() =>
    {
        using (var rig = Make(_ => Chat("m1", "high"), compact: true))
        {
            Assert.Empty(rig.Texts); // no name, no effort
            Assert.Null(rig.Symbol("chevron.down"));
            Assert.Equal(14, rig.Frame.Bounds.Width, 0.5); // a one-child HStack has no spacing
            Assert.Equal(14 + 18, rig.Button.Bounds.Width, 0.5); // the chip's own 9 + 9 padding
            Assert.Equal(0, rig.Left(rig.Chip.GetVisualDescendants().OfType<ProviderIcon>().Single()), 0.5);
        }
        using (var rig = Make(_ => Chat("m1", "high", fast: true), compact: true))
        {
            var icon = rig.Chip.GetVisualDescendants().OfType<ProviderIcon>().Single();
            var bolt = Assert.IsType<SwiftImage>(rig.Symbol("bolt.fill")); // the bolt stays in the compact chip
            Assert.Empty(rig.Texts);
            Assert.Equal(5, rig.Left(bolt) - rig.Right(icon), 0.5);
            Assert.Equal(14 + 5 + bolt.Bounds.Width, rig.Frame.Bounds.Width, 0.5);
        }
    });

    [Theory]
    [Trait("Req", "PR-COMPOSER-107")] // App/Views/Composer/EffortSlider.swift:118-119,142-149 helpText: shortName, effort title, pair title, joined by " · "
    [InlineData(false)]
    [InlineData(true)]
    public void The_tooltip_and_accessible_name_join_short_name_effort_and_pair_title(bool compact) => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "m1", WorkerProvider = ProviderKind.Codex };
        using var rig = Make(_ => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "m1", "xhigh", RuntimeMode.Supervised) { HydraEnabled = true, HydraPairID = pair.Id },
            compact, (model, _) => { model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair); });
        var title = Marrow.App.Views.Chat.HydraPairSummary.Title(pair, rig.Model.Providers);
        var expected = $"{LongName} · Extra High · {title}"; // shortName (the whole name), not chipName
        Assert.Equal(expected, ToolTip.GetTip(rig.Button));
        Assert.Equal(expected, AutomationProperties.GetName(rig.Button));
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-107")] // App/Views/Composer/EffortSlider.swift:142-149 helpText without an effort level or a pair
    public void The_tooltip_has_only_the_parts_there_are() => headless.OnUiThread(() =>
    {
        using (var rig = Make(_ => Chat("m3", null), compact: true))
            Assert.Equal("No levels", ToolTip.GetTip(rig.Button)); // no efforts: no effort part; no pair: no pair part
        using (var rig = Make(_ => Chat("an-unlisted-id", null), compact: true))
            Assert.Equal("an-unlisted-id", ToolTip.GetTip(rig.Button)); // no entry: the raw id
        using (var rig = Make(_ => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised), compact: true))
            Assert.Equal(ProviderKind.Codex.DisplayName, ToolTip.GetTip(rig.Button));
        using (var rig = Make(_ => Chat("m1", null), compact: true))
            Assert.Equal($"{LongName} · Medium", ToolTip.GetTip(rig.Button)); // no effort on the thread: the model's default
    });

    // ---- PR-COMPOSER-108: the width held while the popover is open -------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-108")] // App/Views/Composer/EffortSlider.swift:50-57,105-117
    public void A_held_chip_keeps_the_width_it_opened_at_and_centres_shorter_content() => headless.OnUiThread(() =>
    {
        using var rig = Make(_ => Chat("m1", "xhigh")); // "Opus 5  Extra High v": the widest content
        var natural = rig.Frame.Bounds.Width;
        Assert.True(double.IsNaN(rig.Frame.Width));
        rig.Click();
        Assert.True(rig.Presenter.Calls[^1].Presented);
        Assert.Equal(natural, rig.Frame.Width, 0.5); // :116 lockedWidth = naturalWidth when the popover opens
        // the effort changes while it is open (the slider does exactly this): the box does not move
        rig.Model.UpdateThread(rig.Thread.Id, current => current with { Effort = "low" });
        Dispatcher.UIThread.RunJobs();
        rig.Text("Low");
        Assert.Equal(natural, rig.Frame.Width, 0.5);
        Assert.Equal(natural, rig.Frame.Bounds.Width, 0.5);
        var icon = rig.Chip.GetVisualDescendants().OfType<ProviderIcon>().Single();
        var chevron = rig.Symbol("chevron.down")!;
        var left = rig.Left(icon);
        var right = rig.Frame.Bounds.Width - rig.Right(chevron);
        Assert.True(left > 8, $"the shorter content should leave room to its sides; left gap {left}");
        Assert.True(Math.Abs(left - right) <= 1, $"left gap {left}, right gap {right}, box {rig.Frame.Bounds.Width}, row {rig.Frame.Children[0].Bounds}"); // :105-108 .frame(width:) centres the HStack in the held box
        // the name is still its natural width: the spare room goes around the content, not into the name's column
        var name = rig.Text("Opus 5");
        Assert.DoesNotContain(name.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.True(Math.Abs(name.TextLayout.Width - name.Bounds.Width) <= 1, $"name text {name.TextLayout.Width}, laid out {name.Bounds.Width}");
        // a click closes it: the chip is free again, and sizes itself to the content it has now
        rig.Click();
        Assert.False(rig.Presenter.Calls[^1].Presented);
        Assert.True(double.IsNaN(rig.Frame.Width));
        Assert.True(rig.Frame.Bounds.Width < natural - 8, $"free again at {rig.Frame.Bounds.Width}, held was {natural}");
        Assert.Equal(0, rig.Left(icon), 0.5);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-108")] // App/Views/Composer/EffortSlider.swift:56-57,89-94 a name that no longer fits is cut with an ellipsis
    public void A_held_chip_cuts_only_the_name_when_the_content_outgrows_it_and_is_free_again_on_close() => headless.OnUiThread(() =>
    {
        using var rig = Make(_ => Chat("m2", "low")); // "Short  Low v": narrow
        var natural = rig.Frame.Bounds.Width;
        rig.Click();
        Assert.Equal(natural, rig.Frame.Width, 0.5);
        var effortWidth = rig.Text("Low").Bounds.Width;
        // the list picks a far longer name, and the slider a longer effort, while the popover is open
        rig.Model.UpdateThread(rig.Thread.Id, current => current with { Model = "m4", Effort = "high" });
        Dispatcher.UIThread.RunJobs();
        var name = rig.Text("Sonnet 4.5 Extended Thinking");
        var effort = rig.Text("High");
        var chevron = rig.Symbol("chevron.down")!;
        Assert.Equal(natural, rig.Frame.Width, 0.5); // it does not grow
        Assert.Equal(natural, rig.Frame.Bounds.Width, 0.5);
        Assert.True(name.TextLayout.TextLines.Any(line => line.HasCollapsed), $"name {name.Bounds} text width {name.TextLayout.Width}; box {natural}; row {rig.Frame.Children[0].Bounds}; effort {effort.Bounds}; chevron {chevron.Bounds}"); // :92-93 the name is what gets cut
        Assert.DoesNotContain(effort.TextLayout.TextLines, line => line.HasCollapsed); // :97 .fixedSize(): the effort keeps its size
        Assert.True(effort.Bounds.Width > effortWidth); // "High" is wider than "Low" and is whole
        Assert.True(Math.Abs(effort.TextLayout.Width - effort.Bounds.Width) <= 1, $"effort text {effort.TextLayout.Width}, laid out {effort.Bounds.Width}");
        Assert.True(rig.Right(chevron) <= natural + 0.5, $"the chevron ends at {rig.Right(chevron)} inside the box {natural}");
        Assert.True(Math.Abs(natural - rig.Right(chevron)) <= 1, $"the chevron ends at {rig.Right(chevron)}, the box is {natural}"); // the cut name fills the room: the content spans the whole box
        rig.Click(); // closes
        Assert.True(double.IsNaN(rig.Frame.Width));
        Assert.DoesNotContain(rig.Text("Sonnet 4.5 Extended Thinking").TextLayout.TextLines, line => line.HasCollapsed); // the name is whole again
        Assert.True(rig.Frame.Bounds.Width > natural + 8);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-108")] // App/Views/Composer/EffortSlider.swift:108 `.frame(width: compact ? nil : lockedWidth)`: a compact chip is never held
    public void A_compact_chip_is_never_width_locked() => headless.OnUiThread(() =>
    {
        using var rig = Make(_ => Chat("m1", "high", fast: true), compact: true);
        var natural = rig.Frame.Bounds.Width;
        rig.Click();
        Assert.True(double.IsNaN(rig.Frame.Width));
        rig.Model.UpdateThread(rig.Thread.Id, current => current with { FastMode = false });
        Dispatcher.UIThread.RunJobs();
        Assert.True(double.IsNaN(rig.Frame.Width));
        Assert.Equal(14, rig.Frame.Bounds.Width, 0.5); // the bolt is gone and the chip shrinks with it, popover open or not
        Assert.True(rig.Frame.Bounds.Width < natural);
    });
}
