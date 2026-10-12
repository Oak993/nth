using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Flow;
using Marrow.Setup.Motion;
using Marrow.Setup.Shell;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>Uninstall's states: asked, removing, removed.</summary>
public enum UninstallState
{
    Ask,
    Removing,
    Removed,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, 3b and 7, the canvas's Uninstall; unit U2, ED-NEW-inst-u2-1; unit U6 builds the
/// mode). Cancel is the default (it has the focus; Enter and Esc cancel) and Uninstall is red. The mark (64 DIP, still),
/// "Uninstall nth?", one glass row "Also remove my chats and settings" with the Nocta switch (off by default), and under
/// it the warning "Your chats and settings will be deleted too. This can't be undone." (a polite live region). The warning
/// reveals: turning the switch on glides the block up with the switch and fades the line in under it; turning it off fades
/// the line first, then the block glides back; Cancel and Uninstall never move (the window keeps the warning's height). In
/// Calm its place is kept and it only fades. Removing: the row, the warning and both buttons leave, an indeterminate bar
/// and the Close-paused line arrive, the title and subtitle change. Removed: the bar leaves, the mark dims to 35 % and
/// settles to 94 % (0.6 s), and Cancel comes back as the primary Close. Unit U6 (ED-NEW-inst-u6-1, <see cref="UninstallFlow"/>)
/// answers <see cref="UninstallRequested"/>, calls <see cref="ShowRemoving"/> and <see cref="ShowRemoved"/>, and after Problem's
/// Retry enters the screen already removing (<see cref="EnterRemoving"/>).
/// </summary>
public sealed class UninstallScreen : CanvasScreen, IMotion
{
    public const double RemovedDim = .35, RemovedScale = .94, RemovedFade = .6;

    private static readonly CubicBezier SettleCurve = new(.2, .8, .2, 1);
    private readonly SetupMark mark;
    private readonly Decorator markWrap;
    private readonly ScaleTransform markScale = new(1, 1);
    private readonly Tween dim = new(1), shrink = new(1);
    private readonly TextBlock title;
    private readonly TextBlock subtitle;
    private readonly Panel warningRegion;

    public UninstallScreen(SetupSession session) : base(SetupScreen.Uninstall, session, SetupStrings.ScreenUninstall)
    {
        mark = Mark(64);
        mark.RenderTransformOrigin = RelativePoint.Center;
        mark.RenderTransform = markScale;
        markWrap = new Decorator { Child = mark, HorizontalAlignment = HorizontalAlignment.Center };
        title = ScreenText.Title(SetupStrings.UninstallTitle);
        subtitle = ScreenText.Subtitle(SetupStrings.UninstallSubtitle);
        Switch = new NoctaSwitch(false, Calm);
        Switch.Attach(Frames);
        AutomationProperties.SetName(Switch, SetupStrings.UninstallWipe);
        AutomationProperties.SetHelpText(Switch, SetupStrings.UninstallWipeHint);
        Switch.Toggled += OnWipe;
        Group = new GlassGroup().With(new OptionRow(SetupStrings.UninstallWipe, OptionRow.Hint(SetupStrings.UninstallWipeHint), Switch));
        Warning = ScreenText.Warning(SetupStrings.UninstallWarning);
        Warning.IsVisible = false;
        warningRegion = Reveal.LiveRegion(new Panel { MaxWidth = ScreenText.Column, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { Warning } });
        Reveal.Track(warningRegion);
        Bar = new ProgressBarB(Calm, indeterminate: true) { IsVisible = false };
        Bar.Attach(Frames);
        Hold = new HoldLine(Calm);
        Cancel = Button(SetupStrings.UninstallCancel, ButtonKind.Secondary, OnCancel);
        Cancel.ReserveLabels(SetupStrings.UninstallCancel, SetupStrings.UninstallClose);
        Remove = Button(SetupStrings.UninstallGo, ButtonKind.Destructive, OnUninstall);
        DefaultButton = Cancel;
        Column.With(Space(), markWrap, title, subtitle, Group, warningRegion, Bar, Space(ButtonGap), Hold, Buttons(Cancel, Remove));
        Column.AddGhost(Warning, ScreenText.Warning(SetupStrings.UninstallWarning));
        Reveal.LiveRegionChanged += region => LiveRegionChanged?.Invoke(region);
    }

    public UninstallState State { get; private set; }

    /// <summary>Also remove the chats and settings (the switch).</summary>
    public bool WipeData => Switch.IsOn;

    public NoctaSwitch Switch { get; }

    public GlassGroup Group { get; }

    public TextBlock Warning { get; }

    public Control WarningRegion => warningRegion;

    public ProgressBarB Bar { get; }

    public SetupButton Cancel { get; }

    public SetupButton Remove { get; }

    public SetupMark MarkView => mark;

    public Control MarkWrap => markWrap;

    public TextBlock TitleBlock => title;

    public TextBlock SubtitleBlock => subtitle;

    /// <summary>The mark's removed pose (opacity, scale).</summary>
    public (double Opacity, double Scale) MarkPose => (dim.Value, shrink.Value);

    /// <summary>Raised when a line arrives in a polite live region (the warning): Narrator reads it once.</summary>
    public event Action<Control>? LiveRegionChanged;

    /// <summary>Uninstall was clicked, with the switch's state (unit U6's <see cref="UninstallFlow"/> removes nth).</summary>
    public event Action<bool>? UninstallRequested;

    private bool removingOnEntry;

    /// <summary>
    /// The next time the screen is entered (Problem's Retry) it is set up already removing, as <see cref="ShowRemoving"/>
    /// leaves it, so coming back from Problem never shows the question again or reveals on arrival.
    /// </summary>
    public void EnterRemoving() => removingOnEntry = true;

    protected override void Setup(SetupScreen? from)
    {
        if (removingOnEntry)
        {
            SetupRemoving();
            return;
        }
        State = UninstallState.Ask;
        mark.Show();
        dim.Snap(1);
        shrink.Snap(1);
        Paint();
        title.Text = SetupStrings.UninstallTitle;
        subtitle.Text = SetupStrings.UninstallSubtitle;
        Group.IsVisible = true;
        Bar.IsVisible = false;
        // in Calm a hidden warning keeps its place from the start, so its first arrival only fades as well
        Reveal.Set(Warning, Switch.IsOn, reserve: true);
        Cancel.IsVisible = true;
        Cancel.SetLabelNow(SetupStrings.UninstallCancel);
        Cancel.Kind = ButtonKind.Secondary;
        Remove.IsVisible = true;
    }

    protected override void AfterSetup(SetupScreen? from)
    {
        if (removingOnEntry)
        {
            removingOnEntry = false;
            Session.PauseClose();
            return;
        }
        // Cancel has the focus, quietly (the ring comes with the keyboard); a window that is not open yet gives it as it opens
        if (Cancel.Focus(NavigationMethod.Unspecified)) return;
        void OnOpened(object? sender, EventArgs e)
        {
            Session.Window.Opened -= OnOpened;
            if (Session.Screen == SetupScreen.Uninstall) Cancel.Focus(NavigationMethod.Unspecified);
        }
        Session.Window.Opened += OnOpened;
    }

    /// <summary>The Removing state at once (inside the screen's set-up: no motion), the mark as on Ask.</summary>
    private void SetupRemoving()
    {
        State = UninstallState.Removing;
        mark.Show();
        dim.Snap(1);
        shrink.Snap(1);
        Paint();
        title.Text = SetupStrings.UninstallRemoving;
        subtitle.Text = WipeData ? SetupStrings.UninstallRemovingWipe : SetupStrings.UninstallRemovingKeep;
        Reveal.Hide(Group);
        Reveal.Hide(Warning);
        Reveal.Show(Bar);
        Bar.Run();
        Cancel.SetLabelNow(SetupStrings.UninstallCancel);
        Cancel.Kind = ButtonKind.Secondary;
        Reveal.Hide(Cancel);
        Reveal.Hide(Remove);
    }

    /// <summary>Removing: the row, the warning and the buttons leave; the bar and the paused line arrive.</summary>
    public void ShowRemoving()
    {
        State = UninstallState.Removing;
        Reveal.SetText(title, SetupStrings.UninstallRemoving);
        Reveal.SetText(subtitle, WipeData ? SetupStrings.UninstallRemovingWipe : SetupStrings.UninstallRemovingKeep);
        Reveal.Hide(Group);
        Reveal.Hide(Warning);
        Reveal.Show(Bar);
        Bar.Run();
        Reveal.Hide(Remove);
        Reveal.Hide(Cancel);
        Session.PauseClose();
    }

    /// <summary>Removed: the bar leaves, the mark dims, Cancel comes back as the primary Close.</summary>
    public void ShowRemoved()
    {
        State = UninstallState.Removed;
        // Close works again: the paused line leaves (the canvas's setClose(true))
        Session.ResumeClose();
        Hold?.Hide();
        Reveal.Hide(Bar);
        Reveal.SetText(title, SetupStrings.UninstallRemoved);
        Reveal.SetText(subtitle, WipeData ? SetupStrings.UninstallRemovedWipe : SetupStrings.UninstallRemovedKeep);
        var calm = Calm();
        dim.Retarget(RemovedDim, calm ? .2 : RemovedFade, CubicBezier.Ease);
        shrink.Retarget(calm ? 1 : RemovedScale, calm ? 0 : RemovedFade, SettleCurve);
        Frames.Start(this);
        Reveal.Show(Cancel);
        Reveal.Apply(Cancel, () =>
        {
            Cancel.SetLabelNow(SetupStrings.UninstallClose);
            Cancel.Kind = ButtonKind.Primary;
        }, () =>
        {
            Cancel.SetLabelNow(SetupStrings.UninstallCancel);
            Cancel.Kind = ButtonKind.Secondary;
        }, fade: true);
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = dim.Advance(seconds) | shrink.Advance(seconds);
        Paint();
        return moving;
    }

    private void Paint()
    {
        mark.Opacity = Math.Round(dim.Value, 3);
        markScale.ScaleX = markScale.ScaleY = shrink.Value;
    }

    private void OnWipe(bool on)
    {
        if (State != UninstallState.Ask) return;
        Reveal.Set(Warning, on, reserve: true);
    }

    private void OnCancel()
    {
        if (State == UninstallState.Removing) return;
        Session.Close();
    }

    private void OnUninstall()
    {
        if (State != UninstallState.Ask) return;
        UninstallRequested?.Invoke(WipeData);
    }
}
