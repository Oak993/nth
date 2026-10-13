// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 3.2 "States" (board groups), 3.3 "Mission card" (live values at most 4 times a second,
// never a layout pass), 2.5 "Motion budget"; the canvas's placeAll and flip, work/design/assistant/index.html lines 1823-1840,
// 2108-2131; ED-NEW-dextra-as8-5). No Swift twin.
// The Missions tab: three groups in a fixed order, Needs you first (whatever waits on the owner comes first), then Working, then
// Done today. Newest first inside a group; Done today by when each finished, today only. A card that changes group moves there with
// a transform only (FLIP), no fade. The board reads missions through IMissionJobs (the 16.2 fallback for BackgroundJobs). It
// refreshes on every change, and ticks at 4 Hz while it is shown: a tick that changes only live values (elapsed, agents, spend,
// the activity line) redraws them in their fixed slots and never asks for a layout pass. It is one list of the window's motion
// budget, ranked first.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

public sealed class MissionBoard : Decorator
{
    public static readonly TimeSpan TickEvery = TimeSpan.FromMilliseconds(250);

    private static readonly BoardGroup[] Order = [BoardGroup.NeedsYou, BoardGroup.Working, BoardGroup.DoneToday];
    private readonly IMissionJobs jobs;
    private readonly TaskActions actions;
    private readonly Dictionary<BoardGroup, (TextBlock Heading, StackPanel List, TextBlock Empty)> groups = [];
    private readonly Dictionary<Guid, MissionCard> cards = [];
    private readonly Dictionary<Guid, MissionJob> shown = [];
    private readonly StackPanel body = new() { Orientation = Orientation.Vertical, Spacing = 14 };
    private ITimer? timer;
    private TimeSpan? timerPeriod;
    private DextraViewWatch? availability;
    private MotionScope.Entry? budget;
    private bool narrow;
    private bool calm;
    private readonly Func<DateTimeOffset?>? digestedThrough;
    private readonly Func<Action, IDisposable>? watchDigest;
    private IDisposable? digestSubscription;

    public MissionBoard(IMissionJobs jobs, TaskActions? actions = null, Func<DateTimeOffset?>? digestedThrough = null, Func<Action, IDisposable>? watchDigest = null)
    {
        this.digestedThrough = digestedThrough;
        this.watchDigest = watchDigest;
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        this.actions = actions ?? TaskActions.None;
        foreach (var group in Order)
        {
            var heading = TaskLook.Text(TaskWords.GroupTitle(group), 11, SwiftFont.Weight.Semibold, TaskLook.Ink2);
            AutomationProperties.SetHeadingLevel(heading, 3);
            var empty = TaskLook.Text(TaskWords.GroupEmpty(group), 12, SwiftFont.Weight.Regular, TaskLook.Ink2);
            var list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            groups[group] = (heading, list, empty);
            body.Children.Add(new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Children = { heading, list, empty } });
        }
        Child = body;
        AutomationProperties.SetName(this, "Missions");
        SizeChanged += (_, e) => Narrow = e.NewSize.Width < TaskMetrics.NarrowWidth;
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) { budget?.Changed(); Arm(); } };
    }

    /// <summary>Whether the board is still (its list did not fit the window's budget).</summary>
    public bool Calm
    {
        get => calm;
        set
        {
            calm = value;
            foreach (var card in cards.Values) { card.Calm = value; card.Mark.Calm = value; }
        }
    }

    public IReadOnlyList<MissionCard> Cards(BoardGroup group) => [.. groups[group].List.Children.OfType<MissionCard>()];

    public IReadOnlyCollection<MissionCard> AllCards => cards.Values;

    public MissionCard? Card(Guid job) => cards.GetValueOrDefault(job);

    public TextBlock Heading(BoardGroup group) => groups[group].Heading;

    public TextBlock Empty(BoardGroup group) => groups[group].Empty;

    /// <summary>What the board's marks and shimmers cost the budget now.</summary>
    public int Moving => cards.Values.Count(c => c.IsVisible && (c.Mark.Moves || c.WantsSheen));

    /// <summary>A card's refusal or note, for the home's polite live region.</summary>
    public event Action<string>? Said;

    /// <summary>A card's state changed (the home announces it).</summary>
    public event Action<MissionJob, MissionState?>? StateChanged;

    public bool Narrow
    {
        get => narrow;
        set
        {
            if (narrow == value) return;
            narrow = value;
            foreach (var card in cards.Values) card.Narrow = value;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        availability = new DextraViewWatch(this);
        availability.Changed += OnAvailability;
        jobs.Changed += Refresh;
        digestSubscription = watchDigest?.Invoke(Refresh);
        if (TopLevel.GetTopLevel(this) is { } top)
            budget = MotionScope.For(top).Join(ArmillaListRank.Board, () => Moving, () => IsEffectivelyVisible, c => Calm = c);
        Refresh();
        Arm();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        jobs.Changed -= Refresh;
        digestSubscription?.Dispose();
        digestSubscription = null;
        if (availability is not null) { availability.Changed -= OnAvailability; availability.Dispose(); availability = null; }
        budget?.Leave();
        budget = null;
        timer?.Dispose();
        timer = null; timerPeriod = null;
    }

    private void Arm()
    {
        var want = IsEffectivelyVisible && VisualRoot is not null && availability?.Available != false && cards.Count > 0;
        var period = shown.Values.Any(j => j.Group != BoardGroup.DoneToday) ? TickEvery : Timeout.InfiniteTimeSpan;
        if (!want || timerPeriod != period) { timer?.Dispose(); timer = null; timerPeriod = null; }
        if (want && timer is null)
        {
            var now = Now;
            var local = TaskWords.Local(now).Date.AddDays(1);
            var midnight = new DateTimeOffset(local, SwiftClock.Provider.Current.LocalTimeZone.GetUtcOffset(local));
            var due = period == TickEvery ? TickEvery : midnight - now;
            timer = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(OnClockTick), null, due, period);
            timerPeriod = period;
        }
    }

    private DateTimeOffset Now => SwiftClock.Provider.Current.GetUtcNow();

    private void OnClockTick()
    {
        if (timerPeriod == Timeout.InfiniteTimeSpan) { timer?.Dispose(); timer = null; timerPeriod = null; }
        Tick(); Arm();
    }

    private void OnAvailability()
    {
        budget?.Changed();
        if (availability?.Available == true) Refresh();
        Arm();
    }

    /// <summary>The 4 Hz tick: live values in place when nothing else changed, a full refresh otherwise.</summary>
    public void Tick()
    {
        if (availability?.Available == false) return;
        var now = Now;
        var list = Visible(jobs.Snapshot(now), now);
        if (list.Count != shown.Count || list.Any(j => !shown.TryGetValue(j.Id, out var was) || !SameShape(was, j))) { Place(list, now); return; }
        foreach (var job in list)
        {
            cards[job.Id].Tick(job, now);
            shown[job.Id] = job;
        }
    }

    /// <summary>
    /// Brings one task's card into view (a duty toast's Open, AS-9): the list is read again and the card scrolls to where the owner can see it. False when
    /// no card shows that task (it is not on the board today).
    /// </summary>
    public bool Reveal(Guid job)
    {
        Refresh();
        if (Card(job) is not { } card) return false;
        Avalonia.Threading.Dispatcher.UIThread.Post(card.BringIntoView, Avalonia.Threading.DispatcherPriority.Loaded);
        return true;
    }

    /// <summary>Reads the missions again and places every card.</summary>
    public void Refresh()
    {
        if (availability?.Available == false) return; // the next visible transition reads the newest snapshot once
        var now = Now;
        Place(Visible(jobs.Snapshot(now), now), now);
    }

    private List<MissionJob> Visible(IReadOnlyList<MissionJob> all, DateTimeOffset now)
    {
        var today = TaskWords.Local(now).Date;
        var digest = digestedThrough?.Invoke();
        return [.. all.Where(j => j.Group != BoardGroup.DoneToday || TaskWords.Local(j.Ended ?? j.Created).Date == today && (digest is null || (j.Ended ?? j.Created) > digest.Value))];
    }

    /// <summary>Two snapshots of a task that differ only in live values (what a tick redraws in place).</summary>
    internal static bool SameShape(MissionJob a, MissionJob b) =>
        a with { Detail = "", Activity = null, Spend = ValueList<MissionSpend>.Empty, Agents = 0 } == b with { Detail = "", Activity = null, Spend = ValueList<MissionSpend>.Empty, Agents = 0 };

    private void Place(IReadOnlyList<MissionJob> list, DateTimeOffset now)
    {
        var still = TaskLook.Still(this);
        var before = still ? null : Positions();
        var keep = list.Select(j => j.Id).ToHashSet();
        foreach (var gone in cards.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            var card = cards[gone];
            if (!still && shown[gone].Group == BoardGroup.DoneToday && digestedThrough?.Invoke() is { } through && (shown[gone].Ended ?? shown[gone].Created) <= through)
            {
                card.IsHitTestVisible = false;
                card.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(240) }];
                card.Opacity = 0;
                MainActor.AsyncAfter(TimeSpan.FromMilliseconds(240), () => (card.Parent as Panel)?.Children.Remove(card));
            }
            else (card.Parent as Panel)?.Children.Remove(card);
            cards.Remove(gone);
            shown.Remove(gone);
        }
        var moved = false;
        foreach (var group in Order)
        {
            var (_, panel, empty) = groups[group];
            var mine = list.Where(j => j.Group == group)
                .OrderByDescending(j => group == BoardGroup.DoneToday ? j.Ended ?? j.Created : j.Created)
                .ThenBy(j => j.Id)
                .ToList();
            for (var i = 0; i < mine.Count; i++)
            {
                var job = mine[i];
                var was = shown.GetValueOrDefault(job.Id);
                if (!cards.TryGetValue(job.Id, out var card))
                {
                    card = new MissionCard(jobs, Refresh, actions) { Narrow = narrow, Calm = calm };
                    card.Mark.Calm = calm;
                    card.Said += text => Said?.Invoke(text);
                    card.Surface.Said += text => Said?.Invoke(text);
                    card.Mark.MovesChanged += _ => budget?.Changed();
                    cards[job.Id] = card;
                }
                if (card.Parent != panel || panel.Children.IndexOf(card) != i)
                {
                    (card.Parent as Panel)?.Children.Remove(card);
                    panel.Children.Insert(Math.Min(i, panel.Children.Count), card);
                    moved = true;
                }
                card.Show(job, now);
                shown[job.Id] = job;
                if (was is null || was.State != job.State) StateChanged?.Invoke(job, was?.State);
            }
            empty.IsVisible = mine.Count == 0;
        }
        budget?.Changed();
        if (moved && before is not null) Flip(before);
        Arm();
    }

    private Dictionary<MissionCard, Point>? Positions()
    {
        if (VisualRoot is null) return null;
        var map = new Dictionary<MissionCard, Point>();
        foreach (var card in cards.Values)
            if (card.TranslatePoint(default, this) is { } p) map[card] = p - CurrentShift(card);
        return map;
    }

    private static Point CurrentShift(Control card) => card.RenderTransform is TranslateTransform t ? new Point(t.X, t.Y) : default;

    private void Flip(Dictionary<MissionCard, Point> before)
    {
        UpdateLayout();
        foreach (var (card, old) in before)
        {
            if (card.Parent is null || card.TranslatePoint(default, this) is not { } now) continue;
            var dx = old.X - now.X;
            var dy = old.Y - now.Y;
            if (Math.Abs(dx) < .5 && Math.Abs(dy) < .5) continue;
            var shift = card.RenderTransform as TranslateTransform ?? new TranslateTransform();
            shift.Transitions = null;
            shift.X = dx;
            shift.Y = dy;
            card.RenderTransform = shift;
            var ease = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1);
            shift.Transitions =
            [
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = ease },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = ease },
            ];
            shift.X = 0;
            shift.Y = 0;
        }
    }
}
