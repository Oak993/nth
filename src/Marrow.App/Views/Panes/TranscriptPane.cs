using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W5, the Transcript pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1 "Transcript | narrow | grid |
// 'View transcript' from Background tasks", 3.10 "View transcript opens the job's transcript in a Transcript pane (the head's own
// Runtime(id).Entries)"; ED-NEW-web-w5-1). No Swift counterpart. It reads another chat's timeline beside the thread you are in:
// the same rows the chat draws, with no composer, so it never sends anything. One pane per thread: showing another job's
// transcript swaps what it reads.

namespace Marrow.App.Views.Panes;

/// <summary>Which chat a thread's Transcript pane reads (not saved: a restored pane asks to choose again).</summary>
public sealed class TranscriptTarget
{
    public Guid? ThreadId { get; private set; }

    public string? Title { get; private set; }

    /// <summary>Raised when the target changes.</summary>
    public event Action? Changed;

    public void Set(Guid threadId, string title)
    {
        if (ThreadId == threadId && Title == title) return;
        ThreadId = threadId;
        Title = title;
        Changed?.Invoke();
    }
}

/// <summary>The Transcript pane: registration, the target per thread, and showing a job's transcript.</summary>
public static class TranscriptPane
{
    private static readonly ConditionalWeakTable<ThreadRuntime, TranscriptTarget> Targets = [];

    /// <summary>Registers the pane's content with the workspace (called once at start-up).</summary>
    public static void Register() => PaneContents.Register(PaneKind.Transcript, Create);

    /// <summary>The chat the host thread's Transcript pane reads.</summary>
    public static TranscriptTarget TargetOf(ThreadRuntime host) => Targets.GetValue(host, _ => new TranscriptTarget());

    /// <summary>Opens the Transcript pane of <paramref name="host"/> on the transcript of <paramref name="job"/> (a no-op for a job with none).</summary>
    public static void Show(ThreadRuntime host, BackgroundJob job)
    {
        if (job.TranscriptThreadId is { } thread) Show(host, thread, job.Title);
    }

    public static void Show(ThreadRuntime host, Guid transcriptThread, string title)
    {
        TargetOf(host).Set(transcriptThread, title);
        host.Workspace = host.Workspace.Open(PaneKind.Transcript);
    }

    /// <summary>The pane's content for a thread.</summary>
    public static PaneContent? Create(PaneKind kind, ThreadRuntime host)
    {
        if (host.App is not { } app) return null;
        var target = TargetOf(host);
        var view = new TranscriptView(app, target);
        var content = new PaneContent(view);
        void Header()
        {
            if (target.ThreadId is { } thread && target.Title is { } title)
            {
                var open = new PaneHeaderAction("Open chat", () => app.SelectedThreadID = thread, Text: "Open chat");
                content.Update(title, "Transcript", [open]);
            }
            else content.Update(null, null, []);
        }
        // Subscribed while the pane is on screen, so a closed pane leaves nothing behind.
        view.AttachedToVisualTree += (_, _) =>
        {
            target.Changed += Header;
            Header();
        };
        view.DetachedFromVisualTree += (_, _) => target.Changed -= Header;
        Header();
        return content;
    }
}

/// <summary>The pane's body: a chat's timeline, read only.</summary>
public sealed class TranscriptView : Border
{
    private readonly AppModel app;
    private readonly TranscriptTarget target;
    private Guid? shown;

    public TranscriptView(AppModel app, TranscriptTarget target)
    {
        this.app = app;
        this.target = target;
        AttachedToVisualTree += (_, _) =>
        {
            target.Changed += Show;
            Show();
        };
        DetachedFromVisualTree += (_, _) => target.Changed -= Show;
        Show();
    }

    /// <summary>The timeline drawn now, or null while nothing is chosen.</summary>
    public ThreadTimeline? Timeline => (Child as Grid)?.Children.OfType<ThreadTimeline>().FirstOrDefault();

    /// <summary>The chat read, if any.</summary>
    public Guid? ShownThread => shown;

    private void Show()
    {
        if (target.ThreadId is not { } thread || app.Thread(thread) is not { } chat)
        {
            shown = null;
            Child = Empty();
            return;
        }
        if (shown == thread && Child is Grid) return;
        shown = thread;
        var runtime = app.Runtime(thread);
        var project = app.Project(chat.ProjectID);
        var scrollChrome = new ChromeScrollModel();
        var scrollState = new TimelineScrollState();
        var timeline = new ThreadTimeline(runtime, scrollChrome, scrollState, project?.Name, chat.WorktreePath ?? project?.Path,
            supportsRewind: false, columnHeight: 480, showsMinimap: false);
        var jump = new JumpToLatestButton(scrollState) { VerticalAlignment = VerticalAlignment.Bottom };
        var grid = new Grid();
        grid.Children.Add(timeline);
        grid.Children.Add(new PaneTopVeil(scrollChrome));
        grid.Children.Add(jump);
        Child = grid;
    }

    private static Control Empty()
    {
        var title = new TextBlock { Text = "No transcript chosen", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        title.Foreground = PaneTokens.Text2.Brush(title);
        var hint = new TextBlock
        {
            Text = "Choose View transcript on a task in Background tasks to read it here.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.Foreground = PaneTokens.Text3.Brush(hint);
        return new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { title, hint } };
    }
}
