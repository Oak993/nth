using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.Setup.Flow;
using Marrow.Setup.Motion;
using Marrow.Setup.Shell;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>The product's own names (owner, 2026-10-11: the publisher is "oak993", lower case, everywhere).</summary>
public static class SetupProduct
{
    public const string Publisher = "oak993";
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 3b, the approved canvas's screens; unit U2, ED-NEW-inst-u2-1). Builds the
/// canvas's screens: Welcome, Options, Installing, Ready, Update, Up to date and Newer, Problem and Uninstall.
/// </summary>
public sealed class CanvasScreens : ISetupScreens
{
    public ISetupScreen Create(SetupScreen screen, SetupSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return screen switch
        {
            SetupScreen.Welcome => new WelcomeScreen(session),
            SetupScreen.Options => new OptionsScreen(session),
            SetupScreen.Installing => new InstallingScreen(session),
            SetupScreen.Ready => new ReadyScreen(session),
            SetupScreen.Update => new UpdateScreen(session),
            SetupScreen.UpToDate or SetupScreen.NewerInstalled => new CurrentScreen(session, screen),
            SetupScreen.Problem => new ProblemScreen(session),
            SetupScreen.Uninstall => new UninstallScreen(session),
            _ => throw new ArgumentOutOfRangeException(nameof(screen)),
        };
    }
}

/// <summary>
/// One of the canvas's screens: a <see cref="ScreenColumn"/> (padding 14 40 26, centred lines, flexible spaces), its
/// <see cref="Ui.Reveal"/> for every change made while it is shown, its cues on the window's frame loop, and Enter (the
/// default button) and Esc. It is set up in its final state at once each time it is entered, before it fades in.
/// </summary>
public abstract class CanvasScreen : ISetupScreen, IKeyedScreen
{
    protected CanvasScreen(SetupScreen kind, SetupSession session, string name)
    {
        Kind = kind;
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Calm = () => session.Window.Settings.Current.Calm;
        Column = new ScreenColumn();
        Root = new Border { Child = Column, Background = Avalonia.Media.Brushes.Transparent };
        AutomationProperties.SetName(Root, name);
        Reveal = new Reveal(Root, Column, Calm);
        Reveal.Attach(Frames);
        Cues.Attach(Frames);
    }

    public SetupScreen Kind { get; }

    public Control View => Root;

    /// <summary>The Close-paused line; it takes its place from the space above it, so nothing above it moves.</summary>
    public HoldLine? Hold
    {
        get => hold;
        protected set
        {
            hold = value;
            if (value is null) return;
            ScreenColumn.SetFromSpaceAbove(value, true);
            value.AtCommit = Reveal.AtCommit;
        }
    }

    private HoldLine? hold;

    public SignatureLine? Signature { get; protected set; }

    public Reveal Reveal { get; }

    public ScreenColumn Column { get; }

    public Cues Cues { get; } = new();

    /// <summary>The button Enter presses (the primary one; Cancel on Uninstall).</summary>
    public SetupButton? DefaultButton { get; protected set; }

    protected Border Root { get; }

    protected SetupSession Session { get; }

    protected Func<bool> Calm { get; }

    protected Animator Frames => Session.Window.Animator;

    public void Entered(SetupScreen? from)
    {
        Cues.Clear();
        Reveal.Reset();
        Reveal.Now(() => Setup(from));
        // a control of the screen that left keeps no focus (Enter and Space would reach it)
        if (Session.Window.FocusManager?.GetFocusedElement() is Visual focused && !Root.IsVisualAncestorOf(focused) && !ReferenceEquals(focused, Root))
            Session.Window.FocusManager.Focus(null);
        AfterSetup(from);
    }

    public virtual void Left() => Cues.Clear();

    /// <summary>Puts the screen in its state for now (inside <see cref="Reveal.Now"/>: no motion).</summary>
    protected abstract void Setup(SetupScreen? from);

    /// <summary>What starts once the screen is set up (the mark's pop-in, Ready's sequence).</summary>
    protected virtual void AfterSetup(SetupScreen? from) { }

    public virtual bool OnKey(Key key)
    {
        if (key == Key.Enter)
        {
            if (DefaultButton is not { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } button) return false;
            button.Invoke();
            return true;
        }
        if (key != Key.Escape) return false;
        Escape();
        return true;
    }

    /// <summary>Esc: close the window (refused, with the line brightening, while Close is paused).</summary>
    protected virtual void Escape() => Session.Close();

    protected SetupButton Button(string text, ButtonKind kind, Action click, bool wide = false, bool small = false)
    {
        var button = new SetupButton(text, kind, click, small, wide);
        button.Attach(Frames);
        return button;
    }

    protected ButtonRow Buttons(params SetupButton[] buttons)
    {
        var row = new ButtonRow();
        foreach (var button in buttons) row.Children.Add(button);
        Reveal.Track(row);
        return row;
    }

    protected SetupMark Mark(double size, bool drifts = false)
    {
        var mark = new SetupMark(size, Calm, drifts);
        mark.Attach(Frames);
        return mark;
    }

    protected static Grow Space(double minimum = 10) => new() { MinHeight = minimum };

    /// <summary>A line of fine print with a link at its end ("… · Options"), centred, wrapping like text.</summary>
    protected static TextBlock FineWithLink(LinkButton link, double top, params Inline[] before)
    {
        var line = ScreenText.Fine("", top);
        var inlines = ScreenText.Inlines(line);
        foreach (var inline in before) inlines.Add(inline);
        inlines.Add(new Run(" " + SetupStrings.Dot + " "));
        inlines.Add(ScreenText.Inline(link));
        return line;
    }

    /// <summary>The space kept between the last line and the buttons (PLAN.md 10: at least 16 DIP at any text size).</summary>
    public const double ButtonGap = 16;

    /// <summary>The setup's exit code if this screen closes the window now.</summary>
    protected int ExitCode => SetupFlow.ExitCodeWhenClosed(Kind);
}
