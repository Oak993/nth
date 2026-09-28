using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B3 52bc7926), :1-448.
// R-VIEW-2: Swift's `@State private var query` is read by body and (via a chrome search field elsewhere) written
// from outside the page; ported as a mutable field with a public SetQuery(string) the search field's host calls,
// since this port's Decorator-based views are constructed once rather than re-evaluated per keystroke like SwiftUI
// (so providerExpansion, held as a plain instance field, survives a query change exactly as the Swift @State does).

namespace Marrow.App.Views.Settings;

/// <summary>:4-158 <c>struct ModelsSettingsPage: View</c>: the models the composer's picker offers.</summary>
public sealed class ModelsSettingsPage : Decorator
{
    private string query;
    private readonly Dictionary<ProviderKind, bool> providerExpansion = new(); // :12-14 absent means expanded
    private readonly StackPanel root = new() { Orientation = Orientation.Vertical, Spacing = Chrome.SectionSpacing, HorizontalAlignment = HorizontalAlignment.Stretch }; // :30
    private ValueMap<ModelPin, double> rowHeights = ValueMap<ModelPin, double>.Empty; // :10
    private RowDrag<ModelPin> drag = new(); // :9

    // :6 var query = ""; :19-100 body
    public ModelsSettingsPage(string query = "")
    {
        this.query = query;
        Child = root;
        this.Track(() => AppEnvironment.GetAppModel(this), _ => Rebuild());
    }

    /// <summary>The chrome search field's text, trimmed of whitespace and newlines by the caller or here.</summary>
    public void SetQuery(string value)
    {
        query = value;
        Rebuild();
    }

    // :17 private static let slide = Animation.spring(response: 0.28, dampingFraction: 0.82)
    private static Animation Slide => Animation.Spring(response: 0.28, dampingFraction: 0.82);

    // :19-100 body
    private void Rebuild()
    {
        root.Children.Clear();
        // SetQuery may run before the page is attached; Track's own deferred call guards the first Rebuild, but a
        // search edit that races attachment must not throw.
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        var registry = model.Providers;
        var settings = model.Settings;
        var trimmed = FoundationText.Trim(query, includeNewlines: true); // :23

        var pins = settings.ModelList; // :7
        var visiblePins = VisiblePins(pins, registry, trimmed); // :105-110

        if (trimmed.Length == 0 || visiblePins.Count > 0) // :33
        {
            root.Children.Add(new ChromeSection("Your models", () => YourModels(model, pins, visiblePins, registry, settings, trimmed))); // :30-65
        }

        var providers = registry.AvailableProviders.Where(provider => // :25-27
            trimmed.Length == 0 || registry.Models(provider).Any(option => Matches(option, option.Id, provider, trimmed))).ToList();

        foreach (var provider in providers) // :75-84
        {
            root.Children.Add(new ProviderModelsSection(
                model,
                provider,
                new Binding<bool>(
                    () => providerExpansion.TryGetValue(provider, out var expanded) ? expanded : true,
                    value => providerExpansion[provider] = value),
                trimmed));
        }

        if (trimmed.Length > 0 && visiblePins.Count == 0 && providers.Count == 0) // :86-91
        {
            var noMatch = new TextBlock
            {
                Text = "No models match “" + trimmed + "”.", // U+201C LEFT / U+201D RIGHT DOUBLE QUOTATION MARK
                Foreground = Chrome.SecondaryText.Brush(root),
                Margin = new Thickness(4, 0, 4, 0),
            };
            SwiftFont.System(13).Apply(noMatch);
            root.Children.Add(noMatch);
        }
    }

    // :105-110 visiblePins
    private static ValueList<ModelPin> VisiblePins(ValueList<ModelPin> pins, ProviderRegistry registry, string trimmed) =>
        new(pins.Where(pin => Matches(registry.Model(pin.ModelID, pin.Provider), pin.ModelID, pin.Provider, trimmed)));

    // :30-65 the "Your models" section content
    private Control YourModels(AppModel model, ValueList<ModelPin> pins, ValueList<ModelPin> visiblePins, ProviderRegistry registry, AppSettings settings, string trimmed)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

        if (pins.Count == 0) // :34-40
        {
            stack.Children.Add(new ChromeRow(
                "No models chosen yet",
                () => new Border(),
                "Until you add some below, the picker shows every model from your providers."));
        }
        else
        {
            // :42-46 a plain stack, not lazy, so a dragged row draws on top and does not fly to the origin.
            var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var pin in visiblePins) // :47-62
            {
                var isDragged = drag.Id == pin; // :49
                var divider = new ChromeRowDivider { Opacity = (visiblePins.Count > 0 && pin == visiblePins[0]) || isDragged ? 0 : 1 }; // :51, ruling :50
                rows.Children.Add(divider);

                var row = new PinnedModelRow(
                    model,
                    pin,
                    isDragged,
                    translation => DragChanged(pin, translation, registry, settings, visiblePins),
                    () => DragEnded(settings));
                row.RenderTransform = new TranslateTransform(0, isDragged ? drag.VisualOffset : 0);
                row.ZIndex = isDragged ? 1 : 0;
                row.SizeChanged += (_, args) =>
                {
                    if (args.NewSize.Height <= 0) return;
                    rowHeights = rowHeights.Mutating(map => map[pin] = args.NewSize.Height); // :141-150 PinnedRowHeightReporter
                    if (rowHeights.Count > pins.Count) // :93-99
                        rowHeights = new ValueMap<ModelPin, double>(rowHeights.Where(entry => pins.Contains(entry.Key)));
                };
                rows.Children.Add(row);
            }
            stack.Children.Add(rows);

            if (trimmed.Length == 0) // :66-71
            {
                var count = new TextBlock
                {
                    Text = pins.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " of " + AppSettings.ModelListLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ". Click a model to set its reasoning effort and fast mode for every new chat.",
                    Foreground = Chrome.SecondaryText.Brush(stack),
                    Margin = new Thickness(4, 0, 4, 0),
                };
                SwiftFont.System(11).Apply(count);
                stack.Children.Add(count);
            }
        }
        return stack;
    }

    // :112-134 dragChanged(_:translation:)
    private void DragChanged(ModelPin pin, double translation, ProviderRegistry registry, AppSettings settings, ValueList<ModelPin> visiblePinsAtStart)
    {
        if (drag.Id != pin) drag = new RowDrag<ModelPin> { Id = pin };
        drag = drag with { Translation = translation };

        var trimmed = FoundationText.Trim(query, includeNewlines: true);
        var order = VisiblePins(settings.ModelList, registry, trimmed); // re-read fresh on every move
        var (nextDrag, moved) = drag.Settle(order, rowHeights, fallbackHeight: 54,
            (neighbour, placeAfter) => settings.MoveModel(pin, neighbour, placeAfter),
            pastCentre: 0);
        drag = nextDrag;
        if (moved) Rebuild();
    }

    // :136-139 dragEnded()
    private void DragEnded(AppSettings settings)
    {
        if (drag.Id is null) return;
        Transaction.WithAnimation(Animation.Spring(response: 0.32, dampingFraction: 0.8), () => drag = new RowDrag<ModelPin>());
        Rebuild();
    }

    // :152-157 static func matches(_:id:provider:query:) -> Bool
    public static bool Matches(ModelOption? option, string id, ProviderKind provider, string query)
    {
        if (query.Length == 0) return true;
        var fields = new string?[]
        {
            option?.ShortName,
            option?.Name,
            option?.Detail,
            id,
            provider.DisplayName,
        };
        foreach (var field in fields)
            if (field is { } value && SwiftString.LocalizedCaseInsensitiveContains(value, query))
                return true;
        return false;
    }

    /// <summary>:141-150 <c>private struct PinnedRowHeightReporter: ViewModifier</c>: real height reporting is
    /// wired directly at the row's SizeChanged in YourModels, matching FollowUpQueueTab's RowHeightReporter, which
    /// is also a pass-through Body.</summary>
    private sealed record PinnedRowHeightReporter(ModelPin Pin, Binding<ValueMap<ModelPin, double>> Heights)
    {
        // :145-149 func body(content:)
        public Control Body(Control content) => content;
    }
}

/// <summary>:160-274 <c>private struct PinnedModelRow: View</c>: one chosen model, with its reorder grip.</summary>
internal sealed class PinnedModelRow : Decorator
{
    private Point? dragOrigin;
    private TopLevel? dragRoot;

    // :162,164-166 let pin, isDragged, onDragChanged, onDragEnded; :173-264 body
    // AppModel is passed directly rather than re-read via AppEnvironment.GetAppModel(this): this row is
    // constructed and parented in the same call as ModelsSettingsPage.Rebuild(), which already has it in scope,
    // and reading the environment here would need the same not-yet-attached deferral HydraCookbookRow required.
    public PinnedModelRow(AppModel model, ModelPin pin, bool isDragged, Action<double> onDragChanged, Action onDragEnded)
    {
        var registry = model.Providers;
        var settings = model.Settings;
        var option = registry.Model(pin.ModelID, pin.Provider);
        var preference = settings.Preference(pin.Provider, pin.ModelID);

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // :178-191 the reorder grip
        var grip = SwiftImage.SystemName("line.3.horizontal")
            .Font(SwiftFont.System(11, SwiftFont.Weight.Medium)); // :179 literal size 11, not a text style
        grip.ForegroundStyle(Chrome.SecondaryText);
        grip.Opacity = 0.7;
        var gripHost = new Border { Width = 24, Height = 24, Child = grip };
        gripHost.PointerStyle(isDragged ? PointerStyle.GrabActive : PointerStyle.GrabIdle); // :184
        ToolTip.SetTip(gripHost, "Drag to reorder");
        AutomationProperties.SetName(gripHost, "Drag to reorder");
        gripHost.PointerEntered += (_, _) => grip.Opacity = 1;
        gripHost.PointerExited += (_, _) => grip.Opacity = 0.7;
        gripHost.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(gripHost).Properties.IsLeftButtonPressed) return;
            dragRoot = TopLevel.GetTopLevel(gripHost);
            dragOrigin = e.GetPosition(dragRoot);
            e.Pointer.Capture(gripHost);
        };
        gripHost.PointerMoved += (_, e) =>
        {
            if (dragOrigin is not { } origin || dragRoot is null) return;
            var current = e.GetPosition(dragRoot);
            onDragChanged(current.Y - origin.Y);
        };
        gripHost.PointerReleased += (_, _) =>
        {
            if (dragOrigin is null) return;
            dragOrigin = null;
            dragRoot = null;
            onDragEnded();
        };
        Grid.SetColumn(gripHost, 0);
        row.Children.Add(gripHost);

        // :192-244 the block: provider icon, title, summary; click opens the effort popover.
        var icon = new ProviderIcon { Provider = pin.Provider, Size = 16 };
        var iconHost = new Border { Width = 22, Child = icon, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(iconHost, 1);
        row.Children.Add(iconHost);

        var texts = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 8, 0) };
        var title = new TextBlock { Text = option?.ShortName ?? pin.ModelID, Foreground = Chrome.PrimaryText.Brush(texts) }; // :200
        SwiftFont.System(13).Apply(title);
        texts.Children.Add(title);
        var summary = new TextBlock { Text = Summary(pin, option, preference), Foreground = Chrome.SecondaryText.Brush(texts) };
        SwiftFont.System(11).Apply(summary);
        texts.Children.Add(summary);

        var block = new Button
        {
            Content = texts,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        // BLOCKED: PR-SETTINGS-189's effort/fast-mode popover needs App.Views.Composer.EffortSlider's
        // EffortSliderCard, which is still MARROW_STUB at nearly every member (39 sites) and not this unit's own
        // file. Left unwired rather than guessed; revisit once that unit is ported.
        Grid.SetColumn(block, 2);
        row.Children.Add(block);

        var hint = SwiftImage.SystemName("slider.horizontal.3").Font(SwiftFont.System(12));
        hint.ForegroundStyle(Chrome.SecondaryText);
        hint.Opacity = 0;
        hint.Margin = new Thickness(0, 0, 8, 0);
        block.PointerEntered += (_, _) => hint.Opacity = 1;
        block.PointerExited += (_, _) => hint.Opacity = 0;
        Grid.SetColumn(hint, 3);
        row.Children.Add(hint);

        var remove = new RowControl("minus.circle", "Remove from the picker", true, // :246-248
            () => Transaction.WithAnimation(Chrome.PanelSlide, () => settings.RemoveFromModelList(pin)));
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

        var surface = new Border
        {
            Child = row,
            Padding = new Thickness(10, 9, Chrome.RowControlTrailingPadding, 9), // :250 padding leading 10
        };
        if (isDragged) // :255-263
        {
            surface.Background = Chrome.Overlay(0.12).Brush(surface);
            surface.CornerRadius = new CornerRadius(10);
            surface.BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), Blur = 10, OffsetY = 4 });
            surface.RenderTransform = new ScaleTransform(1.02, 1.02);
        }

        Child = surface;
    }

    // :266-273 summary(option:preference:)
    private static string Summary(ModelPin pin, ModelOption? option, ModelPreference preference)
    {
        var parts = new List<string> { pin.Provider.DisplayName };
        if (option is { Efforts.Count: > 0 } present)
            parts.Add(ModelOption.EffortTitle(preference.Effort ?? present.DefaultEffort ?? "") + " effort");
        if (preference.FastMode && option?.SupportsFast == true)
            parts.Add("Fast");
        return string.Join(" · ", parts); // U+00B7 MIDDLE DOT
    }
}

/// <summary>:276-296 <c>private struct RowControl: View</c>.</summary>
internal sealed class RowControl : Decorator
{
    // :277-280 let symbol, help, isEnabled, action; :282-295 body
    public RowControl(string symbol, string help, bool isEnabled, Action action)
    {
        var icon = SwiftImage.SystemName(symbol).Font(SwiftFont.System(12, SwiftFont.Weight.Medium));
        icon.ForegroundStyle(Chrome.SecondaryText);
        var button = new Button
        {
            Content = icon,
            Width = 24,
            Height = 24,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            IsEnabled = isEnabled,
            Opacity = isEnabled ? 1 : 0.35,
        };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, help);
        AutomationProperties.SetName(button, help);
        Child = button;
    }
}

/// <summary>:298-413 <c>private struct ProviderModelsSection: View</c>: one provider's catalogue.</summary>
internal sealed class ProviderModelsSection : Decorator
{
    // :299-305 let provider, var query = "", @Binding var isExpanded; :307-412 body
    // AppModel is passed directly for the same reason as PinnedModelRow's (see its comment).
    public ProviderModelsSection(AppModel model, ProviderKind provider, Binding<bool> isExpanded, string query = "")
    {
        var registry = model.Providers;
        var settings = model.Settings;

        var stack = new StackPanel { Spacing = Chrome.SectionHeaderSpacing, HorizontalAlignment = HorizontalAlignment.Stretch }; // :325

        // :326 CollapsibleProviderHeader(provider:isExpanded:subject:)
        stack.Children.Add(new CollapsibleProviderHeader(provider, isExpanded, "model list"));

        // :329-410 the eager card
        var cardContent = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var card = new Border
        {
            Child = cardContent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Chrome.Overlay(0.03).Brush(cardContent),
            CornerRadius = new CornerRadius(Chrome.CardCornerRadius),
            ClipToBounds = true,
        };
        stack.Children.Add(card);
        Child = stack;

        void RebuildCard()
        {
            cardContent.Children.Clear();
            var expanded = isExpanded.Value;
            var catalogue = registry.Models(provider); // :312
            var isFull = settings.ModelList.Count >= AppSettings.ModelListLimit; // :313
            var isFiltering = query.Length > 0; // :314

            if (catalogue.Count == 0) // :330-344
            {
                var loading = registry.LoadingCatalogs.Contains(provider);
                var content = new ChromeRow(loading ? "Loading models…" : "No models loaded", () => // U+2026 HORIZONTAL ELLIPSIS
                {
                    var loadButton = new Button { Content = "Load models", IsEnabled = !loading }; // :337 .buttonStyle(.glass)
                    SwiftGlass.StyleButton(loadButton);
                    loadButton.Click += async (_, _) => await registry.LoadCatalog(provider, force: true);
                    return loadButton;
                });
                var reveal = new ChromeRevealLayout(expanded || isFiltering ? 1 : 0) { ClipToBounds = true };
                reveal.Children.Add(content);
                cardContent.Children.Add(reveal);
            }
            else
            {
                var visibleIDs = new HashSet<string>(catalogue
                    .Where(option => isFiltering
                        ? ModelsSettingsPage.Matches(option, option.Id, provider, query)
                        : expanded || settings.IsInModelList(new ModelPin(provider, option.Id)))
                    .Select(option => option.Id)); // :317-321
                var firstVisibleID = catalogue.FirstOrDefault(option => visibleIDs.Contains(option.Id))?.Id; // :322

                foreach (var option in catalogue) // :346-395
                {
                    var isVisible = visibleIDs.Contains(option.Id);
                    var dividerReveal = new ChromeRevealLayout(isVisible && option.Id != firstVisibleID ? 1 : 0) { ClipToBounds = true };
                    dividerReveal.Children.Add(new ChromeRowDivider());
                    cardContent.Children.Add(dividerReveal);

                    var pin = new ModelPin(provider, option.Id);
                    var isAdded = settings.IsInModelList(pin);

                    var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                    if (option.SupportsFast)
                    {
                        var bolt = SwiftImage.SystemName("bolt").Font(SwiftFont.System(11));
                        bolt.ForegroundStyle(Chrome.SecondaryText);
                        ToolTip.SetTip(bolt, "Supports fast mode");
                        controls.Children.Add(bolt);
                    }
                    var toggle = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
                    void UpdateToggle()
                    {
                        var added = settings.IsInModelList(pin);
                        var glyph = SwiftImage.SystemName(added ? "checkmark.circle.fill" : "plus.circle").Font(SwiftFont.System(17));
                        glyph.ForegroundStyle(added ? Chrome.Accent : Chrome.SecondaryText);
                        toggle.Content = glyph;
                        var full = settings.ModelList.Count >= AppSettings.ModelListLimit;
                        toggle.IsEnabled = added || !full;
                        toggle.Opacity = !added && full ? 0.35 : 1;
                        var help = added ? "Remove from the picker" : full ? "The picker holds up to " + AppSettings.ModelListLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) + " models" : "Add to the picker";
                        ToolTip.SetTip(toggle, help);
                        AutomationProperties.SetName(toggle, added ? "Remove " + option.ShortName + " from the picker" : "Add " + option.ShortName + " to the picker");
                    }
                    UpdateToggle();
                    toggle.Click += (_, _) =>
                    {
                        void Flip()
                        {
                            if (settings.IsInModelList(pin)) settings.RemoveFromModelList(pin);
                            else settings.AddToModelList(pin);
                        }
                        if (isFiltering) Flip();
                        else Transaction.WithAnimation(Chrome.PanelSlide, Flip);
                        UpdateToggle();
                    };
                    controls.Children.Add(toggle);

                    var rowContent = new ChromeRow(option.ShortName, () => controls, option.Detail);
                    rowContent.IsEnabled = isVisible;
                    rowContent.IsHitTestVisible = isVisible;
                    var rowReveal = new ChromeRevealLayout(isVisible ? 1 : 0) { ClipToBounds = true };
                    rowReveal.Children.Add(rowContent);
                    cardContent.Children.Add(rowReveal);
                }
            }

            var cardVisible = expanded || isFiltering || (catalogue.Count > 0
                ? catalogue.Any(option => isFiltering
                    ? ModelsSettingsPage.Matches(option, option.Id, provider, query)
                    : expanded || settings.IsInModelList(new ModelPin(provider, option.Id)))
                : false); // :323-324
            card.IsHitTestVisible = cardVisible;
            AutomationProperties.SetAccessibilityView(card, cardVisible ? AccessibilityView.Content : AccessibilityView.Raw);
        }

        RebuildCard();
        this.Track(() => (isExpanded.Value, settings.ModelList, registry.Statuses), _ => RebuildCard());
        this.Task(() => provider, async _ => await registry.LoadCatalog(provider)); // :411, not forced
    }
}

/// <summary>:416-448 <c>struct CollapsibleProviderHeader: View</c>: the disclosure header over one provider's
/// settings or model card. New at B3, out of ModelsSettingsPage's own inline header (used again by
/// App.Views.Settings.SettingsView).</summary>
public sealed class CollapsibleProviderHeader : Decorator
{
    // :417-420 let provider, @Binding var isExpanded, let subject; :422-447 body
    public CollapsibleProviderHeader(ProviderKind provider, Binding<bool> isExpanded, string subject)
    {
        var headerContent = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        // :427-428. Marrow.UI.Common.ProviderIcon has no foreground property to carry :428's
        // .foregroundStyle(Chrome.primaryText): it renders each provider's own colored logo, which
        // .foregroundStyle has no visible effect on for a full-color (non-template) SwiftUI Image either.
        var headerIcon = new ProviderIcon { Provider = provider, Size = 16 };
        Grid.SetColumn(headerIcon, 0);
        headerContent.Children.Add(headerIcon);

        var headerName = new TextBlock { Text = provider.DisplayName, Margin = new Thickness(8, 0, 0, 0) }; // :429
        SwiftFont.System(13, SwiftFont.Weight.Semibold).Apply(headerName); // :430
        headerName.Foreground = SwiftColor.Secondary.Brush(headerName); // :431 .foregroundStyle(.secondary), ED-062
        Grid.SetColumn(headerName, 1);
        headerContent.Children.Add(headerName);

        var chevron = SwiftImage.SystemName("chevron.right").Font(SwiftFont.System(11, SwiftFont.Weight.Semibold)); // :433-434 literal size 11, not a text style
        chevron.ForegroundStyle(Chrome.SecondaryText); // :435
        Grid.SetColumn(chevron, 3);
        headerContent.Children.Add(chevron);

        // :423-436 rotation shares the disclosure transaction; equal notifications must not restart it.
        var lastExpanded = isExpanded.Value;
        var rotation = new SettingsScalarAnimation(chevron, lastExpanded ? 90 : 0, value => chevron.RenderTransform = new RotateTransform(value));
        chevron.RenderTransform = new RotateTransform(lastExpanded ? 90 : 0);

        var header = new Button
        {
            Content = headerContent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 22, // :441
        };
        header.Click += (_, _) =>
        {
            Transaction.WithAnimation(Chrome.PanelSlide, () => { isExpanded.Value = !isExpanded.Value; UpdateHeader(); }); // :423-436
            // isExpanded is a Binding<bool> whose source (ModelsSettingsPage.providerExpansion, a plain
            // Dictionary field, or SettingsView's own equivalent) is not an ObservableModel property, so
            // this.Track(() => isExpanded.Value, ...) below never observes this write (ObservationTracking only
            // records reads that go through ObservableModel.Read - R-VIEW-2 calls for @State to be a ViewState:
            // MainActorModel property precisely so a Binding over it is trackable; providerExpansion predates this
            // unit's B3 change and violates that rule, which is out of this lane's own file scope to redesign).
            // Calling UpdateHeader() here directly, from the one place this control itself writes isExpanded,
            // keeps the header's own chevron/tooltip/accessibility state correct regardless of that gap.
            UpdateHeader();
        };

        void UpdateHeader()
        {
            var expanded = isExpanded.Value;
            if (expanded != lastExpanded) { lastExpanded = expanded; rotation.Set(expanded ? 90 : 0, Transaction.Current); } // :436
            ToolTip.SetTip(header, expanded ? "Hide the " + subject : "Show the " + subject); // :442
            AutomationProperties.SetName(header, provider.DisplayName); // :443 .accessibilityLabel
            // :444 .accessibilityValue(Text(isExpanded ? "Expanded" : "Collapsed")): Avalonia's AutomationProperties
            // has no separate value reader either, but ChatBadge.cs's own header carries its accessibilityValue
            // through SetItemStatus rather than folding it into Name, so this follows that (more recent) precedent.
            AutomationProperties.SetItemStatus(header, expanded ? "Expanded" : "Collapsed");
            AutomationProperties.SetHelpText(header, "Shows or hides the " + subject); // :445 .accessibilityHint
        }
        UpdateHeader();
        // Kept for the case isExpanded's source becomes a real ObservableModel property (SettingsView or a future
        // fix to providerExpansion): a change made from outside this control's own Click would then refresh it too.
        this.Track(() => isExpanded.Value, _ => UpdateHeader());

        // :446 .accessibilityAddTraits(.isHeader): Avalonia's nearest match is a heading level (SetHeadingLevel),
        // read by screen readers as a heading; the source names no level, so this is level 1 (ENGINEERING DECISION,
        // UNVERIFIED under OD-0001: no reference-runtime capture of the level VoiceOver reports for .isHeader alone).
        AutomationProperties.SetHeadingLevel(header, 1);

        Child = header;
    }
}
