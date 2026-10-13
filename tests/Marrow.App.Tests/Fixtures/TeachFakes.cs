using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-19: the hook, as a test drives it: nothing is delivered while it is not installed, as with the real one.</summary>
internal sealed class FakeTeachInput : ITeachInput
{
    private Action<TeachInput>? sink;

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public Exception? FailStart { get; set; }

    public bool Installed => sink is not null;

    public void Start(Action<TeachInput> onInput)
    {
        if (FailStart is { } failure) throw failure;
        Starts++;
        sink = onInput;
    }

    /// <summary>A burst of typing that had not ended when the hooks came off; handed back by the next Stop.</summary>
    public TeachInput? Tail { get; set; }

    public TeachInput? Stop()
    {
        Stops++;
        sink = null;
        var tail = Tail;
        Tail = null;
        return tail;
    }

    public void Send(TeachInput input) => sink?.Invoke(input);

    public void Click(int x, int y, string verb = "click") => Send(new TeachInput.Click(x, y, verb));

    public void Type(int count) => Send(new TeachInput.Typing(count));

    public void TypeStarted() => Send(new TeachInput.TypingStarted());

    public void Chord(string text) => Send(new TeachInput.Chord(text));

    public void Dispose()
    {
    }
}

/// <summary>Dextra AS-19: UI Automation as a test sets it: what is under each point, what is in focus, what is in front.</summary>
internal sealed class FakeProbe : IElementProbe
{
    public Dictionary<(int X, int Y), TeachElement> Under { get; } = [];

    public TeachFocus? Focus { get; set; }

    public TeachElement? Front { get; set; } = new("notepad", "Untitled - Notepad", "", "");

    public TeachElement? At(int x, int y) => Under.GetValueOrDefault((x, y));

    public TeachFocus? Focused() => Focus;

    public TeachElement? Foreground() => Front;
}

/// <summary>Dextra AS-19: the screen as a test sets it: a new picture every call unless it is told to repeat one.</summary>
internal sealed class FakeFrames : IFrameSource
{
    public int Calls { get; private set; }

    public byte[]? Fixed { get; set; }

    public bool Nothing { get; set; }

    public int Size { get; set; } = 40;

    public byte[]? Capture()
    {
        Calls++;
        if (Nothing) return null;
        return Fixed ?? [.. Enumerable.Range(0, Size).Select(i => (byte)((i + Calls) % 251))];
    }
}

/// <summary>Dextra AS-19: the narration as a test drives it: it says when it was started, paused, resumed and stopped, and hears what the test gives it.</summary>
internal sealed class FakeNarration : ITeachNarration
{
    public List<string> Calls { get; } = [];

    public string? Refusal { get; set; }

    public Func<TimeSpan>? Clock { get; set; }

    public Action<TeachNarrationLine>? Heard { get; set; }

    public Action<string>? Noted { get; set; }

    public string? Start()
    {
        Calls.Add("start");
        return Refusal;
    }

    public void Pause() => Calls.Add("pause");

    public void Resume() => Calls.Add("resume");

    public Task StopAsync()
    {
        Calls.Add("stop");
        return Task.CompletedTask;
    }

    /// <summary>The owner says a line at the recording's time now.</summary>
    public void Say(string text, double seconds = 2) => Heard!(new TeachNarrationLine(Clock!(), Clock!() + TimeSpan.FromSeconds(seconds), text));
}

/// <summary>Dextra AS-19: the browser steps of a Visus tab, as a test raises them.</summary>
internal sealed class FakeBrowser : IBrowserSteps
{
    private Action<TeachBrowserStep>? sink;

    public bool Subscribed => sink is not null;

    public IDisposable Subscribe(Action<TeachBrowserStep> onStep)
    {
        sink = onStep;
        return new Release(this);
    }

    public void Raise(TeachBrowserStep step) => sink?.Invoke(step);

    private sealed class Release(FakeBrowser owner) : IDisposable
    {
        public void Dispose() => owner.sink = null;
    }
}

/// <summary>Dextra AS-19: the recording strip as a test sees it: whether it went up, whether it came down, and whether it can be shown at all.</summary>
internal sealed class FakeIndicator : ITeachIndicator
{
    public int Shown { get; private set; }

    public int Hidden { get; private set; }

    public bool CannotShow { get; set; }

    public bool Up { get; private set; }

    public TeachController? For { get; private set; }

    /// <summary>Called as the strip goes up, before anything is watched.</summary>
    public Action? OnShow { get; set; }

    public bool Show(TeachController controller)
    {
        OnShow?.Invoke();
        if (CannotShow) return false;
        Shown++;
        Up = true;
        For = controller;
        return true;
    }

    public void Hide()
    {
        Hidden++;
        Up = false;
    }
}
