using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W5, the Background tasks pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1, 3.10; the approved
// canvas index.html "background tasks": .tasks, .tfilter, .tcard; ED-NEW-web-w5-1). No Swift counterpart. One list of every
// job the app runs in the background (Hydra heads, helpers, Tactus streams, Visus agent tabs, terminals and dev servers,
// downloads, workflows), live, with Stop and Pause on the cards that can, the filter "This thread / All threads", "Clear done"
// in the header, and "View transcript" opening the Transcript pane. The list is Core's BackgroundJobs; this file only draws it.
// The pane holds a lease on the list while it is on screen, so the 4 Hz ticker runs only then.

namespace Marrow.App.Views.Panes;

/// <summary>The Background tasks pane's registration and the places it reaches out to.</summary>
public static class BackgroundTasksPane
{
    /// <summary>The filter last chosen, kept for the session (a new pane opens on it).</summary>
    public static BackgroundJobScope Scope { get; set; } = BackgroundJobScope.ThisThread;

    /// <summary>
    /// What "Open preview" does with a dev server's address. Visus's project preview (W9) takes this over; until then the
    /// address opens in the default browser, as links from chats do today.
    /// </summary>
    public static Action<ThreadRuntime, string> OpenPreview { get; set; } = (_, url) =>
    {
        if (SwiftURL.Create(url) is { } address) Marrow.Services.Port.Workspace.Open.Current(address);
    };

    /// <summary>Registers the pane's content with the workspace (called once at start-up).</summary>
    public static void Register() => PaneContents.Register(PaneKind.BackgroundTasks, Create);

    /// <summary>The pane's content for a thread, or null when the thread's runtime has no app (a test runtime).</summary>
    public static PaneContent? Create(PaneKind kind, ThreadRuntime runtime)
    {
        if (runtime.App is not { } app) return null;
        return Create(app.BackgroundJobs, runtime);
    }

    /// <summary>The pane's content over any list of jobs (tests and the assistant's board use this).</summary>
    public static PaneContent Create(BackgroundJobs jobs, ThreadRuntime runtime)
    {
        var commands = new JobCardCommands(
            Stop: job => jobs.Stop(job.Id),
            Pause: job => jobs.Pause(job.Id),
            Resume: job => jobs.Resume(job.Id),
            ViewTranscript: job => TranscriptPane.Show(runtime, job),
            OpenPreview: job =>
            {
                if (job.DevServerUrl is { } url) OpenPreview(runtime, url);
            });
        var view = new BackgroundTasksView(jobs, runtime.ThreadID, commands, Scope);
        var content = new PaneContent(view);
        var clearDone = UiText.Get("jobs.clear_done");
        var clear = new PaneHeaderAction(clearDone, view.ClearDone, Text: clearDone);
        view.HasFinishedChanged += has => content.Update(null, null, has ? [clear] : []);
        return content;
    }
}

/// <summary>The pane's body: the filter, the cards, and the empty word.</summary>
public sealed class BackgroundTasksView : Grid
{
    private readonly BackgroundJobs jobs;
    private readonly Guid? thread;
    private readonly JobStack stack;
    private readonly StackPanel empty;
    private readonly ToggleButton thisThread;
    private readonly ToggleButton allThreads;
    private IDisposable? lease;
    private BackgroundJobScope scope;
    private bool hasFinished;
    private bool rendered;

    public BackgroundTasksView(BackgroundJobs jobs, Guid? thread, JobCardCommands commands, BackgroundJobScope scope)
    {
        this.jobs = jobs;
        this.thread = thread;
        this.scope = scope;
        stack = new JobStack(commands);

        thisThread = Segment(UiText.Get("jobs.filter.this_thread"), BackgroundJobScope.ThisThread);
        allThreads = Segment(UiText.Get("jobs.filter.all_threads"), BackgroundJobScope.AllThreads);
        var strip = new Border
        {
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { thisThread, allThreads } },
        };
        strip.Background = PaneTokens.HoverFill.Opacity(0.6).Brush(strip);
        strip.BorderBrush = PaneTokens.Line.Brush(strip);
        AutomationProperties.SetName(strip, UiText.Get("jobs.filter.group_name"));
        strip.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Group);
        strip.Margin = new Thickness(0, 2, 2, 4);

        var column = new StackPanel { Margin = new Thickness(JobTokens.ListPadding), Spacing = 0, Children = { strip, stack } };
        var scroll = new ScrollViewer
        {
            Content = column,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(stack, UiText.Get("jobs.list_name"));
        stack.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.List);

        var title = new TextBlock { Text = UiText.Get("jobs.empty.title"), FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        title.Foreground = PaneTokens.Text2.Brush(title);
        var hint = new TextBlock
        {
            Text = UiText.Get("jobs.empty.body"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.Foreground = PaneTokens.Text3.Brush(hint);
        empty = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(16, 40, 16, 0), Children = { title, hint } };

        Children.Add(scroll);
        Children.Add(empty);
        ShowScope();
        AttachedToVisualTree += (_, _) =>
        {
            if (lease is not null) return;
            lease = jobs.Attach();
            jobs.Ticked += OnTicked;
            Render();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            jobs.Ticked -= OnTicked;
            lease?.Dispose();
            lease = null;
        };
    }

    /// <summary>The list of cards.</summary>
    public JobStack Stack => stack;

    public bool HasFinished => hasFinished;

    public ToggleButton ThisThreadButton => thisThread;

    public ToggleButton AllThreadsButton => allThreads;

    /// <summary>The empty word is showing.</summary>
    public bool IsEmptyShown => empty.IsVisible;

    /// <summary>Which jobs are listed.</summary>
    public BackgroundJobScope Scope
    {
        get => scope;
        set
        {
            if (scope == value) return;
            scope = value;
            BackgroundTasksPane.Scope = value;
            ShowScope();
            Render();
        }
    }

    /// <summary>Raised when the list gains its first finished job or loses its last ("Clear done" then shows in the header or goes).</summary>
    public event Action<bool>? HasFinishedChanged;

    /// <summary>Clears the finished jobs of the list shown.</summary>
    public void ClearDone()
    {
        jobs.ClearDone(thread, scope);
        Render();
    }

    private void OnTicked() => Render();

    /// <summary>Draws the list as the jobs stand now (the ticker calls it four times a second).</summary>
    public void Render()
    {
        var list = jobs.For(thread, scope);
        stack.Sync(list, jobs.RefreshedAt, animateChanges: rendered);
        rendered = true;
        var isEmpty = list.Count == 0;
        if (empty.IsVisible != isEmpty) empty.IsVisible = isEmpty;
        var finished = list.Any(job => job.IsFinished);
        if (finished != hasFinished)
        {
            hasFinished = finished;
            HasFinishedChanged?.Invoke(finished);
        }
    }

    private void ShowScope()
    {
        thisThread.IsChecked = scope == BackgroundJobScope.ThisThread;
        allThreads.IsChecked = scope == BackgroundJobScope.AllThreads;
    }

    // The canvas's .seg button, scaled to 0.9: 26 DIP high, 11.5 text, the chosen one filled.
    private ToggleButton Segment(string text, BackgroundJobScope value)
    {
        var label = new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border { Height = 26, Padding = new Thickness(11, 0), CornerRadius = new CornerRadius(8), Child = label, Background = Brushes.Transparent };
        var button = new ToggleButton { Content = face, Padding = default, Template = SegmentTemplate };
        void Look()
        {
            var on = button.IsChecked == true;
            face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent;
            label.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(label);
            label.FontWeight = on ? FontWeight.Medium : FontWeight.Normal;
        }
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) Look();
        };
        button.Click += (_, _) =>
        {
            Scope = value;
            ShowScope(); // a click on the chosen segment keeps it chosen
        };
        Look();
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static readonly FuncControlTemplate<ToggleButton> SegmentTemplate = new((owner, scope) =>
    {
        var content = new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] };
        content.RegisterInNameScope(scope);
        var ring = new Border
        {
            Margin = new Thickness(-2),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            IsVisible = false,
            IsHitTestVisible = false,
            BorderBrush = Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Brush(owner),
        };
        AutomationProperties.SetAccessibilityView(ring, AccessibilityView.Raw);
        owner.Classes.CollectionChanged += (_, _) => ring.IsVisible = owner.Classes.Contains(":focus-visible");
        return new Panel { Children = { content, ring } };
    });
}
