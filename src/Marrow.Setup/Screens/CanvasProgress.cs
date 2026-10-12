using Marrow.Setup.Install;
using Marrow.Setup.Motion;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4; the canvas's Installing and Update; units U2 and U3, bound at integration,
/// ED-NEW-inst-integ-1). The progress engine's <see cref="IProgressView"/> on the approved pieces: the getting-ready rows
/// (<see cref="StepList"/>: ring, tile-trace spinner, tick) and bar B (<see cref="ProgressBarB"/>: liquid silver with its
/// light, critically damped, stiffness 110, never going back). A frame sets each row's state (the glyphs cross-fade in
/// 0.2 s, the label's ink follows), the byte count beside Copying, a changed label (the Adding step follows the shortcut
/// choice) and the bar's target; the stall line goes through the screen's own reserved subtitle.
/// </summary>
public sealed class CanvasProgress : IProgressView
{
    private readonly Action<IReadOnlyList<StepSpec>> begin;
    private readonly Action<bool> slow;
    private bool wasSlow;

    /// <param name="steps">The screen's rows.</param>
    /// <param name="bar">The screen's bar.</param>
    /// <param name="begin">Lays the run's rows out and shows them (Installing already does; Update's hidden rows and bar appear).</param>
    /// <param name="slow">Shows or takes away the stall line under the title.</param>
    public CanvasProgress(StepList steps, ProgressBarB bar, Action<IReadOnlyList<StepSpec>> begin, Action<bool> slow)
    {
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        Bar = bar ?? throw new ArgumentNullException(nameof(bar));
        this.begin = begin ?? throw new ArgumentNullException(nameof(begin));
        this.slow = slow ?? throw new ArgumentNullException(nameof(slow));
    }

    public StepList Steps { get; }

    public ProgressBarB Bar { get; }

    /// <summary>The bar's drawn value (0 to 100).</summary>
    public double BarValue => Bar.Shown >= 99.6 && Bar.Target >= 100 ? 100 : Bar.Shown;

    /// <summary>The percentage beside the bar, as written.</summary>
    public string PercentText => Bar.PercentText;

    /// <summary>The rows' states as the engine names them.</summary>
    public IReadOnlyList<Install.StepState> RowStates => [.. Steps.Rows.Select(row => Of(row.State))];

    public void Attach(Animator animator)
    {
        ArgumentNullException.ThrowIfNull(animator);
        Steps.Attach(animator);
        Bar.Attach(animator);
    }

    public void Begin(ProgressFrame first)
    {
        ArgumentNullException.ThrowIfNull(first);
        wasSlow = false;
        begin([.. first.Steps.Select(step => new StepSpec(step.Label, step.DetailLongest))]);
        Bar.Reset();
        Render(first);
    }

    public void Render(ProgressFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Steps.Rows.Count != frame.Steps.Count) begin([.. frame.Steps.Select(step => new StepSpec(step.Label, step.DetailLongest))]);
        for (var i = 0; i < frame.Steps.Count; i++)
        {
            var view = frame.Steps[i];
            var row = Steps.Rows[i];
            if (!string.Equals(row.Label, view.Label, StringComparison.Ordinal)) row.SetLabel(view.Label);
            if (view.Detail is { } detail && !string.Equals(row.Detail, detail, StringComparison.Ordinal)) row.SetDetail(detail);
            row.State = To(view.State);
        }
        if (frame.Slow != wasSlow)
        {
            wasSlow = frame.Slow;
            slow(frame.Slow);
        }
        Bar.SetTarget(frame.Target);
    }

    private static Ui.StepState To(Install.StepState state) => state switch
    {
        Install.StepState.Working => Ui.StepState.Working,
        Install.StepState.Done => Ui.StepState.Done,
        _ => Ui.StepState.Waiting,
    };

    private static Install.StepState Of(Ui.StepState state) => state switch
    {
        Ui.StepState.Working => Install.StepState.Working,
        Ui.StepState.Done => Install.StepState.Done,
        _ => Install.StepState.Waiting,
    };
}
