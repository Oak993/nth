using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Install;
using Marrow.Setup.Motion;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4; the canvas's Installing section; unit U3, ED-NEW-inst-u3-1). The progress engine's
/// rows, bar and percentage for the interim Installing and Update screens. Real here: the line under the title is a
/// <see cref="ReservedText"/> (its taller wording's height is kept, the words cross-fade), the byte count beside Copying is a
/// <see cref="ByteCount"/> (its longest form's width is kept, and a label that wraps at a large text size grows its row),
/// the bar follows <see cref="ProgressMotion"/> and is drawn through a transform, the percentage is the value rounded
/// down, and the progress value is exposed to Narrator. STUB(U2): the rows' glyphs and the bar's look stand in for the
/// approved getting-ready rows (ring, spinner B, tick) and bar B with its light, which unit U2 brings from the app.
/// </summary>
public sealed class ProgressPanel : StackPanel, IProgressView
{
    private readonly Func<bool> calm;
    private readonly StackPanel rows = new() { Spacing = 2 };
    private readonly List<StepRowView> rowViews = [];
    private readonly Border fill;
    private readonly ScaleTransform fillScale = new(0, 1);
    private readonly TextBlock percent;
    private readonly ProgressBar value = new() { Minimum = 0, Maximum = 100, Opacity = 0, IsHitTestVisible = false, Height = 4 };
    private Animator? animator;

    public ProgressPanel(Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        Bar = new ProgressMotion(calm);
        Subtitle = new ReservedText(TextRole.Body, SetupTheme.Ink2, calm, [SetupStrings.InstallingSubtitle, SetupStrings.InstallingSlow], TextAlignment.Left)
        {
            Margin = new Thickness(0, 8, 0, 0),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        MaxWidth = 420;
        HorizontalAlignment = HorizontalAlignment.Left;
        rows.Margin = new Thickness(0, 22, 0, 0);
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.22 };
        track.Bind(Border.BackgroundProperty, track.GetResourceObservable(SetupTheme.Ink3));
        fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, RenderTransform = fillScale, RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative) };
        fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable(SetupTheme.Ink1));
        percent = SetupTheme.Text(new TextBlock { Width = 38, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = SetupStrings.ProgressPercent(0) }, TextRole.Numbers, SetupTheme.Ink2);
        AutomationProperties.SetAccessibilityView(percent, AccessibilityView.Raw);
        AutomationProperties.SetName(value, SetupStrings.InstallingTitle);
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), Margin = new Thickness(0, 18, 0, 0) };
        var cell = new Grid { Children = { track, fill, value } };
        bar.Children.Add(cell);
        Grid.SetColumn(percent, 2);
        bar.Children.Add(percent);
        Children.Add(rows);
        Children.Add(bar);
        Bar.Changed += DrawBar;
    }

    /// <summary>The line under the title; the screen places it under its title.</summary>
    public ReservedText Subtitle { get; }

    public ProgressMotion Bar { get; }

    public double BarValue => Bar.Value;

    /// <summary>The rows drawn now (tests read them).</summary>
    public IReadOnlyList<StepRowView> Rows => rowViews;

    /// <summary>The percentage shown beside the bar.</summary>
    public string? PercentText => percent.Text;

    /// <summary>The bar fill's drawn scale (0 to 1).</summary>
    public double FillScale => fillScale.ScaleX;

    public void Attach(Animator frames)
    {
        animator = frames ?? throw new ArgumentNullException(nameof(frames));
        Bar.Attach(frames);
        Subtitle.Attach(frames);
        foreach (var row in rowViews) row.Attach(frames);
    }

    public void Begin(ProgressFrame first)
    {
        ArgumentNullException.ThrowIfNull(first);
        IsVisible = true;
        rows.Children.Clear();
        rowViews.Clear();
        foreach (var _ in first.Steps)
        {
            var row = new StepRowView(calm);
            if (animator is not null) row.Attach(animator);
            rowViews.Add(row);
            rows.Children.Add(row);
        }
        for (var i = 0; i < first.Steps.Count; i++) rowViews[i].Now(first.Steps[i]);
        Subtitle.Now(first.Subtitle);
        Bar.Now(first.Target);
    }

    public void Render(ProgressFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (rowViews.Count != frame.Steps.Count) Begin(frame);
        for (var i = 0; i < frame.Steps.Count; i++) rowViews[i].Set(frame.Steps[i]);
        Subtitle.Set(frame.Subtitle);
        Bar.SetTarget(frame.Target);
    }

    private void DrawBar()
    {
        fillScale.ScaleX = Bar.Value / 100;
        percent.Text = SetupStrings.ProgressPercent(Bar.Percent);
        value.Value = Bar.Percent;
    }
}

/// <summary>
/// One step row: a 16 DIP slot, the label (wrapping at a large text size, which grows the row), and the byte count beside
/// Copying. The label's ink follows the state (waiting: hint ink, working: first ink, done: second ink, the canvas's
/// <c>.steps li</c>); the slot cross-fades its glyphs in 0.2 s. STUB(U2): the glyphs stand in for the approved rows.
/// </summary>
public sealed class StepRowView : Grid, IMotion
{
    private readonly Ellipse ring = new() { Width = 8, Height = 8, StrokeThickness = 1.5 };
    private readonly Arc arc = new() { Width = 14, Height = 14, StrokeThickness = 1.5, StartAngle = -90, SweepAngle = 270 };
    private readonly Ellipse disc = new() { Width = 14, Height = 14 };
    private readonly Tween ringFade = new(1);
    private readonly Tween arcFade = new(0);
    private readonly Tween discFade = new(0);
    private readonly TextBlock label;
    private readonly Func<bool> calm;
    private Animator? animator;

    public StepRowView(Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        ColumnDefinitions = new ColumnDefinitions("16,12,*,Auto");
        MinHeight = 34;
        ring.Bind(Shape.StrokeProperty, ring.GetResourceObservable(SetupTheme.Ink3));
        arc.Bind(Shape.StrokeProperty, arc.GetResourceObservable(SetupTheme.Ink1));
        disc.Bind(Shape.FillProperty, disc.GetResourceObservable(SetupTheme.Ink2));
        var slot = new Panel { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Children = { ring, arc, disc } };
        foreach (var glyph in new Control[] { ring, arc, disc })
        {
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
        }
        AutomationProperties.SetAccessibilityView(slot, AccessibilityView.Raw);
        label = SetupTheme.Text(new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, TextRole.Row, ink: null);
        Count = new ByteCount(calm) { Margin = new Thickness(8, 0, 0, 0) };
        Children.Add(slot);
        Grid.SetColumn(label, 2);
        Children.Add(label);
        Grid.SetColumn(Count, 3);
        Children.Add(Count);
        Draw();
    }

    public StepView? Step { get; private set; }

    public string? Label => label.Text;

    public ByteCount Count { get; }

    public void Attach(Animator frames)
    {
        animator = frames;
        Count.Attach(frames);
    }

    /// <summary>The row as <paramref name="step"/> says, at once.</summary>
    public void Now(StepView step)
    {
        Apply(step);
        ringFade.Snap(step.State == Install.StepState.Waiting ? 1 : 0);
        arcFade.Snap(step.State == Install.StepState.Working ? 1 : 0);
        discFade.Snap(step.State == Install.StepState.Done ? 1 : 0);
        Draw();
    }

    /// <summary>The row as <paramref name="step"/> says, the glyphs cross-fading (0.2 s; Calm 0.15 s).</summary>
    public void Set(StepView step)
    {
        var changed = Step?.State != step.State;
        Apply(step);
        if (!changed) return;
        var seconds = calm() ? 0.15 : 0.2;
        ringFade.Retarget(step.State == Install.StepState.Waiting ? 1 : 0, seconds, CubicBezier.Ease);
        arcFade.Retarget(step.State == Install.StepState.Working ? 1 : 0, seconds, CubicBezier.Ease);
        discFade.Retarget(step.State == Install.StepState.Done ? 1 : 0, seconds, CubicBezier.Ease);
        if (animator is null)
        {
            Now(step);
            return;
        }
        animator.Start(this);
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = ringFade.Advance(seconds) | arcFade.Advance(seconds) | discFade.Advance(seconds);
        Draw();
        return moving;
    }

    private void Apply(StepView step)
    {
        Step = step;
        label.Text = step.Label;
        var ink = step.State switch { Install.StepState.Working => SetupTheme.Ink1, Install.StepState.Done => SetupTheme.Ink2, _ => SetupTheme.Ink3 };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(ink));
        Count.Set(step.Detail, step.DetailLongest, step.DetailShown);
        AutomationProperties.SetName(this, step.Detail is not null && step.DetailShown ? step.Label + ", " + step.Detail : step.Label);
    }

    private void Draw()
    {
        ring.Opacity = ringFade.Value;
        arc.Opacity = arcFade.Value;
        disc.Opacity = discFade.Value;
    }
}
