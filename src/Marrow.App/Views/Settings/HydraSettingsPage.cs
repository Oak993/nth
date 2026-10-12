using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B3 52bc792), :1-589.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`;
// `@Environment(AppModel.self)` is read from the environment, not passed in; a @ViewBuilder parameter is a
// Func<Control>. Swift `private` top-level types are `internal`.

/// <summary>:5-201 <c>struct HydraSettingsPage: View</c>: Hydra's switch and its heads' settings.</summary>
public sealed class HydraSettingsPage : Decorator
{
    private readonly StackPanel root = new() { Orientation = Orientation.Vertical, Spacing = Chrome.SectionHeaderSpacing, HorizontalAlignment = HorizontalAlignment.Stretch };
    private Control? shownGroup;

    // :8-190 body
    public HydraSettingsPage()
    {
        Child = root;
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            var s = model.Settings;
            return (s.HydraEnabled, s.HydraIsolateHeads, s.HydraMaxHeads, s.HydraAutoClearFinished,
                s.HydraAutoHidesIdleHeads, s.HydraShowsHeadDetails, s.HydraAutoPopsHeads, s.HydraTempersHeadEffort,
                s.HydraQueueHeads, s.HydraAlwaysHeads, s.HydraReviewHeads, s.HydraAutoMerge, s.HydraPairs,
                s.DisabledProviders, model.Providers.Statuses, model.Providers.Catalogs);
        }, _ => Rebuild());
    }

    private void Rebuild()
    {
        root.Children.Clear();
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        var settings = model.Settings;

        // :11-37 the switch card
        var glyphs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -9 };
        for (var index = 0; index < 5; index++)
            glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(index), 26));
        AutomationProperties.SetAccessibilityView(glyphs, AccessibilityView.Raw); // :18 .accessibilityHidden(true)

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(MakeText("Hydra", SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText));
        titleRow.Children.Add(new InfoHoverButton("How Hydra works", () => new HydraInfoPopover(HydraInfoPopover.Topic.Hydra)));

        // ED-NEW-textsize-1 (owner, 2026-10-11): the subtitle wraps onto more lines in a narrow window instead of being cut off.
        var subtitle = MakeText("One chat leads a team of heads on big jobs.", SwiftFont.Subheadline, Chrome.SecondaryText);

        var textColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        textColumn.Children.Add(titleRow);
        textColumn.Children.Add(subtitle);

        // :12-35 HStack(spacing: 14) { glyphs; text; Spacer(minLength: 12); switch }: the text takes the remaining width
        // (a Grid star column, since a StackPanel gives a stretched child only its desired width); the switch trails.
        var switchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        glyphs.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetColumn(textColumn, 1);
        var hydraSwitch = new SettingsSwitch(new Binding<bool>(() => settings.HydraEnabled, value => settings.HydraEnabled = value)) { Margin = new Thickness(14 + 12 + 14, 0, 0, 0) }; // HStack spacing is added on both sides of the Spacer: 14 + 12 + 14
        Grid.SetColumn(hydraSwitch, 2);
        switchRow.Children.Add(glyphs);
        switchRow.Children.Add(textColumn);
        switchRow.Children.Add(hydraSwitch);
        switchRow.Margin = new Thickness(16, 14, Chrome.RowControlTrailingPadding, 14);

        var switchCard = new ChromeCard(() => switchRow);
        root.Children.Add(switchCard);

        // :38-45 the whole-page note while off
        if (!settings.HydraEnabled)
            root.Children.Add(MakeText("Switch Hydra on to set up its heads.", SwiftFont.Subheadline, Chrome.SecondaryText, margin: new Thickness(4, 0)));

        // :47-163 the dimmed group
        var group = new StackPanel { Orientation = Orientation.Vertical, Spacing = Chrome.SectionHeaderSpacing, HorizontalAlignment = HorizontalAlignment.Stretch };

        // :48-93 Heads
        group.Children.Add(new ChromeSection("Heads", () =>
        {
            var card = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            card.Children.Add(new ChromeRow(
                "Checkout",
                () => new ChromeVisualPicker<bool, Control>(
                    [(true, "Own copy"), (false, "Shared")],
                    new Binding<bool>(() => settings.HydraIsolateHeads, value => settings.HydraIsolateHeads = value),
                    isolated => SwiftImage.SystemName(isolated ? "doc.on.doc" : "folder").Font(SwiftFont.Body)),
                settings.HydraIsolateHeads
                    ? "Each head works in a copy of its own; its changes land in the chat's checkout when it reports."
                    : "Heads work in the chat's checkout itself."));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(new ChromeRow(
                "Heads at once",
                () => new ChromeSegmentedPicker<long?>(
                    new[] { new ChromeSegmentedOption<long?>(null, "No cap", "infinity") }
                        .Concat(HydraPair.MaxHeadsRange.Select(count => new ChromeSegmentedOption<long?>(count, count.ToString(CultureInfo.InvariantCulture))))
                        .ToArray(),
                    new Binding<long?>(() => settings.HydraMaxHeads, value => settings.HydraMaxHeads = value)),
                settings.HydraMaxHeads is { } cap ? $"At most {cap} heads in parallel." : "As many heads as the job needs."));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(ToggleRow("Clear finished heads", "A finished head moves from the panel to the sidebar.",
                new Binding<bool>(() => settings.HydraAutoClearFinished, value => settings.HydraAutoClearFinished = value)));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(ToggleRow("Auto-hide heads in the sidebar",
                "A chat's heads show in the sidebar while one of them runs; the whole card leaves once every head of that chat has finished, and comes back on its own when one starts again.",
                new Binding<bool>(() => settings.HydraAutoHidesIdleHeads, value => settings.HydraAutoHidesIdleHeads = value)));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(new ChromeRow(
                "Head panel",
                () => new ChromeVisualPicker<bool, Control>(
                    [(false, "Progress"), (true, "Every step")],
                    new Binding<bool>(() => settings.HydraShowsHeadDetails, value => settings.HydraShowsHeadDetails = value),
                    showsSteps => SwiftImage.SystemName(showsSteps ? "list.number" : "chart.bar").Font(SwiftFont.Body)),
                "The head's progress bar, or each of its steps as it goes."));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(new ChromeRow(
                "Heads' panels",
                () => new ChromeVisualPicker<bool, Control>(
                    [(false, "One panel"), (true, "A panel each")],
                    new Binding<bool>(() => settings.HydraAutoPopsHeads, value => settings.HydraAutoPopsHeads = value),
                    popped => SwiftImage.SystemName(popped ? "square.grid.2x2" : "rectangle").Font(SwiftFont.Body)),
                settings.HydraAutoPopsHeads
                    ? "Each head after the first gets a panel of its own beside the chat while there is room, left and right until each side is full; the usage panel keeps its spot."
                    : "One panel over the chat lists every head."));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(ToggleRow("Heads work at a working effort",
                "A lead thinking above medium sends its heads out at medium on the same model — on a scale without a medium (Z.ai), at the scale's own middle rung; a pair that names the heads' effort keeps it.",
                new Binding<bool>(() => settings.HydraTempersHeadEffort, value => settings.HydraTempersHeadEffort = value)));
            return new ChromeCard(() => card);
        }, accessory: new InfoHoverButton("How heads work", () => new HydraInfoPopover(HydraInfoPopover.Topic.Heads))));

        // :94-100 Messages
        group.Children.Add(new ChromeSection("Messages", () =>
        {
            var card = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            card.Children.Add(ToggleRow("Queued follow-ups start on heads", "A prompt queued behind a running turn goes out at once.",
                new Binding<bool>(() => settings.HydraQueueHeads, value => settings.HydraQueueHeads = value),
                blockedBy: settings.HydraAlwaysHeads ? "Every message already goes to a head." : null));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(ToggleRow("Every message goes to a head", "The lead only hears the reports.",
                new Binding<bool>(() => settings.HydraAlwaysHeads, value => settings.HydraAlwaysHeads = value)));
            return new ChromeCard(() => card);
        }));

        // :101-107 When the team is done
        group.Children.Add(new ChromeSection("When the team is done", () =>
        {
            var card = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            card.Children.Add(ToggleRow("Lead checks the heads' work", "Reads every changed file and corrects it. Slower, more careful.",
                new Binding<bool>(() => settings.HydraReviewHeads, value => settings.HydraReviewHeads = value)));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(ToggleRow("Merge automatically", "The team's changes go out as a merge request and are merged.",
                new Binding<bool>(() => settings.HydraAutoMerge, value => settings.HydraAutoMerge = value)));
            return new ChromeCard(() => card);
        }));

        // :108-162 Pairs
        group.Children.Add(new ChromeSection("Pairs", () =>
        {
            var pairsGroup = new StackPanel { Orientation = Orientation.Vertical, Spacing = Chrome.SectionHeaderSpacing };

            var glyphRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            glyphRow.Children.Add(new HydraGlyph(HydraRoster.Persona(0), 30));
            var arrow = SwiftImage.SystemName("arrow.right").Font(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText);
            glyphRow.Children.Add(arrow);
            var trailingGlyphs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -8 };
            for (var index = 1; index < 4; index++)
                trailingGlyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(index), 22));
            glyphRow.Children.Add(trailingGlyphs);
            AutomationProperties.SetAccessibilityView(glyphRow, AccessibilityView.Raw); // :125

            var blurb = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
            blurb.Children.Add(MakeText("One leads, the others build", SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText));
            blurb.Children.Add(MakeText("A pair says which model leads and which runs the heads. Put a deep thinker over quick hands on one provider, or lead on one provider and send the heads out on another: the strongest lead with the fastest team, every time.",
                SwiftFont.Subheadline, Chrome.SecondaryText));

            // :118-141 HStack(spacing: 14) { roster; text; Spacer(minLength: 0) }: the text takes the width left beside the
            // roster and wraps (:135 fixedSize(horizontal: false)).
            var introRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            glyphRow.Margin = new Thickness(0, 0, 14, 0);
            blurb.Margin = new Thickness(0, 0, 14, 0); // the 14 of spacing between the text and Spacer(minLength: 0)
            Grid.SetColumn(blurb, 1);
            introRow.Children.Add(glyphRow);
            introRow.Children.Add(blurb);
            introRow.Margin = new Thickness(16, 14);
            pairsGroup.Children.Add(new ChromeCard(() => introRow));

            var pairs = settings.HydraPairs;
            pairsGroup.Children.Add(new ChromeCard(() =>
            {
                var listCard = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
                if (pairs.Count == 0)
                {
                    listCard.Children.Add(new ChromeRow("No pairs yet", () => new HydraAddPairButton(),
                        "Heads run on the chat's own model until a chat joins a pair."));
                }
                else
                {
                    for (var index = 0; index < pairs.Count; index++)
                    {
                        if (index > 0) listCard.Children.Add(new ChromeRowDivider());
                        listCard.Children.Add(new HydraPairRow(pairs[index]));
                    }
                    listCard.Children.Add(new ChromeRowDivider());
                    listCard.Children.Add(new ChromeRow("Another pair", () => new HydraAddPairButton(), "One per provider is enough."));
                }
                return listCard;
            }));
            pairsGroup.Children.Add(MakeText("Pairs sit at the top of the model picker: a tap puts the chat in one. Right-click a project in the sidebar to start each of its new chats on one pair.",
                SwiftFont.Subheadline, Chrome.SecondaryText, margin: new Thickness(4, 0)));
            return pairsGroup;
        }, trailing: new HydraCookbookButton()));

        group.IsEnabled = settings.HydraEnabled; // :164 .disabled(!settings.hydraEnabled)
        // :165-166 .opacity(...).animation(.smooth(duration: 0.2), value: settings.hydraEnabled): the rebuilt group starts
        // where the one it replaces stands and fades to its target.
        var target = settings.HydraEnabled ? 1.0 : 0.5;
        var shown = shownGroup?.Opacity;
        var fade = new AnimatedOpacity(group, shown ?? target);
        if (shown is { } from && from != target) fade.Set(target, Marrow.UI.Port.Animation.Smooth(0.2));
        shownGroup = group;
        root.Children.Add(group);

        // :167-189 Where heads run
        root.Children.Add(new ChromeSection("Where heads run", () =>
        {
            var card = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            card.Children.Add(new ChromeRow("Claude, Codex and Copilot", () =>
            {
                var providerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                providerRow.Children.Add(new ProviderIcon { Provider = ProviderKind.Claude, Size = 14 });
                providerRow.Children.Add(new ProviderIcon { Provider = ProviderKind.Codex, Size = 14 });
                // Copilot's mark is wider than tall, so it fills less of a square frame; size it up to share the same optical height. :173-174
                providerRow.Children.Add(new ProviderIcon { Provider = ProviderKind.Copilot, Size = 16.75 });
                providerRow.SetValue(Avalonia.Controls.TextBlock.ForegroundProperty, Chrome.SecondaryText.Brush(providerRow));
                return providerRow;
            }, "In the provider's own session, through the lead's own agent tools."));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(new ChromeRow("Other providers", () => new Border(), "As threads of their own, asked for at the end of the lead's reply."));
            card.Children.Add(new ChromeRowDivider());
            card.Children.Add(new ChromeRow("Across providers", () =>
            {
                var mark = new HydraPairMark(ProviderKind.Claude, ProviderKind.Antigravity, 14);
                mark.SetValue(Avalonia.Controls.TextBlock.ForegroundProperty, Chrome.SecondaryText.Brush(mark));
                return mark;
            }, "A pair can lead on one provider and run its heads on another."));
            return new ChromeCard(() => card);
        }));
    }

    /// <summary>:194-200 A toggle row. A reason it is blocked stands in for its detail, and the row is dimmed and
    /// inert while there is one.</summary>
    private static Control ToggleRow(string title, string detail, Binding<bool> isOn, string? blockedBy = null)
    {
        var row = new ChromeRow(title, () => new SettingsSwitch(isOn), blockedBy ?? detail);
        row.IsEnabled = blockedBy is null; // :198 .disabled(reason != nil)
        row.Opacity = blockedBy is null ? 1 : 0.5; // :199
        return row;
    }

    private static Avalonia.Controls.TextBlock MakeText(string value, SwiftFont font, SwiftColor color, int? maxLines = null, Thickness? margin = null)
    {
        var text = new Avalonia.Controls.TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.WrapWithOverflow, // ED-NEW-textsize-1: whole words only; a word wider than the line ends in an ellipsis
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = maxLines ?? 0,
            Margin = margin ?? default,
        };
        font.Apply(text);
        text.Foreground = color.Brush(text);
        return text;
    }
}

/// <summary>:204-220 <c>private struct HydraCookbookButton: View</c>: opens the cookbook: the best pairs for the
/// providers set up here, added with a click.</summary>
internal sealed class HydraCookbookButton : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isOpen;
        public bool IsOpen { get => Read(isOpen); set => Set(ref isOpen, value); }
    }

    // :207-219 body
    public HydraCookbookButton()
    {
        var state = new ViewState();
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(SwiftImage.SystemName("book").Font(SwiftFont.Body));
        label.Children.Add(new Avalonia.Controls.TextBlock { Text = "Open cookbook", VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Content = label, Classes = { "glass" } };
        button.Click += (_, _) => state.IsOpen = true;

        var popover = new Popup { Placement = PlacementMode.Bottom, PlacementTarget = button, IsLightDismissEnabled = true };
        // :215-218 SwiftUI hands the popover's content the presenting view's environment; this popup never joins the page's tree,
        // so its content (HydraCookbookPanel.swift:6-7 reads @Environment(AppModel.self)) is given the model directly.
        popover.Opened += (_, _) => popover.Child = AppEnvironment.Carry(new HydraCookbookPanel().PresentedChrome(), button);
        popover.Closed += (_, _) => { if (state.IsOpen) state.IsOpen = false; };
        var shown = false;
        button.Track(() => state.IsOpen, presented =>
        {
            if (presented == shown) return;
            shown = presented;
            Presentation.SetPresented(popover, presented, button, () => state.IsOpen = false); // ED-253: Escape closes it
        });

        Child = button;
    }
}

/// <summary>:222-236 <c>private struct HydraAddPairButton: View</c>.</summary>
internal sealed class HydraAddPairButton : Decorator
{
    // :225-235 body
    public HydraAddPairButton()
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(SwiftImage.SystemName("plus").Font(SwiftFont.Body));
        label.Children.Add(new Avalonia.Controls.TextBlock { Text = "Add pair", VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Content = label, Classes = { "glass" } };
        button.Click += (_, _) =>
        {
            AppModel model;
            try { model = AppEnvironment.GetAppModel(this); }
            catch (InvalidOperationException) { return; }

            var registry = model.Providers;
            var installed = SwiftEnum<ProviderKind>.AllCases.Where(provider => registry.Status(provider).IsInstalled && model.Settings.IsEnabled(provider)).ToArray();
            var taken = new HashSet<ProviderKind>(model.Settings.HydraPairs.Select(pair => pair.Provider));
            var provider = installed.Where(candidate => !taken.Contains(candidate)).Select(candidate => (ProviderKind?)candidate).FirstOrDefault()
                ?? installed.Select(candidate => (ProviderKind?)candidate).FirstOrDefault() ?? model.Settings.DefaultProvider;
            Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.AddHydraPair(new HydraPair(provider)));
        };
        Child = button;
    }
}

/// <summary>:239-318 <c>private struct HydraPairRow: View</c>: one pair, with the editor behind a click.</summary>
internal sealed class HydraPairRow : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isEditing;
        private bool isHovering;
        public bool IsEditing { get => Read(isEditing); set => Set(ref isEditing, value); }
        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    // :241 let pair: HydraPair; :246-297 body
    public HydraPairRow(HydraPair pair)
    {
        var state = new ViewState();
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return (Model: model, Pair: model.Settings.HydraPairs.FirstOrDefault(current => current.Id == pair.Id), model.Settings.DisabledProviders,
                model.Providers.Statuses, model.Providers.Catalogs);
        }, value =>
        {
            if (value.Pair is { } current) Build(value.Model, current, state);
            else Child = null;
        });
        this.Task(() =>
        {
            var current = AppEnvironment.GetAppModel(this).Settings.HydraPairs.FirstOrDefault(current => current.Id == pair.Id) ?? pair;
            return current.HeadsProvider;
        }, async headsProvider =>
        {
            var model = AppEnvironment.GetAppModel(this);
            var current = model.Settings.HydraPair(pair.Id) ?? pair;
            await model.Providers.LoadCatalog(current.Provider);
            await model.Providers.LoadCatalog(headsProvider);
        });
    }

    private void Build(AppModel model, HydraPair pair, ViewState state)
    {
        var registry = model.Providers;

        var title = new Avalonia.Controls.TextBlock { Text = HydraPairSummary.Title(pair, registry), TextWrapping = TextWrapping.Wrap };
        SwiftFont.Body.Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        var summary = new Avalonia.Controls.TextBlock { Text = Summary(pair, registry, model), TextWrapping = TextWrapping.Wrap };
        SwiftFont.Subheadline.Apply(summary);
        summary.Foreground = Chrome.SecondaryText.Brush(summary);
        var textColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        textColumn.Children.Add(title);
        textColumn.Children.Add(summary);

        var chevron = SwiftImage.SystemName("slider.horizontal.3").Font(SwiftFont.Callout).ForegroundStyle(Chrome.SecondaryText);

        // :251-263 HStack(spacing: 12) { icons; text; Spacer(minLength: 8); chevron }: the text column takes the remaining
        // width and wraps, the chevron trails.
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var icons = new HydraPairIcons(pair) { Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(textColumn, 1);
        chevron.Margin = new Thickness(12 + 8 + 12, 0, 0, 0);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(icons);
        content.Children.Add(textColumn);
        content.Children.Add(chevron);

        var openButton = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        openButton.Click += (_, _) => state.IsEditing = true;
        openButton.PointerEntered += (_, _) => state.IsHovering = true;
        openButton.PointerExited += (_, _) => state.IsHovering = false;
        openButton.Track(() => state.IsHovering, hovering => chevron.Opacity = hovering ? 1 : 0);

        var editorPopover = new Popup { Placement = PlacementMode.Right, PlacementTarget = openButton, IsLightDismissEnabled = true };
        // :268-271 as the cookbook popover: the editor reads @Environment(AppModel.self), so the content carries the model.
        editorPopover.Opened += (_, _) => editorPopover.Child = AppEnvironment.Carry(new HydraPairEditor(pair.Id) { Width = 360 }.PresentedChrome(), openButton);
        editorPopover.Closed += (_, _) => { if (state.IsEditing) state.IsEditing = false; };
        var shown = false;
        openButton.Track(() => state.IsEditing, presented =>
        {
            if (presented == shown) return;
            shown = presented;
            Presentation.SetPresented(editorPopover, presented, openButton, () => state.IsEditing = false); // ED-253
        });

        var removeButton = new Button
        {
            Content = SwiftImage.SystemName("minus.circle").Font(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.SecondaryText),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 24,
            Height = 24,
        };
        ToolTip.SetTip(removeButton, "Remove this pair");
        AutomationProperties.SetName(removeButton, "Remove this pair");
        // :278 includes the model's chat/project pair-reference cleanup.
        removeButton.Click += (_, _) => Transaction.WithAnimation(Chrome.PanelSlide, () => model.RemoveHydraPair(pair.Id));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; // :248 HStack(spacing: 10): the button fills, the remove control trails
        removeButton.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(removeButton, 1);
        row.Children.Add(openButton);
        row.Children.Add(removeButton);
        row.Margin = new Thickness(16, 9, Chrome.RowControlTrailingPadding, 9);

        Child = row;

    }

    /// <summary>:299-317 the row's second line.</summary>
    private static string Summary(HydraPair pair, ProviderRegistry registry, AppModel model)
    {
        var parts = new List<string> { HydraPairSummary.Providers(pair) };
        if (pair.CustomName is not null) parts.Insert(0, HydraPairSummary.ModelsTitle(pair, registry));
        if (HydraPairSummary.Efforts(pair) is { } efforts) parts.Add(efforts);
        if (pair.MaxHeads is { } cap) parts.Add(cap == 1 ? "One head at a time" : $"Up to {cap} heads");
        if (pair.SendsHeadsElsewhere && model.HydraHeadsProvider(pair) == pair.Provider)
            parts.Add($"{pair.HeadsProvider.DisplayName} is not set up, heads stay on {pair.Provider.DisplayName}");
        return string.Join(" · ", parts);
    }
}

/// <summary>:323-330 <c>private struct HydraPairIcons: View</c>: a pair's provider marks.</summary>
internal sealed class HydraPairIcons : Decorator
{
    // :324 let pair: HydraPair; :326-329 body
    public HydraPairIcons(HydraPair pair)
    {
        var mark = new HydraPairMark(pair.Provider, pair.HeadsProvider);
        mark.MinWidth = 22;
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        Child = mark;
    }
}

/// <summary>:336-589 <c>private struct HydraPairEditor: View</c>: the pair's settings.</summary>
internal sealed class HydraPairEditor : Decorator
{
    private readonly Guid pairID;
    private readonly StackPanel root = new() { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

    // :338 let pairID: UUID; :345-520 body
    public HydraPairEditor(Guid pairID)
    {
        this.pairID = pairID;
        Child = root;
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return (model.Settings.HydraPairs, model.Settings.DisabledProviders, model.Providers.Statuses,
                model.Providers.Catalogs, model.Providers.LoadingCatalogs);
        }, _ => Rebuild());
        this.Task(() =>
        {
            var pair = AppEnvironment.GetAppModel(this).Settings.HydraPair(pairID);
            return pair is null ? ValueList<ProviderKind>.Empty : CatalogProviders(pair);
        }, async providers =>
        {
            var registry = AppEnvironment.GetAppModel(this).Providers;
            foreach (var provider in providers) await registry.LoadCatalog(provider);
        });
    }

    // :360-379 the name field keeps its identity, focus, selection and caret across the value change: it is built
    // once and only the rows after it are rebuilt.
    private StackPanel? nameColumn;
    private TextBox? nameField;

    private void Rebuild()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { root.Children.Clear(); return; }

        // :346 if let pair = model.settings.hydraPair(pairID)
        if (model.Settings.HydraPair(pairID) is not { } pair) { root.Children.Clear(); return; }

        var registry = model.Providers;
        var providers = SwiftEnum<ProviderKind>.AllCases.Where(provider => model.Settings.IsEnabled(provider) && (registry.Status(provider).IsInstalled || provider == pair.Provider)).ToArray();
        var headsProviders = SwiftEnum<ProviderKind>.AllCases.Where(provider => model.Settings.IsEnabled(provider) && (registry.Status(provider).IsInstalled || provider == pair.HeadsProvider)).ToArray();
        var options = registry.Models(pair.Provider);
        var headsOptions = registry.Models(pair.HeadsProvider);
        var leadOption = registry.Model(pair.OrchestratorModel, pair.Provider);
        var workerOption = registry.Model(pair.WorkerModel, pair.HeadsProvider);
        var leadEfforts = Efforts(leadOption, AllEfforts(options));
        var workerEfforts = Efforts(workerOption, pair.SendsHeadsElsewhere ? Efforts(registry.DefaultModel(pair.HeadsProvider), AllEfforts(headsOptions)) : leadEfforts);

        // :360-379 name field, prompted by the pair's own summary title
        if (nameColumn is null || nameField is null)
        {
            nameField = PlainControls.TextField("", new Binding<string>(
                () => model.Settings.HydraPairs.FirstOrDefault(current => current.Id == pairID)?.Name ?? "", // the observable list, as the row reads it
                text => Update(model, p => p with { Name = string.IsNullOrEmpty(text) ? null : text }))); // :362-365
            SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold).Apply(nameField); // :369
            nameField.Foreground = Chrome.PrimaryText.Brush(nameField); // :370
            var frame = new Border { Child = nameField, Padding = new Thickness(10, 7) }.RoundedRectangle(9, RoundedCornerStyle.Continuous); // :371-373
            frame.Background = Chrome.Overlay(0.06).Brush(frame); // :373
            var subtitle = MakeText("Shown in the picker and the composer", SwiftFont.Subheadline, Chrome.SecondaryText, new Thickness(2, 0)); // :374-377
            nameColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Margin = new Thickness(14, 14, 14, 0) };
            nameColumn.Children.Add(frame);
            nameColumn.Children.Add(subtitle);
        }
        nameField.PlaceholderText = HydraPairSummary.ModelsTitle(pair, registry);
        for (var index = root.Children.Count - 1; index >= 0; index--)
            if (!ReferenceEquals(root.Children[index], nameColumn)) root.Children.RemoveAt(index);
        if (root.Children.Count == 0) root.Children.Add(nameColumn);

        root.Children.Add(SectionLabel("Lead"));
        root.Children.Add(EditorRow("Provider", null, () => new GlassPickerButton<ProviderKind>(
            providers.Select(provider => (provider, provider.DisplayName)).ToArray(),
            new Binding<ProviderKind>(() => pair.Provider, provider =>
            {
                Update(model, p =>
                {
                    if (p.Provider == provider) return p;
                    var next = p with { Provider = provider, OrchestratorModel = null, OrchestratorEffort = null };
                    // :398-403 heads on the old lead provider go with it; heads on a provider of their own keep
                    // their model and effort, unless that provider is the new lead's.
                    if (p.WorkerProvider is null) next = next with { WorkerModel = null, WorkerEffort = null };
                    else if (p.WorkerProvider == provider) next = next with { WorkerProvider = null };
                    return next;
                });
                _ = registry.LoadCatalog(provider);
            }),
            asset: provider => provider.IconName)));
        root.Children.Add(EditorRow("Model", null, () => new GlassPickerButton<string?>(
            new (string?, string)[] { (null, "Any model") }.Concat(options.Select(option => ((string?)option.Id, option.ShortName))).ToArray(),
            new Binding<string?>(() => pair.OrchestratorModel, id => Update(model, p =>
            {
                var next = p with { OrchestratorModel = id };
                if (next.OrchestratorEffort is { } effort && registry.Model(id, next.Provider) is { } option && !option.Efforts.Contains(effort))
                    next = next with { OrchestratorEffort = null };
                return next;
            })),
            maxWidth: 190)));
        if (leadEfforts.Count > 0)
            root.Children.Add(EditorRow("Effort", null, () => EffortPicker(leadEfforts, "Chat's effort",
                new Binding<string?>(() => pair.OrchestratorEffort, effort => Update(model, p => p with { OrchestratorEffort = effort })))));

        root.Children.Add(SectionLabel("Heads"));
        root.Children.Add(EditorRow("Provider", null, () => new GlassPickerButton<ProviderKind?>(
            new (ProviderKind?, string)[] { (null, "Same as the lead") }
                .Concat(headsProviders.Where(provider => provider != pair.Provider).Select(provider => ((ProviderKind?)provider, provider.DisplayName)))
                .ToArray(),
            new Binding<ProviderKind?>(() => pair.SendsHeadsElsewhere ? pair.WorkerProvider : null, provider => Update(model, p =>
            {
                var chosen = provider == p.Provider ? null : provider;
                if (p.WorkerProvider == chosen) return p;
                if (provider is { } concrete) _ = registry.LoadCatalog(concrete);
                return p with { WorkerProvider = chosen, WorkerModel = null, WorkerEffort = null };
            })),
            asset: provider => (provider ?? pair.Provider).IconName)));
        root.Children.Add(EditorRow("Model", null, () => new GlassPickerButton<string?>(
            new (string?, string)[] { (null, pair.SendsHeadsElsewhere ? $"{pair.HeadsProvider.DisplayName}'s default" : "Same as the chat") }
                .Concat(headsOptions.Select(option => ((string?)option.Id, option.ShortName)))
                .ToArray(),
            new Binding<string?>(() => pair.WorkerModel, id => Update(model, p =>
            {
                var next = p with { WorkerModel = id };
                if (next.WorkerEffort is { } effort && registry.Model(id, next.HeadsProvider) is { } option && !option.Efforts.Contains(effort))
                    next = next with { WorkerEffort = null };
                return next;
            })),
            maxWidth: 190)));
        if (workerEfforts.Count > 0)
            root.Children.Add(EditorRow("Effort", null, () => EffortPicker(workerEfforts, pair.SendsHeadsElsewhere ? $"{pair.HeadsProvider.DisplayName}'s default" : "Same as the chat",
                new Binding<string?>(() => pair.WorkerEffort, effort => Update(model, p => p with { WorkerEffort = effort })))));
        root.Children.Add(EditorRow("At once", null, () => new GlassPickerButton<long?>(
            new (long?, string)[] { (null, "As many as it takes") }.Concat(HydraPair.MaxHeadsRange.Select(count => ((long?)count, count.ToString(CultureInfo.InvariantCulture)))).ToArray(),
            new Binding<long?>(() => pair.MaxHeads, count => Update(model, p => p with { MaxHeads = count })))));

        foreach (var provider in CatalogProviders(pair))
        {
            if (registry.Models(provider).Count == 0)
            {
                var note = registry.LoadingCatalogs.Contains(provider) ? $"Loading {provider.DisplayName}'s models…" : $"No models loaded for {provider.DisplayName} yet.";
                root.Children.Add(MakeText(note, SwiftFont.Subheadline, Chrome.SecondaryText, new Thickness(14, 8, 14, 0)));
            }
        }

        root.Margin = new Thickness(0, 0, 0, 14);

    }

    // :389 includes the model's propagation to chats already leading with this pair.
    private void Update(AppModel model, Func<HydraPair, HydraPair> change) =>
        model.UpdateHydraPair(pairID, change);

    private static Control EditorRow(string title, string? detail, Func<Control> control)
    {
        var titleColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var titleText = new Avalonia.Controls.TextBlock { Text = title, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        SwiftFont.Body.Apply(titleText);
        titleText.Foreground = Chrome.PrimaryText.Brush(titleText);
        titleColumn.Children.Add(titleText);
        if (detail is not null) titleColumn.Children.Add(MakeText(detail, SwiftFont.Subheadline, Chrome.SecondaryText));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        Grid.SetColumn(titleColumn, 0);
        var controlHost = control();
        Grid.SetColumn(controlHost, 2);
        row.Children.Add(titleColumn);
        row.Children.Add(controlHost);
        row.Margin = new Thickness(14, 5);
        return row;
    }

    private static Control SectionLabel(string title)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        // :548-550 Divider().padding(.horizontal, 14).padding(.top, 12): the port's one Divider() hairline (PlainControls.Divider),
        // not the secondary text colour, which drew it about three times as strong as every other divider.
        var rule = PlainControls.Divider();
        rule.Margin = new Thickness(14, 12, 14, 0);
        column.Children.Add(rule);
        column.Children.Add(MakeText(title, SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold), Chrome.SecondaryText, new Thickness(14, 10, 14, 4)));
        return column;
    }

    private static Control EffortPicker(IReadOnlyList<string> efforts, string inherit, Binding<string?> selection) =>
        new GlassPickerButton<string?>(
            new (string?, string)[] { (null, inherit) }.Concat(efforts.Select(effort => ((string?)effort, ModelOption.EffortTitle(effort)))).ToArray(),
            selection);

    private static Avalonia.Controls.TextBlock MakeText(string value, SwiftFont font, SwiftColor color, Thickness? margin = null)
    {
        var text = new Avalonia.Controls.TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = margin ?? default };
        font.Apply(text);
        text.Foreground = color.Brush(text);
        return text;
    }

    /// <summary>:568-574 Every effort any of the provider's models offers, in the order the first lists them.</summary>
    private static ValueList<string> AllEfforts(IReadOnlyList<ModelOption> options)
    {
        var seen = new List<string>();
        foreach (var option in options)
            foreach (var effort in option.Efforts)
                if (!seen.Contains(effort)) seen.Add(effort);
        return new ValueList<string>(seen);
    }

    /// <summary>:579-582 A model's efforts for the picker, or the fallback.</summary>
    private static ValueList<string> Efforts(ModelOption? of, IReadOnlyList<string> fallback) =>
        of is { } option && option.Efforts.Count > 0 ? option.Efforts : new ValueList<string>(fallback);

    /// <summary>:586-588 The providers whose catalogs the editor shows.</summary>
    private static ValueList<ProviderKind> CatalogProviders(HydraPair pair) =>
        pair.SendsHeadsElsewhere ? new ValueList<ProviderKind>([pair.Provider, pair.HeadsProvider]) : new ValueList<ProviderKind>([pair.Provider]);
}
