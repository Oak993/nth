using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-19: the teach controller over a throwaway store, a decision queue, a stop chord on a fake host, the hook, UI Automation and the
/// screen as fakes, an own-skill service in memory, a draft model that answers as the test says, and a clock a test moves. Helper code only (R-TEST-3).
/// </summary>
internal sealed class TeachControllerRig : IDisposable
{
    private readonly TeachRig services = new();

    public TeachControllerRig(ITeachDraftModel? model = null, bool executor = false, bool skills = true)
    {
        Queue = new DecisionQueue(Clock);
        Host = new FakeStopChordHost();
        Stop = new GlobalStop(Host, () => null, Clock);
        Skills = new TeachSkillStore();
        TrialHost = new FakeTrialHost();
        Executor = executor ? new FakeExecutor(TrialHost) : null;
        Controller = new TeachController(new TeachControllerOptions
        {
            Store = Store,
            AssistantId = TeachRig.Assistant,
            Platform = () => new TeachPlatform(Input = new FakeTeachInput(), Probe, Frames),
            Queue = Queue,
            Model = model,
            StopKey = Stop,
            Skills = skills ? Skills.Sources : null,
            OwnSkills = () => Skills.Sources.List(CustomAddKind.Skills),
            SkillsChanged = () => SkillsChanges++,
            Executor = Executor,
            Time = Clock,
            Audit = entry =>
            {
                lock (Audited) Audited.Add(entry);
            },
            Faulted = message =>
            {
                lock (Faults) Faults.Add(message);
            },
        });
        Controller.Changed += () => Changes++;
    }

    public ManualTimeProvider Clock => services.Clock;

    public AssistantStore Store => services.Store;

    public TeachRecordings Recordings => services.Recordings;

    public DecisionQueue Queue { get; }

    public FakeStopChordHost Host { get; }

    public GlobalStop Stop { get; }

    public TeachSkillStore Skills { get; }

    public FakeTrialHost TrialHost { get; }

    public FakeExecutor? Executor { get; }

    public TeachController Controller { get; }

    public FakeTeachInput? Input { get; private set; }

    public FakeProbe Probe { get; } = new();

    public FakeFrames Frames { get; } = new();

    public List<AuditEntry> Audited { get; } = [];

    public List<string> Faults { get; } = [];

    public int Changes { get; private set; }

    public int SkillsChanges { get; private set; }

    public string TeachRoot => services.TeachRoot;

    /// <summary>Records a short mail: open a new message, type the address and the subject, press Send. Returns the recording's id after it ended.</summary>
    public async Task<string> RecordMail(bool password = false)
    {
        var started = Controller.Start();
        Assert.True(started.Started, started.Refusal);
        Probe.Under[(1, 1)] = new TeachElement("outlook", "Inbox - Outlook", "button", "New mail");
        Probe.Under[(2, 2)] = new TeachElement("outlook", "New message - Outlook", "edit", "To");
        Probe.Under[(3, 3)] = new TeachElement("outlook", "New message - Outlook", "button", "Send");
        Frames.Nothing = true;
        var recorder = Controller.Active!;
        Clock.Advance(TimeSpan.FromSeconds(2));
        Input!.Click(1, 1);
        await recorder.Flush();
        Clock.Advance(TimeSpan.FromSeconds(3));
        Input.Click(2, 2);
        await recorder.Flush();
        Probe.Focus = new TeachFocus(new TeachElement("outlook", "New message - Outlook", "edit", "To"), "sam@example.com");
        Clock.Advance(TimeSpan.FromSeconds(1));
        Input.Type(15);
        await recorder.Flush();
        if (password)
        {
            Probe.Focus = new TeachFocus(new TeachElement("outlook", "Sign in", "edit", "Password") { IsPassword = true }, "hunter2");
            Clock.Advance(TimeSpan.FromSeconds(2));
            Input.Type(7);
            await recorder.Flush();
        }
        Clock.Advance(TimeSpan.FromSeconds(5));
        Input.Click(3, 3);
        await recorder.Flush();
        var ended = await Controller.Stop();
        return ended!.Id;
    }

    /// <summary>Waits until a condition on the controller holds, looking again each time the controller says it changed.</summary>
    public async Task Until(Func<bool> condition)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check()
        {
            if (condition()) reached.TrySetResult();
        }
        Controller.Changed += Check;
        try
        {
            Check();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            Controller.Changed -= Check;
        }
    }

    /// <summary>A hash of every file under the teach folder: the same before and after means nothing was written.</summary>
    public string Snapshot()
    {
        if (!Directory.Exists(TeachRoot)) return "";
        var lines = Directory.EnumerateFiles(TeachRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(TeachRoot, p) + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
        return string.Join('\n', lines);
    }

    public void Dispose()
    {
        Controller.Dispose();
        Stop.Dispose();
        services.Dispose();
    }
}
