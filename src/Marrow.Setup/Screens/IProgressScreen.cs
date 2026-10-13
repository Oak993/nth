using Marrow.Setup.Install;
using Marrow.Setup.Motion;

namespace Marrow.Setup.Screens;

/// <summary>
/// What the progress engine draws into (nth installer PLAN.md 4; unit U3, ED-NEW-inst-u3-1): the step rows, the bar, the
/// percentage and the line under the title. Installing implements it, and Update in its updating state, on the canvas's
/// approved rows and bar B (<see cref="CanvasProgress"/>). Every call is on the window's thread.
/// </summary>
public interface IProgressView
{
    /// <summary>The bar's drawn value, 0 to 100 (the screen draws it through a transform).</summary>
    double BarValue { get; }

    /// <summary>Puts the view's motions (the bar, the stall line's cross-fade, the byte count's fade) on the window's frame loop.</summary>
    void Attach(Animator animator);

    /// <summary>A run starts on this screen: the rows for its steps are laid out at once, before anything moves.</summary>
    void Begin(ProgressFrame first);

    /// <summary>A new frame: the rows' states and words, the byte count, the line under the title and the bar's target.</summary>
    void Render(ProgressFrame frame);
}

/// <summary>A screen that carries a progress view (Installing; Update while it updates).</summary>
public interface IProgressScreen : ISetupScreen
{
    IProgressView Progress { get; }
}
