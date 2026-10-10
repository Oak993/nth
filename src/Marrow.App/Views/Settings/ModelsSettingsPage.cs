using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

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
    // :12-14 @State providerExpansion: absent means expanded. An ObservableModel so the sections that read the binding
    // are told when a header flips it (a plain dictionary is invisible to observation tracking).
    private sealed class ViewState : MainActorModel
    {
        private ValueMap<ProviderKind, bool> expansion = ValueMap<ProviderKind, bool>.Empty;
        public ValueMap<ProviderKind, bool> Expansion { get => Read(expansion); set => Set(ref expansion, value); }
    }

    private readonly ViewState state = new();
    private readonly Dictionary<ProviderKind, ProviderModelsSection> sections = new(); // a section keeps its identity (and focus) across body updates
    private AppModel? sectionsModel;
    private readonly Control accounts = new MultiAccountHost<ModelsAccountsView>(context => new ModelsAccountsView(context.Accounts, context.OpenUsage, context.OpenKeys)); // PORT-ONLY T11 step 2
    private ModelPin? pressedPin; // the grip currently held down, tracked on the page so a rebuilt row never loses the drag
    private Point? dragOrigin;
    private TopLevel? dragRoot;
    private readonly StackPanel root = new() { Orientation = Orientation.Vertical, Spacing = Chrome.SectionSpacing, HorizontalAlignment = HorizontalAlignment.Stretch }; // :30
    private ValueMap<ModelPin, double> rowHeights = ValueMap<ModelPin, double>.Empty; // :10
    private RowDrag<ModelPin> drag = new(); // :9
    // :263 .animation(.spring(response: 0.25, dampingFraction: 0.8), value: isDragged): how far the grabbed row is lifted (0 at rest,
    // 1 held). It lives on the page because every row is rebuilt as the pointer moves; the row that was last held keeps it while it drops.
    private readonly SettingsScalarAnimation liftMotion;
    private ModelPin? liftedPin;
    private double lift;
    private readonly Dictionary<ModelPin, PinnedModelRow> pinnedRows = new();
    private static Animation LiftSpring => Animation.Spring(response: 0.25, dampingFraction: 0.8);

    // :6 var query = ""; :19-100 body
    public ModelsSettingsPage(string query = "")
    {
        this.query = query;
        Child = root;
        liftMotion = new SettingsScalarAnimation(root, 0, value =>
        {
            lift = value;
            if (liftedPin is { } held && pinnedRows.TryGetValue(held, out var row)) row.SetLift(value);
        });
        // :20-22 body reads model.settings.modelList, the providers' statuses and catalogues, so it re-evaluates with them.
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return (Model: model, model.Settings.ModelList, model.Settings.DisabledProviders, model.Providers.Statuses,
                model.Providers.Catalogs, model.Providers.LoadingCatalogs);
        }, _ => Rebuild());
        // The reorder gesture lives on the page's own stack, which survives every rebuild of the rows under it.
        root.PointerMoved += (_, e) => GripMoved(e);
        root.PointerReleased += (_, _) => GripReleased();
        root.PointerCaptureLost += (_, _) => GripReleased();
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
        // SetQuery may run before the page is attached; Track's own deferred call guards the first Rebuild, but a
        // search edit that races attachment must not throw.
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }
        if (!ReferenceEquals(sectionsModel, model)) { sections.Clear(); sectionsModel = model; }

        var registry = model.Providers;
        var settings = model.Settings;
        var trimmed = FoundationText.Trim(query, includeNewlines: true); // :23

        var pins = settings.ModelList; // :7
        var visiblePins = VisiblePins(pins, registry, trimmed); // :105-110

        var desired = new List<Control>();
        if (trimmed.Length == 0) desired.Add(accounts); // PORT-ONLY (T11 step 2, OD-0114): the accounts, "When an account runs out" and the Handshake, above the model lists
        if (trimmed.Length == 0 || visiblePins.Count > 0) // :33
        {
            desired.Add(new ChromeSection("Your models", () => YourModels(model, pins, visiblePins, registry, settings, trimmed))); // :30-65
        }

        var providers = registry.AvailableProviders.Where(provider => // :25-27
            trimmed.Length == 0 || registry.Models(provider).Any(option => Matches(option, option.Id, provider, trimmed))).ToList();

        foreach (var provider in providers) // :75-84
        {
            if (sections.TryGetValue(provider, out var existing)) existing.SetQuery(trimmed);
            else
                sections[provider] = existing = new ProviderModelsSection(
                    model,
                    provider,
                    new Binding<bool>(
                        () => state.Expansion.TryGetValue(provider, out var expanded) ? expanded : true,
                        value => state.Expansion = state.Expansion.Mutating(map => map[provider] = value)),
                    trimmed);
            desired.Add(existing);
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
            desired.Add(noMatch);
        }

        // Keep every control that stays (a section holds the focus of its buttons); only what changed is added or removed.
        for (var index = root.Children.Count - 1; index >= 0; index--)
            if (!desired.Contains(root.Children[index])) root.Children.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            var at = root.Children.IndexOf(desired[index]);
            if (at == index) continue;
            if (at < 0) root.Children.Insert(index, desired[index]);
            else root.Children.Move(at, index);
        }
    }

    // :105-110 visiblePins
    private static ValueList<ModelPin> VisiblePins(ValueList<ModelPin> pins, ProviderRegistry registry, string trimmed) =>
        new(pins.Where(pin => Matches(registry.Model(pin.ModelID, pin.Provider), pin.ModelID, pin.Provider, trimmed)));

    // :30-65 the "Your models" section content
    private Control YourModels(AppModel model, ValueList<ModelPin> pins, ValueList<ModelPin> visiblePins, ProviderRegistry registry, AppSettings settings, string trimmed)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        pinnedRows.Clear();

        if (pins.Count == 0) // :34-40
        {
            stack.Children.Add(new ChromeCard(() => new ChromeRow(
                "No models chosen yet",
                () => new Border(),
                "Until you add some below, the picker shows every model from your providers.")));
        }
        else
        {
            // :42-46 a plain stack, not lazy, so a dragged row draws on top and does not fly to the origin.
            var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var pin in visiblePins) // :47-62
            {
                var isDragged = drag.Id == pin; // :49
                // :50-58 VStack { divider; row }: the slot is what is offset, lifted and measured (:59-60).
                var slot = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                slot.Children.Add(new ChromeRowDivider { Opacity = (visiblePins.Count > 0 && pin == visiblePins[0]) || isDragged ? 0 : 1 }); // :51, ruling :50
                var pinnedRow = new PinnedModelRow(
                    model,
                    pin,
                    isDragged,
                    _ => { },
                    () => { },
                    press => GripPressed(pin, press),
                    liftedPin == pin ? lift : 0); // :255-263 the lift is animated, not a step
                pinnedRows[pin] = pinnedRow;
                slot.Children.Add(pinnedRow);
                slot.RenderTransform = new TranslateTransform(0, isDragged ? drag.VisualOffset : 0); // :59
                slot.ZIndex = isDragged ? 1 : 0; // :60
                slot.SizeChanged += (_, args) =>
                {
                    if (args.NewSize.Height <= 0) return;
                    rowHeights = rowHeights.Mutating(map => map[pin] = args.NewSize.Height); // :141-150 PinnedRowHeightReporter
                    if (rowHeights.Count > pins.Count) // :93-99
                        rowHeights = new ValueMap<ModelPin, double>(rowHeights.Where(entry => pins.Contains(entry.Key)));
                };
                rows.Children.Add(slot);
            }
            stack.Children.Add(new ChromeCard(() => rows)); // :34-64 the eager model card
        }

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
        return stack;
    }

    // :175-206 the grip's DragGesture(minimumDistance: 1, coordinateSpace: .global), held on the page so a rebuild of the
    // rows never drops it: onChanged reports the height of the translation, onEnded ends the drag.
    private void GripPressed(ModelPin pin, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        dragRoot = top;
        dragOrigin = e.GetPosition(top);
        pressedPin = pin;
        e.Pointer.Capture(root);
        e.Handled = true;
    }

    private void GripMoved(PointerEventArgs e)
    {
        if (pressedPin is not { } pin || dragOrigin is not { } origin || dragRoot is null) return;
        var delta = e.GetPosition(dragRoot) - origin;
        if (drag.Id != pin && Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < 1) return; // minimumDistance: 1
        DragChanged(pin, delta.Y);
    }

    private void GripReleased()
    {
        if (pressedPin is null) return;
        pressedPin = null;
        dragOrigin = null;
        dragRoot = null;
        DragEnded();
    }

    // :112-134 dragChanged(_:translation:)
    private void DragChanged(ModelPin pin, double translation)
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }
        var registry = model.Providers;
        var settings = model.Settings;

        if (drag.Id != pin)
        {
            drag = new RowDrag<ModelPin> { Id = pin };
            liftedPin = pin; // :263 isDragged turns true: the lift springs in
            liftMotion.Set(1, LiftSpring);
        }
        drag = drag with { Translation = translation };

        var trimmed = FoundationText.Trim(query, includeNewlines: true);
        var order = VisiblePins(settings.ModelList, registry, trimmed); // re-read fresh on every move
        var (nextDrag, _) = drag.Settle(order, rowHeights, fallbackHeight: 54,
            (neighbour, placeAfter) => settings.MoveModel(pin, neighbour, placeAfter),
            pastCentre: 0);
        drag = nextDrag;
        Rebuild(); // drag.id and drag.translation are observed state: the grabbed row follows the pointer at once
    }

    // :136-139 dragEnded()
    private void DragEnded()
    {
        if (drag.Id is null) return;
        liftMotion.Set(0, LiftSpring); // :263 isDragged turns false: the drop springs back
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
    // :169 @State private var isPresented = false
    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    private Point? dragOrigin;
    private TopLevel? dragRoot;

    // :162,164-166 let pin, isDragged, onDragChanged, onDragEnded; :173-264 body
    // AppModel is passed directly rather than re-read via AppEnvironment.GetAppModel(this): this row is
    // constructed and parented in the same call as ModelsSettingsPage.Rebuild(), which already has it in scope,
    // and reading the environment here would need the same not-yet-attached deferral HydraCookbookRow required.
    public PinnedModelRow(AppModel model, ModelPin pin, bool isDragged, Action<double> onDragChanged, Action onDragEnded, Action<PointerPressedEventArgs>? onGripPressed = null, double? lift = null)
    {
        var state = new ViewState();
        var registry = model.Providers;
        var settings = model.Settings;
        var option = registry.Model(pin.ModelID, pin.Provider);
        var preference = settings.Preference(pin.Provider, pin.ModelID);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // :178-191 the reorder grip
        var grip = SwiftImage.SystemName("line.3.horizontal")
            .Font(SwiftFont.System(11, SwiftFont.Weight.Medium)); // :179 literal size 11, not a text style
        grip.ForegroundStyle(Chrome.SecondaryText);
        grip.Opacity = 0.7;
        var gripHost = new Border { Width = 24, Height = 24, Child = grip, Margin = new Thickness(0, 0, 10, 0) }; // :173 HStack(spacing: 10)
        gripHost.PointerStyle(isDragged ? PointerStyle.GrabActive : PointerStyle.GrabIdle); // :184
        ToolTip.SetTip(gripHost, "Drag to reorder");
        AutomationProperties.SetName(gripHost, "Drag to reorder");
        gripHost.PointerEntered += (_, _) => grip.Opacity = 1;
        gripHost.PointerExited += (_, _) => grip.Opacity = 0.7;
        gripHost.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(gripHost).Properties.IsLeftButtonPressed) return;
            if (onGripPressed is not null) { onGripPressed(e); return; } // the page holds the gesture
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
        var label = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        label.Children.Add(iconHost);

        var texts = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 12 + 8 + 12, 0) }; // :195 label HStack(spacing: 12) before the text; :207 Spacer(minLength: 8) with the spacing on both its sides after it
        var title = new TextBlock { Text = option?.ShortName ?? pin.ModelID, Foreground = Chrome.PrimaryText.Brush(texts) }; // :200
        SwiftFont.System(13).Apply(title);
        texts.Children.Add(title);
        var summary = new TextBlock { Text = Summary(pin, option, preference), Foreground = Chrome.SecondaryText.Brush(texts) };
        SwiftFont.System(11).Apply(summary);
        texts.Children.Add(summary);
        // :176 `settings.preference(for:model:)` is read by this row's own body: a choice made in the effort card, or anywhere else,
        // re-draws the summary here without rebuilding the row (which would close the open card, whose presentation state is the row's).
        this.Track(() => settings.Preference(pin.Provider, pin.ModelID), updated => summary.Text = Summary(pin, option, updated));
        Grid.SetColumn(texts, 1);
        label.Children.Add(texts);

        var block = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 10, 0), // :173 spacing before the remove control
        };
        // :192-194, :217-241. Click presents the effort card; `arrowEdge: .trailing` is PlacementMode.Right, as
        // SidebarView.cs and HydraSettingsPage.cs already read it. Each closure re-reads the settings' preference,
        // so a write to one field keeps the other (:227-229, :235-237 `var updated = settings.preference(...)`).
        var isPresented = new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value);
        block.Click += (_, _) => isPresented.Value = true; // :192-194
        var popover = PlainControls.Popover(block, isPresented, () =>
        {
            var card = new EffortSliderCard(
                option?.ShortName ?? pin.ModelID, // :219
                pin.Provider, // :220
                option?.Efforts ?? ValueList<string>.Empty, // :221
                option?.DefaultEffort, // :222
                option?.SupportsFast ?? false, // :223
                new Binding<string?>(
                    () => settings.Preference(pin.Provider, pin.ModelID).Effort, // :225
                    effort => settings.SetPreference(settings.Preference(pin.Provider, pin.ModelID) with { Effort = effort }, pin.Provider, pin.ModelID)), // :224-230
                new Binding<bool>(
                    () => settings.Preference(pin.Provider, pin.ModelID).FastMode, // :233
                    isOn => settings.SetPreference(settings.Preference(pin.Provider, pin.ModelID) with { FastMode = isOn }, pin.Provider, pin.ModelID)), // :234-237
                () => settings.SetPreference(new ModelPreference(), pin.Provider, pin.ModelID)); // :240
            card.Width = 330; // :242 .frame(width: 330)
            return card.PresentedChrome(); // :243 .presentedChrome()
        }, PlacementMode.Right);
        Grid.SetColumn(block, 1);
        row.Children.Add(block);
        row.Children.Add(popover);

        var hint = SwiftImage.SystemName("slider.horizontal.3").Font(SwiftFont.System(12));
        hint.ForegroundStyle(Chrome.SecondaryText);
        hint.Opacity = 0;
        // :208-214 the hint is part of the same clickable model label.
        block.PointerEntered += (_, _) => hint.Opacity = 1;
        block.PointerExited += (_, _) => hint.Opacity = 0;
        Grid.SetColumn(hint, 2);
        label.Children.Add(hint);

        var remove = new RowControl("minus.circle", "Remove from the picker", true, // :246-248
            () => Transaction.WithAnimation(Chrome.PanelSlide, () => settings.RemoveFromModelList(pin)));
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        var surface = new Border
        {
            Child = row,
            Padding = new Thickness(10, 9, Chrome.RowControlTrailingPadding, 9), // :250 padding leading 10
        };
        // :255-263 the lifted look is a layer behind the row, scaled with it: a fill, a shadow and the 1.02 scale, all
        // driven by one amount (0 at rest, 1 held) that the page springs (SetLift).
        liftLayer = new Border { CornerRadius = new CornerRadius(10), IsHitTestVisible = false };
        // :259 the shadow is the filled shape's own coverage, cast at the fill's alpha and under it (SwiftShadow.Caster, ED-358).
        liftShadow = new SwiftShadow.Caster(liftLayer, Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), radius: 10, y: 4); // :259 (sigma 10, ED-327)
        host = new Panel { Children = { liftShadow, liftLayer, surface } };
        SetLift(lift ?? (isDragged ? 1 : 0));
        Child = host;
    }

    private readonly Border liftLayer;
    private readonly SwiftShadow.Caster liftShadow;
    private readonly Panel host;

    /// <summary>:255-262 the lift: <c>.background { if isDragged { RoundedRectangle(cornerRadius: 10, style: .continuous)
    /// .fill(Chrome.overlay(0.12)).shadow(color: .black.opacity(0.28), radius: 10, y: 4) } }</c> and <c>.scaleEffect(isDragged ? 1.02 : 1)</c>.
    /// The background appears and goes by the default opacity transition, the scale follows the spring: one amount drives both.</summary>
    public void SetLift(double amount)
    {
        if (amount <= 0)
        {
            liftLayer.Background = null; // no fill, no shadow (the caster draws the fill's alpha)
            liftLayer.Opacity = 1;
            host.RenderTransform = null;
            return;
        }
        liftLayer.Background = Chrome.Overlay(0.12).Brush(liftLayer);
        liftLayer.Opacity = Math.Clamp(amount, 0, 1);
        var scale = 1 + 0.02 * amount;
        host.RenderTransform = new ScaleTransform(scale, scale);
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
    /// <summary>One catalogue row: the divider above it and the row itself, each in its own reveal (:346-395).</summary>
    private sealed record RowParts(ModelOption Option, ChromeRevealLayout DividerReveal, SettingsScalarAnimation DividerMotion,
        ChromeRow Row, ChromeRevealLayout RowReveal, SettingsScalarAnimation RowMotion, Button Toggle);

    private string query;
    private readonly Action sync;

    // :299-305 let provider, var query = "", @Binding var isExpanded; :307-412 body
    // AppModel is passed directly for the same reason as PinnedModelRow's (see its comment).
    public ProviderModelsSection(AppModel model, ProviderKind provider, Binding<bool> isExpanded, string query = "")
    {
        this.query = query;
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

        // The rows are built once per catalogue (a stable ForEach keyed on option.id, :346) and then only updated, so
        // an activated button keeps its identity and keyboard focus while its glyph and state change.
        ValueList<ModelOption>? builtCatalogue = null;
        var builtLoading = false;
        var arrivingFromSkeleton = false; // nth step 7: the rows that replace the skeleton rise in
        var parts = new List<RowParts>();
        ChromeRevealLayout? emptyReveal = null;
        SettingsScalarAnimation? emptyMotion = null;

        var shownGlyphs = new System.Runtime.CompilerServices.ConditionalWeakTable<Button, SwiftImage>();

        void UpdateToggle(Button toggle, ModelOption option, ModelPin pin)
        {
            var added = settings.IsInModelList(pin);
            var glyph = SwiftImage.SystemName(added ? "checkmark.circle.fill" : "plus.circle").Font(SwiftFont.System(17));
            glyph.ForegroundStyle(added ? Chrome.Accent : Chrome.SecondaryText);
            // :376-379 .contentTransition(.symbolEffect(.replace)): adding or removing replaces the glyph in two steps
            // (SymbolReplace); a change of colour alone swaps it in place.
            if (toggle.Content is not Panel host) toggle.Content = host = new Panel();
            if (shownGlyphs.TryGetValue(toggle, out var current) && host.Children.Contains(current))
            {
                if (current.SymbolName != glyph.SymbolName)
                {
                    host.Children.Add(glyph);
                    host.Children.Remove(current);
                    host.Children.Add(current); // over the new one while it goes
                    var outgoing = current;
                    SymbolReplace.Play(outgoing, glyph, () => host.Children.Remove(outgoing));
                }
                else host.Children[host.Children.IndexOf(current)] = glyph;
            }
            else host.Children.Add(glyph);
            shownGlyphs.AddOrUpdate(toggle, glyph);
            var full = settings.ModelList.Count >= AppSettings.ModelListLimit;
            toggle.IsEnabled = added || !full;
            toggle.Opacity = !added && full ? 0.35 : 1;
            var help = added ? "Remove from the picker" : full ? "The picker holds up to " + AppSettings.ModelListLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) + " models" : "Add to the picker";
            ToolTip.SetTip(toggle, help);
            AutomationProperties.SetName(toggle, added ? "Remove " + option.ShortName + " from the picker" : "Add " + option.ShortName + " to the picker");
        }

        void Build(ValueList<ModelOption> catalogue, bool expanded, bool isFiltering)
        {
            cardContent.Children.Clear();
            parts.Clear();
            emptyReveal = null;
            emptyMotion = null;
            if (catalogue.Count == 0) // :330-344
            {
                var loading = registry.LoadingCatalogs.Contains(provider);
                Button LoadButton()
                {
                    var loadButton = new Button { Content = "Load models", IsEnabled = !loading }; // :337 .buttonStyle(.glass)
                    SwiftGlass.StyleButton(loadButton);
                    loadButton.Click += async (_, _) => await registry.LoadCatalog(provider, force: true);
                    return loadButton;
                }
                // nth step 7: while the models load, three skeleton rows (3 x 40 points) take the place of the "Loading models…" line; the
                // text stays as the rows' accessible name, and the glass button stays at the trailing edge, disabled.
                Control content = loading ? LoadingRows(LoadButton()) : new ChromeRow("No models loaded", LoadButton);
                var start = expanded || isFiltering ? 1 : 0;
                var reveal = new ChromeRevealLayout(start) { ClipToBounds = true };
                reveal.Children.Add(content);
                emptyReveal = reveal;
                emptyMotion = new SettingsScalarAnimation(reveal, start, value => reveal.Fraction = value);
                cardContent.Children.Add(reveal);
                return;
            }

            foreach (var option in catalogue) // :346-395
            {
                var dividerReveal = new ChromeRevealLayout(0) { ClipToBounds = true };
                dividerReveal.Children.Add(new ChromeRowDivider());
                cardContent.Children.Add(dividerReveal);

                var pin = new ModelPin(provider, option.Id);
                var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                if (option.SupportsFast)
                {
                    var bolt = SwiftImage.SystemName("bolt").Font(SwiftFont.System(11));
                    bolt.ForegroundStyle(Chrome.SecondaryText);
                    ToolTip.SetTip(bolt, "Supports fast mode");
                    controls.Children.Add(bolt);
                }
                var toggle = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
                toggle.Click += (_, _) =>
                {
                    void Flip()
                    {
                        if (settings.IsInModelList(pin)) settings.RemoveFromModelList(pin);
                        else settings.AddToModelList(pin);
                    }
                    if (this.query.Length > 0) Flip();
                    else Transaction.WithAnimation(Chrome.PanelSlide, Flip);
                    UpdateToggle(toggle, option, pin);
                };
                controls.Children.Add(toggle);

                var rowContent = new ChromeRow(option.ShortName, () => controls, option.Detail);
                var rowReveal = new ChromeRevealLayout(0) { ClipToBounds = true };
                rowReveal.Children.Add(rowContent);
                cardContent.Children.Add(rowReveal);
                parts.Add(new RowParts(option, dividerReveal, new SettingsScalarAnimation(dividerReveal, 0, value => dividerReveal.Fraction = value),
                    rowContent, rowReveal, new SettingsScalarAnimation(rowReveal, 0, value => rowReveal.Fraction = value), toggle));
            }
        }

        static Control LoadingRows(Button loadButton)
        {
            var skeleton = new NthSkeleton(NthSkeletonKind.List, 3) { Margin = new Thickness(16, 0, 12, 0) };
            AutomationProperties.SetName(skeleton, "Loading models…"); // U+2026 HORIZONTAL ELLIPSIS
            AutomationProperties.SetAccessibilityView(skeleton, AccessibilityView.Content);
            loadButton.HorizontalAlignment = HorizontalAlignment.Right;
            loadButton.VerticalAlignment = VerticalAlignment.Center;
            loadButton.Margin = new Thickness(0, 0, Chrome.RowControlTrailingPadding, 0);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(skeleton);
            Grid.SetColumn(loadButton, 1);
            grid.Children.Add(loadButton);
            return grid;
        }

        var immediate = true;
        void Sync()
        {
            var expanded = isExpanded.Value;
            var catalogue = registry.Models(provider); // :312
            var isFiltering = this.query.Length > 0; // :314
            var loading = registry.LoadingCatalogs.Contains(provider);
            if (builtCatalogue is null || !builtCatalogue.Equals(catalogue) || (catalogue.Count == 0 && loading != builtLoading))
            {
                arrivingFromSkeleton = builtLoading && builtCatalogue?.Count == 0 && catalogue.Count > 0;
                Build(catalogue, expanded, isFiltering);
                builtCatalogue = catalogue;
                builtLoading = loading;
                immediate = true; // a rebuilt structure starts at its state: nothing animates from a new row
            }

            var animation = immediate ? null : Transaction.Current;
            immediate = false;
            if (catalogue.Count == 0)
            {
                emptyMotion!.Set(expanded || isFiltering ? 1 : 0, animation);
                // :341-344 the collapsed empty row is also `.disabled(!cardVisible)` (and not hit-testable, hidden from accessibility;
                // the card below carries those two): a clipped "Load models" button must not take keyboard focus.
                emptyReveal!.IsEnabled = expanded || isFiltering;
                emptyReveal.IsHitTestVisible = expanded || isFiltering;
                AutomationProperties.SetAccessibilityView(emptyReveal, expanded || isFiltering ? AccessibilityView.Content : AccessibilityView.Raw);
            }
            else
            {
                var visibleIDs = new HashSet<string>(catalogue
                    .Where(option => isFiltering
                        ? ModelsSettingsPage.Matches(option, option.Id, provider, this.query)
                        : expanded || settings.IsInModelList(new ModelPin(provider, option.Id)))
                    .Select(option => option.Id)); // :317-321
                var firstVisibleID = catalogue.FirstOrDefault(option => visibleIDs.Contains(option.Id))?.Id; // :322
                foreach (var part in parts)
                {
                    var isVisible = visibleIDs.Contains(part.Option.Id);
                    part.DividerMotion.Set(isVisible && part.Option.Id != firstVisibleID ? 1 : 0, animation);
                    part.RowMotion.Set(isVisible ? 1 : 0, animation);
                    part.Row.IsEnabled = isVisible;
                    part.Row.IsHitTestVisible = isVisible;
                    // :346-395 a row that does not show is clipped, disabled, not hit-testable and accessibilityHidden.
                    AutomationProperties.SetAccessibilityView(part.RowReveal, isVisible ? AccessibilityView.Content : AccessibilityView.Raw);
                    AutomationProperties.SetAccessibilityView(part.DividerReveal, isVisible ? AccessibilityView.Content : AccessibilityView.Raw);
                    UpdateToggle(part.Toggle, part.Option, new ModelPin(provider, part.Option.Id));
                }
                if (arrivingFromSkeleton)
                {
                    arrivingFromSkeleton = false;
                    NthArrive.Rows(parts.Where(part => visibleIDs.Contains(part.Option.Id)).Select(part => (Control)part.RowReveal).ToList()); // nth step 7: 6 px up, 0.36 s, 40 ms apart
                }
            }

            var cardVisible = expanded || isFiltering || (catalogue.Count > 0
                ? catalogue.Any(option => isFiltering
                    ? ModelsSettingsPage.Matches(option, option.Id, provider, this.query)
                    : expanded || settings.IsInModelList(new ModelPin(provider, option.Id)))
                : false); // :323-324
            card.IsHitTestVisible = cardVisible;
            AutomationProperties.SetAccessibilityView(card, cardVisible ? AccessibilityView.Content : AccessibilityView.Raw);
        }

        sync = Sync;
        Sync();
        // :307-410 the body reads the expansion, the chosen list, the catalogue and its loading state.
        this.Track(() => (isExpanded.Value, settings.ModelList, registry.Statuses, registry.Catalogs, registry.LoadingCatalogs), _ => Sync());
        this.Task(() => provider, async _ => await registry.LoadCatalog(provider)); // :411, not forced
    }

    /// <summary>The page hands the trimmed search text to the section it keeps across body updates (:300 var query).</summary>
    public void SetQuery(string value)
    {
        if (query == value) return;
        query = value;
        sync();
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
