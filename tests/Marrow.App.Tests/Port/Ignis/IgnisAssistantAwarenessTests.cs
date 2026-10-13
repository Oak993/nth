using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Ignis;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Ignis"; ED-NEW-dextra-as15-7): Ignis knows Dextra's chats. It compacts only between tasks; a mission's chat
// is mid-task until the mission is done, so it is left alone (no keep-warm, no tidy-up, not even by hand); Dextra's home thread is the owner's own
// conversation, so it is never kept warm (a cold cache costs one slower first reply) but is still tidied between tasks. Any other chat is as before.
// The controller on a manual clock, with the engine, points, ledger and runtime as fakes; no provider is reached.
[Trait("Decision", "ED-NEW-dextra-as15-7")]
[Trait("Feature", "AS-15")]
public sealed class IgnisAssistantAwarenessTests : IDisposable
{
    private readonly TestRuntime main = new();
    private readonly FakeIgnisPoints points = new();
    private readonly FakeSavingsLedger ledger = new();
    private readonly FakeIgnisEngine engine = new();
    private readonly FakeIgnisEnvironment environment = new();
    private readonly FakeIgnisPort port = new();
    private readonly IgnisController controller;

    public IgnisAssistantAwarenessTests()
    {
        var settings = new IgnisSettings(main.Defaults);
        controller = main.OnMain(() => new IgnisController(settings, points, ledger, engine, environment));
    }

    public void Dispose() => main.Dispose();

    private Guid Thread => port.ThreadID;

    private void Start(IgnisTurnUsage? usage = null) => main.OnMain(() =>
    {
        controller.Attach(port);
        controller.Observe(Thread, usage ?? IgnisSamples.Usage(read: 40_000, write1h: 2_000));
        controller.TurnFinished(Thread);
    });

    private IgnisChatStatus Status => main.OnMain(() => controller.Status(Thread));

    private static IgnisTurnUsage OverTheTidyPoint() => IgnisSamples.Usage(read: 150_000, write1h: 10_000, context: 160_000);   // the fake's point is 150K

    [Fact]
    public void A_chat_that_belongs_to_no_assistant_is_kept_warm_and_tidied_as_it_always_was()
    {
        Start();
        Assert.True(Status.CanKeepWarm);
        Assert.Equal(IgnisPause.None, Status.Pause);
        main.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);

        var tidy = new FakeIgnisPort();
        main.OnMain(() =>
        {
            controller.Attach(tidy);
            controller.Observe(tidy.ThreadID, OverTheTidyPoint());
            controller.TurnFinished(tidy.ThreadID);
        });
        Assert.Equal([12], tidy.Tidies);
    }

    [Fact]
    public void Dextras_home_thread_is_never_kept_warm_by_a_ping_or_silently_whatever_the_odds()
    {
        environment.Uses[Thread] = IgnisAssistantUse.HomeThread;
        environment.Draft = true;                                   // the owner is typing: the odds he is back are as good as they get
        port.CanKeepWarmSilently = true;
        Start();
        Assert.False(Status.CanKeepWarm);
        Assert.Equal(IgnisPause.NotSupported, Status.Pause);        // "no keep-warm here", the state a model without keep-warm is in

        main.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, port.Pings);
        Assert.Equal(0, port.SilentKeepWarms);
        Assert.Empty(engine.Questions);                             // the engine is not even asked whether it would pay
        main.OnMain(() => controller.KeepWarmNow(Thread));          // nor does the button (the popover hides it) do anything
        Assert.Equal(0, port.Pings);
        Assert.Equal(0, port.SilentKeepWarms);
        main.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, port.Pings);
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);           // the cache went cold, which only costs one slower first reply
    }

    [Fact]
    public void Dextras_home_thread_is_still_tidied_between_tasks_and_never_while_a_task_runs()
    {
        environment.Uses[Thread] = IgnisAssistantUse.HomeThread;
        port.IsBetweenTasks = false;
        Start(OverTheTidyPoint());
        Assert.Empty(port.Tidies);                                  // Ignis compacts only between tasks
        main.Advance(IgnisController.BusyRetry);
        Assert.Empty(port.Tidies);
        Assert.False(Status.CanTidy);

        port.IsBetweenTasks = true;
        main.Advance(IgnisController.BusyRetry);
        Assert.Equal([12], port.Tidies);
        Assert.Equal(0, port.Pings);
    }

    [Fact]
    public void A_missions_chat_is_left_alone_mid_task_no_timer_no_ping_no_tidy_and_the_buttons_do_nothing()
    {
        environment.Uses[Thread] = IgnisAssistantUse.MissionMidTask;
        Start(OverTheTidyPoint());                                  // over the tidy point, and warm for an hour
        Assert.Empty(port.Tidies);
        Assert.False(Status.CanTidy);
        Assert.False(Status.CanKeepWarm);
        main.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, port.Pings);
        Assert.Empty(port.Tidies);
        Assert.Empty(engine.Questions);
        main.OnMain(() =>
        {
            controller.TidyUpNow(Thread);
            controller.KeepWarmNow(Thread);
        });
        Assert.Empty(port.Tidies);
        Assert.Equal(0, port.Pings);
        Assert.Equal(IgnisPause.NotSupported, Status.Pause);
    }

    [Fact]
    public void Once_its_mission_is_done_a_missions_chat_is_an_ordinary_chat_again()
    {
        environment.Uses[Thread] = IgnisAssistantUse.MissionMidTask;
        Start(OverTheTidyPoint());
        main.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty(port.Tidies);

        environment.Uses[Thread] = IgnisAssistantUse.None;          // the mission is Done
        main.OnMain(() => controller.TurnFinished(Thread));         // the chat's next turn end plans it again
        Assert.Equal([12], port.Tidies);
        Assert.False(Status.CanTidy);                               // it is tidying now
    }

    [Fact]
    public void Ignis_turned_off_in_a_chat_stays_off_whatever_the_chat_is()
    {
        environment.Uses[Thread] = IgnisAssistantUse.HomeThread;
        main.OnMain(() => controller.SetMode(Thread, IgnisMode.Off));
        Start(OverTheTidyPoint());
        main.Advance(TimeSpan.FromHours(3));
        Assert.Equal(0, port.Pings);
        Assert.Empty(port.Tidies);
        Assert.Equal(IgnisNext.Off, Status.Next);
    }

    [Fact]
    public void What_the_popover_says_of_these_chats_is_true_the_home_thread_is_not_kept_warm_and_a_task_s_chat_is_left_alone()
    {
        environment.Uses[Thread] = IgnisAssistantUse.HomeThread;
        main.OnMain(() => controller.SetMode(Thread, IgnisMode.KeepWarm));
        Start();
        Assert.Equal(IgnisNext.HomeThread, Status.Next);
        main.OnMain(() => controller.SetMode(Thread, IgnisMode.WarmAndTidy));
        Assert.Equal(IgnisNext.HomeThreadTidy, Status.Next);
        main.Advance(TimeSpan.FromHours(2));                         // however cold it goes, the line does not promise a warm-up
        Assert.Equal(IgnisNext.HomeThreadTidy, Status.Next);

        environment.Uses[Thread] = IgnisAssistantUse.MissionMidTask;
        Assert.Equal(IgnisNext.MissionRunning, Status.Next);
        environment.Uses[Thread] = IgnisAssistantUse.None;
        Assert.NotEqual(IgnisNext.MissionRunning, Status.Next);       // an ordinary chat is read as it always was
        Assert.NotEqual(IgnisNext.HomeThreadTidy, Status.Next);
    }
}
