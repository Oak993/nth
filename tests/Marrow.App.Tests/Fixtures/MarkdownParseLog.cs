namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The tests' end of MarkdownView.ParseObserver: every text the markdown parser is asked to parse, with the thread that is parsing it, in the order they began.
/// A parse on any thread but the UI thread can be held (<see cref="Hold"/>) until the test lets it go, so that a test decides what has happened before the parse
/// lands (a pill that has appeared but whose warm has not finished; a tap in that window).
/// </summary>
internal sealed class MarkdownParseLog(int uiThread) : IDisposable
{
    private readonly object gate = new();
    private readonly List<(string Text, int Thread)> parses = [];
    private readonly ManualResetEventSlim release = new(true);

    public int UiThread { get; } = uiThread;

    /// <summary>What the parser was asked, in order, with the managed thread id it ran on.</summary>
    public IReadOnlyList<(string Text, int Thread)> Parses { get { lock (gate) return [.. parses]; } }

    public IReadOnlyList<string> Texts => [.. Parses.Select(parse => parse.Text)];

    public IReadOnlyList<string> OnUiThread => [.. Parses.Where(parse => parse.Thread == UiThread).Select(parse => parse.Text)];

    public IReadOnlyList<string> OffUiThread => [.. Parses.Where(parse => parse.Thread != UiThread).Select(parse => parse.Text)];

    /// <summary>The observer: called by the parser on the thread that parses, before it parses.</summary>
    public void Record(string text)
    {
        lock (gate) parses.Add((text, Environment.CurrentManagedThreadId));
        // Never held on the UI thread (it would never be released), and never for ever.
        if (Environment.CurrentManagedThreadId != UiThread) release.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>From now on a parse off the UI thread waits, after it has been recorded, until <see cref="Release"/>.</summary>
    public void Hold() => release.Reset();

    public void Release() => release.Set();

    public void Dispose()
    {
        release.Set();
    }
}
