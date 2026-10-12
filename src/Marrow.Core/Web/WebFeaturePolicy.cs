// PORT-ONLY: web surfaces W8b (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.4 "Browser features, each decided": fullscreen,
// pointer lock, HTTP auth, <input type=file>, drag and drop of local files, passkeys, clipboard write, clipboard read, autofill and
// password saving, script dialogs, each for "Visus browse and preview", "Agent tabs" and "Artifacts, widgets, Apps"; 5.4 "The model
// cannot accept" a download; 5.5 permissions "always the user's decision"; 8.1 "Downloads and permissions: always the user's
// decision"; ED-NEW-web-w8b-3). No Swift counterpart. The table as data, so every consumer (the engine layer's per-view guard,
// the pane, the model tools) reads one answer and the tests pin every row.
using System.Text.Json;

namespace Marrow.Core.Web;

/// <summary>The rows of DESIGN 8.4.</summary>
public enum WebFeature
{
    Fullscreen,
    PointerLock,
    HttpAuth,
    FileInput,
    FileDrop,
    Passkeys,
    ClipboardWrite,
    ClipboardRead,
    Autofill,
    ScriptDialogs,
}

/// <summary>What a profile does with a feature (one cell of 8.4).</summary>
public enum WebFeatureRule
{
    /// <summary>Never, and nothing asks.</summary>
    Denied,

    /// <summary>Fullscreen: the pane expands to the workspace with the trust strip visible; never the screen.</summary>
    Contained,

    /// <summary>Asks like a permission (5.5): pointer lock, clipboard read.</summary>
    Asks,

    /// <summary>nth draws it as its own card under the toolbar: HTTP auth, script dialogs.</summary>
    NthCard,

    /// <summary>The system's own dialog or UI, as a browser: the file dialog, Windows Hello, Chromium's autofill.</summary>
    System,

    /// <summary>Allowed when the user's own gesture starts it (drag and drop of files, clipboard write).</summary>
    UserGesture,

    /// <summary>Agent tabs' file input: only from the user's click on Choose in the guarded "file upload" card (5.10).</summary>
    GuardedCard,

    /// <summary>Agent tabs' clipboard: only nth's own copy actions, never page script.</summary>
    NthCopyOnly,

    /// <summary>Agent tabs' script dialogs: dismissed at once and logged for the model.</summary>
    AutoDismissed,

    /// <summary>Sandboxed content's files: the native picker is off; nth.files.pick (6.5) and nth's card for drops.</summary>
    NthPicker,
}

/// <summary>DESIGN 8.4 as data, and the DevTools Protocol calls a model may never make (5.4, 5.5, 8.4).</summary>
public static class WebFeaturePolicy
{
    /// <summary>The 8.4 cell for a profile. Private tabs follow browse except where 8.4 says "in browse only".</summary>
    public static WebFeatureRule Rule(WebProfileKind profile, WebFeature feature) => profile switch
    {
        WebProfileKind.Agent => feature switch
        {
            WebFeature.FileInput => WebFeatureRule.GuardedCard,
            WebFeature.FileDrop => WebFeatureRule.UserGesture,
            WebFeature.ClipboardWrite => WebFeatureRule.NthCopyOnly,
            WebFeature.ScriptDialogs => WebFeatureRule.AutoDismissed,
            _ => WebFeatureRule.Denied,
        },
        WebProfileKind.Artifacts => feature switch
        {
            WebFeature.FileInput or WebFeature.FileDrop => WebFeatureRule.NthPicker,
            _ => WebFeatureRule.Denied,
        },
        _ => feature switch
        {
            WebFeature.Fullscreen => WebFeatureRule.Contained,
            WebFeature.PointerLock or WebFeature.ClipboardRead => WebFeatureRule.Asks,
            WebFeature.HttpAuth or WebFeature.ScriptDialogs => WebFeatureRule.NthCard,
            WebFeature.FileInput => WebFeatureRule.System,
            WebFeature.FileDrop or WebFeature.ClipboardWrite => WebFeatureRule.UserGesture,
            // 8.4: passkeys "allowed in browse (Windows shows its own UI)", autofill and password saving "Chromium's, in browse
            // only". A private tab is a browse tab that keeps nothing: passkeys work there (Windows keeps the key, not the
            // profile), autofill does not (it would keep what was typed).
            WebFeature.Passkeys => profile is WebProfileKind.Browse or WebProfileKind.Private ? WebFeatureRule.System : WebFeatureRule.Denied,
            WebFeature.Autofill => profile == WebProfileKind.Browse ? WebFeatureRule.System : WebFeatureRule.Denied,
            _ => WebFeatureRule.Denied,
        },
    };

    /// <summary>The profiles of Visus tabs the user sees as pages (browse, preview, private).</summary>
    public static bool IsVisusPage(WebProfileKind profile) => profile is WebProfileKind.Browse or WebProfileKind.Preview or WebProfileKind.Private;

    /// <summary>
    /// The DevTools Protocol methods nth keeps for itself: downloads, permissions, file choosers, file drops and passkeys stay the
    /// user's (5.4 "The model cannot accept", 5.5, 8.1, 8.4 "never synthesized by the model", "account sign-in is a hard deny").
    /// </summary>
    public static IReadOnlySet<string> ModelRefusedMethods { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Browser.setDownloadBehavior",
        "Page.setDownloadBehavior",
        "Browser.cancelDownload",
        "Browser.grantPermissions",
        "Browser.setPermission",
        "Browser.resetPermissions",
        "Page.setInterceptFileChooserDialog",
        "Page.handleFileChooser",
        "DOM.setFileInputFiles",
        "Input.setInterceptDrags",
        "WebAuthn.enable",
        "WebAuthn.disable",
        "WebAuthn.addVirtualAuthenticator",
        "WebAuthn.addCredential",
        "WebAuthn.setUserVerified",
        "WebAuthn.setAutomaticPresenceSimulation",
        // nth's own per-view guard scripts and the pointer-lock binding (ED-NEW-web-w8b-3) are nth's: a model step cannot add,
        // remove or answer them.
        "Page.addScriptToEvaluateOnNewDocument",
        "Page.removeScriptToEvaluateOnNewDocument",
        "Runtime.addBinding",
        "Runtime.removeBinding",
    };

    /// <summary>
    /// Whether a model step may send this DevTools Protocol call. Refused: every method of <see cref="ModelRefusedMethods"/>, and a
    /// synthetic drag that carries files (<c>Input.dispatchDragEvent</c> with <c>data.files</c>). A params text that does not
    /// parse is refused for the methods that read it.
    /// </summary>
    public static bool ModelMayCall(string method, string? paramsJson)
    {
        if (string.IsNullOrEmpty(method) || ModelRefusedMethods.Contains(method)) return false;
        if (method != "Input.dispatchDragEvent") return true;
        if (string.IsNullOrWhiteSpace(paramsJson)) return true;
        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return true;
            return !data.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
