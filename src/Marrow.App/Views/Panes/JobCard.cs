using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

// PORT-ONLY: web surfaces W5, one task's card in Background tasks (E:/Droppy/work/design/web-surfaces/index.html .tcard;
// DESIGN.md 3.10 "Card (glass, no glass on glass: cards are tinted fills inside the pane): title, kind, elapsed (live, tabular
// digits), tokens and tool uses (live), current activity with a shining label, and actions: View transcript, Pause, Stop
// (square). Dev servers show their URL and Open preview. Downloads show a progress line"; ED-NEW-web-w5-1). No Swift
// counterpart. Every part is made once and only shown, hidden, or given new words, so a task changing state never rebuilds the
// card (a focused Stop button stays focused) and a tick only draws.

namespace Marrow.App.Views.Panes;

/// <summary>What a card's buttons do. The pane supplies them; the card never knows a model.</summary>
public sealed record JobCardCommands(
    Action<BackgroundJob> Stop,
    Action<BackgroundJob> Pause,
    Action<BackgroundJob> Resume,
    Action<BackgroundJob> ViewTranscript,
    Action<BackgroundJob> OpenPreview);

/// <summary>A task's card.</summary>
public sealed class JobCard : Border
{
    private readonly JobCardCommands commands;
    private readonly TextBlock title = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeight.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly LiveText kind = new() { FontSize = 11.5 };
    private readonly LiveText elapsed = new() { FontSize = 11.5, Reserve = BackgroundJobText.ElapsedReserve };
    private readonly LiveText tokens = new() { FontSize = 11.5, Reserve = BackgroundJobText.TokensReserve };
    private readonly LiveText tools = new() { FontSize = 11.5, Reserve = BackgroundJobText.ToolUsesReserve };
    private readonly LiveText percent = new() { FontSize = 11.5, Reserve = BackgroundJobText.PercentReserve };
    private readonly LiveText ended = new() { FontSize = 11.5, Reserve = "Stopped 59 min ago" };
    private readonly NthShineText activity = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    private readonly MetricsRow metrics = new();
    private readonly ProgressLine progress = new() { Margin = new Thickness(0, 3, 0, 0) };
    private readonly Button pause;
    private readonly Button stop;
    private readonly JobGlyph pauseGlyph = new() { Kind = JobGlyphKind.Pause };
    private readonly JobGlyph stopGlyph = new() { Kind = JobGlyphKind.Stop };
    private readonly Button transcript;
    private readonly Button preview;
    private readonly List<TextBlock> actionLabels = [];
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 2, 0, 0) };
    private BackgroundJob? job;
    private bool finishedLook;

    public JobCard(BackgroundJob first, DateTimeOffset now, JobCardCommands commands)
    {
        ArgumentNullException.ThrowIfNull(first);
        this.commands = commands;
        JobId = first.Id;
        CornerRadius = new CornerRadius(JobTokens.CardRadius);
        BorderThickness = new Thickness(0.5);
        Padding = new Thickness(12, 9, 10, 9);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ClipToBounds = false;

        title.Foreground = PaneTokens.Text1.Brush(this);
        foreach (var live in new[] { kind, elapsed, tokens, tools, percent, ended }) live.Foreground = PaneTokens.Text2.Brush(this);
        kind.Foreground = PaneTokens.Text3.Brush(this);
        activity.Foreground = PaneTokens.Text2.Brush(this);
        pauseGlyph.Foreground = PaneTokens.Text2.Brush(this);
        stopGlyph.Foreground = PaneTokens.Text2.Brush(this);

        (pause, _) = Op(pauseGlyph, () => { if (job is { } now) { if (now.IsPaused) commands.Resume(now); else commands.Pause(now); } });
        (stop, _) = Op(stopGlyph, () => { if (job is { } now) commands.Stop(now); });
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top, Children = { pause, stop } };

        transcript = TextAction("View transcript", () => { if (job is { } now) commands.ViewTranscript(now); });
        preview = TextAction("Open preview", () => { if (job is { } now) commands.OpenPreview(now); });
        actions.Children.Add(transcript);
        actions.Children.Add(preview);

        foreach (var child in new Control[] { elapsed, tokens, tools, percent, ended, activity }) metrics.Children.Add(child);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), RowSpacing = JobTokens.RowGap, ColumnSpacing = 8 };
        Grid.SetRow(title, 0);
        Grid.SetRow(ops, 0);
        Grid.SetRowSpan(ops, 2);
        Grid.SetColumn(ops, 1);
        Grid.SetRow(kind, 1);
        Grid.SetRow(metrics, 2);
        Grid.SetColumnSpan(metrics, 2);
        Grid.SetRow(progress, 3);
        Grid.SetColumnSpan(progress, 2);
        Grid.SetRow(actions, 4);
        Grid.SetColumnSpan(actions, 2);
        foreach (var child in new Control[] { title, ops, kind, metrics, progress, actions }) grid.Children.Add(child);
        Child = grid;
        AutomationProperties.SetAutomationId(this, "job-" + first.Id);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.ListItem);
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        Apply(first, now);
    }

    /// <summary>The id of the job the card shows.</summary>
    public string JobId { get; }

    /// <summary>The job as last applied.</summary>
    public BackgroundJob Job => job!;

    public Button PauseButton => pause;

    public Button StopButton => stop;

    public Button TranscriptButton => transcript;

    public Button PreviewButton => preview;

    public TextBlock TitleBlock => title;

    public LiveText ElapsedText => elapsed;

    public LiveText TokensText => tokens;

    public LiveText ToolsText => tools;

    public LiveText PercentText => percent;

    public LiveText EndedText => ended;

    public LiveText KindText => kind;

    public NthShineText ActivityText => activity;

    public ProgressLine ProgressBar => progress;

    public MetricsRow MetricsLine => metrics;

    /// <summary>The card shows a job that has ended: a fainter tint and one line of when.</summary>
    public bool IsFinishedLook => finishedLook;

    /// <summary>Shows <paramref name="next"/> as of <paramref name="now"/>: a different shape shows and hides parts, the same shape only gives the live parts new words.</summary>
    public void Apply(BackgroundJob next, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(next);
        var reshape = job is null || !next.SameShape(job);
        job = next;
        if (reshape) Reshape(next);
        Refresh(next, now);
    }

    private void Reshape(BackgroundJob next)
    {
        var finished = next.IsFinished;
        if (finishedLook != finished || Background is null)
        {
            finishedLook = finished;
            ApplyLook();
        }
        title.Text = next.Title;
        ToolTip.SetTip(title, next.Title); // a title the card trims reads in full here
        elapsed.IsVisible = !finished;
        tokens.IsVisible = !finished && next.Tokens is not null;
        tools.IsVisible = !finished && next.ToolUses is not null;
        percent.IsVisible = !finished && next.Progress is not null;
        activity.IsVisible = !finished && (next.Activity is not null || next.State != BackgroundJobState.Running);
        ended.IsVisible = finished;
        progress.IsVisible = !finished && next.Progress is not null;

        var canTogglePause = next.IsLive && (next.CanPause || next.IsPaused);
        pause.IsVisible = canTogglePause;
        pause.IsEnabled = next.State is BackgroundJobState.Running or BackgroundJobState.Paused;
        pauseGlyph.Kind = next.IsPaused ? JobGlyphKind.Play : JobGlyphKind.Pause;
        SetName(pause, next.IsPaused ? "Resume" : "Pause", next.Title);

        stop.IsVisible = next.IsLive && (next.CanStop || next.State == BackgroundJobState.Stopping);
        stop.IsEnabled = next.CanStop && next.State != BackgroundJobState.Stopping;
        SetName(stop, "Stop", next.Title);

        transcript.IsVisible = next.HasTranscript;
        preview.IsVisible = next.IsDevServer && next.IsLive;
        actions.IsVisible = transcript.IsVisible || preview.IsVisible;
        SetName(transcript, "View transcript", next.Title);
        SetName(preview, "Open preview", next.Title);

        AutomationProperties.SetName(this, next.Title + ", " + next.KindLabel);
        AutomationProperties.SetHelpText(this, StateWord(next.State));
        ApplyLook();
    }

    private void Refresh(BackgroundJob next, DateTimeOffset now)
    {
        Set(kind, next.KindText);
        if (next.IsFinished)
        {
            Set(ended, BackgroundJobText.EndedAgo(next.State, next.Age(now)) ?? "");
            return;
        }
        Set(elapsed, BackgroundJobText.Elapsed(next.Elapsed(now)));
        if (next.Tokens is { } spent) Set(tokens, BackgroundJobText.Tokens(spent));
        if (next.ToolUses is { } uses) Set(tools, BackgroundJobText.ToolUses(uses));
        if (next.Progress is { } fraction)
        {
            Set(percent, BackgroundJobText.Percent(fraction));
            if (progress.Value != fraction) progress.Value = fraction;
        }
        var words = next.State switch
        {
            BackgroundJobState.Paused => "Paused",
            BackgroundJobState.Pausing => "Pausing\u2026",
            BackgroundJobState.Stopping => "Stopping\u2026",
            _ => next.Activity ?? "",
        };
        if (activity.Text != words) activity.Text = words;
        var shining = next.State == BackgroundJobState.Running && next.Activity is not null;
        if (activity.IsShining != shining) activity.IsShining = shining;
    }

    private static void Set(LiveText text, string value)
    {
        if (text.Text != value) text.Text = value;
    }

    private static string StateWord(BackgroundJobState state) => state switch
    {
        BackgroundJobState.Running => "Running",
        BackgroundJobState.Pausing => "Pausing",
        BackgroundJobState.Paused => "Paused",
        BackgroundJobState.Stopping => "Stopping",
        BackgroundJobState.Done => "Done",
        BackgroundJobState.Failed => "Failed",
        _ => "Stopped",
    };

    private static void SetName(Button button, string verb, string subject)
    {
        AutomationProperties.SetName(button, BackgroundJobText.ActionName(verb, subject));
        ToolTip.SetTip(button, verb);
    }

    private void ApplyLook()
    {
        var fill = finishedLook ? JobTokens.DoneFill : JobTokens.Fill;
        Background = fill.Brush(this);
        BorderBrush = JobTokens.Rim.Brush(this);
        // A finished card recedes by its tint, not by dimming its words: they stay at 4.5:1 (DESIGN 10).
        title.Foreground = (finishedLook ? PaneTokens.Text2 : PaneTokens.Text1).Brush(this);
        // The text actions are the accent, lifted to read at 4.5:1 on the card.
        var action = new SolidColorBrush(JobTokens.ActionColor(ActualThemeVariant));
        foreach (var label in actionLabels) label.Foreground = action;
    }

    private (Button Button, Border Face) Op(JobGlyph glyph, Action invoke)
    {
        var face = new Border
        {
            Width = JobTokens.ButtonSize,
            Height = JobTokens.ButtonSize,
            CornerRadius = new CornerRadius(JobTokens.ButtonRadius),
            BorderThickness = new Thickness(1),
            Child = glyph,
            Background = Brushes.Transparent,
        };
        face.BorderBrush = JobTokens.Outline.Brush(face);
        var button = PlainControls.Button(face, invoke).KeyboardFocus(JobTokens.ButtonRadius);
        button.OnHover(on =>
        {
            face.Background = on && button.IsEnabled ? JobTokens.Hover.Brush(face) : Brushes.Transparent;
            glyph.Foreground = (on && button.IsEnabled ? PaneTokens.Text1 : PaneTokens.Text2).Brush(glyph);
        });
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEnabledProperty) face.Opacity = button.IsEnabled ? 1 : 0.45;
        };
        return (button, face);
    }

    private Button TextAction(string text, Action invoke)
    {
        var label = new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        actionLabels.Add(label);
        var face = new Border { Height = 20, Child = label, Background = Brushes.Transparent, CornerRadius = new CornerRadius(5) };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(5);
        button.OnHover(on => label.TextDecorations = on ? Avalonia.Media.TextDecorations.Underline : null);
        return button;
    }
}
