using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
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
// Capture-only branches are excluded by ED-001. The exact remaining shared dependency sites are in the tests' STOP notes.

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
    /// <param name="thread">:38 <c>let thread: ChatThread</c>.</param>
    /// <param name="hasHistory">:39 <c>let hasHistory: Bool</c>.</param>
    /// <param name="compact">:42 <c>var compact: Bool = false</c>.</param>
    // :59-138 body
    public ModelEffortButton(ChatThread thread, bool hasHistory, bool compact = false)
    {
        var frame = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
        var presented = false;
        double? naturalWidth = null;
        double? lockedWidth = null;
        var popup = new Popup { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
        var button = PlainControls.Button(frame, () =>
        {
            presented = !presented;
            lockedWidth = presented ? naturalWidth : null; frame.Width = compact ? double.NaN : lockedWidth ?? double.NaN; // :108-117
            if (presented)
            {
                var content = new ModelEffortPanel(thread.Id, hasHistory).PresentedChrome();
                AppEnvironment.SetAppModel(content, AppEnvironment.GetAppModel(this));
                Popovers.SetClosePopover(content, () => Presentation.Presenter.Current.SetPresented(popup, false));
                popup.Child = content;
            }
            Presentation.Presenter.Current.SetPresented(popup, presented);
        });
        button.Focusable = false; popup.PlacementTarget = button; // :115,120
        // :114 .chip(active:) is unwritten: ChipButtonStyleHelper is empty (STOP #3).
        frame.SizeChanged += (_, _) => { if (lockedWidth is null) naturalWidth = frame.Bounds.Width; }; // :109-112
        popup.Closed += (_, _) => { presented = false; lockedWidth = null; frame.Width = double.NaN; popup.Child = null; };
        DetachedFromVisualTree += (_, _) => { if (presented) Presentation.Presenter.Current.SetPresented(popup, false); };
        Child = button;
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            var currentThread = model.Thread(thread.Id) ?? thread;
            var option = model.Providers.Model(currentThread.Model, currentThread.Provider);
            var pair = model.HydraIsOn(currentThread) ? model.HydraPair(currentThread) : null;
            return (model, currentThread, option, pair);
        }, snapshot =>
        {
            var (model, currentThread, current, pair) = snapshot;
            frame.Children.Clear();
            Control mark = pair is null
                ? new ProviderIcon { Provider = currentThread.Provider, Size = 14, Width = 14, Height = 14 } // :73-75 STOP #3: renderer
                : new HydraFusedPairIcon(pair.Provider, model.HydraHeadsProvider(pair), 15); // :70-72
            mark.Margin = new Thickness(0, 0, 5, 0); frame.Children.Add(mark);
            if (currentThread.FastMode && current?.SupportsFast == true)
            {
                var bolt = SwiftImage.SystemName("bolt.fill").Font(Chrome.InlineIconFont).ForegroundStyle(SwiftColor.Yellow); bolt.Margin = new Thickness(0, 0, 5, 0);
                Grid.SetColumn(bolt, 1); frame.Children.Add(bolt); // :77-80
            }
            if (!compact)
            {
                var name = EffortView.Text(pair?.CustomName ?? current?.ChipName ?? currentThread.Model ?? currentThread.Provider.DisplayName, SwiftFont.Callout, Chrome.PrimaryText);
                name.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(name, 2); frame.Children.Add(name); // :90-94
                if (current is { Efforts.Count: > 0 })
                {
                    var effort = EffortView.Text(ModelOption.EffortTitle(currentThread.Effort ?? current.DefaultEffort ?? ""), SwiftFont.Callout, Chrome.PrimaryText.Opacity(0.72));
                    effort.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(effort, 3); frame.Children.Add(effort); // :95-99
                }
                var chevron = SwiftImage.SystemName("chevron.down").Font(Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText);
                chevron.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(chevron, 4); frame.Children.Add(chevron); // :101-103
            }
            var parts = new System.Collections.Generic.List<string> { current?.ShortName ?? currentThread.Model ?? currentThread.Provider.DisplayName };
            if (current is { Efforts.Count: > 0 }) parts.Add(ModelOption.EffortTitle(currentThread.Effort ?? current.DefaultEffort ?? ""));
            if (pair is not null) parts.Add(HydraPairSummary.Title(pair, model.Providers));
            var help = string.Join(" · ", parts); ToolTip.SetTip(button, help); AutomationProperties.SetName(button, help); // :142-149
        });
        this.Task(() => thread.Provider, async provider => await AppEnvironment.GetAppModel(this).Providers.LoadCatalog(provider)); // :138
        // :125-137 capture-only anchor/frame sites remain outside the shipped path (ED-001).
    }
}

/// <summary>:153-259 <c>private struct ModelEffortPanel: View</c>.</summary>
public sealed class ModelEffortPanel : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool showsModels;
        public bool ShowsModels { get => Read(showsModels); set => Set(ref showsModels, value); }
    }
    private readonly ViewState state = new();
    private AppModel model = null!;
    private readonly Panel pages = new();
    private bool? displayedModels;
    private (ProviderKind Provider, string? Model, HydraPair? Pair, ModelOption? Option)? sliderKey;
    /// <param name="threadID">:156 <c>let threadID: UUID</c>.</param>
    /// <param name="hasHistory">:157 <c>let hasHistory: Bool</c>.</param>
    // :161-189 body
    public ModelEffortPanel(Guid threadID, bool hasHistory)
    {
        Width = 330; Child = pages; // :186
        this.Track(() => (AppEnvironment.GetAppModel(this), AppEnvironment.GetAppModel(this).Thread(threadID), state.ShowsModels), snapshot =>
        {
            var (app, thread, showsModels) = snapshot; model = app;
            if (thread is null) { pages.Children.Clear(); displayedModels = null; return; } // :162
            var key = (thread.Provider, thread.Model, model.HydraIsOn(thread) ? model.HydraPair(thread) : null, model.Providers.Model(thread.Model, thread.Provider));
            // :163-185 the branch retains its view/state while only the bound effort or fast mode changes.
            if (!showsModels && displayedModels == false && Equals(sliderKey, key)) return;
            sliderKey = key;
            Control content;
            if (showsModels)
                content = new ModelList(thread, hasHistory, entry => { Choose(entry, thread); state.ShowsModels = false; },
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
        return new EffortSliderCard(option?.ShortName ?? thread.Model ?? thread.Provider.DisplayName, thread.Provider,
            option?.Efforts ?? ValueList<string>.Empty, option?.DefaultEffort, option?.SupportsFast ?? false,
            new Binding<string?>(() => model.Thread(thread.Id)?.Effort, effort => { model.UpdateThread(thread.Id, current => current with { Effort = effort }); Remember(thread.Id); }),
            new Binding<bool>(() => model.Thread(thread.Id)?.FastMode ?? false, fast => { model.UpdateThread(thread.Id, current => current with { FastMode = fast }); Remember(thread.Id); }),
            () =>
            {
                var leadEffort = pair?.OrchestratorEffort is { } effort && option?.Efforts.Contains(effort) == true ? effort : null;
                model.UpdateThread(thread.Id, current => current with { Effort = leadEffort, FastMode = false }); Remember(thread.Id);
            }, pairLook, () => state.ShowsModels = true);
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
            var leadsWithPair = pairs.Any(pair => model.LeadsWithHydraPair(pair, thread)); // :280
            var showsProviders = entries.Select(entry => entry.Provider).Distinct().Count() > 1;
            var rowHeight = showsProviders || hasHistory && entries.Any(entry => entry.Provider != thread.Provider) ? 46 : 34;
            var pairsHeight = pairs.Count == 0 ? 0 : SectionHeaderHeight * 2 + pairs.Count * (PairRowHeight + 1);
            var rows = new StackPanel { Spacing = 1 };
            if (pairs.Count > 0)
            {
                rows.Children.Add(new PopoverSectionHeader("Pairs")); // :311
                foreach (var pair in pairs)
                {
                    var locked = hasHistory && pair.Provider != thread.Provider;
                    var detail = string.Join(" · ", new[] { pair.CustomName is null ? null : HydraPairSummary.ModelsTitle(pair, model.Providers), HydraPairSummary.Providers(pair), HydraPairSummary.Efforts(pair) }.Where(value => value is not null));
                    rows.Children.Add(new HydraPairListRow(pair, HydraPairSummary.Title(pair, model.Providers), locked ? "New chats only" : detail, PairRowHeight,
                        model.LeadsWithHydraPair(pair, thread), !locked, () => onEnterPair?.Invoke(pair))); // :312-323
                }
                rows.Children.Add(new PopoverSectionHeader("Models")); // :325
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
            var back = PlainControls.Button(new Border { Width = 20, Height = 20, Child = backIcon }, onBack); ToolTip.SetTip(back, "Back"); // :290-299
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
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,10,*,12,Auto") };
        row.Children.Add(new HydraFusedPairIcon(pair.Provider, pair.HeadsProvider, 16, false) { Width = 18 }); // :387-388
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            EffortView.Text(title, SwiftFont.System(14), Chrome.PrimaryText),
            EffortView.Text(detail, SwiftFont.Callout, Chrome.SecondaryText),
        } }; Grid.SetColumn(texts, 2); row.Children.Add(texts); // :389-399
        var check = SwiftImage.SystemName("checkmark").Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        check.Opacity = isSelected ? 1 : 0; Grid.SetColumn(check, 4); row.Children.Add(check); // :401-405
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
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(showsIcon ? "18,10,*,12,Auto,10,Auto" : "*,12,Auto,10,Auto") };
        var column = 0;
        if (showsIcon)
        {
            // :436-439 ProviderIcon's artwork is still missing in UI.Common.Components (STOP #3).
            row.Children.Add(new ProviderIcon { Provider = entry.Provider, Size = 14, Width = 18, Height = 14 });
            column = 2;
        }
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(EffortView.Text(entry.Option.ShortName, SwiftFont.System(14), Chrome.PrimaryText)); // :441-444
        if (detail is not null) texts.Children.Add(EffortView.Text(detail, SwiftFont.Callout, Chrome.SecondaryText)); // :445-449
        Grid.SetColumn(texts, column);
        row.Children.Add(texts);
        if (entry.Option.SupportsFast)
        {
            var bolt = SwiftImage.SystemName("bolt").Font(SwiftFont.System(SwiftFont.TextStyle.Footnote)).ForegroundStyle(Chrome.SecondaryText);
            Grid.SetColumn(bolt, column + 2); row.Children.Add(bolt); // :452-455
        }
        var check = SwiftImage.SystemName("checkmark").Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        check.Opacity = isSelected ? 1 : 0; // :456-460
        Grid.SetColumn(check, column + 4); row.Children.Add(check);
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
        Action? onTitleTap = null)
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
        var title = EffortView.Text("", SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium), Chrome.PrimaryText);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { title } };
        if (onTitleTap is not null) titleRow.Children.Add(SwiftImage.SystemName("chevron.right").Font(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText)); // :575-578
        var titlePlate = new Border { Padding = new Thickness(8, 2), Child = titleRow }.RoundedRectangle(7, RoundedCornerStyle.Continuous);
        var titleFill = new AnimatedColor(titlePlate); titlePlate.Background = titleFill.Brush;
        var titleButton = PlainControls.Button(titlePlate, () => onTitleTap?.Invoke()); // :568-597
        titleButton.IsEnabled = onTitleTap is not null;
        titleButton.OnHover(hovering => titleFill.Set(hovering && onTitleTap is not null ? Chrome.Overlay(0.08) : SwiftColor.Clear, Chrome.Hover));
        ToolTip.SetTip(titleButton, onTitleTap is null ? "" : "Choose a model");
        centre.Children.Add(titleButton);
        var subtitle = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), HorizontalAlignment = HorizontalAlignment.Center };
        if (pair is not null)
        {
            var mark = new HydraMarkImage { Width = 12, Height = 12 };
            if (mark.Child is SwiftImage image) image.ForegroundStyle(Chrome.PrimaryText.Opacity(0.75));
            subtitle.Children.Add(mark); // :602-605
        }
        else if (provider is { } kind) subtitle.Children.Add(new ProviderIcon { Provider = kind, Size = 12, Width = 12, Height = 12 }); // :606-608 STOP #3: artwork
        var name = EffortView.Text(pair?.Title ?? modelName, SwiftFont.Body, Chrome.PrimaryText.Opacity(0.7)); // :609-614
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(subtitle.Children.Count == 0 ? 0 : 5, 0, 0, 0); // :599 HStack spacing, leaving a finite text column.
        var scaledName = TextScaling.Apply(name, 0.85); // :614 minimumScaleFactor, ED-214.
        Grid.SetColumn(scaledName, 1); subtitle.Children.Add(scaledName);
        centre.Children.Add(subtitle); header.Children.Add(centre); rows.Children.Add(header);
        var sliderSlot = new Panel(); rows.Children.Add(sliderSlot);
        if (efforts.Count == 0)
        {
            var note = EffortView.Text("This model has one reasoning level.", SwiftFont.Callout, Chrome.SecondaryText);
            note.Height = EffortSlider.ThumbSize; note.HorizontalAlignment = HorizontalAlignment.Center; sliderSlot.Children.Add(note); // :620-624
        }
        if (pair is not null && efforts.Count > 0)
        {
            var caption = EffortView.Text(pair.Caption, SwiftFont.System(SwiftFont.TextStyle.Subheadline), Chrome.SecondaryText);
            caption.Margin = new Thickness(0, -6, 0, 0); caption.TextTrimming = TextTrimming.CharacterEllipsis;
            var scaledCaption = TextScaling.Apply(caption, 0.9); // :648 (ED-214).
            rows.Children.Add(scaledCaption);
            // :650, UI/Chrome/Chrome.swift:940-944,960: animate the modifier's layer properties
            // with its shared timing; the Bool modifier itself carries no interpolated animatable data.
            EffortView.Animate(scaledCaption, Animation.SoftAppear, progress =>
            {
                scaledCaption.Opacity = Math.Clamp(progress, 0, 1);
                scaledCaption.RenderTransform = new TranslateTransform(0, 4 * (1 - progress));
            });
        }
        Child = new Border { Padding = new Thickness(16, 14, 16, 16), Child = rows }; // :653-655
        string? previousTitle = null;
        this.Track(() => (ResolvedIndex(), supportsFast && fastMode.Value, ActualThemeVariant), value =>
        {
            var (index, fast, theme) = value;
            var maximum = efforts.Count > 1 && index == efforts.Count - 1; // :527-529
            var nextTitle = efforts.Count == 0 ? "Standard" : ModelOption.EffortTitle(efforts[index]);
            title.Text = nextTitle;
            if (previousTitle is not null && previousTitle != nextTitle)
                EffortView.Animate(titleRow, Transaction.Current ?? Animation.Smooth(0.3), progress => title.Opacity = Math.Clamp(progress, 0, 1)); // :574 numericText -> whole-label cross-fade (packet windows_path)
            // :543-555 AnyShapeStyle is represented by Avalonia's brush; every literal colour comes from Swift.
            var titleBrush = pair is not null
                ? EffortView.Gradient(new[] { brand.TitleColor }.Concat(pair.Brand.TitleColors).Concat(fast ? new[] { EffortPalette.Fast } : Array.Empty<SwiftColor>()), theme)
                : maximum && fast ? EffortView.Gradient([brand.TitleColor, EffortPalette.Fast], theme)
                : maximum ? EffortView.BrandTitle(brand, theme)
                : (fast ? EffortPalette.Fast : EffortPalette.Title).Brush(title);
            if (previousTitle is null) title.Foreground = titleBrush;
            else EffortView.AnimateBrush(title, () => title.Foreground, brush => title.Foreground = brush, titleBrush, Animation.Smooth(0.3)); // :582-583
            previousTitle = nextTitle;
            if (efforts.Count == 0) return;
            // :626-637 keep the gesture's own binding alive; changes to look/title update it without replacing the dragging view.
            if (sliderSlot.Children.FirstOrDefault() is EffortSlider existing)
                existing.UpdateLook(fast, title.Text!, pair?.Brand);
            else sliderSlot.Children.Add(new EffortSlider(efforts.Count,
                new Binding<int>(ResolvedIndex, chosen => Transaction.WithAnimation(Animation.Snappy(0.2), () => effort.Value = efforts[chosen])),
                title.Text!, fast, brand, pair?.Brand));
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

        void Apply(bool on, bool hovering)
        {
            iconHost.Children.Clear();
            iconHost.Children.Add(SwiftImage.SystemName(on ? "bolt.fill" : "bolt") // :667
                .Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)) // :668
                .ForegroundStyle(on ? SwiftColor.Yellow : Chrome.SecondaryText) // :669
                .SymbolEffect(SymbolEffectKind.Bounce, on)); // :670 MARROW_DEFERRED ED-182 (EffortSlider.swift:670)
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
public sealed class EffortSlider : Decorator
{
    // :712 static let trackHeight: CGFloat = 34
    public const double TrackHeight = 34;

    // :713 static let thumbSize: CGFloat = 42
    public const double ThumbSize = 42;

    /// <param name="count">:715 <c>let count: Int</c>.</param>
    /// <param name="index">:716 <c>@Binding var index: Int</c>.</param>
    /// <param name="accessibilityTitle">:725 <c>let accessibilityTitle: String</c>; moves before the defaulted ones
    /// (ED-095).</param>
    /// <param name="fastMode">:719 <c>var fastMode = false</c>.</param>
    /// <param name="brand">:721 <c>var brand: EffortBrand = .purple</c>.</param>
    /// <param name="pair">:724 <c>var pair: EffortPairBrand? = nil</c>.</param>
    // :740-831 body
    private readonly Binding<int> index;
    private readonly int count;
    private readonly EffortBrand brand;
    private bool fastMode;
    private EffortPairBrand? pair;
    private double? dragX;
    private double? animatedX;
    private double drawnX;
    private double knobScale = 1;
    private readonly Canvas canvas = new();
    private readonly Border track = new() { Height = TrackHeight };
    private readonly Border fill = new() { Name = "EffortFill", Height = TrackHeight };
    private readonly Panel effects = new();
    private readonly Panel knob = new() { Width = ThumbSize, Height = ThumbSize };
    private readonly Ellipse glow = new();
    private readonly Ellipse lens = new();
    private TrackLook? drawnLook;
    private readonly System.Collections.Generic.List<Ellipse> stops = [];

    public EffortSlider(int count, Binding<int> index, string accessibilityTitle, bool fastMode = false, EffortBrand brand = EffortBrand.Purple, EffortPairBrand? pair = null)
    {
        this.count = count; this.index = index; this.brand = brand; this.fastMode = fastMode; this.pair = pair;
        Height = ThumbSize;
        track.Capsule(RoundedCornerStyle.Continuous); fill.Capsule(RoundedCornerStyle.Continuous);
        fill.Child = effects; fill.ClipToBounds = true;
        canvas.Children.Add(track); canvas.Children.Add(fill);
        for (var stop = 0; stop < count; stop++)
        {
            var dot = new Ellipse { Width = 5, Height = 5 }; stops.Add(dot); canvas.Children.Add(dot); // :767-774
        }
        knob.Children.Add(glow); knob.Children.Add(lens); canvas.Children.Add(knob); // :780-792
        Child = canvas;
        AutomationProperties.SetName(this, pair is null ? "Reasoning effort" : "The lead's reasoning effort");
        AutomationProperties.SetItemStatus(this, accessibilityTitle);
        foreach (var child in canvas.Children) AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(this); Drag(e.GetPosition(this).X); e.Handled = true;
        };
        PointerMoved += (_, e) => { if (dragX is not null) Drag(e.GetPosition(this).X); };
        PointerReleased += (_, e) =>
        {
            if (dragX is null) return;
            var fromX = drawnX; var fromScale = knobScale;
            dragX = null;
            var step = count > 1 ? (Bounds.Width - ThumbSize) / (count - 1) : 0;
            var toX = ThumbSize / 2 + index.Value * step;
            animatedX = fromX;
            EffortView.Animate(knob, Animation.Spring(response: 0.32, dampingFraction: 0.78), progress =>
            {
                animatedX = progress == 1 ? null : fromX + (toX - fromX) * progress;
                knobScale = fromScale + (1 - fromScale) * progress;
                LayoutTrack();
            }); // :817
            e.Pointer.Capture(null); e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (dragX is not null) { dragX = null; LayoutTrack(); } };
        SizeChanged += (_, _) => LayoutTrack();
        ActualThemeVariantChanged += (_, _) => LayoutTrack();
        this.Track(() => index.Value, _ => LayoutTrack());
    }

    // :729-738, :822-830. A range peer exposes the increment/decrement step to Windows accessibility clients.
    internal void UpdateLook(bool fast, string title, EffortPairBrand? pairBrand)
    {
        fastMode = fast; pair = pairBrand;
        AutomationProperties.SetItemStatus(this, title); LayoutTrack();
    }

    private TrackLook Look => new(count > 1 && index.Value == count - 1
        ? fastMode ? TrackLook.Kind.Fusion : TrackLook.Kind.Supercharged
        : fastMode ? TrackLook.Kind.Fast : TrackLook.Kind.Plain, brand, pair);

    // :802-818, :833-836
    private void Drag(double x)
    {
        if (dragX is null)
        {
            dragX = x; animatedX = null;
            var fromScale = knobScale;
            EffortView.Animate(knob, Animation.Spring(response: 0.22, dampingFraction: 0.8), progress => { knobScale = fromScale + (1.06 - fromScale) * progress; LayoutTrack(); });
        }
        else dragX = x;
        var step = count > 1 ? (Bounds.Width - ThumbSize) / (count - 1) : 0;
        var nearest = count > 1 && step > 0 ? Math.Clamp((int)Math.Round((x - ThumbSize / 2) / step, MidpointRounding.AwayFromZero), 0, count - 1) : 0;
        if (nearest != index.Value) { index.Value = nearest; Haptics.Perform(HapticPattern.Alignment); } // PR-COMPOSER-125 windows_path: silent Windows no-op, no substitute cue.
        LayoutTrack();
    }

    // :740-799
    private void LayoutTrack()
    {
        var width = Bounds.Width;
        if (width < ThumbSize) return;
        const double inset = ThumbSize / 2;
        var step = count > 1 ? (width - inset * 2) / (count - 1) : 0;
        var x = Math.Clamp(dragX ?? animatedX ?? inset + index.Value * step, inset, width - inset);
        drawnX = x;
        var look = Look;
        track.Width = width; track.Background = Chrome.Overlay(0.1).Brush(track);
        Canvas.SetTop(track, (ThumbSize - TrackHeight) / 2);
        fill.Width = x + inset;
        if (drawnLook is null) fill.Background = EffortView.Fill(look, ActualThemeVariant);
        else if (!Equals(drawnLook, look)) EffortView.AnimateBrush(fill, () => fill.Background, brush => fill.Background = brush, EffortView.Fill(look, ActualThemeVariant), Animation.Smooth(0.3));
        Canvas.SetTop(fill, (ThumbSize - TrackHeight) / 2);
        for (var stop = 0; stop < count; stop++)
        {
            var stopX = inset + stop * step;
            stops[stop].Fill = (stopX <= x ? look.StopColor : Chrome.Overlay(0.32)).Brush(stops[stop]);
            Canvas.SetLeft(stops[stop], stopX - 2.5); Canvas.SetTop(stops[stop], ThumbSize / 2 - 2.5);
        }
        glow.Fill = look.Glow.Brush(glow);
        glow.Effect = CometPainter.GaussianBlur(look.TrackLookKind == TrackLook.Kind.Plain ? 4 : 8);
        glow.RenderTransform = new TranslateTransform(0, 1);
        lens.Fill = Brushes.Transparent; // :785 fill(.clear); the material/tint is retained below per ED-180.
        lens.Stroke = SwiftColor.Black.Opacity(look.IsLightFill ? 0.14 : 0).Brush(lens); lens.StrokeThickness = 1;
        SwiftGlass.SetEffect(lens, GlassEffect.Regular(GlassShape.Circle).Tint(SwiftColor.White, look.IsLightFill ? 0.55 : 0.32).Interactive()); // MARROW_DEFERRED ED-180
        knob.RenderTransform = new ScaleTransform(knobScale, knobScale);
        Canvas.SetLeft(knob, x - inset);
        if (drawnLook?.EffectIdentity != look.EffectIdentity && !Equals(drawnLook?.EffectIdentity, look.EffectIdentity))
        {
            effects.Children.Clear();
            if (look.TrackLookKind != TrackLook.Kind.Plain || look.Pair is not null) effects.Children.Add(new TrackEffect(look));
        }
        else if (effects.Children.FirstOrDefault() is TrackEffect effect) effect.SetLook(look);
        drawnLook = look;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new EffortPeer(this);
    private sealed class EffortPeer(EffortSlider owner) : ControlAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public bool IsReadOnly => false;
        public double Minimum => 0;
        public double Maximum => Math.Max(0, owner.count - 1);
        public double SmallChange => 1;
        public double LargeChange => 1;
        public double Value => owner.index.Value;
        public void SetValue(double value) => owner.index.Value = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, Math.Max(0, owner.count - 1));
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
        if (look.Pair is { } pair) return Gradient(new[] { look.Brand.FillColor }.Concat(pair.Colors).Concat(look.TrackLookKind is TrackLook.Kind.Fast or TrackLook.Kind.Fusion ? new[] { EffortPalette.FastFill } : Array.Empty<SwiftColor>()), theme);
        return look.TrackLookKind switch
        {
            TrackLook.Kind.Plain => new SolidColorBrush(Chrome.Accent.Resolve(theme)),
            TrackLook.Kind.Supercharged => look.Brand == EffortBrand.Antigravity ? Gradient(EffortBrand.Gemini, theme) : new SolidColorBrush(look.Brand.FillColor.Resolve(theme)),
            TrackLook.Kind.Fast => new SolidColorBrush(EffortPalette.FastFill.Resolve(theme)),
            _ => Gradient([look.Brand.FillColor, EffortPalette.FastFill], theme),
        };
    }
}

/// <summary>:839-847 <c>enum EffortPalette</c>.</summary>
public static class EffortPalette
{
    // :841 static var title: Color { Chrome.accent.mix(with: .white, by: 0.22) }
    public static SwiftColor Title => SwiftColor.Dynamic(
        EffortView.Resolved(Chrome.Accent, ThemeVariant.Light).Mix(SwiftColor.White, 0.22),
        EffortView.Resolved(Chrome.Accent, ThemeVariant.Dark).Mix(SwiftColor.White, 0.22));

    public static SwiftColor Supercharged { get; } = new(red: 0.74, green: 0.55, blue: 1.0);      // :842
    public static SwiftColor SuperchargedFill { get; } = new(red: 0.55, green: 0.34, blue: 0.97); // :843
    public static SwiftColor Fast { get; } = new(red: 1.0, green: 0.84, blue: 0.36);              // :845
    public static SwiftColor FastFill { get; } = new(red: 0.93, green: 0.66, blue: 0.13);         // :846
}

/// <summary>:853-936 <c>enum EffortBrand: Hashable</c>, no raw values.</summary>
public enum EffortBrand
{
    Claude,      // :854
    Deepseek,    // :855
    Meta,        // :856
    Antigravity, // :857
    Silver,      // :858
    Purple,      // :860
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
            ProviderKind.Codex or ProviderKind.Cursor or ProviderKind.Grok or ProviderKind.Opencode
                or ProviderKind.OpencodeGo or ProviderKind.Devin or ProviderKind.Copilot
                or ProviderKind.Commandcode or ProviderKind.Pi or ProviderKind.Zai => EffortBrand.Silver, // :868
            null => EffortBrand.Purple, // :869
            _ => throw new UnreachableException(),
        };

        // :931-935 static let gemini
        public static ValueList<SwiftColor> Gemini => GeminiColors;
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
            _ => throw new UnreachableException(),
        };

        // :911-920 var fillColor: Color
        public SwiftColor FillColor => brand switch
        {
            EffortBrand.Claude => new SwiftColor(0.80, 0.42, 0.28), // :895
            EffortBrand.Deepseek => new SwiftColor(0.24, 0.36, 0.98), // :896
            EffortBrand.Meta => new SwiftColor(0.0, 0.45, 0.93), // :897-900
            EffortBrand.Antigravity => new SwiftColor(0.56, 0.45, 0.80), // :901
            EffortBrand.Silver => new SwiftColor(0.92, 0.92, 0.94), // :902-905
            EffortBrand.Purple => new SwiftColor(0.55, 0.34, 0.97), // :906, EffortPalette.SuperchargedFill
            _ => throw new UnreachableException(),
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

    // :948-961 init(_ pair: HydraPair). PR-COMPOSER-130: a pair's far-end colours are the heads' provider's brand
    // when they run elsewhere, else the first four roster persona colours.
    public EffortPairBrand(HydraPair pair)
    {
        if (pair.SendsHeadsElsewhere)
        {
            var brand = EffortBrand.Create(pair.HeadsProvider);
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

    // :964 var titleColors: [Color]
    public ValueList<SwiftColor> TitleColors => new(Colors.Select(color => color.Mix(SwiftColor.White, 0.18)));
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

    // :1012 var isLightFill: Bool. PR-COMPOSER-131: the light-fill test follows the pair when there is one.
    public bool IsLightFill => Pair?.IsLight ?? (TrackLookKind == Kind.Supercharged && Brand.IsLight);

    // :1015 var stopColor: Color. PR-COMPOSER-131: the stop colour follows the pair when there is one (via IsLightFill).
    public SwiftColor StopColor => IsLightFill ? SwiftColor.Black.Opacity(0.35) : SwiftColor.White.Opacity(0.6);

    // :1018 var spark: Color
    public SwiftColor Spark => Pair is null && TrackLookKind is Kind.Supercharged or Kind.Fusion ? Brand.Spark : SwiftColor.White;

    // :1024-1026 var effectIdentity: AnyHashable
    public object EffectIdentity => Pair is { } pair ? this with { TrackLookKind = Kind.Plain, Pair = pair } : this;
}

/// <summary>:1039-1347 <c>struct TrackEffect: View</c>.</summary>
public sealed class TrackEffect : Decorator
{
    private TrackLook look = null!;
    private readonly DateTimeOffset startedAt = SwiftDate.Now;
    private readonly TraitCache traits = new();
    private readonly EffectCanvas canvas;
    private double time;
    /// <param name="look">:1040 <c>let look: TrackLook</c>.</param>
    // :1045-1072 body
    public TrackEffect(TrackLook look)
    {
        this.look = look;
        canvas = new EffectCanvas(this);
        Child = canvas;
        IsHitTestVisible = false; AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :1070-1071
        AttachedToVisualTree += (_, _) => { if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window) FrontMonitor.Shared.Current.Observe(window); };
        this.TimelineView(() => new AnimationTimelineSchedule(
            1.0 / (this.look.TrackLookKind == TrackLook.Kind.Supercharged && this.look.Pair is null ? 30 : 60),
            EnvironmentValues.GetAccessibilityReduceMotion(this) || !FrontMonitor.Shared.Current.IsVisible), date =>
        {
            time = (date - startedAt).TotalSeconds; canvas.InvalidateVisual();
        }); // :1049-1068
    }

    // :1024-1026 pair kind changes preserve this view's clock and trait cache.
    internal void SetLook(TrackLook value) { look = value; canvas.InvalidateVisual(); }

    private sealed class EffectCanvas(TrackEffect owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var size = Bounds.Size;
            if (size.Width <= 8) return; // :1053
            var look = owner.look; var kind = look.TrackLookKind; var time = owner.time;
            if (look.Pair is { } pair) DrawFusion(context, size, time, look.Brand.FillColor, pair.Colors); // :1054-1056, ED-213.
            var traits = owner.traits.Traits(size.Width); // :1057
            if (kind is TrackLook.Kind.Supercharged or TrackLook.Kind.Fusion) DrawParticles(context, size, time, look.Spark, traits.Particles);
            if (kind is TrackLook.Kind.Fast or TrackLook.Kind.Fusion) { DrawStreaks(context, size, time, traits.Streaks); DrawBolts(context, size, time); }
            if (kind == TrackLook.Kind.Fusion) DrawSheen(context, size, time, look.Spark);
        }
    }

    /// <summary>:1074-1082 <c>struct Particle: Equatable</c>.</summary>
    public sealed record Particle
    {
        public double Height { get; init; }      // :1075
        public double Speed { get; init; }       // :1076
        public double Radius { get; init; }      // :1077
        public double Start { get; init; }       // :1079
        public double Brightness { get; init; }  // :1080
        public double ShimmerRate { get; init; } // :1081
    }

    /// <summary>:1084-1091 <c>struct Streak: Equatable</c>.</summary>
    public sealed record Streak
    {
        public double Length { get; init; }     // :1085
        public double Speed { get; init; }      // :1086
        public double Thickness { get; init; }  // :1087
        public double Start { get; init; }      // :1088
        public double Height { get; init; }     // :1089
        public double Brightness { get; init; } // :1090
    }

    /// <summary>:1093-1116 <c>struct Traits: Equatable</c>.</summary>
    public sealed record Traits(ValueList<Particle> Particles, ValueList<Streak> Streaks) // :1094-1095
    {
        // :1099-1115 static func build(width:) -> Traits. PR-COMPOSER-134: one particle per five points and one
        // streak per fourteen, with per-index deterministic values (TrackEffect.Random).
        public static Traits Build(double width)
        {
            var particles = Enumerable.Range(0, Math.Max(10, (int)(width / 5))).Select(index =>
            {
                var seed = (double)index;
                return new Particle
                {
                    Height = Random(seed, 1),
                    Speed = 12 + 28 * Random(seed, 2),
                    Radius = 0.7 + 1.1 * Random(seed, 3),
                    Start = Random(seed, 4),
                    Brightness = 0.22 + 0.5 * Random(seed, 5),
                    ShimmerRate = 2 + 3 * Random(seed, 6),
                };
            });

            var streaks = Enumerable.Range(0, Math.Max(5, (int)(width / 14))).Select(index =>
            {
                var seed = (double)index + 100;
                return new Streak
                {
                    Length = 10 + 20 * Random(seed, 1),
                    Speed = 110 + 190 * Random(seed, 2),
                    Thickness = 1.1 + 1.1 * Random(seed, 3),
                    Start = Random(seed, 4),
                    Height = 0.2 + 0.6 * Random(seed, 5),
                    Brightness = 0.35 + 0.55 * Random(seed, 6),
                };
            });

            return new Traits(new ValueList<Particle>(particles), new ValueList<Streak>(streaks));
        }
    }

    /// <summary>:1121-1132 <c>final class TraitCache</c>.</summary>
    public sealed class TraitCache
    {
        private double width = -1.0;
        private Traits built = new(ValueList<Particle>.Empty, ValueList<Streak>.Empty);

        // :1125-1131 func traits(width:) -> Traits. PR-COMPOSER-134: rebuilt only when the width changes.
        public Traits Traits(double width)
        {
            if (width != this.width)
            {
                this.width = width;
                built = TrackEffect.Traits.Build(width);
            }

            return built;
        }
    }

    // :1134-1148 private static func drawParticles(_:size:time:color:particles:)
    private static void DrawParticles(DrawingContext context, Size size, double time, SwiftColor color, IReadOnlyList<Particle> particles)
    {
        var span = size.Width + 8;
        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            var x = (particle.Start * span + time * particle.Speed) % span - 4;
            var y = size.Height * (0.16 + 0.68 * particle.Height);
            var shimmer = 0.6 + 0.4 * Math.Sin(time * particle.ShimmerRate + i);
            var opacity = particle.Brightness * shimmer * EdgeFade(x, size.Width);
            if (opacity <= 0.01) continue;
            context.DrawGeometry(new SolidColorBrush(color.Opacity(opacity).Resolve(ThemeVariant.Light)), null,
                new EllipseGeometry(new Rect(x - particle.Radius, y - particle.Radius, particle.Radius * 2, particle.Radius * 2)));
        }
    }

    // :1151-1171 private static func drawStreaks(_:size:time:streaks:)
    private static void DrawStreaks(DrawingContext context, Size size, double time, IReadOnlyList<Streak> streaks)
    {
        foreach (var streak in streaks)
        {
            var span = size.Width + streak.Length + 12;
            var x = (streak.Start * span + time * streak.Speed) % span - streak.Length - 6;
            var y = size.Height * streak.Height;
            var brightness = streak.Brightness * EdgeFade(x + streak.Length, size.Width);
            if (brightness <= 0.02) continue;
            var rect = new Rect(x, y - streak.Thickness / 2, streak.Length, streak.Thickness);
            var brush = EffortView.Gradient([SwiftColor.White.Opacity(0), SwiftColor.White.Opacity(brightness)], ThemeVariant.Light);
            context.DrawGeometry(brush, null, new RectangleGeometry(rect, streak.Thickness / 2, streak.Thickness / 2));
        }
    }

    // :1175-1201 private static func drawBolts(_:size:time:)
    private static void DrawBolts(DrawingContext context, Size size, double time)
    {
        for (var slot = 0; slot < 2; slot++)
        {
            var seed = slot + 200.0;
            var period = 1.4 + 0.9 * Random(seed, 1);
            var offset = Random(seed, 2) * period;
            var cycle = Math.Floor((time + offset) / period);
            var phase = time + offset - cycle * period;
            const double flash = 0.26;
            if (phase >= flash) continue;
            var intensity = Math.Sin(phase / flash * Math.PI);
            var cycleSeed = seed + cycle * 7.31;
            var x = 12 + (size.Width - 24) * Random(cycleSeed, 3);
            if (x <= 6 || x >= size.Width - 6) continue;
            var lean = 3 + 3 * Random(cycleSeed, 4);
            var path = EffortView.Path([new Point(x + lean, 3), new Point(x - lean * 0.4, size.Height * 0.42), new Point(x + lean * 0.5, size.Height * 0.5), new Point(x - lean, size.Height - 3)], false);
            using (context.PushEffect(CometPainter.GaussianBlur(3), new Rect(size).Inflate(12)))
                context.DrawGeometry(null, new Pen(new SolidColorBrush(EffortPalette.Fast.Opacity(0.9 * intensity).Resolve(ThemeVariant.Light)), 4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), path);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(SwiftColor.White.Opacity(intensity).Resolve(ThemeVariant.Light)), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), path);
        }
    }

    // :1204-1225 private static func drawSheen(_:size:time:color:)
    private static void DrawSheen(DrawingContext context, Size size, double time, SwiftColor color)
    {
        const double band = 46; const double period = 2.8;
        var x = -band + (size.Width + band * 2) * ((time / period) % 1);
        var path = EffortView.Path([new Point(x + 10, 0), new Point(x + band + 10, 0), new Point(x + band, size.Height), new Point(x, size.Height)], true);
        var brush = (LinearGradientBrush)EffortView.Gradient([color.Opacity(0), color.Opacity(0.22), color.Opacity(0)], ThemeVariant.Light);
        brush.StartPoint = new RelativePoint(x, 0, RelativeUnit.Absolute); brush.EndPoint = new RelativePoint(x + band + 10, 0, RelativeUnit.Absolute);
        context.DrawGeometry(brush, null, path);
    }

    // :1232-1335 private static func drawFusion(_:size:time:lead:heads:)
    private static void DrawFusion(DrawingContext context, Size size, double time, SwiftColor lead, IReadOnlyList<SwiftColor> heads) =>
        context.Custom(new FusionOperation(size, time, lead, heads.ToArray())); // ED-213: additive paths on the Skia lease.

    // ED-213: private to this sole plusLighter site. All frame values are captured before the render-thread lease.
    private sealed class FusionOperation(Size size, double time, SwiftColor lead, SwiftColor[] heads) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = new Rect(size).Inflate(28); // :1321 sigma 7, retaining the glow's four-sigma reach.
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
                ?? throw new InvalidOperationException("The fusion drawing needs the Skia renderer (ISkiaSharpApiLeaseFeature).");
            using var lease = feature.Lease();
            // The lease bypasses Avalonia's opacity paint, so apply the inherited opacity to the whole frame once.
            using var opacity = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(lease.CurrentOpacity, 0, 1) * 255)) };
            var save = lease.SkCanvas.SaveLayer(opacity);
            try { DrawFrame(lease.SkCanvas, size, time, lead, heads); }
            finally { lease.SkCanvas.RestoreToCount(save); }
        }

        private static SKColor Color(SwiftColor value)
        {
            var color = value.Resolve(ThemeVariant.Light); // The source's lead/head colours are fixed sRGB literals.
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        // :1278-1282,1320-1328. A GraphicsContext copy with a filter is an isolated save layer; the additive
        // blend applies on restore. Clip before the layer so its filtered result also stays inside the side path.
        private static void PaintLayer(SKCanvas canvas, SKPath path, SKPaint paint, double sigma, bool additive, SKPath? clip)
        {
            var save = canvas.Save();
            try
            {
                if (clip is not null) canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
                using var blur = sigma > 0 ? SKImageFilter.CreateBlur((float)sigma, (float)sigma, SKShaderTileMode.Decal) : null;
                using var layer = new SKPaint { BlendMode = additive ? SKBlendMode.Plus : SKBlendMode.SrcOver, ImageFilter = blur };
                canvas.SaveLayer(layer);
                canvas.DrawPath(path, paint);
            }
            finally { canvas.RestoreToCount(save); }
        }

        // :1232-1335. Gradients, plasma paths, deterministic sparks, glow, core, then the white sheen.
        private static void DrawFrame(SKCanvas canvas, Size size, double time, SwiftColor lead, IReadOnlyList<SwiftColor> heads)
        {
            var width = size.Width; var height = size.Height;
            var seam = width * (0.5 + 0.13 * Math.Sin(time * 0.7) + 0.05 * Math.Sin(time * 1.9 + 1));
            var pulse = 0.5 + 0.5 * Math.Sin(time * 2.4);
            var headsNear = heads.Count > 0 ? heads[0] : lead;

            var colors = new System.Collections.Generic.List<SKColor> { Color(lead), Color(lead), Color(SwiftColor.White.Opacity(0.92)) };
            var positions = new System.Collections.Generic.List<float> { 0, (float)Math.Max(0, seam / width - 0.22), (float)(seam / width) };
            var farStart = Math.Min(1, seam / width + 0.16);
            if (heads.Count == 1)
            {
                colors.Add(Color(headsNear)); positions.Add((float)farStart);
                colors.Add(Color(headsNear)); positions.Add(1);
            }
            else
            {
                for (var index = 0; index < heads.Count; index++)
                {
                    colors.Add(Color(heads[index]));
                    positions.Add((float)Math.Min(1, farStart + (1 - farStart) * index / (heads.Count - 1)));
                }
            }
            using (var gradient = SKShader.CreateLinearGradient(new SKPoint(0, (float)(height / 2)), new SKPoint((float)width, (float)(height / 2)), colors.ToArray(), positions.ToArray(), SKShaderTileMode.Clamp))
            using (var fill = new SKPaint { Shader = gradient, IsAntialias = true })
                canvas.DrawRect(new SKRect(0, 0, (float)width, (float)height), fill); // :1240-1256

            for (var band = 0; band < 3; band++) // :1261-1295
            {
                var seed = band + 300.0;
                var amplitude = height * (0.12 + 0.16 * Random(seed, 1));
                var wavelength = 30 + 34 * Random(seed, 2);
                var speed = 22 + 34 * Random(seed, 3);
                var thickness = 1.8 + 2.2 * Random(seed, 4);
                var phase = time * speed / wavelength * 2 * Math.PI;
                for (var stream = 0; stream < 2; stream++)
                {
                    var toNear = stream == 0;
                    using var path = new SKPath();
                    var steps = (int)(width / 3) + 1;
                    for (var step = 0; step <= steps; step++)
                    {
                        var x = Math.Min(width, step * 3.0);
                        var y = height / 2 + amplitude * Math.Sin(x / wavelength * 2 * Math.PI + (toNear ? phase : -phase) + seed * (toNear ? 1 : 1.7));
                        if (step == 0) path.MoveTo((float)x, (float)y); else path.LineTo((float)x, (float)y);
                    }
                    var reach = width * 0.55;
                    using var side = new SKPath();
                    side.AddRect(toNear ? new SKRect(0, 0, (float)seam, (float)height) : new SKRect((float)seam, 0, (float)width, (float)height));
                    var color = toNear ? headsNear : lead;
                    var brightness = 0.32 + 0.16 * Random(seed, 5);
                    var streamColors = toNear
                        ? new[] { Color(color.Opacity(0)), Color(color.Opacity(brightness)) }
                        : new[] { Color(color.Opacity(brightness)), Color(color.Opacity(0)) };
                    using var shader = SKShader.CreateLinearGradient(new SKPoint((float)(toNear ? seam - reach : seam), 0),
                        new SKPoint((float)(toNear ? seam : seam + reach), 0), streamColors, null, SKShaderTileMode.Clamp);
                    using var stroke = new SKPaint { Shader = shader, IsAntialias = true, Style = SKPaintStyle.Stroke,
                        StrokeWidth = (float)thickness, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
                    PaintLayer(canvas, path, stroke, 2.5, true, side);
                }
            }

            for (var index = 0; index < 22; index++) // :1297-1316
            {
                var seed = index + 400.0;
                var life = 0.7 + 0.8 * Random(seed, 1);
                var age = (time + Random(seed, 2) * life) % life;
                var progress = age / life;
                var toNear = Random(seed, 3) < 0.5;
                var speed = 28 + 74 * Random(seed, 4);
                var x = seam + (toNear ? -1 : 1) * speed * age;
                var y = height * (0.18 + 0.64 * Random(seed, 5)) + 5 * Math.Sin(age * 8 + seed);
                var radius = 0.7 + 1.5 * (1 - progress);
                var opacity = (1 - progress) * (0.5 + 0.5 * Random(seed, 6)) * EdgeFade(x, width);
                if (opacity <= 0.01) continue;
                var color = SwiftColor.White.Mix(toNear ? lead : headsNear, progress * 0.8);
                using var spark = new SKPaint { Color = Color(color.Opacity(opacity)), IsAntialias = true };
                canvas.DrawOval(new SKRect((float)(x - radius), (float)(y - radius), (float)(x + radius), (float)(y + radius)), spark);
            }

            var coreWidth = 9 + 8 * pulse; // :1318-1333
            using (var glowPath = new SKPath())
            using (var glow = new SKPaint { Color = Color(SwiftColor.White.Opacity(0.32 + 0.3 * pulse)), IsAntialias = true })
            {
                glowPath.AddOval(new SKRect((float)(seam - coreWidth), (float)(-height * 0.2), (float)(seam + coreWidth), (float)(height * 1.2)));
                PaintLayer(canvas, glowPath, glow, 7, true, null);
            }
            using (var corePath = new SKPath())
            using (var core = new SKPaint { Color = Color(SwiftColor.White.Opacity(0.5 + 0.4 * pulse)), IsAntialias = true })
            {
                corePath.AddRoundRect(new SKRect((float)(seam - 1.1), 2, (float)(seam + 1.1), (float)(height - 2)), 1.1f, 1.1f);
                PaintLayer(canvas, corePath, core, 0.8, false, null);
            }

            // :1335 drawSheen(.white), transcribed from :1204-1225 on this same Skia canvas.
            const double sheenBand = 46; const double period = 2.8;
            var sheenX = -sheenBand + (width + sheenBand * 2) * ((time / period) % 1);
            using var sheenPath = new SKPath();
            sheenPath.MoveTo((float)(sheenX + 10), 0); sheenPath.LineTo((float)(sheenX + sheenBand + 10), 0);
            sheenPath.LineTo((float)(sheenX + sheenBand), (float)height); sheenPath.LineTo((float)sheenX, (float)height); sheenPath.Close();
            using var sheenShader = SKShader.CreateLinearGradient(new SKPoint((float)sheenX, 0), new SKPoint((float)(sheenX + sheenBand + 10), 0),
                new[] { Color(SwiftColor.White.Opacity(0)), Color(SwiftColor.White.Opacity(0.22)), Color(SwiftColor.White.Opacity(0)) }, null, SKShaderTileMode.Clamp);
            using var sheen = new SKPaint { Shader = sheenShader, IsAntialias = true };
            canvas.DrawPath(sheenPath, sheen);
        }
    }

    // :1338-1340 private static func edgeFade(_:width:). PR-COMPOSER-135: an edge fade over the last 14 points.
    private static double EdgeFade(double x, double width) =>
        Math.Min(1, Math.Min(Math.Max(0, x / 14), Math.Max(0, (width - x) / 14)));

    // :1343-1346 nonisolated static func random(_:_:). Deterministic per-index pseudo-random value (PR-COMPOSER-134).
    public static double Random(double index, double trait)
    {
        var value = Math.Sin(index * 12.9898 + trait * 78.233) * 43758.5453;
        return value - Math.Floor(value);
    }
}
