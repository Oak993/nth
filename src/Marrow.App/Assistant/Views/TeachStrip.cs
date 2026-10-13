// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 12.6, 22 "Armilla"; unit AS-19; ED-NEW-dextra-as19-8, -10). No Swift twin.
// The recording strip: a small glass pill at the top of the screen that says a recording runs, for how long, and has Pause and Stop. It is
// the Tactus pill's place and look until Tactus's pill lands (16.2). The state is a word and a shape (a filled dot while recording, a hollow ring
// while paused), never colour alone; the time has a fixed width so nothing shifts as it counts; the time left shows in the last five minutes;
// there is no animation, so Reduce motion has nothing to change. The window it sits in takes no focus (what the owner types must go to the app
// he is showing) and is left out of every frame; the stop chord and Stop all reach the recording without it.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>What the strip says, from the recording's own status. Pure but for the controller it reads.</summary>
public sealed class TeachStripModel : IDisposable
{
    private readonly TeachController controller;
    private readonly object gate = new();
    private string signature = "";
    private bool disposed;

    public TeachStripModel(TeachController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        controller.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Raised when what the strip shows changed (any thread).</summary>
    public event Action? Changed;

    public bool Starting { get; private set; } = true;

    public bool Paused { get; private set; }

    public bool Ending { get; private set; }

    /// <summary>"Recording", "Paused", "Starting" or "Keeping the recording".</summary>
    public string StateWord { get; private set; } = "Starting";

    /// <summary>"2:14", or "4:12 left" in the last five minutes.</summary>
    public string Time { get; private set; } = "0:00";

    public string PauseLabel => Paused ? TeachViewWords.ResumeTitle : TeachViewWords.PauseTitle;

    public IReadOnlyList<string> Notes { get; private set; } = [];

    /// <summary>The strip said aloud: "Recording, 2 minutes 14 seconds".</summary>
    public string Spoken { get; private set; } = "Starting";

    /// <summary>Whether Pause and Stop do anything now (not while starting or ending).</summary>
    public bool CanAct => !Starting && !Ending;

    public void TogglePause()
    {
        if (!CanAct) return;
        if (Paused) controller.Resume();
        else controller.Pause();
    }

    public void Stop()
    {
        if (!CanAct) return;
        _ = controller.Stop();
    }

    /// <summary>Reads the recording again. Called when the controller says it changed, and a few times a second while the strip is up.</summary>
    public void Refresh()
    {
        if (disposed) return;
        var recorder = controller.Active;
        var status = recorder?.Status;
        var changed = false;
        lock (gate)
        {
            if (status is null)
            {
                // Not recording any more but the strip is not taken away yet (the manifest is being written), or not recording yet.
                var wasRecording = !Starting;
                Starting = !wasRecording;
                Ending = wasRecording;
                Paused = false;
                StateWord = wasRecording ? "Keeping the recording" : "Starting";
                Spoken = StateWord;
                Notes = [];
            }
            else
            {
                Starting = false;
                Ending = false;
                Paused = status.State == TeachRecordingState.Paused;
                StateWord = Paused ? "Paused" : "Recording";
                Time = status.CountingDown ? TeachWords.Clock(status.Remaining) + " left" : TeachWords.Clock(status.Elapsed);
                Spoken = StateWord + ", " + SpokenTime(status.CountingDown ? status.Remaining : status.Elapsed) + (status.CountingDown ? " left" : "");
                Notes = status.Notes;
            }
            var now = string.Join('|', StateWord, Time, string.Join('/', Notes));
            if (now != signature)
            {
                signature = now;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>"2 minutes 14 seconds", "1 minute", "45 seconds": a time as it is said.</summary>
    public static string SpokenTime(TimeSpan time)
    {
        var whole = (long)Math.Max(0, Math.Floor(time.TotalSeconds));
        var hours = whole / 3600;
        var minutes = whole / 60 % 60;
        var seconds = whole % 60;
        var parts = new List<string>();
        if (hours > 0) parts.Add($"{hours.ToString(CultureInfo.InvariantCulture)} {(hours == 1 ? "hour" : "hours")}");
        if (minutes > 0) parts.Add($"{minutes.ToString(CultureInfo.InvariantCulture)} {(minutes == 1 ? "minute" : "minutes")}");
        if (seconds > 0 || parts.Count == 0) parts.Add($"{seconds.ToString(CultureInfo.InvariantCulture)} {(seconds == 1 ? "second" : "seconds")}");
        return string.Join(' ', parts);
    }

    private void OnChanged() => Refresh();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        controller.Changed -= OnChanged;
    }
}

/// <summary>The pill. It draws a <see cref="TeachStripModel"/> and holds nothing else.</summary>
public sealed class TeachStrip : Decorator
{
    /// <summary>The width the time keeps, so counting never moves the buttons.</summary>
    public const double TimeWidth = 92;

    private readonly TeachStripModel model;
    private readonly Ellipse dot = new() { Width = 10, Height = 10 };
    private readonly LiveStatusText state = new() { MinWidth = 78 };
    private readonly TextBlock time = new() { MinWidth = TimeWidth, TextAlignment = TextAlignment.Right };
    private readonly StackPanel notes = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    private readonly TextBlock? pauseLabel;
    private DispatcherTimer? clock;

    public TeachStrip(TeachStripModel model)
    {
        this.model = model;
        SwiftFont.System(13, SwiftFont.Weight.Semibold).Apply(state);
        state.Foreground = Chrome.PrimaryText.Brush(state);
        SwiftFont.System(13, SwiftFont.Weight.Medium).Apply(time);
        time.Foreground = Chrome.PrimaryText.Brush(time);
        time.FontFeatures = [FontFeature.Parse("tnum")];
        AutomationProperties.SetAccessibilityView(time, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);

        var pause = PlanCard.Capsule(TeachViewWords.ResumeTitle, model.TogglePause, "Pause recording");
        PauseButton = pause.Button;
        // The button's own label is the one that changes between Pause and Resume; the face keeps the width of the longer word.
        if (PauseButton.Content is Border { Child: TextBlock label } face)
        {
            pauseLabel = label;
            face.MinWidth = 72;
            label.TextAlignment = TextAlignment.Center;
        }
        var stop = PlanCard.Capsule("Stop", model.Stop, "Stop recording and keep it");
        StopButton = stop.Button;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { dot, state, time, pause.Shown, stop.Shown },
        };
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Children = { row, notes } };
        var pill = new Border { Child = column, Padding = new Thickness(14, 8) };
        pill.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        pill.Background = Chrome.Overlay(0.10).Brush(pill);
        Child = pill;
        AutomationProperties.SetName(pill, "Teach recording");

        AttachedToVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            model.Changed += OnChanged;
            clock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => model.Refresh());
            clock.Start();
            Redraw();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            clock?.Stop();
            clock = null;
        };
        Redraw();
    }

    public TeachStripModel Model => model;

    public Button PauseButton { get; }

    public Button StopButton { get; }

    public string StateText => state.Text ?? "";

    public string TimeText => time.Text ?? "";

    public IReadOnlyList<string> NoteTexts => [.. notes.Children.OfType<TextBlock>().Select(t => t.Text ?? "")];

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Redraw();
        else Dispatcher.UIThread.Post(Redraw);
    }

    private void Redraw()
    {
        state.Text = model.StateWord;
        AutomationProperties.SetName(state, model.Spoken);
        time.Text = model.Time;
        if (pauseLabel is not null) pauseLabel.Text = model.PauseLabel;
        AutomationProperties.SetName(PauseButton, model.Paused ? "Resume recording" : "Pause recording");
        PauseButton.IsEnabled = model.CanAct;
        StopButton.IsEnabled = model.CanAct;
        // A filled dot while it records, a hollow ring while it waits: the shape says it as well as the word.
        dot.Fill = model.Paused || model.Starting || model.Ending ? Brushes.Transparent : Chrome.Danger.Brush(dot);
        dot.Stroke = Chrome.Danger.Brush(dot);
        dot.StrokeThickness = 2;
        notes.Children.Clear();
        foreach (var note in model.Notes)
        {
            var line = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(line);
            line.Foreground = Chrome.SecondaryText.Brush(line);
            notes.Children.Add(line);
        }
    }
}
