// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The Kindle popover (design "Kindle (locked)", POP.e): 344 pt wide, 14 pt
// in, 13 pt between blocks. From the top: the head (Ignis, Auto), the Details drawer (collapsed by default; Savings, History,
// Each message), the Quick memory track with its countdown and ticks, the Chat size track with the tidy-up notch, the one
// summary sentence, the quiet usage line with Open usage, the mode control (Off / Keep warm / Warm + tidy up), Keep warm now
// and Tidy up now, the idle-limit pair (Cool idle chats after, Learn when to cool; ED-NEW-ignis-guards), and the model points
// row (Details, the points' age, (i), Update). Every word is IgnisWords'. The (i) buttons follow Settings › Ignis "Show (i)
// explanations". No keyboard shortcuts (owner); Escape is the popover host's.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port.Ignis.Settings;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Ignis.Views;

public sealed class KindlePopover : Decorator
{
    public const double PopoverWidth = 344, Inset = 14, BlockGap = 13;

    internal enum Section { Savings, History, EachMessage }

    private sealed class ViewState : MainActorModel
    {
        private bool details;
        private Section section = Section.Savings;
        private bool report;
        private string pointsLabel = IgnisWords.Update;
        private string? pointsAge;
        private string copyNote = "";
        private int prefs;
        public bool Details { get => Read(details); set => Set(ref details, value); }
        public Section Section { get => Read(section); set => Set(ref section, value); }
        public bool Report { get => Read(report); set => Set(ref report, value); }
        public string PointsLabel { get => Read(pointsLabel); set => Set(ref pointsLabel, value); }
        public string? PointsAge { get => Read(pointsAge); set => Set(ref pointsAge, value); }
        public string CopyNote { get => Read(copyNote); set => Set(ref copyNote, value); }
        /// <summary>Bumped when the Ignis preferences change (Show (i) explanations, Smart helpers), which are not observable.</summary>
        public int Prefs { get => Read(prefs); set => Set(ref prefs, value); }
    }

    private readonly KindleCapsule capsule;
    private readonly IIgnisController controller;
    private readonly Guid thread;
    private readonly ViewState view = new();
    private readonly RevealSlot drawer = new();
    private readonly AnimatedScalar drawerReveal;
    private readonly KindleTrack cacheTrack = new(KindleTrack.Kind.QuickMemory);
    private readonly KindleTrack sizeTrack = new(KindleTrack.Kind.ChatSize);
    private readonly List<Control> infos = [], lineInfos = [], paneInfos = [];
    private List<Control> infoSink;
    private bool primed;

    internal KindlePopover(KindleCapsule capsule)
    {
        this.capsule = capsule;
        controller = capsule.Controller;
        thread = capsule.ThreadID;
        Width = PopoverWidth;
        infoSink = infos;
        drawerReveal = new AnimatedScalar(drawer, 0, value => drawer.Reveal = value);
        var stack = new StackPanel { Spacing = BlockGap, Margin = new Thickness(Inset) };
        Child = stack;
        AutomationProperties.SetName(this, IgnisWords.PopoverAccessibleName);
        AttachedToVisualTree += (_, _) => controller.Settings.Changed += OnPrefs;
        DetachedFromVisualTree += (_, _) => controller.Settings.Changed -= OnPrefs;

        // ---- head: Ignis, then Auto
        var head = new DockPanel { MinHeight = 22 };
        var title = KindleUi.Text(IgnisWords.Title, 13, KindleTone.Primary, strong: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        var autoSwitch = new SettingsSwitch(new Binding<bool>(() => Status.Auto, value => controller.SetAuto(thread, value)));
        AutomationProperties.SetName(autoSwitch, IgnisWords.AutoAccessibleName);
        var autoLabel = KindleUi.Text(IgnisWords.Auto, 12);
        autoLabel.VerticalAlignment = VerticalAlignment.Center;
        var auto = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { autoSwitch, autoLabel } };
        DockPanel.SetDock(auto, Dock.Right);
        head.Children.Add(auto);
        head.Children.Add(title);
        stack.Children.Add(head);
        AutoControl = auto;

        // ---- the Details drawer (above the tracks, so the popover grows upward out of the composer)
        drawer.Child = BuildDetails();
        drawer.Margin = new Thickness(0, -BlockGap, 0, 0);
        stack.Children.Add(drawer);

        // ---- Quick memory
        var cacheLabel = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        SwiftFont.System(12).Apply(cacheLabel);
        var cacheLeft = KindleUi.Text("", 12, tabular: true);
        var ticks = new Grid();
        stack.Children.Add(new StackPanel
        {
            Children =
            {
                LabelRow(new StackPanel { Orientation = Orientation.Horizontal, Children = { cacheLabel, Info(IgnisWords.InfoQuickMemoryName, () => Status.Ttl is { } ttl && ttl < TimeSpan.FromMinutes(30) ? IgnisWords.InfoQuickMemoryShort : IgnisWords.InfoQuickMemory) } }, cacheLeft),
                cacheTrack, ticks,
            },
        });
        ticks.Margin = new Thickness(0, 5, 0, 0);

        // ---- Chat size
        var sizeLabel = KindleUi.Text(IgnisWords.ChatSize, 12, KindleTone.Primary, strong: true);
        var sizeFull = KindleUi.Text("", 12, tabular: true);
        var notchLabel = KindleUi.Text(IgnisWords.TidyUpPoint, 10.5);
        var notchHost = new Canvas { Height = 14, Children = { notchLabel } };
        stack.Children.Add(new StackPanel
        {
            Children =
            {
                LabelRow(new StackPanel { Orientation = Orientation.Horizontal, Children = { sizeLabel, Info(IgnisWords.InfoChatSizeName, () => IgnisWords.InfoChatSize) } }, sizeFull),
                sizeTrack, notchHost,
            },
        });
        notchHost.Margin = new Thickness(0, 2, 0, 0);

        // ---- the one sentence
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18.75 };
        SwiftFont.System(12.5).Apply(summary);
        stack.Children.Add(summary);

        // ---- the quiet usage line
        var quiet = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 17.25, Margin = new Thickness(0, -4, 0, 0) };
        SwiftFont.System(11.5).Apply(quiet);
        stack.Children.Add(quiet);

        // ---- the mode control
        var modes = new CzSegmentedControl(IgnisWords.ModeGroupAccessibleName,
            [(IgnisSettings.RawValue(IgnisMode.Off), IgnisWords.ModeName(IgnisMode.Off)),
             (IgnisSettings.RawValue(IgnisMode.KeepWarm), IgnisWords.ModeName(IgnisMode.KeepWarm)),
             (IgnisSettings.RawValue(IgnisMode.WarmAndTidy), IgnisWords.ModeName(IgnisMode.WarmAndTidy))],
            IgnisSettings.RawValue(Status.Mode), small: true) { HorizontalAlignment = HorizontalAlignment.Stretch };
        modes.SelectionChanged += id => { if (IgnisSettings.ModeFrom(id) is { } mode && mode != Status.Mode) controller.SetMode(thread, mode); };
        stack.Children.Add(modes);
        ModeControl = modes;

        // ---- Keep warm now, Tidy up now
        var warmFlame = new FlameMark { Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0) };
        var warmWords = KindleUi.Text(IgnisWords.KeepWarmNow, 12, KindleTone.Primary);
        SwiftFont.System(12, SwiftFont.Weight.Medium).Apply(warmWords);
        KeepWarmButton = KindleUi.Pill(new StackPanel { Orientation = Orientation.Horizontal, Children = { warmFlame, warmWords } }, () => controller.KeepWarmNow(thread));
        AutomationProperties.SetName(KeepWarmButton, IgnisWords.KeepWarmNow);
        var tidyWords = KindleUi.Text(IgnisWords.TidyUpNow, 12, KindleTone.Primary);
        SwiftFont.System(12, SwiftFont.Weight.Medium).Apply(tidyWords);
        TidyButton = KindleUi.Pill(tidyWords, () => controller.TidyUpNow(thread));
        KeepWarmButton.HorizontalAlignment = TidyButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        Grid.SetColumn(TidyButton, 2);
        foot.Children.Add(KeepWarmButton); foot.Children.Add(TidyButton);
        stack.Children.Add(foot);

        // ---- the idle limit (ED-NEW-ignis-guards): Settings › Ignis's two controls, compact, over the same settings
        stack.Children.Add(KindleUi.Divider());
        CoolAfterMenu = IgnisCoolingControls.Menu(controller.Settings, Preferences);
        CoolAfterMenu.Height = CompactRowHeight;
        LearnCoolingSwitch = IgnisCoolingControls.Switch(controller.Settings, Preferences);
        stack.Children.Add(new StackPanel
        {
            Spacing = 6,
            Children = { CompactRow(IgnisSettingsWords.RowCoolAfter, CoolAfterMenu), CompactRow(IgnisSettingsWords.RowLearnCooling, LearnCoolingSwitch) },
        });

        // ---- the model points row
        stack.Children.Add(KindleUi.Divider());
        var chevron = new ChevronUp { Width = 9, Height = 9, Margin = new Thickness(0, 0, 5, 0) };
        var detailsWords = KindleUi.Text(IgnisWords.Details, 11.5);
        SwiftFont.System(11.5, SwiftFont.Weight.Medium).Apply(detailsWords);
        DetailsButton = KindleUi.Pill(new StackPanel { Orientation = Orientation.Horizontal, Children = { chevron, detailsWords } }, () => view.Details = !view.Details, height: 22, padding: 7, ghost: true);
        AutomationProperties.SetName(DetailsButton, IgnisWords.Details);
        var pointsWords = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
        SwiftFont.System(11.5).Apply(pointsWords);
        var pointsInfo = Info(IgnisWords.InfoModelPointsName, () => IgnisWords.InfoModelPoints);
        UpdateButton = KindleUi.Pill(IgnisWords.Update, CheckPoints, height: 22, size: 11.5, padding: 10);
        var points = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(pointsWords, 1); Grid.SetColumn(pointsInfo, 2); Grid.SetColumn(UpdateButton, 3);
        points.Children.Add(DetailsButton); points.Children.Add(pointsWords); points.Children.Add(pointsInfo); points.Children.Add(UpdateButton);
        stack.Children.Add(points);

        // ---- what changes
        this.Track(() => (capsule.Status, capsule.Tick, view.Prefs, Explain: controller.Settings.Value.ShowExplanations), input =>
        {
            var status = input.Status;
            var now = SwiftDate.Now;
            var cache = KindleCapsule.Effective(status, now);
            var left = KindleCapsule.Left(status, now);
            var animate = primed;
            primed = true;
            lineInfos.Clear();
            infoSink = lineInfos;

            cacheLabel.Inlines = [KindleUi.Run(cacheLabel, IgnisWords.QuickMemory, KindleTone.Primary, strong: true), KindleUi.Run(cacheLabel, " · ", KindleTone.Secondary), KindleUi.Run(cacheLabel, IgnisWords.CacheStatus(cache), KindleTone.Secondary)];
            cacheLeft.Text = status.CacheReported || cache != IgnisCacheState.Warm ? IgnisWords.TimeLeft(cache, left) : IgnisWords.NotReported;
            cacheTrack.Show(KindleCapsule.CacheLeftOf(status, now), KindleCapsule.HeatOf(status, now), cache == IgnisCacheState.Warm, null, animate);
            FillTicks(ticks, status.Ttl ?? TimeSpan.FromHours(1));

            var fill = status.ContextFill;
            sizeFull.Text = fill is { } f ? IgnisWords.PercentFull(f) : IgnisWords.NotReported;
            double? notch = status.TidyAt is { } at && status.ContextWindow is { } window && window > 0 ? (double)at / window : null;
            sizeTrack.Show(fill ?? 0, 0, false, notch, animate);
            notchLabel.IsVisible = notch is not null;
            if (notch is { } n)
            {
                notchLabel.Measure(Size.Infinity);
                var x = (PopoverWidth - 2 * Inset) * n - notchLabel.DesiredSize.Width / 2;
                Canvas.SetLeft(notchLabel, Math.Clamp(x, 0, PopoverWidth - 2 * Inset - notchLabel.DesiredSize.Width));
            }

            var next = status.Tidying ? IgnisNext.Tidying : cache == IgnisCacheState.Cold && status.Mode != IgnisMode.Off && status.Next is not (IgnisNext.Off or IgnisNext.HomeThread or IgnisNext.HomeThreadTidy or IgnisNext.MissionRunning) ? IgnisNext.Cold : status.Next;
            summary.Inlines = [
                KindleUi.Run(summary, IgnisWords.SavedTodayLead, KindleTone.Secondary),
                KindleUi.Run(summary, IgnisWords.Money(status.SavedToday), KindleTone.Good, strong: true),
                InlineInfo(IgnisWords.InfoMoneySavedName, IgnisWords.InfoMoneySaved),
                KindleUi.Run(summary, ". " + IgnisWords.Next(next), KindleTone.Secondary)];
            SummaryText = IgnisWords.Summary(status.SavedToday, next);

            quiet.Inlines = AccountLine(quiet, status);

            if (modes.SelectedId != IgnisSettings.RawValue(status.Mode)) modes.Show(IgnisSettings.RawValue(status.Mode));
            var on = status.Mode != IgnisMode.Off;
            auto.IsEnabled = on;
            auto.Opacity = on ? 1 : .45;
            KeepWarmButton.IsEnabled = on && status.CanKeepWarm && !status.KeepingWarm;
            TidyButton.IsEnabled = status.Mode == IgnisMode.WarmAndTidy && status.CanTidy && !status.Tidying;
            tidyWords.Text = status.Tidying ? IgnisWords.TidyingUp : status.JustTidied ? IgnisWords.TidiedUp : IgnisWords.TidyUpNow;
            AutomationProperties.SetName(TidyButton, tidyWords.Text);

            infoSink = infos;
            foreach (var info in infos.Concat(lineInfos)) info.IsVisible = input.Explain;
        });
        this.Track(() => (view.PointsLabel, view.PointsAge), input =>
        {
            pointsWords.Inlines = [KindleUi.Run(pointsWords, IgnisWords.ModelPoints + " ", KindleTone.Secondary), KindleUi.Run(pointsWords, input.PointsAge ?? PointsAge(), KindleTone.Tertiary)];
            ((TextBlock)((Border)UpdateButton.Content!).Child!).Text = input.PointsLabel;
            AutomationProperties.SetName(UpdateButton, input.PointsLabel);
            UpdateButton.IsEnabled = input.PointsLabel != IgnisWords.Checking;
        });
        this.Track(() => view.Details, open =>
        {
            drawerReveal.Set(open ? 1 : 0, primedDrawer ? KindleMotion.Ease(this, KindleMotion.Smooth(.44)) : null);
            primedDrawer = true;
            chevron.RenderTransform = new RotateTransform(open ? 180 : 0);
            AutomationProperties.SetHelpText(DetailsButton, open ? "Expanded" : "Collapsed");
        });
    }

    private bool primedDrawer;

    // ---- what tests read

    internal IgnisChatStatus Status => capsule.Status;
    internal Control AutoControl { get; }
    internal CzSegmentedControl ModeControl { get; }
    internal Button KeepWarmButton { get; }
    internal Button TidyButton { get; }
    internal Button DetailsButton { get; }
    internal Button UpdateButton { get; }
    internal string SummaryText { get; private set; } = "";
    internal bool DetailsOpen { get => view.Details; set => view.Details = value; }
    internal double DetailsReveal => drawer.Reveal;
    internal Section DetailsSection { get => view.Section; set => view.Section = value; }
    private void OnPrefs()
    {
        if (MainActor.IsIsolated) view.Prefs++;
        else MainActor.Post(() => view.Prefs++);
    }

    internal IReadOnlyList<Control> Infos => [.. infos, .. lineInfos, .. paneInfos];
    internal Button OpenUsageButton { get; private set; } = null!;
    internal string AccountText { get; private set; } = "";

    /// <summary>"Open usage": shows Marrow's usage panel (Settings.ShowsUsagePanel) and closes the popover.</summary>
    internal static Action<Control> OpenUsage { get; set; } = anchor =>
    {
        if (anchor.GetValue(AppEnvironment.AppModelProperty) is not { } model) return;
        Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.ShowsUsagePanel = true);
    };

    /// <summary>"Open Tools settings": Settings › MCP, where each server has its own switch.</summary>
    internal static Action OpenTools { get; set; } = () => Marrow.App.Window.WindowManager.Shared.ShowSettings(SettingsPage.Mcp);

    /// <summary>The compact settings rows' height: the 22 pt pills' rhythm plus the menu's own inset.</summary>
    internal const double CompactRowHeight = 24;

    internal CzMenuButton CoolAfterMenu { get; }
    internal SettingsSwitch LearnCoolingSwitch { get; }

    // The preferences as this popover observes them (bumped by IgnisSettings.Changed), so both views show one value.
    private IgnisPreferences Preferences()
    {
        _ = view.Prefs;
        return controller.Settings.Value;
    }

    private static Grid CompactRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = CompactRowHeight };
        var words = KindleUi.Text(label, 12, KindleTone.Primary);
        words.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(words); row.Children.Add(control);
        return row;
    }

    private Grid LabelRow(Control left, Control right)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 7) };
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        row.Children.Add(left); row.Children.Add(right);
        return row;
    }

    private Button Info(string name, Func<string> tip)
    {
        var info = KindleUi.Info(name, tip());
        info.PointerEntered += (_, _) => ToolTip.SetTip(info, tip());
        infoSink.Add(info);
        return info;
    }

    private InlineUIContainer InlineInfo(string name, string tip)
    {
        var info = KindleUi.Info(name, tip);
        info.IsVisible = controller.Settings.Value.ShowExplanations;
        infoSink.Add(info);
        return new InlineUIContainer(info) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static void FillTicks(Grid ticks, TimeSpan ttl)
    {
        var words = IgnisWords.TrackTicks(ttl);
        if (ticks.Children.Count == words.Count && ticks.Children.OfType<TextBlock>().Select(t => t.Text).SequenceEqual(words)) return;
        ticks.Children.Clear();
        ticks.ColumnDefinitions.Clear();
        for (var i = 0; i < words.Count; i++)
        {
            ticks.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var tick = KindleUi.Text(words[i], 10.5, KindleTone.Tertiary, tabular: true);
            tick.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == words.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            Grid.SetColumn(tick, i);
            ticks.Children.Add(tick);
        }
    }

    private InlineCollection AccountLine(TextBlock owner, IgnisChatStatus status)
    {
        var inlines = new InlineCollection();
        string text;
        if (status.Account == IgnisAccountKind.ApiKey)
        {
            var cost = status.ChatCost ?? 0m;
            inlines.Add(KindleUi.Run(owner, "API key: you pay per token. This chat: ", KindleTone.Tertiary));
            inlines.Add(KindleUi.Run(owner, IgnisWords.Money(cost), KindleTone.Secondary, strong: true));
            if (status.ChatCostWithoutIgnis is { } without) inlines.Add(InlineInfo(IgnisWords.InfoChatCostName, IgnisWords.InfoChatCost(without)));
            inlines.Add(KindleUi.Run(owner, ". ", KindleTone.Tertiary));
            text = IgnisWords.ApiKeyLine(cost);
        }
        else
        {
            var parts = new List<string>();
            if (status.WeekShare is { } share)
            {
                inlines.Add(KindleUi.Run(owner, "This chat used ", KindleTone.Tertiary));
                inlines.Add(KindleUi.Run(owner, IgnisWords.Percent(share), KindleTone.Secondary, strong: true));
                inlines.Add(KindleUi.Run(owner, " of your week", KindleTone.Tertiary));
                inlines.Add(InlineInfo(IgnisWords.InfoWeekShareName, IgnisWords.InfoWeekShare(share)));
                inlines.Add(KindleUi.Run(owner, ". ", KindleTone.Tertiary));
                parts.Add(IgnisWords.WeekShare(share));
            }
            if (status.WeeklyLimitAt is { } at)
            {
                var when = IgnisWords.DayPart(at.ToLocalTime());
                inlines.Add(KindleUi.Run(owner, "At this pace you’ll reach the weekly limit ", KindleTone.Tertiary));
                inlines.Add(KindleUi.Run(owner, when, KindleTone.Secondary, strong: true));
                inlines.Add(KindleUi.Run(owner, ". ", KindleTone.Tertiary));
                parts.Add(IgnisWords.WeeklyLimitAt(when));
            }
            text = string.Join(" ", parts);
        }
        OpenUsageButton = KindleUi.Link(IgnisWords.OpenUsage, () =>
        {
            capsule.IsPresented = false;
            OpenUsage(capsule);
        });
        inlines.Add(new InlineUIContainer(OpenUsageButton) { BaselineAlignment = BaselineAlignment.Center });
        AccountText = text;
        return inlines;
    }

    private string PointsAge()
    {
        var version = controller.Points.Current.Version;
        if (!DateOnly.TryParseExact(version, "yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)) return "";
        var today = DateOnly.FromDateTime(SwiftDate.Now.ToLocalTime().DateTime);
        return IgnisWords.PointsAge(today.DayNumber - day.DayNumber);
    }

    private async void CheckPoints()
    {
        if (view.PointsLabel == IgnisWords.Checking) return;
        view.PointsLabel = IgnisWords.Checking;
        try
        {
            var result = await controller.Points.CheckAsync(install: true, CancellationToken.None);
            MainActor.Post(() =>
            {
                view.PointsLabel = IgnisWords.Update;
                if (result.Error is null) view.PointsAge = IgnisWords.PointsJustChecked;
            });
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            MainActor.Post(() => view.PointsLabel = IgnisWords.Update);
        }
    }

    // ---- Details: Savings, History, Each message

    private Control BuildDetails()
    {
        var tabs = new CzSegmentedControl(IgnisWords.TabsAccessibleName,
            [("src", IgnisWords.TabSavings), ("hist", IgnisWords.TabHistory), ("pack", IgnisWords.TabEachMessage)], "src", small: true) { HorizontalAlignment = HorizontalAlignment.Stretch };
        tabs.SelectionChanged += id => view.Section = id switch { "hist" => Section.History, "pack" => Section.EachMessage, _ => Section.Savings };
        var pane = new Decorator();
        var inner = new StackPanel { Spacing = 14, Margin = new Thickness(0, BlockGap, 0, 0), Children = { tabs, pane, KindleUi.Divider() } };
        this.Track(() => (view.Section, capsule.Status, view.Report, view.CopyNote, view.Prefs, Explain: controller.Settings.Value.ShowExplanations, Helpers: controller.Settings.Value.SmartHelpers), input =>
        {
            var id = input.Section switch { Section.History => "hist", Section.EachMessage => "pack", _ => "src" };
            if (tabs.SelectedId != id) tabs.Show(id);
            paneInfos.Clear();
            var sink = infoSink;
            infoSink = paneInfos;
            pane.Child = input.Section switch
            {
                Section.History => HistoryPane(input.Status),
                Section.EachMessage => EachMessagePane(input.Status),
                _ => SavingsPane(input.Status),
            };
            infoSink = sink;
            foreach (var info in paneInfos) info.IsVisible = input.Explain;
        });
        return inner;
    }

    private static SwiftColor SourceColor(IgnisSource source) => source switch
    {
        IgnisSource.Reuse => Chrome.Accent,
        IgnisSource.KeptWarm => SwiftColor.Rgb(0xff6a1f),
        IgnisSource.TidyUp => KindleUi.Color(KindleTone.Good),
        IgnisSource.SmartHelpers => SwiftColor.Dynamic(SwiftColor.Rgb(0x7b55e0), SwiftColor.Rgb(0xa98bff)),
        IgnisSource.LongerMemory => SwiftColor.Dynamic(SwiftColor.Rgb(0xffb040), SwiftColor.Rgb(0xffc464)), // ED-NEW-ignis-runtime: amber
        _ => Chrome.Accent,
    };

    private Grid Header(Control left, Control right)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(right, 1);
        row.Children.Add(left); row.Children.Add(right);
        return row;
    }

    private Control SavingsPane(IgnisChatStatus status)
    {
        var pane = new StackPanel { Spacing = 10 };
        pane.Children.Add(Header(KindleUi.Text(IgnisWords.WhereTodaysSavingsCameFrom, 12), KindleUi.Text(IgnisWords.Money(status.SavedToday), 12, KindleTone.Good, strong: true, tabular: true)));
        decimal Amount(IgnisSource s) => status.SavedBySource.TryGetValue(s, out var v) ? v : 0m;
        // ED-NEW-ignis-runtime: "Longer quick memory" joins the design's four only on a day it saved something.
        IgnisSource[] sources = Amount(IgnisSource.LongerMemory) > 0m
            ? [IgnisSource.Reuse, IgnisSource.KeptWarm, IgnisSource.TidyUp, IgnisSource.SmartHelpers, IgnisSource.LongerMemory]
            : [IgnisSource.Reuse, IgnisSource.KeptWarm, IgnisSource.TidyUp, IgnisSource.SmartHelpers];
        var bar = new Grid { Height = 6, ColumnSpacing = 2, ClipToBounds = true };
        var total = sources.Sum(Amount);
        for (var i = 0; i < sources.Length; i++)
        {
            var weight = total > 0 ? (double)(Amount(sources[i]) / total) : .25;
            bar.ColumnDefinitions.Add(new ColumnDefinition(weight <= 0 ? new GridLength(0) : new GridLength(weight, GridUnitType.Star)));
            var segment = new Border { CornerRadius = new CornerRadius(i == 0 ? 3 : 0, i == sources.Length - 1 ? 3 : 0, i == sources.Length - 1 ? 3 : 0, i == 0 ? 3 : 0) };
            segment.Background = (total > 0 ? SourceColor(sources[i]) : KindleUi.TrackFill).Brush(segment);
            Grid.SetColumn(segment, i);
            bar.Children.Add(segment);
        }
        AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
        pane.Children.Add(bar);
        var list = new StackPanel { Spacing = 6 };
        foreach (var source in sources)
        {
            var name = source == IgnisSource.SmartHelpers && !controller.Settings.Value.SmartHelpers ? IgnisWords.SmartHelpersNowOff : IgnisWords.SourceName(source);
            var info = source == IgnisSource.SmartHelpers && status.Provider == Marrow.Core.Models.ProviderKind.Codex ? IgnisWords.SmartHelpersCodex : IgnisWords.SourceInfo(source);
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { KindleUi.Dot(SourceColor(source)), KindleUi.Text(name, 12) } };
            ((StackPanel)label).Children.Add(Info(name.ToLowerInvariant(), () => info));
            list.Children.Add(Header(label, KindleUi.Text(IgnisWords.Money(Amount(source)), 12, KindleTone.Primary, strong: true, tabular: true)));
        }
        pane.Children.Add(list);
        var quiet = new TextBlock { TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(11.5).Apply(quiet);
        quiet.Inlines =
        [
            KindleUi.Run(quiet, status.ReuseRate is { } rate ? IgnisWords.ReusedOfTheTime(rate) : "Quick memory reuse " + IgnisWords.NotReported, KindleTone.Tertiary),
            InlineInfo(IgnisWords.InfoReuseRateName, IgnisWords.InfoReuseRate),
            KindleUi.Run(quiet, " · " + IgnisWords.LostWhenCold(status.LostToday), KindleTone.Tertiary),
            InlineInfo(IgnisWords.InfoMoneyLostName, IgnisWords.InfoMoneyLost),
        ];
        pane.Children.Add(quiet);
        pane.Children.Add(KindleUi.Divider());
        var helperModel = controller.Points.Find(status.Provider ?? Marrow.Core.Models.ProviderKind.Claude, status.Model)?.HelperModel ?? IgnisWords.DefaultHelperModel;
        var words = new StackPanel { Spacing = 2, Children = { KindleUi.Text(IgnisWords.SmartHelpersSwitch, 12.5, KindleTone.Primary, strong: true, wraps: true), KindleUi.Text(IgnisWords.SmartHelpersDetail(helperModel), 11.5, wraps: true) } };
        var helpers = new SettingsSwitch(new Binding<bool>(() => controller.Settings.Value.SmartHelpers, value => controller.Settings.Update(p => p with { SmartHelpers = value })));
        AutomationProperties.SetName(helpers, IgnisWords.SmartHelpersSwitch);
        helpers.VerticalAlignment = VerticalAlignment.Top;
        helpers.Margin = new Thickness(0, 1, 0, 0);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(helpers, 1);
        row.Children.Add(words); row.Children.Add(helpers);
        pane.Children.Add(row);
        return pane;
    }

    private Control HistoryPane(IgnisChatStatus status)
    {
        var ledger = controller.Ledger;
        var today = DateOnly.FromDateTime(SwiftDate.Now.ToLocalTime().DateTime);
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(day => (Day: day, Saved: ledger.ChatDay(thread, day).Saved)).ToList();
        var week = days.Sum(d => d.Saved);
        var pane = new StackPanel { Spacing = 10 };
        var left = new TextBlock();
        SwiftFont.System(12).Apply(left);
        left.Inlines = [KindleUi.Run(left, IgnisWords.LastSevenDays + " ", KindleTone.Secondary), KindleUi.Run(left, IgnisWords.Money(week), KindleTone.Good, strong: true)];
        var right = new TextBlock();
        SwiftFont.System(12).Apply(right);
        right.Inlines = [KindleUi.Run(right, IgnisWords.AllChats + " ", KindleTone.Secondary), KindleUi.Run(right, IgnisWords.MoneyWhole(ledger.AllTimeSaved), KindleTone.Primary, strong: true), InlineInfo(IgnisWords.InfoAllChatsName, IgnisWords.InfoAllChats)];
        pane.Children.Add(Header(left, right));
        // the 7-day chart: bars to the week's best day (at least $7.30 tall, the design's scale), today's in full green
        var max = Math.Max(7.3m, days.Max(d => d.Saved));
        var chart = new Grid { Height = 54, ColumnSpacing = 8 };
        var labels = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < 7; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            labels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var isToday = i == 6;
            var barHeight = Math.Max(3, 54 * (double)(days[i].Saved / max));
            var barView = new Border { Height = barHeight, MaxWidth = 24, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(4, 4, 2, 2) };
            barView.Background = (isToday ? KindleUi.Color(KindleTone.Good) : KindleUi.Color(KindleTone.Good).Opacity(.32)).Brush(barView);
            ToolTip.SetTip(barView, IgnisWords.ShortDay(days[i].Day, today) + " · " + IgnisWords.Money(days[i].Saved));
            Grid.SetColumn(barView, i);
            chart.Children.Add(barView);
            var label = KindleUi.Text(IgnisWords.ShortDay(days[i].Day, today), 10, isToday ? KindleTone.Secondary : KindleTone.Tertiary);
            if (isToday) SwiftFont.System(10, SwiftFont.Weight.Medium).Apply(label);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(label, i);
            labels.Children.Add(label);
        }
        AutomationProperties.SetName(chart, IgnisWords.WeekChartAccessibleName(days.Select(d => (IgnisWords.ShortDay(d.Day, today), d.Saved))));
        AutomationProperties.SetAccessibilityView(labels, AccessibilityView.Raw);
        pane.Children.Add(new StackPanel { Children = { chart, labels } });
        if (!view.Report)
        {
            var list = new StackPanel { Spacing = 5 };
            foreach (var e in ledger.Events(thread, today))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
                var time = KindleUi.Text(IgnisWords.EventTime(e.At.ToLocalTime()), 11.5, KindleTone.Tertiary, tabular: true);
                var what = KindleUi.Text(IgnisWords.HistoryRow(e), 11.5, KindleTone.Secondary);
                what.TextTrimming = TextTrimming.CharacterEllipsis;
                var amount = KindleUi.Text(IgnisWords.EventAmount(e) ?? "", 11.5, e.AmountKind == IgnisAmountKind.Saved ? KindleTone.Good : KindleTone.Secondary, tabular: true);
                Grid.SetColumn(what, 1); Grid.SetColumn(amount, 2);
                row.Children.Add(time); row.Children.Add(what); row.Children.Add(amount);
                list.Children.Add(row);
            }
            pane.Children.Add(new ScrollViewer { MaxHeight = 104, Content = list, Margin = new Thickness(0, -4, 0, 0), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            var save = KindleUi.Pill(IgnisWords.SaveReport, () => view.Report = true, height: 22, size: 11.5, padding: 6, ghost: true);
            ((TextBlock)((Border)save.Content!).Child!).Foreground = Chrome.Accent.Brush(save);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(-6, 6, 0, 0);
            pane.Children.Add(save);
        }
        else
        {
            var text = new TextBox { Text = controller.Report(thread), IsReadOnly = true, Height = 116, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(10, 9), BorderThickness = default, CornerRadius = new CornerRadius(10), AcceptsReturn = true };
            text.Background = KindleUi.ControlFill.Brush(text);
            text.FontFeatures = KindleCapsule.Tabular();
            SwiftFont.System(11.5).Apply(text);
            AutomationProperties.SetName(text, IgnisWords.ReportAccessibleName);
            var note = KindleUi.Text(view.CopyNote, 11.5);
            note.VerticalAlignment = VerticalAlignment.Center;
            var done = KindleUi.Pill(IgnisWords.Done, () => { view.CopyNote = ""; view.Report = false; }, height: 22, size: 11.5, padding: 10);
            var copy = KindleUi.Pill(IgnisWords.Copy, async () =>
            {
                try
                {
                    if (TopLevel.GetTopLevel(capsule)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(text.Text ?? ""); view.CopyNote = IgnisWords.Copied; }
                    else { text.Focus(); text.SelectAll(); view.CopyNote = IgnisWords.SelectedPressToCopy; }
                }
                catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    text.Focus(); text.SelectAll(); view.CopyNote = IgnisWords.SelectedPressToCopy;
                }
            }, height: 22, size: 11.5, padding: 10);
            var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 9, 0, 0) };
            Grid.SetColumn(done, 1); Grid.SetColumn(copy, 2);
            foot.Children.Add(note); foot.Children.Add(done); foot.Children.Add(copy);
            pane.Children.Add(new StackPanel { Children = { text, foot } });
            ReportText = text.Text;
        }
        return pane;
    }

    internal string? ReportText { get; private set; }

    private Control EachMessagePane(IgnisChatStatus status)
    {
        var pane = new StackPanel { Spacing = 10 };
        var weight = status.Weight;
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(KindleUi.Text(weight is { } w ? IgnisWords.Tokens(w.Total) : IgnisWords.NotReported, 12, KindleTone.Primary, strong: true, tabular: true));
        if (weight is not null && controller.Points.Find(status.Provider ?? Marrow.Core.Models.ProviderKind.Claude, status.Model)?.Prices is { } prices)
        {
            var warm = weight.Total * prices.CacheRead / 1_000_000m;
            var cold = weight.Total * (prices.CacheWrite1h ?? prices.CacheWrite5m ?? prices.Input) / 1_000_000m;
            right.Children.Add(Info(IgnisWords.InfoWeightName, () => IgnisWords.InfoWeight(warm, cold)));
        }
        pane.Children.Add(Header(KindleUi.Text(IgnisWords.CarriedByEveryMessage, 12), right));
        if (weight is { } parts)
        {
            var rows = new (string Name, long Tokens, string? Tip)[]
            {
                (IgnisWords.Instructions, parts.Instructions, null),
                (IgnisWords.BuiltInTools, parts.BuiltInToolsEstimate, IgnisWords.InfoBuiltInTools),
                (IgnisWords.ToolsFromServers(parts.ServerCount), parts.ServerTools, IgnisWords.InfoServers),
                (IgnisWords.Skills, parts.Skills, null),
            };
            var most = Math.Max(1, rows.Max(r => r.Tokens));
            var list = new StackPanel { Spacing = 8 };
            foreach (var (name, tokens, tip) in rows)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,78,30"), ColumnSpacing = 10 };
                var label = new StackPanel { Orientation = Orientation.Horizontal, Children = { KindleUi.Text(name, 12) } };
                if (tip is not null) label.Children.Add(Info(name.ToLowerInvariant(), () => tip));
                var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
                track.Background = KindleUi.TrackFill.Brush(track);
                var fill = new Border { Width = 78.0 * tokens / most, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Opacity = .75 };
                fill.Background = Chrome.Accent.Brush(fill);
                track.Child = fill;
                var value = KindleUi.Text(IgnisWords.Tokens(tokens), 12, KindleTone.Primary, strong: true, tabular: true);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(track, 1); Grid.SetColumn(value, 2);
                row.Children.Add(label); row.Children.Add(track); row.Children.Add(value);
                list.Children.Add(row);
            }
            pane.Children.Add(list);
            if (parts.UnusedServers > 0)
            {
                var words = new TextBlock { TextWrapping = TextWrapping.Wrap };
                SwiftFont.System(11.5).Apply(words);
                words.Inlines = [KindleUi.Run(words, IgnisWords.ServersUnused(parts.UnusedServers), KindleTone.Primary, strong: true), KindleUi.Run(words, " " + IgnisWords.TurningThemOffSaves(parts.UnusedServerTokens), KindleTone.Secondary)];
                var open = KindleUi.Pill(IgnisWords.OpenToolsSettings, () => { capsule.IsPresented = false; OpenTools(); }, height: 22, size: 11.5, padding: 10);
                open.HorizontalAlignment = HorizontalAlignment.Left;
                var box = new Border { Padding = new Thickness(11, 10), CornerRadius = new CornerRadius(12), Child = new StackPanel { Spacing = 8, Children = { words, open } } };
                box.Background = KindleUi.ControlFill.Brush(box);
                pane.Children.Add(box);
            }
        }
        return pane;
    }
}

/// <summary>A still flame glyph at a fixed warm heat (the Keep warm now button's, the event line's, the notice's).</summary>
internal sealed class FlameMark : Control
{
    public double HeatLevel { get; set; } = .85;

    public override void Render(DrawingContext context)
    {
        var palette = KindlePalette.For(this);
        var outer = Geometry.Parse(KindleFlame.Outer);
        var core = Geometry.Parse(KindleFlame.Core);
        var scale = Bounds.Width / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(new SolidColorBrush(palette.EmberCold), null, outer);
            context.DrawGeometry(new SolidColorBrush(palette.EmberOuter, HeatLevel), null, outer);
            context.DrawGeometry(new SolidColorBrush(palette.EmberCore, HeatLevel), null, core);
        }
    }
}

/// <summary>The Details button's chevron (points up closed, turns over open).</summary>
internal sealed class ChevronUp : Control
{
    public ChevronUp() => RenderTransformOrigin = RelativePoint.Center;

    public override void Render(DrawingContext context)
    {
        var s = Bounds.Width / 10;
        var pen = new Pen(KindleUi.Color(KindleTone.Secondary).Brush(this), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var geometry = new PolylineGeometry([new Point(2 * s, 6.5 * s), new Point(5 * s, 3.5 * s), new Point(8 * s, 6.5 * s)], false);
        context.DrawGeometry(null, pen, geometry);
    }
}
