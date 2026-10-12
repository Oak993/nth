using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-10): the scheduler over a manual clock, a memory store and a fake launcher. The clock starts at 06:00 UTC on
/// Tuesday 2026-10-13. Helpers advance time in whole steps so every timer between fires in order.
/// </summary>
public sealed class SchedulerRig : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 6, 0, 0, TimeSpan.Zero);

    public SchedulerRig(DateTimeOffset? start = null) => Clock = new ManualTimeProvider(start ?? Start);

    public ManualTimeProvider Clock { get; }

    public MemoryDutyStore Store { get; } = new();

    public FakeDutyLauncher Launcher { get; } = new();

    public List<DutyFeedLine> Feed { get; } = [];

    public List<string> Faults { get; } = [];

    public bool MayFire { get; set; } = true;

    public bool Locked { get; set; }

    public Func<string, bool>? FolderGranted { get; set; }

    public FakeFolderEvents? FolderEvents { get; set; }

    public WebWatcher? Web { get; set; }

    public string[] Assistants { get; set; } = ["dextra"];

    public AssistantScheduler? Scheduler { get; private set; }

    public int Changes { get; private set; }

    public List<(Duty Duty, bool First)> Added { get; } = [];

    /// <summary>Builds the scheduler over what the store holds now and starts it.</summary>
    public AssistantScheduler Begin(Func<AssistantSchedulerOptions, AssistantSchedulerOptions>? tune = null)
    {
        var options = new AssistantSchedulerOptions
        {
            Store = Store,
            Launcher = Launcher,
            AssistantIds = () => Assistants,
            Time = Clock,
            MayFire = () => MayFire,
            DesktopLocked = () => Locked,
            FolderGranted = FolderGranted,
            FolderSource = FolderEvents,
            Web = Web,
            Feed = line =>
            {
                lock (Feed) Feed.Add(line);
            },
            Faulted = message =>
            {
                lock (Faults) Faults.Add(message);
            },
        };
        Scheduler = new AssistantScheduler(tune?.Invoke(options) ?? options);
        Scheduler.Changed += () => Changes++;
        Scheduler.DutyAdded += (duty, first) => Added.Add((duty, first));
        Scheduler.Start();
        return Scheduler;
    }

    /// <summary>
    /// Missions end at once after each timer, as a quick duty's would. A test that needs a run that goes on turns it off, or ends the
    /// mission itself.
    /// </summary>
    public bool AutoEnd { get; set; } = true;

    public void Advance(TimeSpan by) => Clock.Advance(by, AutoEnd ? EndRunning : null);

    public void AdvanceTo(DateTimeOffset moment) => Advance(moment - Clock.GetUtcNow());

    private void EndRunning()
    {
        if (Scheduler is null) return;
        foreach (var view in Scheduler.Duties.Where(v => v.Running).ToList())
            if (view.Duty.History.Count > 0 && view.Duty.History[0].MissionId is { } mission && view.Duty.History[0].Outcome == DutyRunOutcome.Started)
                Scheduler.MissionEnded(mission, DutyRunOutcome.Done, "ok");
    }

    public static Duty Daily(string id, int hour, int minute = 0, string zone = "UTC") =>
        new(id, "Duty " + id, "dextra", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(hour, minute)), zone), "Do " + id + ".") { Since = Start };

    public static Duty Every(string id, int minutes) =>
        new(id, "Duty " + id, "dextra", new TimeTrigger(TimeSchedule.Every(minutes), "UTC"), "Do " + id + ".") { Since = Start };

    public DateTimeOffset Now => Clock.GetUtcNow();

    public void Dispose() => Scheduler?.Dispose();
}
