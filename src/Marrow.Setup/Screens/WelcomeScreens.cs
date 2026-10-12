using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Flow;
using Marrow.Setup.Motion;
using Marrow.Setup.Shell;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 8a, the canvas's Welcome; unit U2, ED-NEW-inst-u2-1). Nothing to decide: the
/// mark (72 DIP, its pop-in once per window, its silver drifting), "Welcome to nth", "by oak993", the version and size,
/// the facts (the signature line, for you only, offline), one wide Install button, and three lines of fine print: where
/// it installs with the Options link, the desktop shortcut sentence with its one-tap link ("Adds nth to Start and your
/// desktop · Don't add to desktop" ↔ "Adds nth to Start · Add to desktop", a 0.13 s cross-fade in a place reserved for
/// the taller wording, a polite live region), and the licence with View license. Returning from Options does not replay
/// the mark. Enter installs; Esc closes.
/// </summary>
public sealed class WelcomeScreen : CanvasScreen, IMotion
{
    public const double LineFade = 0.13;

    private readonly SetupMark mark;
    private readonly TextBlock meta;
    private readonly Run pathRun;
    private readonly Run pathPrefix;
    private readonly Run pathSuffix;
    private readonly TextBlock deskText;
    private readonly Run deskSentence;
    private readonly LinkButton deskLink;
    private readonly ReserveBox deskBox;
    private readonly Tween deskFade = new(1);
    private bool deskPending;

    public WelcomeScreen(SetupSession session) : base(SetupScreen.Welcome, session, SetupStrings.ScreenWelcome)
    {
        mark = Mark(72, drifts: true);
        var title = ScreenText.Title(SetupStrings.WelcomeTitle, hero: true);
        var by = ScreenText.Subtitle(SetupStrings.WelcomeBy(SetupProduct.Publisher), top: 2);
        meta = ScreenText.Meta("");
        Signature = new SignatureLine(Calm);
        var facts = new FactsList()
            .AddLine(Signature)
            .Add(FactIcon.Kind.Person, SetupStrings.WelcomeForYou)
            .Add(FactIcon.Kind.Package, SetupStrings.WelcomeOffline);
        Install = Button(SetupStrings.WelcomeInstall, ButtonKind.Primary, session.ChooseInstall, wide: true);
        DefaultButton = Install;
        OptionsLink = new LinkButton(SetupStrings.WelcomeOptions, TextRole.Hint, () => session.Go(SetupScreen.Options));
        var installs = ScreenText.Fine("", 12);
        var installsInlines = ScreenText.Inlines(installs);
        pathPrefix = new Run();
        pathRun = ScreenText.Run(installs, "", TextRole.Path);
        pathSuffix = new Run();
        installsInlines.Add(pathPrefix);
        installsInlines.Add(pathRun);
        installsInlines.Add(pathSuffix);
        installsInlines.Add(new Run(" " + SetupStrings.Dot + " "));
        installsInlines.Add(ScreenText.Inline(OptionsLink));
        InstallsLine = installs;

        deskLink = new LinkButton(SetupStrings.WelcomeDesktopLeaveOut, TextRole.Hint, () => session.SetDesktopShortcut(!session.DesktopShortcut));
        deskSentence = new Run(SetupStrings.WelcomeAddsBoth);
        deskText = ScreenText.Fine("", 0);
        var deskInlines = ScreenText.Inlines(deskText);
        deskInlines.Add(deskSentence);
        deskInlines.Add(new Run(" " + SetupStrings.Dot + " "));
        deskInlines.Add(ScreenText.Inline(deskLink));
        deskText.Margin = default;
        Reveal.LiveRegion(deskText);
        deskBox = Reveal.Reserve(deskText, TextRole.Hint,
            SetupStrings.WelcomeAddsBoth + " " + SetupStrings.Dot + " " + SetupStrings.WelcomeDesktopLeaveOut,
            SetupStrings.WelcomeAddsStart + " " + SetupStrings.Dot + " " + SetupStrings.WelcomeDesktopAdd);
        deskBox.Margin = new Thickness(0, 4, 0, 0);
        deskBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        LicenseLink = new LinkButton(SetupStrings.WelcomeLicenceView, TextRole.Hint, () => session.Actions.ViewLicense());
        var licence = FineWithLink(LicenseLink, 4, new Run(SetupStrings.WelcomeLicence));

        Column.With(mark, title, by, meta, facts, Space(ButtonGap), Buttons(Install), installs, deskBox, licence);
        session.DesktopShortcutChanged += OnDesktopChanged;
        session.InstallRootChanged += _ => SetPath();
        SetDesk(session.DesktopShortcut);
        SetPath();
    }

    public SetupButton Install { get; }

    public LinkButton OptionsLink { get; }

    public LinkButton DeskLink => deskLink;

    public LinkButton LicenseLink { get; }

    public TextBlock InstallsLine { get; }

    public SetupMark MarkView => mark;

    /// <summary>The desktop sentence as shown now, and the link's words.</summary>
    public (string Sentence, string Link) DeskLine => (deskSentence.Text ?? "", deskLink.Words);

    /// <summary>The desktop line's opacity (its cross-fade).</summary>
    public double DeskLineOpacity => deskText.Opacity;

    public Control DeskLineBox => deskBox;

    protected override void Setup(SetupScreen? from)
    {
        meta.IsVisible = Session.SizeMegabytes is not null;
        meta.Text = Session.SizeMegabytes is { } size ? SetupStrings.WelcomeMeta(Session.Version, size) : "";
        deskFade.Snap(1);
        deskText.Opacity = 1;
        deskPending = false;
        SetDesk(Session.DesktopShortcut);
        SetPath();
    }

    protected override void AfterSetup(SetupScreen? from)
    {
        if (from == SetupScreen.Options)
        {
            mark.Resume();
            return;
        }
        if (Session.TakeFirstPop()) mark.Pop();
        else mark.Show(drift: true);
    }

    public override void Left()
    {
        base.Left();
        mark.Pause();
    }

    private void SetPath()
    {
        var path = Session.InstallRoot;
        var full = SetupStrings.WelcomeInstallsTo(path);
        var at = full.IndexOf(path, StringComparison.Ordinal);
        pathPrefix.Text = at >= 0 ? full[..at] : full + " ";
        pathRun.Text = path;
        pathSuffix.Text = at >= 0 ? full[(at + path.Length)..] : "";
    }

    private void OnDesktopChanged(bool on)
    {
        if (Session.Screen != SetupScreen.Welcome || !Root.IsEffectivelyVisible)
        {
            SetDesk(on);
            return;
        }
        // the sentence and its link cross-fade together: out 0.13 s, the words change, in 0.13 s
        deskPending = true;
        deskFade.Retarget(0, LineFade, CubicBezier.Ease);
        Frames.Start(this);
    }

    private void SetDesk(bool on)
    {
        var sentence = on ? SetupStrings.WelcomeAddsBoth : SetupStrings.WelcomeAddsStart;
        deskSentence.Text = sentence;
        deskLink.Words = on ? SetupStrings.WelcomeDesktopLeaveOut : SetupStrings.WelcomeDesktopAdd;
        AutomationProperties.SetName(deskText, sentence);
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = deskFade.Advance(seconds);
        if (deskPending && deskFade.Value <= 0)
        {
            deskPending = false;
            SetDesk(Session.DesktopShortcut);
            deskFade.Retarget(1, LineFade, CubicBezier.Ease);
            moving = true;
        }
        deskText.Opacity = Math.Round(deskFade.Value, 3);
        return moving || deskPending;
    }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, the canvas's Install options; unit U2, ED-NEW-inst-u2-1). Only for those who look
/// for it: the title and what is here, one glass group (Location with its path, reserved for the default and the chosen
/// one so nothing moves when it changes, and Change…; Desktop shortcut with its detail and the Nocta switch, and under it,
/// inside the row, the desktop component at 144 × 90 that follows the switch and flips it when tapped), the sign-in note,
/// and Done. The same choice as Welcome's link, always in step. Enter and Esc go back to Welcome.
/// </summary>
public sealed class OptionsScreen : CanvasScreen
{
    private readonly TextBlock path;
    private readonly ReserveBox pathBox;
    private readonly string defaultRoot;

    public OptionsScreen(SetupSession session) : base(SetupScreen.Options, session, SetupStrings.ScreenOptions)
    {
        defaultRoot = session.Route.TargetRoot;
        var title = ScreenText.Title(SetupStrings.OptionsTitle);
        var subtitle = ScreenText.Subtitle(SetupStrings.OptionsSubtitle);
        path = SetupTheme.Text(new TextBlock { Text = session.InstallRoot, TextWrapping = TextWrapping.Wrap }, TextRole.Path, SetupTheme.Ink3);
        pathBox = Reveal.Reserve(path, TextRole.Path, defaultRoot, session.InstallRoot);
        pathBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        Change = Button(SetupStrings.OptionsChange, ButtonKind.Secondary, () => _ = ChangeAsync(), small: true);
        AutomationProperties.SetHelpText(Change, SetupStrings.OptionsLocation);
        var location = new OptionRow(SetupStrings.OptionsLocation, pathBox, Change);

        Switch = new NoctaSwitch(session.DesktopShortcut, Calm);
        Switch.Attach(Frames);
        AutomationProperties.SetName(Switch, SetupStrings.OptionsDesktop);
        AutomationProperties.SetHelpText(Switch, SetupStrings.OptionsDesktopHint);
        Switch.Toggled += on => session.SetDesktopShortcut(on);
        Preview = new DesktopPreview(large: false, Calm);
        Preview.Attach(Frames);
        Preview.Activated += () => Switch.Toggle();
        var desktop = new OptionRow(SetupStrings.OptionsDesktop, OptionRow.Hint(SetupStrings.OptionsDesktopHint), Switch).Below(Preview);
        Group = new GlassGroup().With(location, desktop);
        var signIn = ScreenText.Fine(SetupStrings.OptionsSignIn);
        Done = Button(SetupStrings.OptionsDone, ButtonKind.Primary, () => session.Go(SetupScreen.Welcome));
        DefaultButton = Done;
        Column.With(title, subtitle, Group, signIn, Space(ButtonGap), Buttons(Done));
        session.DesktopShortcutChanged += OnDesktopChanged;
        session.InstallRootChanged += OnRootChanged;
        session.WallpaperChanged += scene => Preview.SetWallpaper(scene.UsesTheme ? null : scene);
        if (session.Wallpaper is { UsesTheme: false } known) Preview.SetWallpaper(known);
        Preview.SetState(session.DesktopShortcut ? DeskState.On : DeskState.Off, instant: true);
    }

    public SetupButton Change { get; }

    public NoctaSwitch Switch { get; }

    public DesktopPreview Preview { get; }

    public GlassGroup Group { get; }

    public SetupButton Done { get; }

    public TextBlock PathBlock => path;

    public Control PathBox => pathBox;

    protected override void Setup(SetupScreen? from)
    {
        Switch.Set(Session.DesktopShortcut, animate: false);
        Preview.SetState(Session.DesktopShortcut ? DeskState.On : DeskState.Off, instant: true);
        path.Text = Session.InstallRoot;
    }

    protected override void Escape() => Session.Go(SetupScreen.Welcome);

    private void OnDesktopChanged(bool on)
    {
        var shown = Session.Screen == SetupScreen.Options && Root.IsEffectivelyVisible;
        if (Switch.IsOn != on) Switch.Set(on, animate: shown);
        Preview.SetState(on ? DeskState.Land : DeskState.Off, instant: !shown);
    }

    private void OnRootChanged(string root)
    {
        pathBox.SetWordings(defaultRoot, root);
        Reveal.SetText(path, root);
    }

    private async Task ChangeAsync()
    {
        var picked = await Session.Actions.PickFolderAsync(Session.Window, Session.InstallRoot);
        if (picked is { Length: > 0 }) Session.SetInstallRoot(SetupSession.RootFor(picked));
    }
}
