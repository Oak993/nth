using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Flow;
using Marrow.Setup.Shell;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Screens;

/// <summary>
/// STUB(U2): PORT-ONLY (nth installer PLAN.md 10, unit U1, ED-NEW-inst-u1-1). Plain stand-ins for the canvas's screens so
/// the window shell can be run and tested before unit U2 builds them: each shows its title and subtitle from the string
/// table, Welcome its signature line, and Installing, Update and Uninstall their Close-paused line. Welcome's Install and
/// Update's Update hand the install to the unchanged gate (STUB(U3/U4)). Nothing else here is the canvas's design.
/// </summary>
public sealed class InterimScreens : ISetupScreens
{
    public ISetupScreen Create(SetupScreen screen, SetupSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Func<bool> calm = () => session.Window.Settings.Current.Calm;
        var installed = session.Route.Installed.Version ?? "";
        return screen switch
        {
            SetupScreen.Welcome => Build(screen, SetupStrings.WelcomeTitle, null, hero: true, calm, signature: true, hold: false, action: (SetupStrings.WelcomeInstall, session.ChooseInstall)),
            SetupScreen.Options => Build(screen, SetupStrings.OptionsTitle, SetupStrings.OptionsSubtitle, hero: false, calm),
            SetupScreen.Installing => Build(screen, SetupStrings.InstallingTitle, null, hero: false, calm, hold: true, progress: new ProgressPanel(calm), progressSubtitle: true),
            SetupScreen.Ready => Build(screen, SetupStrings.ReadyTitle, null, hero: true, calm),
            SetupScreen.Update => Build(screen, SetupStrings.UpdateTitle, SetupStrings.UpdateSubtitle(installed, session.Version), hero: false, calm, hold: true, action: (SetupStrings.UpdateGo, session.ChooseInstall), progress: new ProgressPanel(calm) { IsVisible = false }),
            SetupScreen.UpToDate => Build(screen, SetupStrings.CurrentTitle, SetupStrings.CurrentSubtitle(session.Version), hero: false, calm),
            SetupScreen.NewerInstalled => Build(screen, SetupStrings.NewerTitle, SetupStrings.NewerSubtitle(installed), hero: false, calm),
            SetupScreen.Problem => Build(screen, SetupStrings.ProblemTitle, (session.ProblemReason ?? SetupStrings.ProblemOther) + " " + session.ProblemSecondLine, hero: false, calm),
            SetupScreen.Uninstall => Build(screen, SetupStrings.UninstallTitle, SetupStrings.UninstallSubtitle, hero: false, calm, hold: true),
            _ => throw new ArgumentOutOfRangeException(nameof(screen)),
        };
    }

    private static ISetupScreen Build(
        SetupScreen kind,
        string title,
        string? subtitle,
        bool hero,
        Func<bool> calm,
        bool signature = false,
        bool hold = false,
        (string Label, Action Run)? action = null,
        ProgressPanel? progress = null,
        bool progressSubtitle = false)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(40, 26, 40, 28) };
        var heading = SetupTheme.Text(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, hero ? TextRole.Hero : TextRole.Title);
        layout.Children.Add(heading);
        if (subtitle is not null)
        {
            var sub = SetupTheme.Text(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }, TextRole.Body, SetupTheme.Ink2);
            Grid.SetRow(sub, 1);
            layout.Children.Add(sub);
        }
        if (progress is not null)
        {
            if (progressSubtitle)
            {
                Grid.SetRow(progress.Subtitle, 1);
                layout.Children.Add(progress.Subtitle);
            }
            Grid.SetRow(progress, 2);
            layout.Children.Add(progress);
        }
        SignatureLine? line = null;
        if (signature)
        {
            line = new SignatureLine(calm) { Margin = new Thickness(0, 18, 0, 0) };
            Grid.SetRow(line, 2);
            layout.Children.Add(line);
        }
        HoldLine? holdLine = null;
        if (hold)
        {
            holdLine = new HoldLine(calm) { Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(holdLine, 4);
            layout.Children.Add(holdLine);
        }
        if (action is { } chosen)
        {
            var button = new InterimButton(chosen.Label, chosen.Run) { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Grid.SetRow(button, 5);
            layout.Children.Add(button);
        }
        return progress is null ? new InterimScreen(kind, layout, holdLine, line) : (ISetupScreen)new InterimProgressScreen(kind, layout, holdLine, line, progress);
    }

    private sealed record InterimScreen(SetupScreen Kind, Control View, HoldLine? Hold, SignatureLine? Signature) : ISetupScreen;

    private sealed record InterimProgressScreen(SetupScreen Kind, Control View, HoldLine? Hold, SignatureLine? Signature, IProgressView Progress) : IProgressScreen;
}

/// <summary>
/// STUB(U2): a plain accent button for the interim screens: a click, Enter or Space runs it; it takes keyboard focus and
/// shows a ring only when focus came from the keyboard. Unit U2 replaces it with the canvas's buttons.
/// </summary>
public sealed class InterimButton : Border
{
    private readonly Action run;
    private readonly Border ring = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Margin = new Thickness(-3), IsHitTestVisible = false, Opacity = 0 };
    private bool pointerFocus;

    public InterimButton(string label, Action run)
    {
        this.run = run ?? throw new ArgumentNullException(nameof(run));
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        MinHeight = 36;
        MinWidth = 120;
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(20, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
        Bind(BackgroundProperty, this.GetResourceObservable(SetupTheme.Highlight));
        ring.Bind(BorderBrushProperty, ring.GetResourceObservable(SetupTheme.Focus));
        var text = SetupTheme.Text(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, TextRole.Button, SetupTheme.HighlightInk);
        Child = new Panel { Children = { ring, text } };
        AutomationProperties.SetName(this, label);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Button);
    }

    public string Label => AutomationProperties.GetName(this) ?? "";

    public bool ShowsFocusRing => ring.Opacity > 0;

    /// <summary>Runs the button's action (a click, Enter or Space).</summary>
    public void Invoke() => run();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pointerFocus = true;
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var point = e.GetPosition(this);
        if (point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height) Invoke();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        Invoke();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsFocusedProperty) return;
        ring.Opacity = IsFocused && !pointerFocus ? 1 : 0;
        if (!IsFocused) pointerFocus = false;
    }
}
