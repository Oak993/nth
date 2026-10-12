using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>What Ready says for a result: the title, the sentence, the second line, and whether the desktop moment plays.</summary>
public sealed record ReadyWords(string Title, string Sentence, string? Note, bool Moment);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 8a, the canvas's Ready; unit U2, ED-NEW-inst-u2-1). The sentence follows what
/// the install hook reported (<c>install-result.json</c>), never the choice: created or kept, "Find it in Start and on your
/// desktop." and the moment; off, "Find it in Start."; a different "nth" shortcut, or Windows refusing, "Find it in Start."
/// and a second line saying so; desktop icons hidden, the full sentence and a second line, no moment; after an update,
/// "nth is updated", no moment. The moment, one thing at a time: the mark finishes its pop-in, the desktop fades in at
/// 1.35 s, the icon lands at 1.75 s with one soft glow, and the mark's drift waits until 3.1 s and starts mid-path. Calm:
/// the desktop fades in at once and the icon at 0.08 s, nothing moves. The screen is laid out in its final state before it
/// shows; a result that changes while it is shown reveals (the desktop fades in with its place as it opens, never an empty
/// gap). Open nth works throughout: its words cross-fade to "Opening…" and the window closes 1.4 s later.
/// </summary>
public sealed class ReadyScreen : CanvasScreen
{
    public const double DeskAt = 1.35, LandAt = 1.75, DriftAt = 3.1, CalmLandAt = .08, OpenClosesAfter = 1.4;

    private readonly SetupMark mark;
    private readonly TextBlock title;
    private readonly TextBlock sentence;
    private readonly TextBlock note;
    private readonly Decorator deskWrap;
    private bool moment;

    public ReadyScreen(SetupSession session) : base(SetupScreen.Ready, session, SetupStrings.ScreenReady)
    {
        mark = Mark(88, drifts: true);
        title = ScreenText.Title(SetupStrings.ReadyTitle, hero: true);
        sentence = ScreenText.Subtitle(SetupStrings.ReadyStart);
        note = ScreenText.Aside("");
        note.IsVisible = false;
        Desk = new DesktopPreview(large: true, Calm);
        Desk.Attach(Frames);
        deskWrap = new Decorator { Child = Desk, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        Close = Button(SetupStrings.ReadyClose, ButtonKind.Secondary, session.Close);
        Open = Button(SetupStrings.ReadyOpen, ButtonKind.Primary, OpenNth);
        Open.ReserveLabels(SetupStrings.ReadyOpen, SetupStrings.ReadyOpening);
        DefaultButton = Open;
        Column.With(Space(), mark, title, sentence, note, deskWrap, Space(ButtonGap), Buttons(Close, Open));
        session.ResultChanged += OnResult;
        session.WallpaperChanged += scene => Desk.SetWallpaper(scene.UsesTheme ? null : scene);
        if (session.Wallpaper is { UsesTheme: false } known) Desk.SetWallpaper(known);
    }

    public DesktopPreview Desk { get; }

    public Control DeskSlot => deskWrap;

    public SetupButton Close { get; }

    public SetupButton Open { get; }

    public SetupMark MarkView => mark;

    public TextBlock TitleBlock => title;

    public TextBlock Sentence => sentence;

    public TextBlock Note => note;

    /// <summary>True when the desktop moment belongs to the result shown.</summary>
    public bool HasMoment => moment;

    /// <summary>Ready's words for a result (a missing result reads as failed, so Ready never claims a shortcut it did not see).</summary>
    public static ReadyWords WordsFor(InstallResult? result, bool updated, string version)
    {
        if (updated) return new ReadyWords(SetupStrings.ReadyTitleUpdated, SetupStrings.ReadyUpdated(version), null, false);
        var outcome = result ?? InstallResult.Missing;
        return outcome.DesktopShortcut switch
        {
            DesktopShortcutOutcome.Created or DesktopShortcutOutcome.Kept when outcome.IconsHidden => new ReadyWords(SetupStrings.ReadyTitle, SetupStrings.ReadyBoth, SetupStrings.ReadyHidden, false),
            DesktopShortcutOutcome.Created or DesktopShortcutOutcome.Kept => new ReadyWords(SetupStrings.ReadyTitle, SetupStrings.ReadyBoth, null, true),
            DesktopShortcutOutcome.Off => new ReadyWords(SetupStrings.ReadyTitle, SetupStrings.ReadyStart, null, false),
            DesktopShortcutOutcome.NameTaken => new ReadyWords(SetupStrings.ReadyTitle, SetupStrings.ReadyStart, SetupStrings.ReadyNameTaken, false),
            _ => new ReadyWords(SetupStrings.ReadyTitle, SetupStrings.ReadyStart, SetupStrings.ReadyFailed, false),
        };
    }

    protected override void Setup(SetupScreen? from)
    {
        var words = WordsFor(Session.Result, Session.Updated, Session.Version);
        title.Text = words.Title;
        sentence.Text = words.Sentence;
        note.Text = words.Note ?? "";
        note.IsVisible = words.Note is not null;
        moment = words.Moment;
        deskWrap.IsVisible = moment;
        Desk.Appear(false, instant: true);
        Desk.SetState(DeskState.Off, instant: true);
        Open.SetLabelNow(SetupStrings.ReadyOpen);
    }

    protected override void AfterSetup(SetupScreen? from)
    {
        var calm = Calm();
        mark.Pop(moment && !calm ? DriftAt : null);
        if (!moment) return;
        Cues.After(calm ? 0 : DeskAt, () => Desk.Appear(true));
        Cues.After(calm ? CalmLandAt : LandAt, () => Desk.SetState(DeskState.Land));
    }

    private void OnResult()
    {
        if (Session.Screen != SetupScreen.Ready || !Root.IsEffectivelyVisible) return;
        var words = WordsFor(Session.Result, Session.Updated, Session.Version);
        Reveal.SetText(title, words.Title);
        Reveal.SetText(sentence, words.Sentence);
        if (words.Note is { } line)
        {
            Reveal.Show(note);
            Reveal.SetText(note, line);
        }
        else Reveal.Hide(note);
        if (words.Moment == moment) return;
        moment = words.Moment;
        Cues.Clear();
        if (moment)
        {
            // the desktop fades in with its place as the place opens, then the icon lands
            Desk.SetState(DeskState.Off, instant: true);
            Desk.Appear(true, instant: true);
            Reveal.Show(deskWrap);
            Cues.After(Calm() ? CalmLandAt : Reveal.ArrivalDelay + Reveal.FadeIn + .1, () => Desk.SetState(DeskState.Land));
        }
        else Reveal.Hide(deskWrap);
    }

    private void OpenNth()
    {
        if (!Session.Actions.OpenApp(Session.InstallRoot)) return;
        Open.SetLabel(SetupStrings.ReadyOpening, Calm());
        Cues.After(OpenClosesAfter, () => Session.Finish(Session.ExitCode));
    }
}

/// <summary>
/// PORT-ONLY (OD-0072, nth installer PLAN.md 3, the canvas's "Already installed"; unit U2, ED-NEW-inst-u2-1). The same
/// version installed: "nth is up to date" (exit code 0); a newer one: "A newer nth is installed" and its version, nothing
/// changed (exit code 1). The mark (76 DIP) pops in: the first screen of its window. Both offer Open nth, so the download
/// is never a dead end. A change of case while shown reveals.
/// </summary>
public sealed class CurrentScreen : CanvasScreen
{
    private readonly SetupMark mark;
    private readonly TextBlock title;
    private readonly TextBlock subtitle;

    public CurrentScreen(SetupSession session, SetupScreen kind) : base(kind, session, SetupStrings.ScreenCurrent)
    {
        if (kind is not (SetupScreen.UpToDate or SetupScreen.NewerInstalled)) throw new ArgumentOutOfRangeException(nameof(kind));
        Case = kind;
        mark = Mark(76);
        title = ScreenText.Title("");
        subtitle = ScreenText.Subtitle("");
        Close = Button(SetupStrings.ReadyClose, ButtonKind.Secondary, session.Close);
        Open = Button(SetupStrings.ReadyOpen, ButtonKind.Primary, OpenNth);
        Open.ReserveLabels(SetupStrings.ReadyOpen, SetupStrings.ReadyOpening);
        DefaultButton = Open;
        Column.With(Space(), mark, title, subtitle, Space(ButtonGap), Buttons(Close, Open));
    }

    public SetupScreen Case { get; private set; }

    public SetupButton Close { get; }

    public SetupButton Open { get; }

    public TextBlock TitleBlock => title;

    public TextBlock SubtitleBlock => subtitle;

    public static (string Title, string Subtitle) WordsFor(SetupScreen kind, string setupVersion, string? installedVersion) => kind == SetupScreen.NewerInstalled
        ? (SetupStrings.NewerTitle, SetupStrings.NewerSubtitle(installedVersion ?? ""))
        : (SetupStrings.CurrentTitle, SetupStrings.CurrentSubtitle(setupVersion));

    protected override void Setup(SetupScreen? from)
    {
        var (t, s) = WordsFor(Case, Session.Version, Session.Route.Installed.Version);
        title.Text = t;
        subtitle.Text = s;
        Open.SetLabelNow(SetupStrings.ReadyOpen);
    }

    protected override void AfterSetup(SetupScreen? from)
    {
        if (Session.TakeFirstPop()) mark.Pop();
        else mark.Show();
    }

    /// <summary>Up to date ↔ Newer installed while shown (a reveal).</summary>
    public void SetCase(SetupScreen kind)
    {
        if (kind is not (SetupScreen.UpToDate or SetupScreen.NewerInstalled)) throw new ArgumentOutOfRangeException(nameof(kind));
        Case = kind;
        var (t, s) = WordsFor(kind, Session.Version, Session.Route.Installed.Version);
        Reveal.SetText(title, t);
        Reveal.SetText(subtitle, s);
    }

    private void OpenNth()
    {
        if (!Session.Actions.OpenApp(Session.InstallRoot)) return;
        Open.SetLabel(SetupStrings.ReadyOpening, Calm());
        Cues.After(ReadyScreen.OpenClosesAfter, () => Session.Finish(Session.ExitCode));
    }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, the canvas's Problem; unit U2, ED-NEW-inst-u2-1). The reason first, in plain words;
/// then the ending, only when true: "Nothing was changed on your PC." or "Some of nth's files may be left. Retry repairs
/// them." The still mark is dimmed with its amber badge. Copy details copies the header (setup version, Windows, build,
/// architecture) and the details (its words cross-fade to "Copied" and back after 1.6 s, in a place kept for the longer
/// word); Retry runs the install again (hidden for a changed copy of the setup, which must be downloaded again). Show
/// details fades the log in at its full height, in a place the window already keeps for it, so nothing grows and the
/// buttons never move; the link's words follow. Enter retries; Esc closes.
/// </summary>
public sealed class ProblemScreen : CanvasScreen
{
    public const double CopiedFor = 1.6;

    private readonly SetupMark mark;
    private readonly TextBlock reason;
    private readonly TextBlock ending;
    private readonly LogBox log;
    private readonly LinkButton details;

    public ProblemScreen(SetupSession session) : base(SetupScreen.Problem, session, SetupStrings.ScreenProblem)
    {
        mark = Mark(64);
        mark.Dim = true;
        var markHost = new Panel { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, Children = { mark, new MarkBadge() } };
        // uninstall mode (unit U6, ED-NEW-inst-u6-1): the same screen, worded for removing
        var title = ScreenText.Title(session.Uninstalling ? SetupStrings.UninstallProblemTitle : SetupStrings.ProblemTitle);
        reason = ScreenText.Subtitle("");
        ending = ScreenText.Aside("");
        log = new LogBox { IsVisible = false };
        Copy = Button(SetupStrings.ProblemCopy, ButtonKind.Secondary, () => _ = CopyAsync());
        Copy.ReserveLabels(SetupStrings.ProblemCopy, SetupStrings.ProblemCopied);
        Retry = Button(SetupStrings.ProblemRetry, ButtonKind.Primary, session.Retry);
        DefaultButton = Retry;
        details = new LinkButton(SetupStrings.ProblemShow, TextRole.Hint, ToggleLog) { Outer = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        Column.With(markHost, title, reason, ending, log, Space(ButtonGap), Buttons(Copy, Retry), details);
        Column.AddGhost(log, new Border { Height = LogBox.FullHeight, Margin = log.Margin });
        session.ProblemChanged += OnProblem;
    }

    public SetupButton Copy { get; }

    public SetupButton Retry { get; }

    public LinkButton DetailsLink => details;

    public LogBox Log => log;

    public TextBlock Reason => reason;

    public TextBlock Ending => ending;

    public bool LogShown { get; private set; }

    /// <summary>What Copy details copies (and the log shows): the header line and the details.</summary>
    public string Details()
    {
        var look = Session.Window.Look;
        var windows = look.WindowsBuild >= 22000 ? "11" : "10";
        var header = SetupStrings.ProblemHeader(Session.Version, windows, look.WindowsBuild, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        return Session.ProblemDetails.Length == 0 ? header : header + "\n" + Session.ProblemDetails;
    }

    protected override void Setup(SetupScreen? from)
    {
        mark.Show();
        reason.Text = Session.ProblemReason ?? OtherWords;
        ending.Text = EndingWords;
        log.Text.Text = Details();
        log.IsVisible = false;
        LogShown = false;
        details.Words = SetupStrings.ProblemShow;
        Retry.IsVisible = RetryAllowed;
        Copy.SetLabelNow(SetupStrings.ProblemCopy);
    }

    private bool RetryAllowed => Session.ProblemReason != SetupStrings.ProblemChanged;

    private string OtherWords => Session.Uninstalling ? SetupStrings.UninstallProblemOther : SetupStrings.ProblemOther;

    private string EndingWords => Session.ProblemSecondLine;

    private void OnProblem()
    {
        if (Session.Screen != SetupScreen.Problem || !Root.IsEffectivelyVisible) return;
        Reveal.SetText(reason, Session.ProblemReason ?? OtherWords);
        Reveal.SetText(ending, EndingWords);
        log.Text.Text = Details();
        Reveal.Set(Retry, RetryAllowed);
    }

    private void ToggleLog()
    {
        LogShown = !LogShown;
        Reveal.Set(log, LogShown);
        Reveal.SetText(details.TextBlock, LogShown ? SetupStrings.ProblemHide : SetupStrings.ProblemShow);
        Avalonia.Automation.AutomationProperties.SetName(details, LogShown ? SetupStrings.ProblemHide : SetupStrings.ProblemShow);
    }

    private async Task CopyAsync()
    {
        if (!await Session.Actions.CopyAsync(Session.Window, Details())) return;
        Copy.SetLabel(SetupStrings.ProblemCopied, Calm());
        Cues.Clear();
        Cues.After(CopiedFor, () => Copy.SetLabel(SetupStrings.ProblemCopy, Calm()));
    }
}
