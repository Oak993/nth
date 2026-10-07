using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.VisualTree;
using SkiaSharp;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Chat;
using Marrow.App.Port;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1-1347.
// Forms: SwiftUI views are Decorators; bindings and state use the shared main-actor observation and frame seams.
// Capture-only branches are excluded by ED-001. Shared provider artwork and chip styling come from Components (ED-215).

namespace Marrow.App.Views.Composer;

/// <summary>:4-31 <c>@MainActor enum ModelCatalog</c>.</summary>
public static class ModelCatalog
{
    /// <summary>:7-12 <c>struct Entry: Identifiable, Hashable</c>.</summary>
    public sealed record Entry(ProviderKind Provider, ModelOption Option) // :8-9
    {
        // :11 var id: String
        public string Id => $"{Provider.Id}/{Option.Id}";
    }

    // :14-30 static func entries(_:including:) -> [Entry]. PR-COMPOSER-140: the chosen model list, or every ready
    // provider's models until one is set up, always including the thread's current model.
    public static ValueList<Entry> Entries(AppModel model, ChatThread? including)
    {
        var registry = model.Providers;
        var result = model.Settings.ModelList.Count == 0
            ? registry.AvailableProviders
                .SelectMany(provider => registry.Models(provider).Select(option => new Entry(provider, option)))
                .ToList()
            : model.Settings.ModelList
                .Select(pin => registry.Model(pin.ModelID, pin.Provider) is { } option
                    ? new Entry(pin.Provider, option)
                    : null)
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .ToList();

        if (including is { } thread &&
            registry.Model(thread.Model, thread.Provider) is { } current &&
            !result.Any(entry => entry.Provider == thread.Provider && entry.Option.Id == current.Id))
        {
            result.Insert(0, new Entry(thread.Provider, current));
        }

        return new ValueList<Entry>(result);
    }
}

/// <summary>:36-150 <c>struct ModelEffortButton: View</c>.</summary>
public sealed class ModelEffortButton : Decorator
{
    // :38-39 the chip's inputs. SwiftUI re-evaluates the chip's body with new values and keeps its @State (isPresented,
    // naturalWidth, lockedWidth, the popover), so a thread write made from the open popover (the slider's effort, the fast
    // toggle, a pair, a model) changes these two and never makes the chip again. ComposerControls hands them in with Update.
    private sealed class Inputs(ChatThread thread, bool hasHistory) : MainActorModel
    {
        private ChatThread thread = thread;
        private bool hasHistory = hasHistory;
        public ChatThread Thread { get => Read(thread); set => Set(ref thread, value); }
        public bool HasHistory { get => Read(hasHistory); set => Set(ref hasHistory, value); }
    }
    private readonly Inputs inputs;
    private ModelEffortPanel? openPanel;

    /// <summary>:38-39 the composer hands the chip the thread it now draws and the turns it now has (SwiftUI's re-evaluated
    /// body); the chip, its popover and its held width stay as they are, and the open panel is given the new
    /// <c>hasHistory</c> (:121).</summary>
    internal void Update(ChatThread thread, bool hasHistory)
    {
        inputs.Thread = thread;
        inputs.HasHistory = hasHistory;
        openPanel?.SetHasHistory(hasHistory);
    }

    /// <param name="thread">:38 <c>let thread: ChatThread</c>.</param>
    /// <param name="hasHistory">:39 <c>let hasHistory: Bool</c>.</param>
    /// <param name="compact">:42 <c>var compact: Bool = false</c>.</param>
    // :59-138 body
    public ModelEffortButton(ChatThread thread, bool hasHistory, bool compact = false)
    {
        inputs = new Inputs(thread, hasHistory);
        // :105-108 the held box (.frame(width: lockedWidth)) with the label's HStack(spacing: 5) centred in it. A DockPanel is that
        // HStack: its marks and its effort and chevron keep their size, and the name, the one flexible part and the last
        // child, takes what is left and is what gets cut (:89-94); every item after the first carries the 5 point gap, so
        // an icon alone has none (:65, a one-child HStack has no spacing).
        var frame = new Grid();
        var row = new DockPanel { LastChildFill = !compact, HorizontalAlignment = HorizontalAlignment.Center };
        frame.Children.Add(row);
        var presented = false;
        double? naturalWidth = null;
        double? lockedWidth = null;
        // :120 .popover(isPresented:arrowEdge: .bottom): the reference runtime opens it ABOVE the chip, its arrow on the
        // card's bottom edge (2-chat-use.mov 33.74-33.94 s; the main window's other arrowEdge .bottom menus open above
        // with room below, 19.28 s, 21.73 s). Without room above, the positioner flips it below (FlipY, the default).
        var popup = new Popup { Placement = PlacementMode.Top, IsLightDismissEnabled = true };
        Button button = null!;
        button = PlainControls.Button(frame, () =>
        {
            presented = !presented;
            button.Chip(presented); // :114 .buttonStyle(.chip(active: isPresented)).
            lockedWidth = presented ? naturalWidth : null; frame.Width = compact ? double.NaN : lockedWidth ?? double.NaN; // :108-117
            if (presented)
            {
                popup.VerticalOffset = inputs.Thread.Provider == ProviderKind.OpenRouter ? -Marrow.App.Port.ModelsPalette.OpenRouterPicker.ChipClearance : 0; // ED-NEW-models-palette-13.
                var panel = openPanel = new ModelEffortPanel(inputs.Thread.Id, inputs.HasHistory);
                var content = panel.PresentedChrome();
                AppEnvironment.SetAppModel(content, AppEnvironment.GetAppModel(this));
                Popovers.SetClosePopover(content, () => Presentation.Presenter.Current.SetPresented(popup, false));
                popup.Child = content;
            }
            else { popup.Child = null; openPanel = null; }
            Presentation.Presenter.Current.SetPresented(popup, presented);
        });
        button.Focusable = false; popup.PlacementTarget = button; // :115,120
        GpuWarmup.WhenIdle(this); // OD-0090 / ED-252: the effort knob's programs primed before this chip can open the popover
        button.Chip(); // :114 inactive until the popover opens.
        frame.SizeChanged += (_, _) => { if (lockedWidth is null) naturalWidth = frame.Bounds.Width; }; // :109-112
        popup.Closed += (_, _) => { presented = false; button.Chip(); lockedWidth = null; frame.Width = double.NaN; popup.Child = null; openPanel = null; }; // :114,116-117
        DetachedFromVisualTree += (_, _) => { if (presented) Presentation.Presenter.Current.SetPresented(popup, false); };
        Child = button;
        ProviderRegistry registry = null!; // :60 `let registry = model.providers`, set by every body evaluation below
        TextBlock? shownName = null, shownEffort = null;
        // :73 .id(pair.id): the fused mark's view, and with it its state, is kept while the pair (and the providers it fuses) stay the same, so a
        // change of effort or of fast mode does not play the fuse-in again (HydraFusedPairIcon.swift:37-42 plays it from its own .task(id:)).
        HydraFusedPairIcon? keptMark = null;
        (Guid Pair, ProviderKind Lead, ProviderKind Heads)? keptKey = null;
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            var input = inputs.Thread;
            var currentThread = model.Thread(input.Id) ?? input;
            var option = model.Providers.Model(currentThread.Model, currentThread.Provider);
            var pair = model.HydraIsOn(currentThread) ? model.HydraPair(currentThread) : null;
            return (model, currentThread, option, pair);
        }, snapshot =>
        {
            var (model, currentThread, current, pair) = snapshot;
            registry = model.Providers;
            var (oldName, oldEffort) = (shownName, shownEffort);
            (shownName, shownEffort) = (null, null);
            Control mark;
            if (pair is null)
            {
                (keptMark, keptKey) = (null, null);
                mark = new ProviderIcon { Provider = currentThread.Provider, Size = 14, Width = 14, Height = 14 }; // :73-75 real provider artwork from Components.
            }
            else
            {
                var key = (pair.Id, pair.Provider, model.HydraHeadsProvider(pair));
                if (keptMark is null || keptKey != key) (keptMark, keptKey) = (new HydraFusedPairIcon(key.Provider, key.Item3, 15), key); // :70-72
                mark = keptMark;
            }
            // everything but the kept mark is rebuilt; the mark itself stays in the row, never detached, so it keeps its state
            foreach (var child in row.Children.ToArray()) if (!ReferenceEquals(child, mark)) row.Children.Remove(child);
            DockPanel.SetDock(mark, Dock.Left); if (!row.Children.Contains(mark)) row.Children.Insert(0, mark);
            if (currentThread.FastMode && current?.SupportsFast == true)
            {
                var bolt = SwiftImage.SystemName("bolt.fill").Font(Chrome.InlineIconFont).ForegroundStyle(SwiftColor.Yellow); bolt.Margin = new Thickness(5, 0, 0, 0);
                DockPanel.SetDock(bolt, Dock.Left); row.Children.Add(bolt); // :77-80
            }
            if (!compact)
            {
                // the right-docked items come before the name that fills the rest, the chevron first so it ends the row
                var chevron = SwiftImage.SystemName("chevron.down").Font(Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText);
                chevron.Margin = new Thickness(5, 0, 0, 0); DockPanel.SetDock(chevron, Dock.Right); row.Children.Add(chevron); // :101-103
                Grid? effortCell = null;
                TextBlock? effort = null;
                if (current is { Efforts.Count: > 0 })
                {
                    effort = EffortView.Text(ModelOption.EffortTitle(currentThread.Effort ?? current.DefaultEffort ?? ""), SwiftFont.Callout, Chrome.PrimaryText.Opacity(0.72));
                    effortCell = new Grid { Margin = new Thickness(5, 0, 0, 0), Children = { effort } }; // :95-99
                    DockPanel.SetDock(effortCell, Dock.Right); row.Children.Add(effortCell);
                }
                var name = EffortView.Text(pair?.CustomName ?? current?.ChipName ?? currentThread.Model ?? currentThread.Provider.DisplayName, SwiftFont.Callout, Chrome.PrimaryText);
                name.TextTrimming = TextTrimming.CharacterEllipsis; // :90-94
                var nameCell = new Grid { Children = { name } };
                row.Children.Add(new TruncationFill(name) { Margin = new Thickness(5, 0, 0, 0), Child = nameCell });
                CrossFade(nameCell, oldName, name); shownName = name;
                if (effortCell is not null && effort is not null) { CrossFade(effortCell, oldEffort, effort); shownEffort = effort; }
            }
            var parts = new System.Collections.Generic.List<string> { current?.ShortName ?? currentThread.Model ?? currentThread.Provider.DisplayName };
            if (current is { Efforts.Count: > 0 }) parts.Add(ModelOption.EffortTitle(currentThread.Effort ?? current.DefaultEffort ?? ""));
            if (pair is not null) parts.Add(HydraPairSummary.Title(pair, model.Providers));
            var help = string.Join(" · ", parts); ToolTip.SetTip(button, help); AutomationProperties.SetName(button, help); // :142-149
        });
        // :138 the body closes over the registry of the body evaluation (:60), attached before this task starts; it
        // never looks the environment up, so a task left queued by a chip that has since left the tree still has it.
        this.Task(() => inputs.Thread.Provider, async provider => await registry.LoadCatalog(provider)); // :138
        // :125-137 capture-only anchor/frame sites remain outside the shipped path (ED-001).
    }

    /// <summary>:92-94 a Text cut short by the width it is offered takes the whole of it, as SwiftUI sizes a truncated Text to its
    /// proposal: the name's cell then spans what the held box leaves, and not the last whole character's width.</summary>
    private sealed class TruncationFill(TextBlock text) : Decorator
    {
        protected override Size MeasureOverride(Size available)
        {
            var size = base.MeasureOverride(available);
            return !double.IsInfinity(available.Width) && size.Width < available.Width && text.TextLayout.TextLines.Any(line => line.HasCollapsed)
                ? new Size(available.Width, size.Height) : size;
        }
    }

    // :90-99 plain Texts: a change made inside an animation (the slider's withAnimation(.snappy(duration: 0.2)), :631)
    // cross-fades the old text into the new one in place, SwiftUI's default for a Text without a content transition.
    // Reference runtime, 2-chat-use.mov 34.84-34.97 s: the chip's "Sonnet High" fades under "S... Extra High", the new
    // text's weight 0.37, 0.66, 0.80, 0.90, 0.96 at 27, 49, 69, 90 and 112 ms. The old text keeps its laid-out width (so
    // its own truncation) and takes no room: it sits in a sizeless layer over its cell until it has faded. ENGINEERING
    // DECISION, fitted to those five frames (worst 0.02): the fade runs the change's own curve 13 ms ahead (on the curve
    // alone it trailed the video by about a frame: 0.22 and 0.50 at 27 and 49 ms).
    internal const double FadeLead = 0.013;

    private static void CrossFade(Grid cell, TextBlock? old, TextBlock fresh)
    {
        if (Transaction.Current is not { } change || old is null || old.Text == fresh.Text || old.Bounds.Width <= 0) return;
        var animation = change.Delay(-FadeLead); // ahead: at the change's moment already FadeLead into the curve
        var width = old.Bounds.Width;
        var height = old.Bounds.Height;
        (old.Parent as Panel)?.Children.Remove(old); // it leaves the cell it was laid out in
        old.Width = width;
        var from = old.Opacity; // a text still fading in when the next change comes fades out from where it was
        var layer = new Canvas { Height = height, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Children = { old } };
        cell.Children.Add(layer); // over the fresh text's own cell, taking no room
        EffortView.Animate(old, animation, progress => // replaces the fade-in it may still be running
        {
            old.Opacity = from * (1 - Math.Clamp(progress, 0, 1));
            if (progress >= 1) cell.Children.Remove(layer);
        });
        EffortView.Animate(fresh, animation, progress => fresh.Opacity = Math.Clamp(progress, 0, 1));
    }
}

/// <summary>:153-259 <c>private struct ModelEffortPanel: View</c>.</summary>
public sealed class ModelEffortPanel : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool showsModels;
        public bool ShowsModels { get => Read(showsModels); set => Set(ref showsModels, value); }
        // :157 the input the chip hands the panel again when the composer re-renders under the open popover (:121)
        private bool hasHistory;
        public bool HasHistory { get => Read(hasHistory); set => Set(ref hasHistory, value); }
    }
    private readonly ViewState state = new();
    internal void SetHasHistory(bool hasHistory) => state.HasHistory = hasHistory;
    private AppModel model = null!;
    private readonly Panel pages = new();
    private bool? displayedModels;
    private (ProviderKind Provider, string? Model, ModelOption? Option)? sliderKey;
    private EffortBrand? shownBrand; // OD-0090: the maker the last slider wore, so a model change crossfades from it
    /// <param name="threadID">:156 <c>let threadID: UUID</c>.</param>
    /// <param name="hasHistory">:157 <c>let hasHistory: Bool</c>.</param>
    // :161-189 body
    public ModelEffortPanel(Guid threadID, bool hasHistory)
    {
        Width = 330; Child = pages; // :186
        state.HasHistory = hasHistory;
        this.Track(() =>
        {
            var app = AppEnvironment.GetAppModel(this);
            var thread = app.Thread(threadID);
            // :196 the body reads hydraLaunch's resolved heads effort, so the caption follows it.
            return (app, thread, state.ShowsModels, HeadsEffort: thread is null ? null : app.HydraLaunch(thread)?.WorkerEffort, History: state.HasHistory);
        }, snapshot =>
        {
            var (app, thread, showsModels, headsEffort, history) = snapshot; model = app;
            if (thread is null) { pages.Children.Clear(); displayedModels = null; return; } // :162
            var key = (thread.Provider, thread.Model, model.Providers.Model(thread.Model, thread.Provider));
            // :163-185 the branch retains its view/state while only the bound effort or fast mode changes, and :196 while the
            // pair the chat leads with (or its resolved heads effort) changes: the card is handed the new look.
            if (!showsModels && displayedModels == false && Equals(sliderKey, key) && pages.Children.OfType<EffortSliderCard>().FirstOrDefault() is { } card)
            {
                var kept = model.HydraIsOn(thread) ? model.HydraPair(thread) : null;
                card.UpdatePair(kept is null ? null : new EffortPairLook(kept, model.Providers, headsEffort));
                return;
            }
            sliderKey = key;
            Control content;
            if (showsModels)
                content = new ModelList(thread, history, entry => { Choose(entry, thread); state.ShowsModels = false; },
                    () => state.ShowsModels = false, pair =>
                    {
                        model.EnterHydraPair(pair, thread.Id); state.ShowsModels = false;
                        Popovers.GetClosePopoverBox(this)?.Close(); // :173-175 close box is the port's presentation-scoped dismiss
                    });
            else content = Slider(thread);
            if (displayedModels is { } previous && previous != showsModels)
                Transaction.WithAnimation(Animation.EaseOut(0.12), () =>
                {
                    foreach (var old in pages.Children.ToArray()) SwiftTransition.Remove(pages, old);
                    SwiftTransition.Insert(pages, pages.Children.Count, content);
                }); // :180-188
            else { pages.Children.Clear(); pages.Children.Add(content); }
            displayedModels = showsModels;
        });
    }

    // :191-230
    private Control Slider(ChatThread thread)
    {
        var option = model.Providers.Model(thread.Model, thread.Provider);
        var pair = model.HydraIsOn(thread) ? model.HydraPair(thread) : null;
        var pairLook = pair is null ? null : new EffortPairLook(pair, model.Providers, model.HydraLaunch(thread)?.WorkerEffort);
        var brand = EffortBrand.Create(thread.Provider);
        var fadeFrom = shownBrand is { } shown && shown != brand ? shown : (EffortBrand?)null;
        shownBrand = brand;
        var card = new EffortSliderCard(option?.ShortName ?? thread.Model ?? thread.Provider.DisplayName, thread.Provider,
            option?.Efforts ?? ValueList<string>.Empty, option?.DefaultEffort, option?.SupportsFast ?? false,
            new Binding<string?>(() => model.Thread(thread.Id)?.Effort, effort => { model.UpdateThread(thread.Id, current => current with { Effort = effort }); Remember(thread.Id); }),
            new Binding<bool>(() => model.Thread(thread.Id)?.FastMode ?? false, fast => { model.UpdateThread(thread.Id, current => current with { FastMode = fast }); Remember(thread.Id); }),
            () =>
            {
                // :219-224 read when it is tapped: the pair the chat leads with then (the card stays while that changes)
                var leading = model.Thread(thread.Id) is { } now && model.HydraIsOn(now) ? model.HydraPair(now) : null;
                var leadEffort = leading?.OrchestratorEffort is { } effort && option?.Efforts.Contains(effort) == true ? effort : null;
                model.UpdateThread(thread.Id, current => current with { Effort = leadEffort, FastMode = false }); Remember(thread.Id);
            }, pairLook, () => state.ShowsModels = true, fadeFrom);
        Marrow.App.Port.ModelsPalette.PaletteComposerBinding.Attach(model, thread.Id, card); // guarded by the unchanged UI gate.
        return card;
    }

    // :235-239
    private void Remember(Guid id)
    {
        if (model.Thread(id) is not { Model: { } modelID } thread) return;
        model.Settings.SetPreference(new ModelPreference { Effort = thread.Effort, FastMode = thread.FastMode }, thread.Provider, modelID);
        model.Settings.Remember(modelID, thread.Effort, thread.Provider);
    }

    // :242-257
    private void Choose(ModelCatalog.Entry entry, ChatThread thread)
    {
        var provider = entry.Provider; var option = entry.Option; var switchesProvider = provider != thread.Provider;
        var preference = model.Settings.Preference(provider, option.Id);
        var effort = preference.Effort is { } remembered && option.Efforts.Contains(remembered) ? remembered : null;
        model.UpdateThread(thread.Id, current => current with
        {
            Provider = switchesProvider ? provider : current.Provider,
            ProviderSessionID = switchesProvider ? null : current.ProviderSessionID,
            ProviderResumeAt = switchesProvider ? null : current.ProviderResumeAt,
            Model = option.Id, Effort = effort, FastMode = option.SupportsFast && preference.FastMode,
        });
        model.LeaveHydraPair(thread.Id);
        if (switchesProvider) model.ExistingRuntime(thread.Id)?.StopSession();
        model.Settings.Remember(option.Id, effort, provider); model.Settings.DefaultProvider = provider;
    }
}

/// <summary>:261-368 <c>struct ModelList: View</c>.</summary>
public sealed class ModelList : Decorator
{
    // :271 private static let pairRowHeight: CGFloat = 46
    private const double PairRowHeight = 46;

    // :273 private static let sectionHeaderHeight: CGFloat = 22
    private const double SectionHeaderHeight = 22;

    /// <param name="thread">:263 <c>let thread: ChatThread</c>.</param>
    /// <param name="hasHistory">:264 <c>let hasHistory: Bool</c>.</param>
    /// <param name="onChoose">:265 <c>let onChoose: (ModelCatalog.Entry) -> Void</c>.</param>
    /// <param name="onBack">:268 <c>let onBack: () -> Void</c>; no default, so it moves before onEnterPair (ED-095).</param>
    /// <param name="onEnterPair">:267 <c>var onEnterPair: (HydraPair) -> Void = { _ in }</c>; null stands for the
    /// default that does nothing.</param>
    // :275-359 body
    public ModelList(ChatThread thread, bool hasHistory, Action<ModelCatalog.Entry> onChoose, Action onBack, Action<HydraPair>? onEnterPair = null)
    {
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return (Model: model, Entries: ModelCatalog.Entries(model, thread), Pairs: model.HydraIsOn(thread) ? model.HydraPickerPairs : ValueList<HydraPair>.Empty);
        }, snapshot =>
        {
            var (model, entries, pairs) = snapshot;
            if (Marrow.App.Port.ModelsPalette.ModelPickerBinding.TryCreate(model, thread, hasHistory, entries, pairs, onChoose, onBack, onEnterPair, Child) is { } approved) { Child = approved; return; }
            var leadsWithPair = pairs.Any(pair => model.LeadsWithHydraPair(pair, thread)); // :280
            var showsProviders = entries.Select(entry => entry.Provider).Distinct().Count() > 1;
            var rowHeight = showsProviders || hasHistory && entries.Any(entry => entry.Provider != thread.Provider) ? 46 : 34;
            var pairsHeight = pairs.Count == 0 ? 0 : SectionHeaderHeight * 2 + pairs.Count * (PairRowHeight + 1);
            var rows = new StackPanel { Spacing = 1 };
            if (pairs.Count > 0)
            {
                rows.Children.Add(new PopoverSectionHeader("Pairs") { Height = SectionHeaderHeight }); // :311; :273 the 22 the arithmetic reserves, whatever this line's font makes of its own
                foreach (var pair in pairs)
                {
                    var locked = hasHistory && pair.Provider != thread.Provider;
                    var detail = string.Join(" · ", new[] { pair.CustomName is null ? null : HydraPairSummary.ModelsTitle(pair, model.Providers), HydraPairSummary.Providers(pair), HydraPairSummary.Efforts(pair) }.Where(value => value is not null));
                    rows.Children.Add(new HydraPairListRow(pair, HydraPairSummary.Title(pair, model.Providers), locked ? "New chats only" : detail, PairRowHeight,
                        model.LeadsWithHydraPair(pair, thread), !locked, () => onEnterPair?.Invoke(pair))); // :312-323
                }
                rows.Children.Add(new PopoverSectionHeader("Models") { Height = SectionHeaderHeight }); // :325
            }
            foreach (var entry in entries)
            {
                var locked = hasHistory && entry.Provider != thread.Provider;
                rows.Children.Add(new ModelListRow(entry, locked ? "New chats only" : showsProviders ? entry.Provider.DisplayName : null,
                    showsProviders, rowHeight, !leadsWithPair && entry.Provider == thread.Provider && entry.Option.Id == thread.Model, !locked, () => onChoose(entry))); // :327-339
            }
            if (entries.Count == 0)
            {
                var loading = EffortView.Text("Loading models…", SwiftFont.Body, Chrome.SecondaryText); loading.Margin = new Thickness(10); rows.Children.Add(loading); // :340-345
            }
            var backIcon = SwiftImage.SystemName("chevron.left").Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText);
            var back = PlainControls.Button(new Border { Width = 20, Height = 20, Background = Brushes.Transparent, Child = backIcon }, onBack); ToolTip.SetTip(back, "Back"); // :290-299, :294 .contentShape(.rect): the whole frame takes the press
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 8, 8, 4), Children = { back, EffortView.Text("Select model", SwiftFont.Body, Chrome.SecondaryText) } };
            var scroll = new ScrollViewer { Content = rows, Height = Math.Min(pairsHeight + Math.Max(entries.Count, 1) * (rowHeight + 1), 440), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Child = new Border { Padding = new Thickness(6), Child = new StackPanel { Spacing = 0, Children = { header, scroll } } }; // :348-353
        });
        this.Task(() => 0, async _ =>
        {
            var model = AppEnvironment.GetAppModel(this);
            var pairs = model.HydraIsOn(thread) ? model.HydraPickerPairs : ValueList<HydraPair>.Empty;
            foreach (var provider in pairs.SelectMany(pair => new[] { pair.Provider, pair.HeadsProvider }).Distinct().Where(provider => provider != thread.Provider))
                await model.Providers.LoadCatalog(provider); // :354-359
        });
    }
}

/// <summary>:373-420 <c>private struct HydraPairListRow: View</c>.</summary>
public sealed class HydraPairListRow : Decorator
{
    /// <param name="pair">:374 <c>let pair: HydraPair</c>.</param>
    /// <param name="title">:375 <c>let title: String</c>.</param>
    /// <param name="detail">:376 <c>let detail: String</c>.</param>
    /// <param name="rowHeight">:377 <c>let rowHeight: CGFloat</c>.</param>
    /// <param name="isSelected">:378 <c>let isSelected: Bool</c>.</param>
    /// <param name="isEnabled">:379 <c>let isEnabled: Bool</c>.</param>
    /// <param name="action">:380 <c>let action: () -> Void</c>.</param>
    // :384-419 body
    public HydraPairListRow(HydraPair pair, string title, string detail, double rowHeight, bool isSelected, bool isEnabled, Action action)
    {
        // :386 HStack(spacing:10), :400 Spacer(minLength:12): preserve the gap on EACH side of the spacer.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,10,*,10,12,10,Auto") };
        row.Children.Add(new HydraFusedPairIcon(pair.Provider, pair.HeadsProvider, 16, false) { Width = 18 }); // :387-388
        var titleText = EffortView.Text(title, SwiftFont.System(14), Chrome.PrimaryText);
        var detailText = EffortView.Text(detail, SwiftFont.Callout, Chrome.SecondaryText);
        foreach (var line in new[] { titleText, detailText }) { line.TextTrimming = TextTrimming.CharacterEllipsis; line.TextWrapping = TextWrapping.NoWrap; } // :393, :397 .lineLimit(1): the default truncation mode is the tail
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { titleText, detailText } };
        Grid.SetColumn(texts, 2); row.Children.Add(texts); // :389-399
        var check = SwiftImage.SystemName("checkmark").Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        check.Opacity = isSelected ? 1 : 0; Grid.SetColumn(check, 6); row.Children.Add(check); // :401-405
        Child = EffortView.Row(row, rowHeight, isEnabled, action); // :407-419
    }
}

/// <summary>:422-477 <c>private struct ModelListRow: View</c>.</summary>
public sealed class ModelListRow : Decorator
{
    /// <param name="entry">:423 <c>let entry: ModelCatalog.Entry</c>.</param>
    /// <param name="detail">:424 <c>let detail: String?</c>.</param>
    /// <param name="showsIcon">:425 <c>let showsIcon: Bool</c>.</param>
    /// <param name="rowHeight">:426 <c>let rowHeight: CGFloat</c>.</param>
    /// <param name="isSelected">:427 <c>let isSelected: Bool</c>.</param>
    /// <param name="isEnabled">:428 <c>let isEnabled: Bool</c>.</param>
    /// <param name="action">:429 <c>let action: () -> Void</c>.</param>
    // :433-476 body
    public ModelListRow(ModelCatalog.Entry entry, string? detail, bool showsIcon, double rowHeight, bool isSelected, bool isEnabled, Action action)
    {
        // :435 HStack(spacing: 10): SwiftUI puts the spacing between EVERY two neighbours, the :451 Spacer(minLength: 12) included
        // (the pair row above does the same), so the text column is followed by 10 + 12 + 10 before the bolt (a model with a fast
        // tier) or the checkmark, and the checkmark 10 after the bolt. The columns follow the children that are there: no icon, no
        // icon column; no bolt, no bolt column and no gap for it.
        var fast = entry.Option.SupportsFast;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions((showsIcon ? "18,10," : "") + "*,10,12," + (fast ? "10,Auto," : "") + "10,Auto") };
        var column = 0;
        if (showsIcon)
        {
            var providerIcon = new ProviderIcon { Provider = entry.Provider, Size = 14, Width = 18, Height = 14 }; // :436-439
            providerIcon.SetValue(TextElement.ForegroundProperty, Chrome.SecondaryText.Brush(providerIcon)); // :438 inherited by its template artwork.
            row.Children.Add(providerIcon);
            column = 2;
        }
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(EffortView.Text(entry.Option.ShortName, SwiftFont.System(14), Chrome.PrimaryText)); // :441-444
        if (detail is not null) texts.Children.Add(EffortView.Text(detail, SwiftFont.Callout, Chrome.SecondaryText)); // :445-449
        Grid.SetColumn(texts, column);
        row.Children.Add(texts);
        column += 3; // the text column, its 10, the spacer's 12
        if (fast)
        {
            var bolt = SwiftImage.SystemName("bolt").Font(SwiftFont.System(SwiftFont.TextStyle.Footnote)).ForegroundStyle(Chrome.SecondaryText);
            Grid.SetColumn(bolt, column + 1); row.Children.Add(bolt); // :452-455
            column += 2;
        }
        var check = SwiftImage.SystemName("checkmark").Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        check.Opacity = isSelected ? 1 : 0; // :456-460
        Grid.SetColumn(check, column + 1); row.Children.Add(check);
        Child = EffortView.Row(row, rowHeight, isEnabled, action); // :462-475
    }
}

/// <summary>:485-504 <c>struct EffortPairLook: Hashable</c>.</summary>
public sealed record EffortPairLook
{
    public string Title { get; init; }           // :487
    public EffortPairBrand Brand { get; init; }  // :488
    public string? HeadsEffort { get; init; }    // :490

    // :492-497 @MainActor init(_:registry:resolvedHeadsEffort:). PR-COMPOSER-115: the heads effort is the pair's
    // own workerEffort when set, else the caller's resolved launch effort; either way it is run through
    // ModelOption.effortTitle and lowercased once here, so Caption never has to repeat that work.
    public EffortPairLook(HydraPair pair, ProviderRegistry registry, string? resolvedHeadsEffort)
    {
        Title = Marrow.App.Views.Chat.HydraPairSummary.Title(pair, registry); // :494
        Brand = new EffortPairBrand(pair); // :495
        HeadsEffort = (pair.WorkerEffort ?? resolvedHeadsEffort) is { } effort // :496
            ? SwiftString.Lowercased(ModelOption.EffortTitle(effort))
            : null;
    }

    // :500-503 var caption: String. PR-COMPOSER-115: byte-for-byte, including the U+00B7 separators.
    public string Caption => HeadsEffort is { } headsEffort
        ? $"The lead's effort · heads run at {headsEffort} effort" // :501
        : "The lead's effort · heads keep their own"; // :502
}

/// <summary>:506-657 <c>struct EffortSliderCard: View</c>.</summary>
public sealed class EffortSliderCard : Decorator
{
    /// <param name="modelName">:507 <c>let modelName: String</c>.</param>
    /// <param name="provider">:508 <c>let provider: ProviderKind?</c>.</param>
    /// <param name="efforts">:511 <c>let efforts: [String]</c>; moves before pair (ED-095).</param>
    /// <param name="defaultEffort">:512 <c>let defaultEffort: String?</c>.</param>
    /// <param name="supportsFast">:513 <c>let supportsFast: Bool</c>.</param>
    /// <param name="effort">:514 <c>@Binding var effort: String?</c>.</param>
    /// <param name="fastMode">:515 <c>@Binding var fastMode: Bool</c>.</param>
    /// <param name="onReset">:517 <c>let onReset: () -> Void</c>; moves before onTitleTap (ED-095).</param>
    /// <param name="pair">:510 <c>var pair: EffortPairLook? = nil</c>.</param>
    /// <param name="onTitleTap">:516 <c>var onTitleTap: (() -> Void)?</c>.</param>
    // :510 `var pair: EffortPairLook?` is read again each time the parent's body runs: the chat may enter or leave a pair, and
    // the pair's heads effort may resolve differently, while the card is on screen. The panel hands the new look to the card
    // that stays (UpdatePair), and everything the pair reaches follows it in place.
    private sealed class ViewState : MainActorModel
    {
        private EffortPairLook? pair;

        public EffortPairLook? Pair { get => Read(pair); set => Set(ref pair, value); }
    }

    private readonly ViewState live = new();

    /// <summary>:191-197, :510 the body runs again with the pair the chat leads with now (or none) and its heads effort
    /// freshly resolved: the title's colours, the line under it, the track and the caption follow in place, the caption
    /// joining or leaving the card on its soft-appear transition (:650).</summary>
    internal void UpdatePair(EffortPairLook? look) => live.Pair = look;

    // :557-656 body
    public EffortSliderCard(
        string modelName,
        ProviderKind? provider,
        ValueList<string> efforts,
        string? defaultEffort,
        bool supportsFast,
        Binding<string?> effort,
        Binding<bool> fastMode,
        Action onReset,
        EffortPairLook? pair = null,
        Action? onTitleTap = null,
        EffortBrand? fadeFrom = null)
    {
        // :521-539 computed properties are read again for every binding change.
        int ResolvedIndex()
        {
            var chosen = effort.Value is { } value ? efforts.ToList().IndexOf(value) : -1;
            if (chosen >= 0) return chosen;
            var fallback = defaultEffort is null ? -1 : efforts.ToList().IndexOf(defaultEffort);
            if (fallback >= 0) return fallback;
            return Math.Max(0, efforts.ToList().IndexOf("medium"));
        }
        var brand = EffortBrand.Create(provider);
        var rows = new StackPanel { Spacing = 16 }; // :558
        var header = new Panel();
        var sides = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (supportsFast) sides.Children.Add(new FastModeButton(fastMode)); // :561-562
        var reset = new RoundIconButton("arrow.counterclockwise", "Reset to default", onReset);
        Grid.SetColumn(reset, 2); sides.Children.Add(reset); // :564
        header.Children.Add(sides);
        var centre = new StackPanel { Spacing = 2, Margin = new Thickness(44, 0), HorizontalAlignment = HorizontalAlignment.Center };
        // :573-574 Text(verbatim: title).font(.system(.title2, weight: .medium)).contentTransition(.numericText())
        // :566-567 the title row is centred in the card's VStack: new letters appear where they settle (GrowthAnchor).
        // DEVIATION OD-0094 (owner ruling): a gradient title (a pair, the brand at maximum, maximum with fast mode's gold)
        // drifts along its letters at rest, once round its mirrored loop in 7 s; a single colour stays (MorphingTextBlock).
        var title = new MorphingTextBlock { Text = "", MaxLines = 1, VerticalAlignment = VerticalAlignment.Center, GrowthAnchor = 0.5, DriftPeriod = 7 };
        SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { title } };
        if (onTitleTap is not null) titleRow.Children.Add(SwiftImage.SystemName("chevron.right").Font(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText)); // :575-578
        // :589 the plain button draws its label past the plate (the leaving letters, below): the plate's clip must not cut beyond its sides (ED-317).
        var titlePlate = new Border { Padding = new Thickness(8, 2), Child = titleRow }.RoundedRectangle(7, RoundedCornerStyle.Continuous).ContentBeyondSides();
        var titleFill = new AnimatedColor(titlePlate); titlePlate.Background = titleFill.Brush;
        var titleButton = PlainControls.Button(titlePlate, () => onTitleTap?.Invoke()); // :568-597
        titleButton.IsEnabled = onTitleTap is not null;
        // :589 .buttonStyle(.plain) draws its label unclipped: the title's leaving letters, carried right past the chevron
        // as the label narrows, fade there (reference runtime, 2-chat-use.mov 35.12-35.29 s).
        titleButton.ClipToBounds = false;
        titleButton.OnHover(hovering => titleFill.Set(hovering && onTitleTap is not null ? Chrome.Overlay(0.08) : SwiftColor.Clear, Chrome.Hover));
        ToolTip.SetTip(titleButton, onTitleTap is null ? "" : "Choose a model");
        titleButton.HorizontalAlignment = HorizontalAlignment.Center; // :567 a VStack centres each child at its own width, so the hover plate fits the title
        centre.Children.Add(titleButton);
        var subtitle = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), HorizontalAlignment = HorizontalAlignment.Center };
        string? subtitleOf = null; // the title the line under it shows now: a pair's, or null for the model's
        bool subtitleMade = false;
        void Subtitle(EffortPairLook? look)
        {
            if (subtitleMade && subtitleOf == look?.Title) return; // only the heads effort changed: the line is the same
            subtitleMade = true; subtitleOf = look?.Title;
            subtitle.Children.Clear();
            if (look is not null)
            {
                var mark = new HydraMarkImage { Width = 12, Height = 12 };
                if (mark.Child is SwiftImage image) image.ForegroundStyle(Chrome.PrimaryText.Opacity(0.75));
                subtitle.Children.Add(mark); // :602-605
            }
            else if (provider is { } kind) subtitle.Children.Add(new ProviderIcon { Provider = kind, Size = 12, Width = 12, Height = 12 }); // :606-608 real provider artwork from Components.
            var name = EffortView.Text(look?.Title ?? modelName, SwiftFont.Body, Chrome.PrimaryText.Opacity(0.7)); // :609-614
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(subtitle.Children.Count == 0 ? 0 : 5, 0, 0, 0); // :599 HStack spacing, leaving a finite text column.
            var scaledName = TextScaling.Apply(name, 0.85); // :614 minimumScaleFactor, ED-214.
            Grid.SetColumn(scaledName, 1); subtitle.Children.Add(scaledName);
        }
        Subtitle(pair);
        centre.Children.Add(subtitle); header.Children.Add(centre); rows.Children.Add(header);
        var sliderSlot = new Panel(); rows.Children.Add(sliderSlot);
        if (efforts.Count == 0)
        {
            // :620-624 .frame(height: EffortSlider.thumbSize): the slot the slider would fill, with the line centred in it (a TextBlock
            // given that height draws its line at the top, so the slot is the Panel and the line sits in the middle of it)
            var note = EffortView.Text("This model has one reasoning level.", SwiftFont.Callout, Chrome.SecondaryText);
            sliderSlot.Height = EffortSlider.ThumbSize; note.HorizontalAlignment = HorizontalAlignment.Center; note.VerticalAlignment = VerticalAlignment.Center; sliderSlot.Children.Add(note);
        }
        // :642-651 `if let pair, !efforts.isEmpty { Text(...).transition(.softAppear) }`: a transition runs only for a view that
        // joins or leaves a card already on screen (SwiftUI). A card made with a pair has its caption whole from its first paint
        // (reference runtime, 2-chat-use.mov 43.665-43.757 s, the popover growing out of its arrow).
        Control? captionHost = null;
        TextBlock? caption = null;
        void Caption(EffortPairLook? look, bool onScreen)
        {
            if (look is null || efforts.Count == 0)
            {
                if (captionHost is { } leaving)
                {
                    captionHost = null; caption = null;
                    // Chrome.swift:957 the removal half of softAppear: opacity, easeOut(0.12)
                    EffortView.Animate(leaving, Animation.EaseOut(0.12), progress =>
                    {
                        leaving.Opacity = 1 - Math.Clamp(progress, 0, 1);
                        if (progress >= 1) rows.Children.Remove(leaving);
                    });
                }
                return;
            }
            if (caption is not null) { if (caption.Text != look.Caption) caption.Text = look.Caption; return; }
            caption = EffortView.Text(look.Caption, SwiftFont.System(SwiftFont.TextStyle.Subheadline), Chrome.SecondaryText);
            caption.Margin = new Thickness(0, -6, 0, 0); caption.TextTrimming = TextTrimming.CharacterEllipsis;
            var scaledCaption = TextScaling.Apply(caption, 0.9); // :648 (ED-214).
            scaledCaption.HorizontalAlignment = HorizontalAlignment.Center; // :558 a VStack centres each child at its own width
            captionHost = scaledCaption;
            rows.Children.Add(scaledCaption);
            // :650, Chrome.swift:940-944,949-955,960: the insertion half of softAppear animates the modifier's layer properties on
            // its shared timing (Animation.softAppear, smooth 0.32): opacity 0 to 1 and a 4 point rise (the Bool modifier itself
            // carries no interpolated data, so the transition type snaps; the layer properties are what move)
            if (onScreen)
                EffortView.Animate(scaledCaption, Animation.SoftAppear, progress =>
                {
                    scaledCaption.Opacity = Math.Clamp(progress, 0, 1);
                    scaledCaption.RenderTransform = new TranslateTransform(0, 4 * (1 - progress));
                });
        }
        Caption(pair, onScreen: false);
        Child = new Border { Padding = new Thickness(16, 14, 16, 16), Child = rows }; // :653-655
        string? previousTitle = null;
        var previousMaximum = false;
        var previousFast = false;
        live.Pair = pair;
        this.Track(() => (ResolvedIndex(), supportsFast && fastMode.Value, ActualThemeVariant, live.Pair), value =>
        {
            var (index, fast, theme, pairNow) = value;
            Subtitle(pairNow);
            if (previousTitle is not null) Caption(pairNow, onScreen: true);
            var maximum = efforts.Count > 1 && index == efforts.Count - 1; // :527-529
            var nextTitle = efforts.Count == 0 ? "Standard" : ModelOption.EffortTitle(efforts[index]);
            // :574 .contentTransition(.numericText()) morphs the title inside the change's animation (the slider's
            // withAnimation(.snappy(duration: 0.2)), :631). :582-583 .animation(.smooth(duration: 0.3), value: isMaxEffort)
            // and (value: isFast): a change into or out of maximum, or one that turns fast mode on or off, runs the title's
            // HStack (the morph, the label's width, the chevron) on smooth(0.3) whatever the transaction around it, the
            // slider's or none (onReset, :219-224); the reference video's chevron follows smooth(0.3) there and snappy(0.2)
            // elsewhere (ED-238 final round 5). DEVIATION OD-0076 (owner ruling: every change transitioned, never cut): a
            // change the Mac makes with no animation (onReset from an effort neither maximum nor fast) still morphs, in the
            // title's own motion, :582's smooth(0.3); OD-0076's reduce-motion clause cuts that added transition.
            var animation = previousTitle is null ? null
                : maximum != previousMaximum || fast != previousFast ? Animation.Smooth(duration: 0.3)
                : Transaction.Current ?? (EnvironmentValues.GetAccessibilityReduceMotion(this) ? null : Animation.Smooth(duration: 0.3));
            var morphing = previousTitle is not null && previousTitle != nextTitle && animation is not null;
            title.SetText(nextTitle, animation);
            previousMaximum = maximum;
            previousFast = fast;
            // :543-555 AnyShapeStyle is represented by Avalonia's brush; every literal colour comes from Swift.
            var titleBrush = pairNow is not null
                ? EffortView.Gradient(new[] { brand.TitleColor }.Concat(pairNow.Brand.TitleColors).Concat(fast ? new[] { EffortPalette.Fast } : Array.Empty<SwiftColor>()), theme)
                : maximum && fast ? EffortView.Gradient([brand.TitleColor, EffortPalette.Fast], theme)
                : maximum ? EffortView.BrandTitle(brand, theme)
                : (fast ? EffortPalette.Fast : EffortPalette.Title).Brush(title);
            // :582-583 the colour fades over smooth 0.3 when only it changes (fast mode, the pair's heads); when the text
            // morphs too, its leaving letters keep the old colour and the arriving ones wear the new one from their first
            // frame (reference runtime, 2-chat-use.mov 35.10-35.21 s: an orange "M" beside the pink leaving "High").
            if (previousTitle is null || morphing) EffortView.SetBrush(title, brush => title.Foreground = brush, titleBrush);
            else EffortView.AnimateBrush(title, () => title.Foreground, brush => title.Foreground = brush, titleBrush, Animation.Smooth(0.3));
            previousTitle = nextTitle;
            if (efforts.Count == 0) return;
            // :626-637 keep the gesture's own binding alive; changes to look/title update it without replacing the dragging view.
            if (sliderSlot.Children.FirstOrDefault() is EffortSlider existing)
                existing.UpdateLook(fast, title.Text!, pairNow?.Brand);
            else sliderSlot.Children.Add(new EffortSlider(efforts.Count,
                new Binding<int>(ResolvedIndex, chosen => Transaction.WithAnimation(Animation.Snappy(0.2), () => effort.Value = efforts[chosen])),
                title.Text!, fast, brand, pairNow?.Brand, fadeFrom)); // OD-0090: a model change crossfades from the last maker's colours
        });
    }
}

/// <summary>:659-683 <c>private struct FastModeButton: View</c>.</summary>
public sealed class FastModeButton : Decorator
{
    // :660 @State private var isHovering = false (implied by :675, :679-681)
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    /// <param name="isOn">:660 <c>@Binding var isOn: Bool</c>.</param>
    // :663-682 body. PR-COMPOSER-119: a 30 by 30 circle whose bolt fills and turns yellow when on.
    public FastModeButton(Binding<bool> isOn)
    {
        var state = new ViewState();
        var iconHost = new Panel(); // holds the swapped bolt/bolt.fill glyph (:664 Image(systemName: isOn ? "bolt.fill" : "bolt"))
        var circle = new Ellipse();
        var fill = new AnimatedColor(circle);
        circle.Fill = fill.Brush;

        bool? shownOn = null;
        void Apply(bool on, bool hovering)
        {
            iconHost.Children.Clear();
            var bolt = SwiftImage.SystemName(on ? "bolt.fill" : "bolt") // :667
                .Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)) // :668
                .ForegroundStyle(on ? SwiftColor.Yellow : Chrome.SecondaryText) // :669
                .SymbolEffect(SymbolEffectKind.Bounce, on); // :670
            iconHost.Children.Add(bolt);
            // :670 .symbolEffect(.bounce, value: isOn): one bounce each time the value changes (SymbolBounce, measured).
            if (shownOn is { } was && was != on) SymbolBounce.Play(bolt);
            shownOn = on;
        }

        Apply(isOn.Value, state.IsHovering);
        fill.Set(CirclePlate(isOn.Value, state.IsHovering), null); // :673 Circle().fill(...)

        var frame = new Panel
        {
            Width = 30, // :671 .frame(width: 30, height: 30)
            Height = 30,
            Children = { circle, iconHost },
        };

        var button = PlainControls.Button(frame, () => // :664
            Transaction.WithAnimation(Animation.Spring(response: 0.3, dampingFraction: 0.7), () => isOn.Value = !isOn.Value)); // :665
        button.OnHover(hovering => state.IsHovering = hovering); // :678 .onHover { isHovering = $0 } has no withAnimation wrapper, unlike RoundIconButton's :702-703

        this.Track(() => (isOn.Value, state.IsHovering), current =>
        {
            var (on, hovering) = current;
            Apply(on, hovering);
            // :673 inherits whichever ambient animation (the :665 spring for isOn, or none for an unwrapped :678 hover) drove this apply.
            fill.Set(CirclePlate(on, hovering), Transaction.Current);
            ToolTip.SetTip(button, on ? "Fast mode is on" : "Fast mode"); // :679 .help(...)
            AutomationProperties.SetItemStatus(button, on ? "On" : "Off"); // :681 .accessibilityValue(...)
        });

        ToolTip.SetTip(button, isOn.Value ? "Fast mode is on" : "Fast mode"); // :679
        AutomationProperties.SetName(button, "Fast mode"); // :680 .accessibilityLabel(Text("Fast mode"))
        AutomationProperties.SetItemStatus(button, isOn.Value ? "On" : "Off"); // :681
        Child = button;
    }

    // :673 Circle().fill(isOn ? Color.yellow.opacity(0.18) : Chrome.overlay(isHovering ? 0.1 : 0.06))
    private static SwiftColor CirclePlate(bool isOn, bool isHovering) =>
        isOn ? SwiftColor.Yellow.Opacity(0.18) : Chrome.Overlay(isHovering ? 0.1 : 0.06);
}

/// <summary>:685-708 <c>private struct RoundIconButton: View</c>.</summary>
public sealed class RoundIconButton : Decorator
{
    // :686 @State private var isHovering = false (implied by :701-703)
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    /// <param name="symbol">:686 <c>let symbol: String</c>.</param>
    /// <param name="help">:687 <c>let help: String</c>.</param>
    /// <param name="action">:688 <c>let action: () -> Void</c>.</param>
    // :692-707 body. PR-COMPOSER-120: a 30 by 30 circular button with a hover plate at 10 percent of the label colour.
    public RoundIconButton(string symbol, string help, Action action)
    {
        var state = new ViewState();
        var icon = SwiftImage.SystemName(symbol) // :693
            .Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Medium))
            .ForegroundStyle(Chrome.SecondaryText);
        var circle = new Ellipse { Fill = Brushes.Transparent }; // :695 Circle().fill(clear at rest)
        var fill = new AnimatedColor(circle);
        circle.Fill = fill.Brush;

        var frame = new Panel
        {
            Width = 30, // :693-694 .frame(width: 30, height: 30)
            Height = 30,
            Children = { circle, icon },
        };

        var button = PlainControls.Button(frame, action); // :692
        button.OnHover(hovering =>
        {
            Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering); // :696
            fill.Set(hovering ? Chrome.Overlay(0.1) : SwiftColor.Clear, Chrome.Hover); // :695
        });
        ToolTip.SetTip(button, help); // :699 .help(help)
        AutomationProperties.SetName(button, help); // :699 .accessibilityLabel(Text(help))
        Child = button;
    }
}

/// <summary>:711-837 <c>struct EffortSlider: View</c>.</summary>
public sealed partial class EffortSlider : Decorator
{
    // :712 static let trackHeight: CGFloat = 34
    public const double TrackHeight = 34;

    // :713 static let thumbSize: CGFloat = 42. DEVIATION OD-0090 (locked, 2026-10-03): the knob's diameter is 1.4 times the
    // track's height (1.4 x 34 = 47.6, 48 px), so it stands proud of the track above and below and sits flush with its end
    // at either extreme; the control's height is the knob's.
    public const double ThumbSize = 48;

    /// <summary>:793 .scaleEffect(1.06) while dragging; OD-0090: the pressed knob lifts to 1.06x where it is.</summary>
    public const double Lift = 1.06;

    /// <summary>OD-0090 (jewel-knob.html FADE): a model change crossfades the knob, the liquid and the lightning, s.</summary>
    public const double ColourFade = 0.6;

    /// <param name="count">:715 <c>let count: Int</c>.</param>
    /// <param name="index">:716 <c>@Binding var index: Int</c>.</param>
    /// <param name="accessibilityTitle">:725 <c>let accessibilityTitle: String</c>; moves before the defaulted ones
    /// (ED-095).</param>
    /// <param name="fastMode">:719 <c>var fastMode = false</c>.</param>
    /// <param name="brand">:721 <c>var brand: EffortBrand = .purple</c>.</param>
    /// <param name="pair">:724 <c>var pair: EffortPairBrand? = nil</c>.</param>
    /// <param name="fadeFrom">OD-0090: the maker shown before a model change; the knob, the liquid and the lightning
    /// crossfade from its colours to <paramref name="brand"/>'s over 0.6 s.</param>
    // :740-831 body
    private readonly Binding<int> index;
    private readonly int count;
    private readonly EffortBrand brand;
    private bool fastMode;
    private EffortPairBrand? pair;
    private SwiftColor?[] stopTargets = [];
    private readonly Canvas canvas = new() { Background = Brushes.Transparent }; // :800 .contentShape(.rect): the whole frame takes a press, the knob's proud 7 px above and below the track and the rounded ends too
    private readonly Border track = new() { Height = TrackHeight };
    private readonly Border fill = new() { Name = "EffortFill", Height = TrackHeight };
    private readonly Panel effects = new();
    private readonly Panel knob = new() { Width = ThumbSize, Height = ThumbSize, RenderTransformOrigin = RelativePoint.Center };
    private readonly JewelKnobView orbView;
    private TrackLook? drawnLook;
    private ThemeVariant? drawnTheme;
    private (double X, double Width, double Scale, TrackLook Look, ThemeVariant Theme)? laidOut;
    private TrackEffect? currentEffect;
    private readonly System.Collections.Generic.HashSet<TrackEffect> outgoingEffects = [];
    private readonly System.Collections.Generic.List<Ellipse> stops = [];

    // OD-0090, jewel-knob.html makeSlider/stepSlider: the knob's centre is the target (the pointer while held, else the
    // stop) plus a spring's offset that decays to nothing; its scale springs to 1.06 while held and back to 1.
    private bool held;
    private double finger, grab, shownX;
    private bool placed;
    private KnobSpring offset, scale = new(1);
    private double response = 0.32, damping = 0.78;

    // the frame loop (springs, the gem's flow, the lightning's signal, a colour fade)
    private readonly Action<TimeSpan> frameTick;
    private IFrameSource? frameSource;
    private bool frameRequested, attached;
    private ContinuousInstant? lastFrame;
    private readonly KnobFrameGate gate = new();
    private readonly ContinuousInstant born = SwiftClock.Now;

    // the knob's material, and a fade between two
    private readonly JewelMaterial material;
    private JewelMaterial? fadeFromMaterial;
    private readonly EffortBrand? fadeFrom;
    private readonly DateTimeOffset fadeStart = SwiftDate.Now;
    private JewelMaterial? recolored;
    private SKColor[]? fillColours;
    private (TrackLook Look, ThemeVariant Theme)? fillFor;
    private readonly ScaleTransform knobScale = new(1, 1);
    internal readonly JewelOrb Orb;
    internal readonly KnobSignal Signal = new();
    internal readonly JewelUniforms Uniforms = new();
    private readonly object jewelGate = new();
    private JewelScene? jewelScene;
    private sealed record JewelScene(JewelMaterial Material, TrackEffect? Effect, StrikeGeometry? Geometry, double Gain,
        bool Reduce, bool Dark, double Scaling, double X, double Width, SKColor[] Along, float Lift);
    internal bool JewelPaused => ReduceMotion || !FrontMonitor.Shared.Current.IsVisible;


    public EffortSlider(int count, Binding<int> index, string accessibilityTitle, bool fastMode = false, EffortBrand brand = EffortBrand.Purple, EffortPairBrand? pair = null,
        EffortBrand? fadeFrom = null)
    {
        this.count = count; this.index = index; this.brand = brand; this.fastMode = fastMode; this.pair = pair;
        material = JewelMaterial.For(brand);
        this.fadeFrom = fadeFrom is { } from && from != brand ? from : null;
        if (this.fadeFrom is { } origin) fadeFromMaterial = JewelMaterial.For(origin);
        Orb = new JewelOrb(fadeFromMaterial ?? material);
        Material = fadeFromMaterial ?? material;
        knob.RenderTransform = knobScale;
        orbView = new JewelKnobView(this) { Width = ThumbSize, Height = ThumbSize };
        frameTick = OnFrame;
        Height = ThumbSize;
        // OD-0090: the knob and the fill move by fractions of a pixel (springs), so they are laid out unrounded: a rounded
        // layout would step them a whole pixel at a time and put the bolt's start up to a pixel off where the page puts it
        foreach (var part in new Layoutable[] { canvas, track, fill, effects, knob, orbView }) part.UseLayoutRounding = false;
        track.Capsule(RoundedCornerStyle.Continuous); fill.Capsule(RoundedCornerStyle.Continuous);
        fill.Child = effects; fill.ClipToBounds = true;
        canvas.Children.Add(track); canvas.Children.Add(fill);
        for (var stop = 0; stop < count; stop++)
        {
            var dot = new Ellipse { Width = 5, Height = 5 }; stops.Add(dot); canvas.Children.Add(dot); // :767-774
        }
        // DEVIATION OD-0090: the source's Liquid Glass lens over a blurred glow (:777-792) is the Jewel orb
        knob.Children.Add(orbView); canvas.Children.Add(knob);
        Child = canvas;
        AutomationProperties.SetName(this, pair is null ? "Reasoning effort" : "The lead's reasoning effort");
        AutomationProperties.SetItemStatus(this, accessibilityTitle);
        foreach (var child in canvas.Children) AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(this); Press(e.GetPosition(this).X); e.Handled = true;
        };
        PointerMoved += (_, e) => { if (held) Move(e.GetPosition(this).X); };
        PointerReleased += (_, e) =>
        {
            if (!held) return;
            Release(); e.Pointer.Capture(null); e.Handled = true;
        };
        PointerCaptureLost += (_, _) => Release(); // jewel-knob.html: lostpointercapture lets go as a release does
        SizeChanged += (_, _) => { if (!held && offset.AtRest(0)) shownX = StopX(index.Value); LayoutTrack(); };
        ActualThemeVariantChanged += (_, _) => { LayoutTrack(); RequestFrame(); };
        AttachedToVisualTree += (_, _) =>
        {
            attached = true; lastFrame = null;
            if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window) FrontMonitor.Shared.Current.Observe(window); // as TrackEffect: paused while hidden
            RequestFrame();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            if (frameRequested) { frameSource?.CancelFrame(frameTick); frameRequested = false; }
            // :765 removal views end with the view lifetime. Animate stops detached playbacks, so finish
            // their removal here; a retained slider shown again starts with its current effect fully visible.
            foreach (var outgoing in outgoingEffects.ToArray()) effects.Children.Remove(outgoing);
            outgoingEffects.Clear();
            if (currentEffect is not null) currentEffect.Opacity = 1;
        };
        PropertyChanged += (_, change) => { if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) RequestFrame(); };
        this.Track(() => FrontMonitor.Shared.Current.IsVisible, visible => { if (visible) RequestFrame(); });
        this.Track(() => index.Value, _ => IndexChanged());
    }

    /// <summary>The knob's centre as shown (x, px from the slider's left).</summary>
    internal double KnobX => shownX;

    /// <summary>The knob's scale as shown (1, lifting to 1.06 while held).</summary>
    internal double KnobScale => scale.P;

    /// <summary>The deeper shadow's opacity: how far the knob has lifted, 0 to 1.</summary>
    internal double LiftAmount => Math.Clamp((scale.P - 1) / (Lift - 1), 0, 1);

    /// <summary>True while the knob is held.</summary>
    internal bool IsHeld => held;

    /// <summary>The knob's colours this frame (a crossfade's mix while one runs).</summary>
    internal JewelMaterial Material { get; private set; } = null!;

    private double Inset => ThumbSize / 2;
    private double Step => count > 1 ? (Bounds.Width - ThumbSize) / (count - 1) : 0;

    /// <summary>:746-748 stop centres sit a knob radius in from each end.</summary>
    private double StopX(int stop) => Inset + stop * Step;

    private double ClampX(double x) => Math.Min(Bounds.Width - Inset, Math.Max(Inset, x));

    // :833-836 nearestStop, here of the knob's centre (OD-0090: the grip's offset is kept, so the stop follows the knob)
    private int Nearest(double x) => count > 1 && Step > 0 ? Math.Clamp((int)Math.Floor((ClampX(x) - Inset) / Step + 0.5), 0, count - 1) : 0;

    private void SetIndex(int nearest)
    {
        if (nearest == index.Value) return;
        index.Value = nearest;
        Haptics.Perform(HapticPattern.Alignment); // PR-COMPOSER-125 windows_path: silent Windows no-op, no substitute cue.
    }

    // OD-0090: pressed on the knob, it lifts where it is and keeps the grip's offset; pressed on the track, it springs to
    // the pointer (:806 spring(response: 0.22, dampingFraction: 0.8)). Either way the shown x does not move at the press.
    private void Press(double px)
    {
        Place();
        var onKnob = Math.Abs(px - shownX) <= ThumbSize / 2;
        grab = onKnob ? px - shownX : 0;
        held = true;
        finger = ClampX(px - grab);
        offset.P = shownX - finger;
        response = 0.22; damping = 0.8;
        SetIndex(Nearest(finger));
        LayoutTrack(); RequestFrame();
    }

    // :808 later changes follow the pointer; the effort changes as the knob passes the midpoint between two stops
    private void Move(double px)
    {
        finger = ClampX(px - grab);
        SetIndex(Nearest(finger));
        RequestFrame();
    }

    // :817 on release the knob springs onto its stop and back to 1x (spring(response: 0.32, dampingFraction: 0.78))
    private void Release()
    {
        if (!held) return;
        held = false;
        offset.P = shownX - StopX(index.Value);
        response = 0.32; damping = 0.78;
        RequestFrame();
    }

    private void Place()
    {
        if (placed || Bounds.Width < ThumbSize) return;
        placed = true; shownX = StopX(index.Value);
    }

    // :822-830 an index change from outside a drag (an accessibility step, a reset): the knob springs to its new stop from
    // where it is (jewel-knob.html: the keyboard's step, spring(response: 0.2, dampingFraction: 1)), never a jump (OD-0076)
    private void IndexChanged()
    {
        if (!held && placed && Bounds.Width >= ThumbSize)
        {
            offset.P = shownX - StopX(index.Value);
            response = 0.2; damping = 1;
            RequestFrame();
        }
        LayoutTrack();
    }

    // :729-738, :822-830. A range peer exposes the increment/decrement step to Windows accessibility clients.
    internal void UpdateLook(bool fast, string title, EffortPairBrand? pairBrand)
    {
        fastMode = fast; pair = pairBrand;
        AutomationProperties.SetItemStatus(this, title); LayoutTrack(); RequestFrame();
    }

    // the look, made again only when what it reads changes (a frame reads it several times: no new record a frame, ED-252)
    private TrackLook Look
    {
        get
        {
            var kind = count > 1 && index.Value == count - 1
                ? fastMode ? TrackLook.Kind.Fusion : TrackLook.Kind.Supercharged
                : fastMode ? TrackLook.Kind.Fast : TrackLook.Kind.Plain;
            if (cachedLook is { } kept && kept.TrackLookKind == kind && Equals(kept.Pair, pair)) return kept;
            return cachedLook = new TrackLook(kind, brand, pair);
        }
    }

    private TrackLook? cachedLook;

    private bool ReduceMotion => EnvironmentValues.GetAccessibilityReduceMotion(this);

    private void RequestFrame()
    {
        if (frameRequested || !attached) return;
        frameRequested = true;
        if (lastFrame is null) lastFrame = SwiftClock.Now; // a loop starting (or waking) counts its first step from the request, never from an idle gap
        frameSource = FrameClock.Source.Current;
        frameSource.RequestFrame(frameTick);
    }

    // jewel-knob.html tick(): the colour fade, the springs, the lightning's signal, the orb
    private void OnFrame(TimeSpan _)
    {
        frameRequested = false;
        if (!attached) return;
        var now = SwiftClock.Now;
        // jewel-knob.html:867-871: drawn only on the refreshes the page's frame gate passes (KnobFrameGate: evenly paced,
        // about 60 a second on any display); the tube keeps its own 1/60 s timeline (ED-232)
        if (!gate.Draw((now - born).TotalMilliseconds)) { RequestFrame(); return; }
        // jewel-knob.html:813 tick(): the springs move by the time since the last drawn frame, at most 50 ms (after a stall
        // the knob goes on from where it was)
        var dt = lastFrame is { } previous ? Math.Min(MaxStep, Math.Max(0, (now - previous).TotalSeconds)) : 0;
        lastFrame = now;
        Advance(dt, (now - born).TotalSeconds);
        var visible = FrontMonitor.Shared.Current.IsVisible;
        var springing = held || !offset.AtRest(0) || !scale.AtRest(1);
        // the gem flows while shown; under reduce motion it is still, so frames run only while something moves (the
        // springs, a fade, the light on its way to the bolt's: coming up with a fast effect's own fade and the flare's two
        // stages, or letting go after fast mode); the springs and a fade always run to their end
        if (springing || fadeFromMaterial is not null || visible && (!orbView.OnRenderThread && !ReduceMotion || ReduceMotion && LightMoves)) RequestFrame();
        else lastFrame = null; // idle until something asks again
    }

    /// <summary>jewel-knob.html:813: a drawn frame moves the knob's springs by at most this, s.</summary>
    internal const double MaxStep = 0.05;

    /// <summary>
    /// The knob's light still has somewhere to go: the shaped signal has not reached the bolt's, or the fast effect the
    /// signal reads is still fading in (its opacity scales the signal, so its next value is not the one read). Under reduce
    /// motion the knob's loop runs while this holds, so the knob comes to the strike's still moment and then idles.
    /// </summary>
    internal bool LightMoves => !Signal.Settled || currentEffect is { } fast && TrackEffect.IsFast(fast.Look) && fast.Opacity < 1;

    /// <summary>One frame: <paramref name="dt"/> s since the last, <paramref name="time"/> s since the slider was made.</summary>
    internal void Advance(double dt, double time)
    {
        Place();
        if (placed)
        {
            var target = held ? finger : StopX(index.Value);
            offset.Step(0, dt, response, damping);
            scale.Step(held ? Lift : 1, dt, held ? 0.22 : 0.32, held ? 0.8 : 0.78);
            shownX = Math.Min(Bounds.Width - Inset + 3, Math.Max(Inset - 3, target + offset.P));
        }
        // the colours: a model change's crossfade, smoothstep over 0.6 s (0.2 s under reduce motion)
        var reduce = ReduceMotion;
        if (fadeFromMaterial is { } from)
        {
            var u = Math.Clamp((SwiftDate.Now - fadeStart).TotalSeconds / (reduce ? 0.2 : ColourFade), 0, 1);
            u = u * u * (3 - 2 * u);
            Material = JewelMaterial.Mix(from, material, u);
            if (u >= 1) fadeFromMaterial = null;
            // the effects' liquid and tint fade with it; under reduce motion their own timelines are paused, so draw them
            foreach (var child in effects.Children) if (child is TrackEffect fading) fading.Redraw();
        }
        else Material = material;
        LayoutTrack();
        // Main posts geometry and appearance only when an intent or a spring changes them.
        // The retained knob resolves the signal and flowing gem where it is drawn.
        var effect = currentEffect is { } current && TrackEffect.IsFast(current.Look) ? current : null;
        var drawn = effect is null ? default : double.IsNaN(fill.Width) ? effect.Bounds.Size : new Size(fill.Width, TrackHeight);
        var geometry = effect?.StrikeGeometryFor(drawn);
        var gain = effect?.Opacity ?? 0;
        var dark = ActualThemeVariant != ThemeVariant.Light;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var along = FillColours();
        var lift = (float)LiftAmount;
        var previousScene = Volatile.Read(ref jewelScene);
        if (previousScene is null || !ReferenceEquals(previousScene.Material, Material) || !ReferenceEquals(previousScene.Effect, effect) ||
            previousScene.Geometry != geometry || previousScene.Gain != gain || previousScene.Reduce != reduce || previousScene.Dark != dark ||
            previousScene.Scaling != scaling || previousScene.X != shownX || previousScene.Width != Bounds.Width ||
            !ReferenceEquals(previousScene.Along, along) || previousScene.Lift != lift)
        {
            effect?.PrepareScene(drawn);
            Volatile.Write(ref jewelScene, new JewelScene(Material, effect, geometry, gain, reduce, dark, scaling,
                shownX, Bounds.Width, along, lift));
        }
        if (!orbView.OnRenderThread || reduce) ResolveJewel(Uniforms.Values, time, SwiftDate.Now);
        orbView.InvalidateVisual();
    }

    internal void ResolveJewel(float[] into) => ResolveJewel(into, (SwiftClock.Now - born).TotalSeconds, SwiftDate.Now);

    private void ResolveJewel(float[] into, double time, DateTimeOffset date)
    {
        if (Volatile.Read(ref jewelScene) is not { } s) return;
        lock (jewelGate)
        {
            if (!ReferenceEquals(recolored, s.Material)) { Orb.Recolor(s.Material); recolored = s.Material; }
            if (s.Effect is { } effect)
            {
                effect.ReadMixSignal(Signal, time, s.Gain, orbView.OnRenderThread ? null : date);
            }
            else Signal.Read(null, 0, 0, time);
            var (size, perPx) = JewelOrb.RenderSize(ThumbSize, s.Scaling);
            Orb.Frame(time, Signal.Charge * s.Material.FxCharge, Signal.Flare * s.Material.FxFlare, Signal.ExitAngle,
                s.Reduce, size, perPx, s.Dark, Uniforms);
            Uniforms.Values[110] = JewelOrb.Supersampled(ThumbSize, s.Scaling) ? 2 : 1;
            Uniforms.SetPage(ThumbSize, 0.26, s.X, s.Width, TrackHeight,
                s.Dark ? new SKColor(0x14, 0x1d, 0x27) : new SKColor(0xe6, 0xeb, 0xf1),
                s.Dark ? new SKColor(0x11, 0x18, 0x20) : new SKColor(0xd1, 0xd8, 0xde), s.Along);
            Uniforms.Lift = s.Lift;
            if (!ReferenceEquals(into, Uniforms.Values)) Array.Copy(Uniforms.Values, into, into.Length);
        }
    }

    // the fill's colours along its length (5 samples), as the ring refracts them: the liquid's stops (every look draws
    // the liquid since OD-0075); made again only when the look or the theme changes
    private SKColor[] FillColours()
    {
        var look = Look;
        var theme = ActualThemeVariant;
        if (fillColours is { } kept && fillFor is { } key && Equals(key.Look, look) && key.Theme == theme) return kept;
        var into = new SKColor[5];
        fillFor = (look, theme); fillColours = into;
        // OD-0075: every look draws the liquid, so the fill's colours are always the liquid's stops
        var stops = LiquidSpec.For(look).Stops();
        for (var i = 0; i < into.Length; i++)
        {
            var at = i / (double)(into.Length - 1);
            var k = 1;
            while (k < stops.Count - 1 && at > stops[k].At) k++;
            var (a, b) = (stops[Math.Max(0, k - 1)], stops[Math.Min(stops.Count - 1, k)]);
            var f = b.At > a.At ? Math.Clamp((at - a.At) / (b.At - a.At), 0, 1) : 0;
            var mixed = LiquidTrack.FromOKLab(Lerp(LiquidTrack.ToOKLab(a.Color.Resolve(theme)), LiquidTrack.ToOKLab(b.Color.Resolve(theme)), f));
            into[i] = mixed;
        }
        return into;
        static (double, double, double) Lerp((double L, double A, double B) a, (double L, double A, double B) b, double f) =>
            (a.L + (b.L - a.L) * f, a.A + (b.A - a.A) * f, a.B + (b.B - a.B) * f);
    }

    // :740-799
    private void LayoutTrack()
    {
        var width = Bounds.Width;
        if (width < ThumbSize) return;
        Place();
        const double inset = ThumbSize / 2;
        var step = Step;
        var x = shownX;
        var look = Look;
        var key = (x, width, scale.P, look, ActualThemeVariant);
        if (laidOut is { } laid && laid == key) return;
        laidOut = key;
        track.Width = width;
        if (drawnLook is null || drawnTheme != ActualThemeVariant) { track.Background = Chrome.Overlay(0.1).Brush(track); drawnTheme = ActualThemeVariant; }
        Canvas.SetTop(track, (ThumbSize - TrackHeight) / 2);
        fill.Width = Math.Max(0, x + inset); // OD-0090: the fill runs to the knob's far edge (:756 x + inset)
        if (drawnLook is null) fill.Background = EffortView.Fill(look, ActualThemeVariant);
        else if (!Equals(drawnLook, look)) EffortView.AnimateBrush(fill, () => fill.Background, brush => fill.Background = brush, EffortView.Fill(look, ActualThemeVariant), Animation.Smooth(0.3));
        Canvas.SetTop(fill, (ThumbSize - TrackHeight) / 2);
        var lookChanged = drawnLook is not null && !Equals(drawnLook, look);
        if (stopTargets.Length != count) stopTargets = new SwiftColor?[count];
        for (var stop = 0; stop < count; stop++)
        {
            var stopX = inset + stop * step;
            var dot = stops[stop];
            var stopColor = stopX <= x ? look.StopColor : Chrome.Overlay(0.32);
            if (!Equals(stopTargets[stop], stopColor)) // an unchanged target leaves a crossfade in flight alone
            {
                stopTargets[stop] = stopColor;
                // :799 .animation(.smooth(duration: 0.3), value: look) reaches the stops as well as the fill.
                if (lookChanged) EffortView.AnimateBrush(dot, () => dot.Fill, brush => dot.Fill = brush, stopColor.Brush(dot), Animation.Smooth(0.3));
                else dot.Fill = stopColor.Brush(dot);
            }
            Canvas.SetLeft(dot, stopX - 2.5); Canvas.SetTop(dot, ThumbSize / 2 - 2.5);
        }
        knobScale.ScaleX = knobScale.ScaleY = scale.P; // about its centre: it lifts where it is (OD-0090)
        Canvas.SetLeft(knob, x - inset);
        if (drawnLook?.EffectIdentity != look.EffectIdentity && !Equals(drawnLook?.EffectIdentity, look.EffectIdentity))
        {
            // :760-767 .transition(.opacity) with :799 .animation(.smooth(duration: 0.3), value: look).
            // A solo identity change crossfades instead of jumping: the previous current effect is retained
            // under its OLD look while its opacity fades out, and the replacement fades in. Every later
            // layout pass with the same identity updates only the incoming effect (currentEffect below),
            // never FirstOrDefault, so the fading view keeps its look for its whole fade. A new change
            // during a fade does not restart the effects already leaving: each keeps its own fade and is
            // removed when that fade ends, the way a SwiftUI view already leaving plays its own removal
            // transition while a later transaction starts a new one (ED-092(2) smooth 0.3, settling per ED-011).
            var previous = currentEffect;
            currentEffect = null;
            // DEVIATION OD-0075 (liquid at every effort level): every look draws an effect, the plain one too (:760 overlays one only
            // when the kind is not plain or a pair exists)
            if (drawnLook is null)
            {
                var first = NewEffect(look);
                first.Opacity = 1;
                effects.Children.Add(first);
                currentEffect = first;
            }
            else
            {
                var fresh = NewEffect(look,previous);
                fresh.Opacity = 0;
                effects.Children.Add(fresh);
                currentEffect = fresh;
                EffortView.Animate(fresh, Animation.Smooth(0.3), progress => fresh.Opacity = Math.Clamp(progress, 0, 1));
                if (previous is not null)
                {
                    outgoingEffects.Add(previous);
                    var fading = previous;
                    var from = fading.Opacity;
                    EffortView.Animate(fading, Animation.Smooth(0.3), progress =>
                    {
                        if (progress >= 1) { fading.Opacity = 0; effects.Children.Remove(fading); outgoingEffects.Remove(fading); }
                        else fading.Opacity = from * (1 - Math.Clamp(progress, 0, 1));
                    });
                }
            }
        }
        else if (currentEffect is not null) currentEffect.SetLook(look);
        BindPaletteEffect(currentEffect); // ED-NEW-models-palette-14: retain the effect selected by the existing identity rule.
        foreach (var child in effects.Children) if (child is TrackEffect effect) effect.TrackWidth = width; // OD-0051: the liquid keeps the track's scale
        drawnLook = look;
    }

    // OD-0090: an effect made while a model change's colours fade starts in the old maker's liquid and lightning
    private TrackEffect NewEffect(TrackLook look,TrackEffect? previous=null)
    {
        var effect = new TrackEffect(look);
        if(previous is not null && previous.Look.Brand==look.Brand) effect.InheritTube(previous);
        effect.PropertyChanged += (_, change) => { if (change.Property == OpacityProperty && orbView.OnRenderThread) RequestFrame(); };
        // (0.2 s under reduce motion, as the knob: the still effect is drawn again each frame of the fade by the slider)
        if (fadeFrom is { } from && fadeFromMaterial is not null)
            effect.FadeFrom(LiquidSpec.For(look with { Brand = from }), StrikeTint.For(from), fadeStart, ColourFade, StrikeInk.Of(JewelLiquid.Of(from)));
        return effect;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new EffortPeer(this);
    // :820-830 one element: the label (Name), the value (the current effort's title, :826-827 accessibilityValue) and the adjustable
    // action (increment and decrement, clamped). UI Automation's two value patterns carry them: the RangeValue pattern the
    // adjustment, the Value pattern (read-only) the title a screen reader reads as the slider's value. ENGINEERING DECISION,
    // ED-286: Narrator reads Name and the RangeValue number or the Value string, not ItemStatus.
    private sealed class EffortPeer(EffortSlider owner) : ControlAutomationPeer(owner), IRangeValueProvider, IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        bool IRangeValueProvider.IsReadOnly => false;
        bool IValueProvider.IsReadOnly => true;
        string? IValueProvider.Value => AutomationProperties.GetItemStatus(owner);
        void IValueProvider.SetValue(string? value) { } // the title follows the effort; it is not a text to set
        public double Minimum => 0;
        public double Maximum => Math.Max(0, owner.count - 1);
        public double SmallChange => 1;
        public double LargeChange => 1;
        public double Value => owner.index.Value;
        public void SetValue(double value) => owner.index.Value = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, Math.Max(0, owner.count - 1));
    }
}

/// <summary>
/// DEVIATION OD-0090: the effort knob as the locked page draws it (jewel-knob.html .kw): its shadow and, as it lifts, a
/// deeper one; the Jewel orb; the tube refracted at its rim; the rim highlight and the glass fringe. One custom draw a frame.
/// </summary>
public sealed class JewelKnobView : Control
{
    private readonly EffortSlider owner;

    internal JewelKnobView(EffortSlider owner)
    {
        this.owner = owner;
        giveBack = op => { lock (free) if (free.Count < 4) free.Push(op); };
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        if (FrameClock.Source.Current is IRenderFrameSourceProvider)
        {
            timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(1.0 / 60, owner.JewelPaused), _ =>
            {
                if (!RenderFrameSource.InvalidateTicking()) InvalidateVisual();
            }, this);
            AttachedToVisualTree += (_, _) => timeline.Appear();
            DetachedFromVisualTree += (_, _) => timeline.Disappear();
            PropertyChanged += (_, change) =>
            {
                if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) timeline.Reschedule();
            };
        }
    }

    private readonly AnimationTimeline? timeline;
    private JewelKnobOperation? live;
    internal bool OnRenderThread => timeline?.Source is RenderFrameSource;

    /// <summary>How far the knob's shadows reach past its box, px.</summary>
    internal const double ShadowReach = 40;

    // the knob's operations, kept and reused once Avalonia gives them back (ED-252: no new object a frame)
    private readonly Stack<JewelKnobOperation> free = new();
    private readonly Action<JewelKnobOperation> giveBack;

    /// <summary>The knob's state on the CPU renderer (render thread).</summary>
    internal JewelCpuKnob Cpu { get; } = new();

    /// <summary>How many operations the view has made (tests: a few, reused).</summary>
    internal int OperationsMade { get; private set; }

    /// <summary>How many times the view has recorded its drawing (diagnostics).</summary>
    internal int Recorded { get; private set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Recorded++;
        var values = owner.Uniforms.Values;
        if (OnRenderThread)
        {
            live ??= new JewelKnobOperation(new Rect(Bounds.Size).Inflate(ShadowReach), new Rect(Bounds.Size),
                (float[])values.Clone(), Cpu, update: owner.ResolveJewel);
            context.Custom(live);
            return;
        }
        if (values[0] <= 0) return;
        JewelKnobOperation? op;
        lock (free) free.TryPop(out op);
        var bounds = new Rect(Bounds.Size).Inflate(ShadowReach);
        if (op is null) { op = new JewelKnobOperation(bounds, new Rect(Bounds.Size), (float[])values.Clone(), Cpu, giveBack); OperationsMade++; }
        else op.Reuse(bounds, new Rect(Bounds.Size), values);
        context.Custom(op);
    }
}

// File-private view construction shared by this Swift file's rows and brushes.
internal static class EffortView
{
    private sealed class Playback
    {
        public IFrameSource? Source;
        public Action<TimeSpan>? Tick;
        public void Stop() { if (Tick is not null) Source?.CancelFrame(Tick); Tick = null; }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Playback> playbacks = new();

    // :582-583, :799, :806, :817. Source animations use the shared frame/clock seams (ED-117).
    internal static void Animate(Control owner, Animation animation, Action<double> apply)
    {
        var playback = playbacks.GetValue(owner, control =>
        {
            var result = new Playback(); control.DetachedFromVisualTree += (_, _) => result.Stop(); return result;
        });
        playback.Stop();
        var start = SwiftClock.Now; var frames = FrameClock.Source.Current;
        apply(0);
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
            var elapsed = (now - start).TotalSeconds;
            var complete = now - start >= TimeSpan.FromSeconds(animation.Duration);
            apply(complete ? 1 : animation.Progress(elapsed));
            if (complete) playback.Tick = null;
            else frames.RequestFrame(tick!);
        };
        playback.Source = frames; playback.Tick = tick; frames.RequestFrame(tick);
    }

    // A brush set at once: any fade still running on the owner is stopped first, so it cannot overwrite it.
    internal static void SetBrush(Control owner, Action<IBrush> apply, IBrush target)
    {
        if (playbacks.TryGetValue(owner, out var playback)) playback.Stop();
        apply(target);
    }

    internal static void AnimateBrush(Control owner, Func<IBrush?> read, Action<IBrush> apply, IBrush target, Animation animation)
    {
        var from = read();
        if (from is null) { apply(target); return; }
        var count = Math.Max(from is LinearGradientBrush first ? first.GradientStops.Count : 1, target is LinearGradientBrush last ? last.GradientStops.Count : 1);
        Animate(owner, animation, progress =>
        {
            if (progress == 1) { apply(target); return; }
            var colors = Enumerable.Range(0, count).Select(i =>
            {
                var at = count > 1 ? i / (double)(count - 1) : 0;
                var color = Mix(Sample(from, at), Sample(target, at), progress);
                return new SwiftColor(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);
            }).ToArray();
            apply(count == 1 ? new SolidColorBrush(colors[0].Resolve(ThemeVariant.Light)) : Gradient(colors, ThemeVariant.Light));
        });
    }

    private static Color Sample(IBrush brush, double at)
    {
        if (brush is ISolidColorBrush solid) return solid.Color;
        var stops = ((LinearGradientBrush)brush).GradientStops;
        for (var i = 1; i < stops.Count; i++)
            if (at <= stops[i].Offset) return Mix(stops[i - 1].Color, stops[i].Color, (at - stops[i - 1].Offset) / (stops[i].Offset - stops[i - 1].Offset));
        return stops[^1].Color;
    }
    private static Color Mix(Color from, Color to, double progress)
    {
        static byte Channel(byte from, byte to, double progress) => (byte)Math.Clamp(Math.Round(from + (to - from) * progress, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(Channel(from.A, to.A, progress), Channel(from.R, to.R, progress), Channel(from.G, to.G, progress), Channel(from.B, to.B, progress));
    }
    // :841 semantic accent must be resolved before mixing; SwiftColor.Mix supports only fixed/appearance pairs.
    internal static SwiftColor Resolved(SwiftColor color, ThemeVariant theme)
    {
        var resolved = color.Resolve(theme);
        return new SwiftColor(resolved.R / 255.0, resolved.G / 255.0, resolved.B / 255.0, resolved.A / 255.0);
    }
    // :1189-1193, :1211-1216 Canvas paths preserve Swift's point order.
    internal static StreamGeometry Path(Point[] points, bool closed)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], closed);
        foreach (var point in points.Skip(1)) context.LineTo(point);
        context.EndFigure(closed);
        return geometry;
    }
    // :441-449, :573, :609-624
    internal static TextBlock Text(string value, SwiftFont font, SwiftColor color)
    {
        var text = new TextBlock { Text = value, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
        font.Apply(text); text.Foreground = color.Brush(text); return text;
    }
    // :412-419, :462-475
    internal static Button Row(Control row, double height, bool enabled, Action action)
    {
        var plate = new Border { Padding = new Thickness(10, 0), Height = height, Child = row }.RoundedRectangle(9, RoundedCornerStyle.Continuous);
        var fill = new AnimatedColor(plate); plate.Background = fill.Brush;
        var button = PlainControls.Button(plate, action); button.IsEnabled = enabled; button.Opacity = enabled ? 1 : 0.45;
        // :415 .contentShape(.rect): include the unpainted corners outside the rounded hover plate.
        // The plain template's presenter needs a transparent background to participate in rectangular hit testing.
        button.TemplateApplied += (_, _) =>
        {
            if (button.GetVisualChildren().Single() is Avalonia.Controls.Presenters.ContentPresenter presenter)
                presenter.Background = Brushes.Transparent;
        };
        button.OnHover(hovering => fill.Set(hovering && enabled ? Chrome.Overlay(0.1) : SwiftColor.Clear, Chrome.Hover));
        return button;
    }
    // :885-908, :985-998
    internal static IBrush Gradient(System.Collections.Generic.IEnumerable<SwiftColor> colors, ThemeVariant theme)
    {
        var values = colors.ToArray(); var stops = new GradientStops();
        for (var i = 0; i < values.Length; i++) stops.Add(new GradientStop(values[i].Resolve(theme), values.Length > 1 ? i / (double)(values.Length - 1) : 0));
        return new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative), GradientStops = stops };
    }
    internal static IBrush BrandTitle(EffortBrand brand, ThemeVariant theme) => brand == EffortBrand.Antigravity ? Gradient(EffortBrand.Gemini, theme) : new SolidColorBrush(brand.TitleColor.Resolve(theme));
    internal static IBrush Fill(TrackLook look, ThemeVariant theme)
    {
        // :986-988: pair colours own the fill for every kind; fast/fusion add effects, never a fill stop.
        if (look.Pair is { } pair) return Gradient(new[] { look.Brand.FillColor }.Concat(pair.Colors), theme);
        return look.TrackLookKind switch
        {
            // :990 plain = Chrome.accent. DEVIATION OD-0075 (liquid at every effort level): the plain track is the liquid too, so the
            // fill under it is the brand's, as at the top (the accent is no longer this track's colour)
            TrackLook.Kind.Plain or TrackLook.Kind.Supercharged => look.Brand switch // :889-909 brand.fill
            {
                EffortBrand.Antigravity => Gradient(EffortBrand.Gemini, theme),
                EffortBrand.Meta => Gradient([new SwiftColor(0.0, 0.39, 0.88), new SwiftColor(0.0, 0.51, 0.98)], theme), // :894-897
                EffortBrand.Silver => Gradient([SwiftColor.White, new SwiftColor(0.88, 0.88, 0.91)], theme), // :902-905
                EffortBrand.Claude or EffortBrand.Deepseek or EffortBrand.Purple => new SolidColorBrush(look.Brand.FillColor.Resolve(theme)),
                _ => Gradient(look.Brand.LiquidStops, theme), // OD-0047: the new brands' liquid colours
            },
            // :992-996 the amber fast fill and the brand-to-amber fusion. DEVIATION OD-0073 (ED-248): fast mode is the model's
            // liquid with Strike at every effort, so the fill under it (seen only at its rim and while it fades in) is the
            // brand's, as at the top
            _ => Fill(look with { TrackLookKind = TrackLook.Kind.Supercharged }, theme),
        };
    }
}

/// <summary>:839-847 <c>enum EffortPalette</c>.</summary>
public static class EffortPalette
{
    // :841 static var title: Color { Chrome.accent.mix(with: .white, by: 0.22) }: no colour space is named, so SwiftUI's default,
    // perceptual (Oklab), mixes it (ENGINEERING DECISION, ED-286, OD-0098)
    public static SwiftColor Title => SwiftColor.Dynamic(
        EffortView.Resolved(Chrome.Accent, ThemeVariant.Light).MixPerceptual(SwiftColor.White, 0.22),
        EffortView.Resolved(Chrome.Accent, ThemeVariant.Dark).MixPerceptual(SwiftColor.White, 0.22));

    public static SwiftColor Supercharged { get; } = new(red: 0.74, green: 0.55, blue: 1.0);      // :842
    public static SwiftColor SuperchargedFill { get; } = new(red: 0.55, green: 0.34, blue: 0.97); // :843
    public static SwiftColor Fast { get; } = new(red: 1.0, green: 0.84, blue: 0.36);              // :845
    public static SwiftColor FastFill { get; } = new(red: 0.93, green: 0.66, blue: 0.13);         // :846
}

/// <summary>
/// :853-936 <c>enum EffortBrand: Hashable</c>, no raw values. DEVIATION OD-0047 (approved and locked, OD-0051): every
/// provider has its own brand; the source's shared <c>.silver</c> stays Codex's (ChatGPT), and nine providers that
/// shared it take their own colours (the canvas of OD-0051, copy at E:\Droppy\work\design\effort-liquid-approved-v15.dc.html).
/// </summary>
public enum EffortBrand
{
    Claude,      // :854
    Deepseek,    // :855
    Meta,        // :856
    Antigravity, // :857
    Silver,      // :858, Codex (OD-0047: locked silver-white)
    Purple,      // :860
    Cursor,      // OD-0047: brand #F54E00, ember
    Grok,        // OD-0047: brand black, liquid obsidian with an oil sheen
    Opencode,    // OD-0047: monochrome brand, terminal green
    OpencodeGo,  // OD-0047: Go gopher cyan
    Zai,         // OD-0047: brand navy into electric blue
    Devin,       // OD-0047: brand teal #5ECFB1
    Copilot,     // OD-0047: brand purple into peach
    Commandcode, // OD-0047: brand deep indigo
    Pi,          // OD-0047: brand black-red, crimson
}

/// <summary>The members of <c>EffortBrand</c> (:862-935) as a C# 14 extension block (R-NAME-4, ED-164).</summary>
public static class EffortBrandExtensions
{
    extension(EffortBrand)
    {
        // :862-871 init(provider:). PR-COMPOSER-129: ten providers share Silver; a null provider (no pair, no
        // fast mode, no maximum effort) is Purple.
        public static EffortBrand Create(ProviderKind? provider) => provider switch
        {
            ProviderKind.Claude => EffortBrand.Claude, // :864
            ProviderKind.Deepseek => EffortBrand.Deepseek, // :865
            ProviderKind.Meta => EffortBrand.Meta, // :866
            ProviderKind.Antigravity => EffortBrand.Antigravity, // :867
            ProviderKind.Codex => EffortBrand.Silver, // :868 (OD-0047: Silver is Codex's alone)
            ProviderKind.Cursor => EffortBrand.Cursor,
            ProviderKind.Grok => EffortBrand.Grok,
            ProviderKind.Opencode => EffortBrand.Opencode,
            ProviderKind.OpencodeGo => EffortBrand.OpencodeGo,
            ProviderKind.Zai => EffortBrand.Zai,
            ProviderKind.Devin => EffortBrand.Devin,
            ProviderKind.Copilot => EffortBrand.Copilot,
            ProviderKind.Commandcode => EffortBrand.Commandcode,
            ProviderKind.Pi => EffortBrand.Pi,
            ProviderKind.OpenRouter => EffortBrand.Purple, // OD-0050 pending: automatic colour
            null => EffortBrand.Purple, // :869
            _ => throw new UnreachableException(),
        };

        // :931-935 static let gemini
        public static ValueList<SwiftColor> Gemini => GeminiColors;
    }

    // OD-0047 / OD-0051: the approved palette, exactly as on the locked canvas. The source brands keep their source
    // colours; their liquid stops bracket the source fill.
    private static readonly System.Collections.Generic.Dictionary<EffortBrand, string[]> LiquidStopsHex = new()
    {
        [EffortBrand.Silver] = ["#FFFFFF", "#EEF0F5", "#D9DBE3"],
        [EffortBrand.Claude] = ["#B85A38", "#CC6B47", "#E0875F"],
        [EffortBrand.Deepseek] = ["#2A45E0", "#3D5CFA", "#6A82FF"],
        [EffortBrand.Meta] = ["#0054C4", "#0070EC", "#2B95FF"],
        [EffortBrand.Antigravity] = ["#4285F5", "#9C73CC", "#D96670"],
        [EffortBrand.Purple] = ["#7A4BE0", "#8C57F7", "#B48CFF"],
        [EffortBrand.Cursor] = ["#D93F00", "#F54E00", "#FF8040"],
        [EffortBrand.Grok] = ["#030304", "#121218", "#26262E"],
        [EffortBrand.Opencode] = ["#168A3A", "#25B34F", "#45DC6C"],
        [EffortBrand.OpencodeGo] = ["#007EA3", "#00A4CD", "#3CCBEC"],
        [EffortBrand.Zai] = ["#0A1630", "#14306E", "#2F6BFF"],
        [EffortBrand.Devin] = ["#239377", "#3CBC9B", "#6FDCBF"],
        [EffortBrand.Copilot] = ["#8534F3", "#D25AB4", "#F4A876"],
        [EffortBrand.Commandcode] = ["#24157E", "#2E1B9C", "#6145DC"],
        [EffortBrand.Pi] = ["#A30C25", "#C8102E", "#EC5560"],
    };

    private static readonly System.Collections.Generic.Dictionary<EffortBrand, string> LiquidTitles = new()
    {
        [EffortBrand.Cursor] = "#FF8A4C",
        [EffortBrand.Grok] = "#D4D6DE",
        [EffortBrand.Opencode] = "#5BE07F",
        [EffortBrand.OpencodeGo] = "#4FD0EE",
        [EffortBrand.Zai] = "#6E98FF",
        [EffortBrand.Devin] = "#7FE3C9",
        [EffortBrand.Copilot] = "#C898FD",
        [EffortBrand.Commandcode] = "#A98BFF",
        [EffortBrand.Pi] = "#FF6B7A",
    };

    private static SwiftColor Hex(string hex)
    {
        var c = Avalonia.Media.Color.Parse(hex);
        return new SwiftColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
    }

    private static readonly ValueList<SwiftColor> GeminiColors = new(
    [
        new SwiftColor(0.26, 0.52, 0.96),
        new SwiftColor(0.61, 0.45, 0.80),
        new SwiftColor(0.85, 0.40, 0.44),
    ]);

    extension(EffortBrand brand)
    {
        // :874-883 var titleColor: Color
        public SwiftColor TitleColor => brand switch
        {
            EffortBrand.Claude => new SwiftColor(0.93, 0.58, 0.44), // :876
            EffortBrand.Deepseek => new SwiftColor(0.49, 0.58, 1.0), // :877
            EffortBrand.Meta => new SwiftColor(0.31, 0.64, 1.0), // :878
            EffortBrand.Antigravity => new SwiftColor(0.61, 0.45, 0.80), // :879
            EffortBrand.Silver => Chrome.PrimaryText, // :880
            EffortBrand.Purple => new SwiftColor(0.74, 0.55, 1.0), // :881, EffortPalette.Supercharged
            _ => Hex(LiquidTitles[brand]), // OD-0047
        };

        /// <summary>OD-0047 / OD-0051: the three colours of the brand's liquid, dark to light along the track.</summary>
        public ValueList<SwiftColor> LiquidStops => new(LiquidStopsHex[brand].Select(Hex));

        /// <summary>OD-0051: Grok's liquid obsidian carries a moving oil sheen.</summary>
        public bool HasOilSheen => brand == EffortBrand.Grok;

        // :911-920 var fillColor: Color
        public SwiftColor FillColor => brand switch
        {
            EffortBrand.Claude => new SwiftColor(0.80, 0.42, 0.28), // :895
            EffortBrand.Deepseek => new SwiftColor(0.24, 0.36, 0.98), // :896
            EffortBrand.Meta => new SwiftColor(0.0, 0.45, 0.93), // :897-900
            EffortBrand.Antigravity => new SwiftColor(0.56, 0.45, 0.80), // :901
            EffortBrand.Silver => new SwiftColor(0.92, 0.92, 0.94), // :902-905
            EffortBrand.Purple => new SwiftColor(0.55, 0.34, 0.97), // :906, EffortPalette.SuperchargedFill
            _ => Hex(LiquidStopsHex[brand][1]), // OD-0047: the liquid's heart
        };

        // :923 var spark: Color
        public SwiftColor Spark => brand == EffortBrand.Silver ? SwiftColor.Black : SwiftColor.White;

        // :926 var isLight: Bool
        public bool IsLight => brand == EffortBrand.Silver;

        // :928 var glow: Color
        public SwiftColor Glow => brand == EffortBrand.Silver ? SwiftColor.Black.Opacity(0.35) : brand.FillColor.Opacity(0.6);
    }
}

/// <summary>:942-965 <c>struct EffortPairBrand: Hashable</c>.</summary>
public sealed record EffortPairBrand
{
    public ValueList<SwiftColor> Colors { get; init; } // :944
    public bool IsLight { get; init; }                 // :946

    /// <summary>OD-0048: the heads' own brand when they run elsewhere (their liquid in the blend), else null.</summary>
    public EffortBrand? HeadsBrand { get; init; }

    // :948-961 init(_ pair: HydraPair). PR-COMPOSER-130: a pair's far-end colours are the heads' provider's brand
    // when they run elsewhere, else the first four roster persona colours.
    public EffortPairBrand(HydraPair pair)
    {
        if (pair.SendsHeadsElsewhere)
        {
            var brand = EffortBrand.Create(pair.HeadsProvider);
            HeadsBrand = brand;
            Colors = brand switch
            {
                EffortBrand.Antigravity => EffortBrand.Gemini,
                EffortBrand.Silver => new ValueList<SwiftColor>([new SwiftColor(white: 0.8), new SwiftColor(white: 0.95)]),
                _ => new ValueList<SwiftColor>([brand.FillColor, brand.TitleColor]),
            };
            IsLight = brand.IsLight;
        }
        else
        {
            Colors = new ValueList<SwiftColor>(HydraRoster.Personas.Take(4).Select(persona =>
                new SwiftColor(persona.Color.Red, persona.Color.Green, persona.Color.Blue, persona.Color.Alpha)));
            IsLight = false;
        }
    }

    // :964 var titleColors: [Color] { colors.map { $0.mix(with: .white, by: 0.18) } }: the default (perceptual) colour space again
    public ValueList<SwiftColor> TitleColors => new(Colors.Select(color => color.MixPerceptual(SwiftColor.White, 0.18)));

    // :942 `struct EffortPairBrand: Hashable`: equal when the colours are equal, whichever instance built them. SwiftColor here is a class, so the
    // record's own member-wise comparison would tell two brands made from the same pair apart, and a card that stays while the thread
    // is written (ModelEffortPanel hands it a new brand each time) would look like a different pair to the track's effect identity
    // (:1020-1026), which would restart the effect's clock on every write.
    public bool Equals(EffortPairBrand? other) =>
        other is not null && IsLight == other.IsLight && Equals(HeadsBrand, other.HeadsBrand) && Colors.Count == other.Colors.Count &&
        Colors.Zip(other.Colors).All(pair => SameColour(pair.First, pair.Second));

    public override int GetHashCode() => HashCode.Combine(IsLight, HeadsBrand, Colors.Count);

    private static bool SameColour(SwiftColor a, SwiftColor b) =>
        a.Resolve(Avalonia.Styling.ThemeVariant.Light) == b.Resolve(Avalonia.Styling.ThemeVariant.Light) &&
        a.Resolve(Avalonia.Styling.ThemeVariant.Dark) == b.Resolve(Avalonia.Styling.ThemeVariant.Dark);
}

/// <summary>:968-1027 <c>struct TrackLook: Hashable</c>.</summary>
/// <param name="TrackLookKind">:979 <c>var kind: Kind</c> (R-NAME-3, Swift: kind).</param>
/// <param name="Brand">:980 <c>var brand: EffortBrand</c>.</param>
/// <param name="Pair">:983 <c>var pair: EffortPairBrand? = nil</c>.</param>
public sealed record TrackLook(TrackLook.Kind TrackLookKind, EffortBrand Brand, EffortPairBrand? Pair = null)
{
    /// <summary>:969-977 <c>enum Kind: Hashable</c>.</summary>
    public enum Kind
    {
        Plain,        // :970
        Supercharged, // :972
        Fast,         // :974
        Fusion,       // :976
    }

    // :1000-1008 var glow: Color. PR-COMPOSER-131: the knob's glow follows the pair when there is one.
    public SwiftColor Glow
    {
        get
        {
            if (Pair is { } pair && pair.Colors.Count > 0)
                return pair.IsLight ? SwiftColor.Black.Opacity(0.35) : pair.Colors[^1].Opacity(0.7);

            return TrackLookKind switch
            {
                Kind.Plain => SwiftColor.Black.Opacity(0.28),
                Kind.Supercharged => Brand.Glow,
                Kind.Fast => new SwiftColor(0.93, 0.66, 0.13).Opacity(0.7),
                Kind.Fusion => new SwiftColor(1.0, 0.84, 0.36).Opacity(0.75),
                _ => throw new UnreachableException(),
            };
        }
    }

    // :1012 var isLightFill: Bool. PR-COMPOSER-131: the light-fill test follows the pair when there is one. :1010-1011 "Whether the
    // fill under the knob is light (a white brand at maximum, not in fusion, whose far end is gold)": the source draws a white brand's
    // fill only at .supercharged, so the kind test there is the fill's colour. DEVIATION OD-0075 (the liquid at every effort level)
    // draws that same white fill on the plain track too, and OD-0073 (ED-248) under the fast look (the source's is the amber :992), so
    // by lead ruling R1 (ED-286 (6)) the test follows the fill drawn and holds for Plain, Fast and Supercharged. The
    // fusion look keeps the source's rule (not light), by the same ruling.
    public bool IsLightFill => Pair?.IsLight ?? (TrackLookKind is Kind.Plain or Kind.Fast or Kind.Supercharged && Brand.IsLight);

    // :1015 var stopColor: Color. PR-COMPOSER-131: the stop colour follows the pair when there is one (via IsLightFill).
    public SwiftColor StopColor => IsLightFill ? SwiftColor.Black.Opacity(0.35) : SwiftColor.White.Opacity(0.6);

    // :1018 var spark: Color
    public SwiftColor Spark => Pair is null && TrackLookKind is Kind.Supercharged or Kind.Fusion ? Brand.Spark : SwiftColor.White;

    // :1024-1026 var effectIdentity: AnyHashable
    public object EffectIdentity => Pair is { } pair ? this with { TrackLookKind = Kind.Plain, Pair = pair } : this;

    // ED-248 review 2: the record's generated description printed every public property, EffectIdentity too, which is the
    // look itself (solo) or another look (pair) whose description prints its EffectIdentity again: ToString recursed until
    // the stack overflowed. A Swift struct describes its stored properties (kind, brand, pair), and so does this.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("TrackLookKind = ").Append(TrackLookKind).Append(", Brand = ").Append(Brand).Append(", Pair = ").Append(Pair);
        return true;
    }
}

/// <summary>:1039-1347 <c>struct TrackEffect: View</c>.</summary>
public sealed partial class TrackEffect : Decorator, LiquidTrack.ILiveSource
{
    private TrackLook look = null!;
    private readonly DateTimeOffset startedAt = SwiftDate.Now;
    private readonly EffectCanvas canvas;
    private double time; // seconds since the track appeared, as of its last draw (written where the timeline drew)
    private long draws;
    /// <param name="look">:1040 <c>let look: TrackLook</c>.</param>
    // :1045-1072 body
    public TrackEffect(TrackLook look)
    {
        this.look = look;
        canvas = new EffectCanvas(this);
        live = new LiquidTrack.Live(this);
        Child = canvas;
        IsHitTestVisible = false; AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :1070-1071
        UseLayoutRounding = false; canvas.UseLayoutRounding = false; // OD-0090: it ends where the knob's far edge is, to the fraction
        AttachedToVisualTree += (_, _) => { if (appeared) Volatile.Write(ref startRequest, 1); appeared=true; if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window) FrontMonitor.Shared.Current.Observe(window); };
        // ED-248 review 2 (OD-0069): the timeline's frames come on the render thread (FrameClock.TimelineView, onRenderThread),
        // where an update only moves the clock and marks the track's area: the same tick redraws the track's one recorded
        // operation at the new time (LiquidTrack.Live). Main records the track again only when what it draws changes.
        this.TimelineView(() => new AnimationTimelineSchedule(
            1.0 / 60, // :1049 (30 for a solo supercharged track); DEVIATION OD-0051: that track is now liquid and runs at 60
            EnvironmentValues.GetAccessibilityReduceMotion(this) || !FrontMonitor.Shared.Current.IsVisible), date =>
        {
            Volatile.Write(ref time, (date - startedAt).TotalSeconds);
            Interlocked.Increment(ref draws);
            if (!RenderFrameSource.InvalidateTicking()) canvas.InvalidateVisual();
        }, onRenderThread: true); // :1049-1068
        // OD-0073: Reduce Motion turned on or off at run time shows the fast look's still (or lets it run) at once
        PropertyChanged += (_, change) => { if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) canvas.InvalidateVisual(); };
        // ED-248 review 2: what the track draws is recorded again when the theme or the size changes (no longer every frame)
        ActualThemeVariantChanged += (_, _) => canvas.InvalidateVisual();
        canvas.SizeChanged += (_, _) => canvas.InvalidateVisual();
        this.Track(() => FrontMonitor.Shared.Current.IsVisible, visible =>
        {
            if (visible && mixClock is not null) Volatile.Write(ref startRequest, 1);
        }); // SPEC B-09 / P:1391-1434: a retained tube shown again waits for a clean frame.
        // ED-248 review: the liquid's surfaces live as long as the track is in a window; leaving it hands them to the render
        // thread to dispose
        DetachedFromVisualTree += (_, _) => { if(ownsLayers) layers?.Retire(); layers = null; scene = null; };
    }

    // :1024-1026 pair kind changes preserve this view's clock and trait cache.
    internal void SetLook(TrackLook value)
    {
        if (look.Brand != value.Brand)
        {
            var at = TimeAt(SwiftDate.Now)+mixPageOrigin; inkFrom = scene is { } s ? InkAt(s, at) : StrikeInk.Of(JewelLiquid.Of(look.Brand)); inkStart = at;
            Volatile.Write(ref startRequest, 1);
        }
        look = value; canvas.InvalidateVisual();
    }

    /// <summary>Draws again now (a colour fade while its own timeline is paused under reduce motion).</summary>
    internal void Redraw() => canvas.InvalidateVisual();

    /// <summary>The look this effect draws.</summary>
    internal TrackLook Look => look;

    /// <summary>This effect's clock at <paramref name="date"/>, s (the Strike's time: the knob reads its lightning on it).</summary>
    internal double TimeAt(DateTimeOffset date) => (date - startedAt).TotalSeconds;

    private (LiquidSpec Spec, StrikeTint Tint, DateTimeOffset Start, double Duration)? fade;
    private SkiaSharp.SKColor[] fadeGradient = [];
    private (LiquidSpec Spec, int Pixels, ThemeVariant Theme)? fadeGradientFor;

    /// <summary>
    /// OD-0090 (jewel-knob.html recolour/stepFade): a model change crossfades the liquid and the lightning's tint from the
    /// previous maker's (<paramref name="spec"/>, <paramref name="tint"/>) over <paramref name="duration"/> s from
    /// <paramref name="start"/>, smoothstep; the page fades the incoming liquid layer's opacity over the outgoing one.
    /// </summary>
    internal void FadeFrom(LiquidSpec spec, StrikeTint tint, DateTimeOffset start, double duration, StrikeInk? ink = null)
    {
        fade = (spec, tint, start, duration);
        if (ink is { } from) { inkFrom = from; inkStart = (start - startedAt).TotalSeconds+mixPageOrigin; }
    }

    /// <summary>How far a colour fade has gone at this effect's <paramref name="time"/> (1: none, or done).</summary>
    internal double FadeAt(double time)
    {
        if (fade is not { } f) return 1;
        // under reduce motion 0.2 s, as the knob's (read here: the effect may be made before it is in the tree)
        var duration = EnvironmentValues.GetAccessibilityReduceMotion(this) ? 0.2 : f.Duration;
        var u = Math.Clamp((startedAt.AddSeconds(time) - f.Start).TotalSeconds / duration, 0, 1);
        return u * u * (3 - 2 * u);
    }

    // the liquid and the tint this frame: during a fade, the outgoing maker's gradient under the incoming one at the fade's
    // opacity (a new row each frame of the fade: the drawing reads it on the render thread), and the tints mixed
    internal (SkiaSharp.SKColor[] Gradient, StrikeTint Tint) Colours(LiquidSpec spec, double width, ThemeVariant theme, double time)
    {
        var gradient = Gradient(spec, width, theme);
        var tint = StrikeTint.For(look.Brand);
        var u = FadeAt(time);
        if (fade is not { } f) return (gradient, tint);
        if (u >= 1) { fade = null; return (gradient, tint); }
        if (fadeGradientFor is not { } key || key.Pixels != gradient.Length || key.Theme != theme || !key.Spec.Equals(f.Spec))
        {
            fadeGradientFor = (f.Spec, gradient.Length, theme);
            fadeGradient = LiquidTrack.GradientFor(f.Spec, gradient.Length, theme);
        }
        var mixed = new SkiaSharp.SKColor[gradient.Length];
        for (var i = 0; i < mixed.Length; i++)
        {
            SkiaSharp.SKColor a = fadeGradient[i], b = gradient[i];
            static byte C(byte x, byte y, double u) => (byte)Math.Clamp(Math.Round(x + (y - x) * u), 0, 255);
            mixed[i] = new SkiaSharp.SKColor(C(a.Red, b.Red, u), C(a.Green, b.Green, u), C(a.Blue, b.Blue, u), C(a.Alpha, b.Alpha, u));
        }
        return (mixed, StrikeTint.Mix(f.Tint, tint, u));
    }

    /// <summary>OD-0051: maximum effort and every pair draw the liquid tube; OD-0073: so does fast mode at every effort; OD-0075:
    /// and every look, the plain one included (:1054 draws nothing for it).</summary>
    internal static bool IsLiquid(TrackLook look) => true;

    /// <summary>The canvas's white liquid has dark-rimmed bubbles: a brand's light fill (never a pair's, whose segments are coloured).</summary>
    internal static bool DarkBubbles(TrackLook look) => look.Pair is null && look.IsLightFill;

    /// <summary>OD-0073: fast mode (below the top, or at it) draws Strike over the liquid.</summary>
    internal static bool IsFast(TrackLook look) => look.TrackLookKind is TrackLook.Kind.Fast or TrackLook.Kind.Fusion;

    /// <summary>How often the timeline has drawn (on main or on the render thread; tests count the frames with it).</summary>
    internal long Draws => Interlocked.Read(ref draws);

    /// <summary>The track's time as of its last draw, seconds since it appeared.</summary>
    internal double Time => Volatile.Read(ref time);

    private StrikeChannels? strikes;
    private LiquidTrack.LiquidLayers? layers;
    private TrackLook? specLook;
    private LiquidSpec? spec;
    private readonly LiquidTrack.Live live;
    private Scene? scene;
    private StrikeStorm? mixStorm;
    private StrikeBubbleLanes? mixLanes;
    private StrikeContacts? mixContacts;
    private StrikeFastClock? mixClock;
    private StrikeReentry? reentry;
    private bool mixFast;
    private int startRequest;
    private bool appeared,ownsLayers=true;
    private double mixPageOrigin;
    private StrikeInk? inkFrom;
    private double inkStart;
    private double resolvedPageTime = double.NaN;
    private Scene? resolvedScene;
    private LiquidTrack.Frame cachedFrame;
    internal IStrikeDraws StrikeDraws { get; set; } = CryptoStrikeDraws.Shared;
    internal double StrikeOffset => (reentry?.Offset ?? 0)+mixPageOrigin;
    internal double StrikeFrameTime { get; private set; }
    internal bool StrikeStartPending => Volatile.Read(ref startRequest) != 0 || reentry?.Pending == true;

    /// <summary>The track's drawing state kept across frames (ED-248 review), made on its first liquid frame.</summary>
    internal LiquidTrack.LiquidLayers? Layers => layers;

    // the look's liquid, made again only when the look changes (not every frame)
    private LiquidSpec SpecFor(TrackLook value)
    {
        if (spec is null || !ReferenceEquals(specLook, value)) { specLook = value; spec = LiquidSpec.For(value); }
        return spec;
    }

    /// <summary>This track's Strike seed (the board's seed * 97 + 13 over the track's own number).</summary>
    internal int StrikeSeed => unchecked(bubbleSeed * 97 + 13);

    /// <summary>The knob's centre, in from the liquid's end, and its radius (the slider's knob: 42 px, centred on the fill's
    /// end less 21). The Strike channel leaves the knob's centre; its sparks start inside the knob.</summary>
    internal double KnobInset { get => knobInset; set { if (knobInset != value) { knobInset = value; canvas.InvalidateVisual(); } } }
    internal double KnobRadius { get => knobRadius; set { if (knobRadius != value) { knobRadius = value; canvas.InvalidateVisual(); } } }
    private double knobInset = EffortSlider.ThumbSize / 2, knobRadius = EffortSlider.ThumbSize / 2;


    /// <summary>
    /// Where the bolt leaves for a knob centred at <paramref name="knob"/> (jewel-knob.html:593 stepSlider, tl.exAt =
    /// Math.round((x - 2) * 4) / 4, read by drawTile as g.ex, :335): 2 px before the centre, on a quarter pixel (JavaScript's
    /// Math.round: halves up).
    /// </summary>
    internal static double ExitAt(double knob) => Math.Floor((knob - 2) * 4 + 0.5) / 4;

    internal StrikeGeometry StrikeGeometryFor(Size size)
    {
        return new StrikeGeometry(Math.Max(TrackWidth, size.Width), size.Height, ExitAt(size.Width - KnobInset), KnobRadius);
    }



    /// <summary>
    /// The whole track's width (the slider sets it; the effect itself spans only the filled part). The liquid's currents,
    /// light and bubbles keep the track's scale, so moving the knob never stretches them.
    /// </summary>
    internal double TrackWidth { get => trackWidth; set { if (trackWidth != value) { trackWidth = value; canvas.InvalidateVisual(); } } }
    private double trackWidth;

    private BubbleLanes? bubbles;

    /// <summary>OD-0078: this track's bursts (fast mode), with lanes of their own.</summary>
    internal BubbleBursts Bursts { get; } = new();
    private int bubbleSeed = System.Threading.Interlocked.Increment(ref bubbleSeeds);
    private static int bubbleSeeds;

    // Keep Swift's replacement-view identity/local clock, while the source Strike tube continues across a kind toggle.
    internal void InheritTube(TrackEffect previous)
    {
        if(previous.layers is not {} heldLayers) return;
        lock(heldLayers)
        {
            layers=heldLayers; previous.ownsLayers=false;
            mixPageOrigin=previous.mixPageOrigin+previous.Time;
            bubbleSeed=previous.bubbleSeed; mixStorm=previous.mixStorm; mixLanes=previous.mixLanes; mixContacts=previous.mixContacts;
            mixClock=previous.mixClock; mixFast=previous.mixFast; reentry=previous.reentry;
            mixLaneWidth=previous.mixLaneWidth; mixLaneHeight=previous.mixLaneHeight;
        }
    }

    // each track keeps its own seeded bubbles, every rise new, started again only when the track's size changes
    internal BubbleLanes Bubbles(double track, double height)
    {
        if (bubbles is null || bubbles.Width != track || bubbles.Height != height) bubbles = new BubbleLanes(bubbleSeed, track, height);
        return bubbles;
    }

    private (LiquidSpec Spec, int Pixels, ThemeVariant Theme)? gradientFor;
    private SkiaSharp.SKColor[] gradient = [];

    // the liquid's OKLab gradient, one texel per device pixel, recomputed only when the look, width or theme changes
    internal SkiaSharp.SKColor[] Gradient(LiquidSpec spec, double width, ThemeVariant theme)
    {
        var pixels = Math.Max(1, (int)Math.Ceiling(width * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)));
        if (gradientFor is not { } key || key.Pixels != pixels || key.Theme != theme || !key.Spec.Equals(spec) && !SameStops(key.Spec, spec))
        {
            gradientFor = (spec, pixels, theme);
            gradient = LiquidTrack.GradientFor(spec, pixels, theme);
        }
        return gradient;
    }

    private static bool SameStops(LiquidSpec a, LiquidSpec b) => a.Stops().SequenceEqual(b.Stops());

    /// <summary>
    /// ED-248 review 2: what the track draws apart from the time, resolved on main when it records the track: the liquid's
    /// size, spec, gradient, bubbles and strand colours, the Strike's channels, geometry and tint, and the kept layers.
    /// </summary>
    private sealed record Scene(Size Size, double Track, LiquidSpec Spec, SkiaSharp.SKColor[] Gradient, BubbleLanes? Bubbles, bool DarkBubbles,
        IReadOnlyList<Color> Strands, bool Fast, bool Still, StrikeChannels? Strikes, StrikeGeometry Geometry, StrikeTint Tint, int Seed,
        LiquidTrack.LiquidLayers Layers, SkiaSharp.SKColor[]? FromGradient, StrikeTint FromTint,
        double FadeStart, double FadeDuration, double IntentTime, StrikeInk Ink, StrikeInk? InkFrom, double InkStart);

    private static StrikeInk InkAt(Scene s, double t) => s.InkFrom is { } from ? StrikeInk.Mix(from, s.Ink, Strike.Ss(0, s.Still ? 0.2 : 0.6, t - s.InkStart)) : s.Ink;

    internal void ReadMixSignal(KnobSignal signal, double knobTime, double gain, DateTimeOffset? drawingDate = null)
    {
        if (scene is not { } s) { signal.Read(null, 0, 0, knobTime, gain); return; }
        lock (s.Layers)
        {
            // A CPU renderer resolves during its frame callback, before another callback may update the tube's timeline.
            // Both use that frame's date; the retained GPU renderer reads the timeline after its callbacks have completed.
            if (drawingDate is { } date && !s.Still) Volatile.Write(ref time,TimeAt(date));
            if (((LiquidTrack.ILiveSource)this).TryFrame(out var f)) signal.ReadMix(s.Geometry, mixStorm!.Plans, StrikeFrameTime, knobTime, gain * (f.Mix?.Opacity ?? 0));
        }
    }

    Rect LiquidTrack.ILiveSource.Bounds => new(Volatile.Read(ref scene)?.Size ?? default);

    // where the live operation is drawn (the render thread; main for a snapshot): the scene at the track's current time,
    // resolved under the layers' lock (the pace and the strike channels change as frames are drawn)
    bool LiquidTrack.ILiveSource.TryFrame(out LiquidTrack.Frame frame)
    {
        if (Volatile.Read(ref scene) is not { } s) { frame = default; return false; }
        var t = Volatile.Read(ref time)+mixPageOrigin;
        lock (s.Layers)
        {
            if (ReferenceEquals(resolvedScene, s) && resolvedPageTime == t && Volatile.Read(ref startRequest) == 0) { frame = cachedFrame; return true; }
            mixStorm ??= new StrikeStorm(s.Seed); reentry ??= new StrikeReentry(s.Seed, StrikeDraws);
            if (mixClock is null) { mixClock = new StrikeFastClock(s.Fast); mixFast = s.Fast; }
            else if (mixFast != s.Fast)
            {
                if (s.Fast) reentry.FastOn(s.IntentTime, mixClock.FastAt(s.IntentTime));
                mixClock.SetFast(s.Fast, s.IntentTime, s.Still); mixFast = s.Fast;
            }
            if (Interlocked.Exchange(ref startRequest, 0) != 0) reentry.Request();
            if (!s.Still) reentry.BeforeFrame(t);
            if (mixLanes is null || mixLaneWidth != s.Track || mixLaneHeight != s.Size.Height)
            {
                var normal=Math.Clamp((int)Strike.Round(s.Track/32),4,24); var nucleation=(int)Strike.Round(normal/3d);
                mixLanes = new StrikeBubbleLanes(bubbleSeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + $",{normal},{nucleation},1", s.Track, s.Size.Height);
                mixContacts = new StrikeContacts(s.Seed, mixLanes, mixClock); mixLaneWidth = s.Track; mixLaneHeight = s.Size.Height;
            }
            var flowTime = s.Still ? 0 : mixClock.ClockC(t);
            var flow = new LiquidFlow(flowTime / 7, flowTime / 11, flowTime / 6);
            var mix = s.FromGradient is null ? 1 : Math.Clamp(((s.Still ? s.IntentTime : t) - s.FadeStart) / s.FadeDuration, 0, 1);
            mix = mix * mix * (3 - 2 * mix);
            var ink = InkAt(s, s.Still ? s.IntentTime : t);
            StrikeFrameTime = s.Still ? StrikeMix.Timing(1, s.Seed).At + 0.035 : t + reentry.Offset;
            var resolved = mixStorm.Resolve(s.Geometry, StrikeFrameTime, s.Still ? 0 : t, s.Still ? s.Fast ? 1 : 0 : mixClock.FastAt(t), ink.Amount);
            if (!s.Still) { mixContacts!.Process(s.Geometry, mixStorm.Plans, resolved, t, reentry.Offset, ink.Amount); mixContacts.ResolveLight(s.Geometry, resolved, t); }
            frame = new LiquidTrack.Frame(s.Size, s.Track, s.Still ? 0 : t, s.Gradient, s.Spec, null, false, s.Strands, flow, null, ink.Amount, Layers: s.Layers, PreviousGradient: mix < 1 ? s.FromGradient : null, GradientMix: mix,
                Mix: resolved, MixGeometry: s.Geometry, MixInk: ink, MixLanes: mixLanes, FastClock: mixClock, MixContacts: mixContacts, MixStill: s.Still, MixSeed: s.Seed);
            frame = ApplyPalette(frame, t); // ED-NEW-models-palette-14: precomputed tables on this retained effect/clock.
            resolvedScene = s; resolvedPageTime = t; cachedFrame = frame;
            return true;
        }
    }
    private double mixLaneWidth, mixLaneHeight;

    /// <summary>How often main has recorded the track (tests: once per change of what it draws, never per frame).</summary>
    internal int Recordings { get; private set; }

    // main, recording the track: the scene for what it draws now, kept while nothing in it changes
    private void Record(DrawingContext context, Size size)
    {
        Recordings++;
        PrepareScene(size);
        if (scene is not null) context.Custom(live);
    }

    // UI intent snapshot: the knob can resolve before the track's first raster recording.
    internal void PrepareScene(Size size)
    {
        if(size.Width<=8) { scene=null; return; }
        var look = this.look;
        if (!IsLiquid(look)) { scene = null; return; }
        // DEVIATION OD-0048 / OD-0051: the liquid tube replaces the source's fusion (:1054-1056) and particles; its
        // sweeping sheen is gone (no artificial shine). DEVIATION OD-0073 (ED-248): fast mode, below the top or at it,
        // is Strike over the liquid, in place of the source's streaks and bolts (:1057-1064), its currents 1.5x.
        var spec = SpecFor(look);
        var track = Math.Max(TrackWidth, size.Width);
        var darkBubbles = look.Pair is null && look.IsLightFill; // the canvas's white liquid has dark-rimmed bubbles
        var fast = IsFast(look);
        var still = fast && EnvironmentValues.GetAccessibilityReduceMotion(this);
        var theme = canvas.ActualThemeVariant;
        if (fast) strikes ??= new StrikeChannels(StrikeSeed); // ED-248 review: resolved on the render thread, where its reused buffers stay
        layers ??= new LiquidTrack.LiquidLayers();
        var row = Gradient(spec, size.Width, theme);
        SkiaSharp.SKColor[]? fromRow = null;
        var fromTint = default(StrikeTint);
        double colourStart = 0, colourDuration = 1;
        if (fade is { } f)
        {
            if (fadeGradientFor is not { } key || key.Pixels != row.Length || key.Theme != theme || !key.Spec.Equals(f.Spec))
            {
                fadeGradientFor = (f.Spec, row.Length, theme);
                fadeGradient = LiquidTrack.GradientFor(f.Spec, row.Length, theme);
            }
            fromRow = fadeGradient; fromTint = f.Tint;
            colourStart = (f.Start - startedAt).TotalSeconds+mixPageOrigin;
            colourDuration = EnvironmentValues.GetAccessibilityReduceMotion(this) ? 0.2 : f.Duration;
        }
        var next = new Scene(size, track, spec, row, still ? null : Bubbles(track, size.Height), darkBubbles,
            layers.StrandColors(spec, theme), fast, still, fast ? strikes : null, StrikeGeometryFor(size), StrikeTint.For(look.Brand), StrikeSeed, layers, fromRow, fromTint, colourStart, colourDuration, TimeAt(SwiftDate.Now)+mixPageOrigin, StrikeInk.Of(JewelLiquid.Of(look.Brand)), inkFrom, inkStart);
        if (scene is null || !scene.Equals(next)) Volatile.Write(ref scene, next); // (records compare their lists by reference)
    }

    private sealed class EffectCanvas(TrackEffect owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var size = Bounds.Size;
            if (size.Width <= 8) { owner.scene = null; return; } // :1053
            owner.Record(context, size);

        }
    }
}
