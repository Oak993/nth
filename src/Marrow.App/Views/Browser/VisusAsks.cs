using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W8b, the questions a Visus tab waits on (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.4 downloads,
// 5.5 permissions, 8.3 app links, 8.4 HTTP auth, script dialogs and pointer lock, 10.1 "F6 ... moves focus into the newest open
// prompt first", "Esc gives the safe answer (Block, Cancel, Don't allow)"; ED-NEW-web-w8b-4). No Swift counterpart. Each ask
// belongs to its tab and waits there until the user answers it in the pane: a tab that is not on screen keeps its asks (a page's
// dialog or a download waits, as in a browser), except app links, which are cancelled (W8a's rule: they need a click again). A
// page that goes away answers its document's asks with the safe answer; a closed tab answers all of them so.

namespace Marrow.App.Views.Browser;

/// <summary>What a tab waits on.</summary>
public enum VisusAskKind
{
    /// <summary>An app link (8.3).</summary>
    App,

    /// <summary>A permission, or pointer lock (5.5, 8.4).</summary>
    Permission,

    /// <summary>A download (5.4).</summary>
    Download,

    /// <summary>HTTP auth (8.4).</summary>
    SignIn,

    /// <summary>A script dialog: alert, confirm, prompt, leave page (8.4).</summary>
    Dialog,

    /// <summary>An agent tab's file input: the guarded file upload card (8.4, 5.10).</summary>
    File,
}

/// <summary>One question a tab waits on. Only the first answer counts; <see cref="AnswerSafe"/> gives the safe one.</summary>
public abstract class VisusAsk
{
    private bool answered;

    protected VisusAsk(VisusTab tab, string source)
    {
        Tab = tab;
        Source = source;
    }

    public VisusTab Tab { get; }

    /// <summary>Who asks, as the trust strip names it ("example.com", "localhost:5173").</summary>
    public string Source { get; }

    public abstract VisusAskKind Kind { get; }

    public bool IsAnswered => answered;

    /// <summary>Answered with the safe answer when its tab is no longer on screen (app links only).</summary>
    public virtual bool CancelsWhenHidden => false;

    /// <summary>Answered with the safe answer when the page leaves its document (not a download, which outlives it).</summary>
    public virtual bool EndsWithDocument => true;

    /// <summary>The ask was answered (by the user, or safely).</summary>
    public event Action<VisusAsk>? Answered;

    /// <summary>
    /// The ask ended without the user (its tab closed, its page went away, an app link's tab left the screen): the safe answer
    /// went to the page, and nothing is kept or announced for it (a closed tab never stores a Block for its site).
    /// </summary>
    public bool IsWithdrawn { get; private set; }

    /// <summary>Block, Cancel or Dismiss, as the user gives them with Esc.</summary>
    public void AnswerSafe()
    {
        if (answered) return;
        Safe();
    }

    /// <summary>The safe answer because the ask's tab or page went (see <see cref="IsWithdrawn"/>).</summary>
    internal void Withdraw()
    {
        if (answered) return;
        IsWithdrawn = true;
        Safe();
    }

    /// <summary>The safe answer itself (a subclass's own answer method: <see cref="Claim"/>, act, <see cref="Done"/>).</summary>
    protected abstract void Safe();

    /// <summary>Takes the one answer; false when the ask was answered already (the caller then does nothing).</summary>
    protected bool Claim()
    {
        if (answered) return false;
        answered = true;
        return true;
    }

    /// <summary>Tells the tab and the pane the ask is over.</summary>
    protected void Done() => Answered?.Invoke(this);
}

/// <summary>5.5 and 8.4: a site wants a permission (or pointer lock); Allow this time, Always on this site (where kept) or Block.</summary>
public sealed class VisusPermissionAsk : VisusAsk
{
    private readonly Action<SitePermissionAnswer> answer;

    internal VisusPermissionAsk(VisusTab tab, string source, string origin, SitePermissionKind permission, bool offersAlways, Action<SitePermissionAnswer> answer)
        : base(tab, source)
    {
        Origin = origin;
        Permission = permission;
        OffersAlways = offersAlways;
        this.answer = answer;
    }

    public override VisusAskKind Kind => VisusAskKind.Permission;

    public string Origin { get; }

    public SitePermissionKind Permission { get; }

    /// <summary>"Always on this site" is offered (browse and preview; never in a private tab).</summary>
    public bool OffersAlways { get; }

    /// <summary>The answer given (null while waiting).</summary>
    public SitePermissionAnswer? Result { get; private set; }

    public void Answer(SitePermissionAnswer value)
    {
        if (value == SitePermissionAnswer.AllowAlways && !OffersAlways) value = SitePermissionAnswer.AllowOnce;
        if (!Claim()) return;
        Result = value;
        answer(value);
        Done();
    }

    protected override void Safe() => Answer(SitePermissionAnswer.Block);
}

/// <summary>5.4: a download waits for Save, Save as… or Cancel. The model cannot answer it (only the prompt's buttons do).</summary>
public sealed class VisusDownloadAsk : VisusAsk
{
    private readonly Action<DownloadChoice, string?> answer;

    internal VisusDownloadAsk(VisusTab tab, string source, WebDownloadRequest request, Action<DownloadChoice, string?> answer) : base(tab, source)
    {
        Request = request;
        FileName = DownloadGate.SafeFileName(string.IsNullOrWhiteSpace(request.SuggestedFileName) ? NameFromUrl(request.Url) : request.SuggestedFileName);
        IsRisky = DownloadGate.IsRisky(FileName, request.MimeType);
        this.answer = answer;
    }

    public override VisusAskKind Kind => VisusAskKind.Download;

    /// <summary>A download outlives the page that started it.</summary>
    public override bool EndsWithDocument => false;

    public WebDownloadRequest Request { get; }

    /// <summary>The name the file is saved under (made safe for Windows).</summary>
    public string FileName { get; }

    /// <summary>5.4's risky types: the warning, and Save needs a second click within 3 s.</summary>
    public bool IsRisky { get; }

    /// <summary>"12 KB · CSV · from localhost:5173".</summary>
    public string Detail => DownloadGate.Detail(Request.TotalBytes, FileName, Request.MimeType, Source);

    /// <summary>The choice made (null while waiting).</summary>
    public DownloadChoice? Result { get; private set; }

    /// <summary>The path Save as… chose (null for Save, which picks a free name in the Downloads folder, and for Cancel).</summary>
    public string? ChosenPath { get; private set; }

    /// <summary>Save, Save as… (with the path the system dialog chose) or Cancel.</summary>
    internal void Answer(DownloadChoice choice, string? path = null)
    {
        if (choice == DownloadChoice.SaveAs && string.IsNullOrEmpty(path)) return; // Save as… needs its path
        if (!Claim()) return;
        Result = choice;
        ChosenPath = choice == DownloadChoice.SaveAs ? path : null;
        answer(choice, ChosenPath);
        Done();
    }

    protected override void Safe() => Answer(DownloadChoice.Cancel);

    private static string NameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return DownloadGate.FallbackName;
        var last = uri.Segments.LastOrDefault()?.Trim('/') ?? "";
        return string.IsNullOrEmpty(last) ? DownloadGate.FallbackName : Uri.UnescapeDataString(last);
    }
}

/// <summary>8.4: HTTP auth: "example.com asks you to sign in" with the realm; the values go to the response and nth keeps none.</summary>
public sealed class VisusSignInAsk : VisusAsk
{
    private readonly WebAuthRequest request;

    internal VisusSignInAsk(VisusTab tab, string source, WebAuthRequest request) : base(tab, source)
    {
        this.request = request;
        Realm = RealmOf(request.Challenge);
    }

    public override VisusAskKind Kind => VisusAskKind.SignIn;

    /// <summary>The realm the server named, or null.</summary>
    public string? Realm { get; }

    /// <summary>The page's own address being signed in to.</summary>
    public string Uri => request.Uri;

    /// <summary>Sends the user's values to the server (nth keeps none of them).</summary>
    internal void SignIn(string userName, string password)
    {
        if (!Claim()) return;
        request.Respond(new WebCredentials(userName, password));
        Done();
    }

    internal void Cancel()
    {
        if (!Claim()) return;
        request.Respond(null);
        Done();
    }

    protected override void Safe() => Cancel();

    /// <summary>The realm of a <c>WWW-Authenticate</c> challenge ("Basic realm=\"Staging\"" gives "Staging").</summary>
    internal static string? RealmOf(string? challenge)
    {
        if (string.IsNullOrEmpty(challenge)) return null;
        var at = challenge.IndexOf("realm=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var rest = challenge[(at + 6)..].TrimStart();
        string value;
        if (rest.StartsWith('"'))
        {
            var end = rest.IndexOf('"', 1);
            value = end < 0 ? rest[1..] : rest[1..end];
        }
        else
        {
            var end = rest.IndexOfAny([',', ' ']);
            value = end < 0 ? rest : rest[..end];
        }
        value = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (value.Length == 0) return null;
        return value.Length <= 80 ? value : value[..77] + "…";
    }
}

/// <summary>8.4: a page's alert, confirm, prompt or leave-page dialog, drawn by nth as a card labelled with the site.</summary>
public sealed class VisusDialogAsk : VisusAsk
{
    private readonly WebScriptDialogRequest request;

    internal VisusDialogAsk(VisusTab tab, string source, WebScriptDialogRequest request, bool offersStop) : base(tab, source)
    {
        this.request = request;
        OffersStop = offersStop;
    }

    public override VisusAskKind Kind => VisusAskKind.Dialog;

    public WebScriptDialogKind DialogKind => request.Kind;

    /// <summary>The page's own words (untrusted; shown as plain text in the card, never as nth's).</summary>
    public string Message => request.Message;

    public string DefaultText => request.DefaultText;

    /// <summary>"Don't let this page show more dialogs" is offered (from the page's second dialog on).</summary>
    public bool OffersStop { get; }

    /// <summary>The user chose to stop this page's dialogs (until it navigates).</summary>
    public bool StopRequested { get; private set; }

    internal void Accept(string? text, bool stop)
    {
        if (!Claim()) return;
        StopRequested = stop && OffersStop;
        request.Respond(new WebScriptDialogDecision(true, request.Kind == WebScriptDialogKind.Prompt ? text ?? "" : null));
        Done();
    }

    internal void Dismiss(bool stop)
    {
        if (!Claim()) return;
        StopRequested = stop && OffersStop;
        request.Respond(WebScriptDialogDecision.Dismiss);
        Done();
    }

    protected override void Safe() => Dismiss(stop: false);
}

/// <summary>
/// 8.4 and 5.10: the model's page opened a file input. It gets a file only when the user clicks Choose in this card and picks
/// one in the system dialog; the model never sees the path.
/// </summary>
public sealed class VisusFileAsk : VisusAsk
{
    private readonly WebFileChooser chooser;
    private readonly WebSurfaceView view;

    internal VisusFileAsk(VisusTab tab, string source, WebFileChooser chooser, WebSurfaceView view) : base(tab, source)
    {
        this.chooser = chooser;
        this.view = view;
    }

    public override VisusAskKind Kind => VisusAskKind.File;

    /// <summary>The input takes several files.</summary>
    public bool Multiple => chooser.Mode == "selectMultiple";

    /// <summary>Whether files went to the page (null while waiting).</summary>
    public bool? Given { get; private set; }

    /// <summary>The files the user picked go to the page's input (one, unless it takes several).</summary>
    internal async Task GiveAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || !Claim()) return;
        var files = Multiple ? paths : [paths[0]];
        bool given;
        try
        {
            given = await view.GiveFilesAsync(chooser, files);
        }
        catch (Exception)
        {
            given = false;
        }
        Given = given;
        Done();
    }

    internal void Cancel()
    {
        if (!Claim()) return;
        Given = false;
        Done();
    }

    protected override void Safe() => Cancel();
}
