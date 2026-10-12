using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

// PORT-ONLY: web surfaces W8b (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.4 "Browser features, each decided" for Visus
// browse and preview tabs and agent tabs: pointer lock asks like a permission and Esc always releases; fullscreen, pointer lock,
// passkeys and clipboard reads are denied in agent tabs, clipboard writes there come only from nth's own copy actions, file inputs
// only through the guarded file upload card; passkeys are allowed in browse only; 8.1 the document-created scripts; ED-NEW-web-w8b-3).
// No Swift counterpart. What each Visus page view gets on top of the engine's settings: document scripts (registered before any
// page script runs) and DevTools Protocol switches (sent when the platform view attaches). Sandboxed views are W13's.

/// <summary>A page asked to lock the pointer (8.4); answer once with <see cref="Answer"/>. Unanswered asks are refused.</summary>
public sealed class WebPointerLockRequest
{
    private readonly Action<bool> answer;
    private bool answered;

    internal WebPointerLockRequest(string origin, Action<bool> answer)
    {
        Origin = origin;
        this.answer = answer;
    }

    /// <summary>The asking page's site ("https://example.com").</summary>
    public string Origin { get; }

    public bool IsAnswered => answered;

    public void Answer(bool allow)
    {
        if (answered) return;
        answered = true;
        answer(allow);
    }
}

/// <summary>An agent tab's page opened a file chooser (8.4): nth intercepted it; only the user's click in the guarded card may answer.</summary>
public sealed record WebFileChooser(string FrameId, string Mode, int? BackendNodeId);

/// <summary>What happened in an agent tab that the model is told (8.4 "auto-dismissed, logged for the model", "page needs sign-in").</summary>
public enum WebAgentNoteKind
{
    DialogDismissed,
    SignInNeeded,
    DownloadWaiting,
    FileChooserBlocked,
    FeatureDenied,
}

/// <summary>One note for the model. <see cref="Text"/> is page-derived and untrusted: the tools mark it so (5.10).</summary>
public sealed record WebAgentNote(WebAgentNoteKind Kind, string Text, DateTimeOffset At);

/// <summary>DESIGN 8.4: the per-view scripts and switches of the Visus page profiles and agent tabs.</summary>
public static class WebFeatureGuard
{
    /// <summary>The DevTools binding the pointer-lock gate calls (added by nth, never by a model step).</summary>
    public const string PointerLockBinding = "__nthPointerLock";

    /// <summary>The gate's answer function, which nth calls through the DevTools Protocol with a user gesture.</summary>
    public const string PointerLockAnswer = "__nthPointerLockAnswer";

    /// <summary>
    /// 8.4 pointer lock in browse, preview and private tabs: <c>requestPointerLock</c> asks nth first through the binding; nth
    /// prompts like a permission and answers through the DevTools Protocol with a user gesture (the page's own gesture is spent
    /// by then). With no binding (DevTools failed) the request is refused. The gate also reports when the pointer is locked and
    /// released, so nth can show "Press Esc to release your pointer" and release it on Esc.
    /// </summary>
    public static string PointerLockGateScript { get; } = """
        (() => {
          'use strict';
          const proto = Element.prototype;
          const original = proto.requestPointerLock;
          if (typeof original !== 'function' || Object.prototype.hasOwnProperty.call(globalThis, '__nthPointerLockAnswer')) return;
          const binding = '__nthPointerLock';
          const pending = new Map();
          let next = 0;
          const tell = (message) => {
            const send = globalThis[binding];
            if (typeof send !== 'function') return false;
            try { send(JSON.stringify(message)); return true; } catch (_) { return false; }
          };
          const refuse = () => new DOMException('Pointer lock was not allowed.', 'NotAllowedError');
          const gated = function requestPointerLock(options) {
            const element = this;
            if (!(element instanceof Element)) return original.call(element, options);
            return new Promise((resolve, reject) => {
              const id = ++next;
              pending.set(id, { element, options, resolve, reject });
              if (pending.size > 4) { const first = pending.keys().next().value; const old = pending.get(first); pending.delete(first); old.reject(refuse()); }
              if (!tell({ kind: 'ask', id })) { pending.delete(id); reject(refuse()); }
            });
          };
          Object.defineProperty(proto, 'requestPointerLock', { value: gated, writable: false, configurable: false });
          const answer = (id, allowed) => {
            const entry = pending.get(id);
            if (!entry) return false;
            pending.delete(id);
            if (!allowed) { entry.reject(refuse()); try { document.dispatchEvent(new Event('pointerlockerror')); } catch (_) {} return false; }
            try { Promise.resolve(original.call(entry.element, entry.options)).then(entry.resolve, entry.reject); } catch (error) { entry.reject(error); }
            return true;
          };
          Object.defineProperty(globalThis, '__nthPointerLockAnswer', { value: answer, writable: false, configurable: false, enumerable: false });
          document.addEventListener('pointerlockchange', () => { tell({ kind: document.pointerLockElement ? 'locked' : 'released' }); }, true);
        })();
        """;

    /// <summary>
    /// 8.4 agent tabs: fullscreen and pointer lock are refused; clipboard writes from page script are refused (nth's own copy
    /// actions do not go through the page); passkeys are refused (account sign-in is a hard deny).
    /// </summary>
    public static string AgentScript { get; } = """
        (() => {
          'use strict';
          if (Object.prototype.hasOwnProperty.call(globalThis, '__nthAgentGuard')) return;
          Object.defineProperty(globalThis, '__nthAgentGuard', { value: true, writable: false, configurable: false, enumerable: false });
          const refuse = (what) => () => Promise.reject(new DOMException(what + ' is not allowed in the model\'s tab.', 'NotAllowedError'));
          const lock = (target, name, value) => { try { Object.defineProperty(target, name, { value, writable: false, configurable: false }); } catch (_) {} };
          for (const name of ['requestFullscreen', 'webkitRequestFullscreen', 'webkitRequestFullScreen']) if (name in Element.prototype) lock(Element.prototype, name, refuse('Fullscreen'));
          lock(Element.prototype, 'requestPointerLock', refuse('Pointer lock'));
          if (globalThis.Clipboard) { lock(Clipboard.prototype, 'writeText', refuse('Writing to the clipboard')); lock(Clipboard.prototype, 'write', refuse('Writing to the clipboard')); lock(Clipboard.prototype, 'readText', refuse('Reading the clipboard')); lock(Clipboard.prototype, 'read', refuse('Reading the clipboard')); }
          const exec = Document.prototype.execCommand;
          lock(Document.prototype, 'execCommand', function execCommand(command, ...rest) { return /^(copy|cut|paste)$/i.test(String(command)) ? false : exec.call(this, command, ...rest); });
          for (const type of ['copy', 'cut']) globalThis.addEventListener(type, (event) => { event.stopImmediatePropagation(); }, true);
        })();
        """;

    /// <summary>8.4 passkeys in preview and agent tabs: WebAuthn requests are refused at once (Windows Hello never opens).</summary>
    public static string PasskeyDenyScript { get; } = """
        (() => {
          'use strict';
          if (!globalThis.CredentialsContainer || Object.prototype.hasOwnProperty.call(globalThis, '__nthPasskeysOff')) return;
          Object.defineProperty(globalThis, '__nthPasskeysOff', { value: true, writable: false, configurable: false, enumerable: false });
          const refuse = () => Promise.reject(new DOMException('Passkeys are not allowed in this tab.', 'NotAllowedError'));
          const proto = CredentialsContainer.prototype;
          for (const name of ['get', 'create']) {
            const original = proto[name];
            if (typeof original !== 'function') continue;
            try { Object.defineProperty(proto, name, { value: function (options) { return options && options.publicKey ? refuse() : original.call(this, options); }, writable: false, configurable: false }); } catch (_) {}
          }
          if (globalThis.PublicKeyCredential) {
            for (const name of ['isUserVerifyingPlatformAuthenticatorAvailable', 'isConditionalMediationAvailable']) {
              try { Object.defineProperty(PublicKeyCredential, name, { value: () => Promise.resolve(false), writable: false, configurable: false }); } catch (_) {}
            }
          }
        })();
        """;

    /// <summary>The document scripts a view of this kind gets (none for sandboxed content, which W13 hardens).</summary>
    public static IReadOnlyList<string> DocumentScriptsFor(WebSurfaceKind kind) => kind switch
    {
        WebSurfaceKind.AgentTab => [AgentScript, PasskeyDenyScript],
        WebSurfaceKind.PreviewTab => [PointerLockGateScript, PasskeyDenyScript],
        WebSurfaceKind.VisusTab or WebSurfaceKind.VisusPrivateTab => [PointerLockGateScript],
        _ => [],
    };

    /// <summary>
    /// The DevTools Protocol switches sent when the platform view attaches, in order: the pointer-lock binding for Visus pages;
    /// for agent tabs, file chooser interception (the native dialog never opens; 8.4); for preview and agent tabs, the WebAuthn
    /// domain with no authenticator, so a page that slips past the script still never reaches Windows Hello.
    /// </summary>
    public static IReadOnlyList<(string Method, JsonObject Params)> AttachCallsFor(WebSurfaceKind kind)
    {
        var calls = new List<(string, JsonObject)>();
        if (kind is WebSurfaceKind.VisusTab or WebSurfaceKind.VisusPrivateTab or WebSurfaceKind.PreviewTab)
            calls.Add(("Runtime.addBinding", new JsonObject { ["name"] = PointerLockBinding }));
        if (kind == WebSurfaceKind.AgentTab)
        {
            calls.Add(("Page.enable", new JsonObject()));
            calls.Add(("Page.setInterceptFileChooserDialog", new JsonObject { ["enabled"] = true }));
        }
        if (kind is WebSurfaceKind.AgentTab or WebSurfaceKind.PreviewTab)
            calls.Add(("WebAuthn.enable", new JsonObject { ["enableUI"] = false }));
        return calls;
    }

    /// <summary>The DevTools expression that answers the gate's request <paramref name="id"/>.</summary>
    public static string AnswerExpression(int id, bool allow) =>
        $"typeof globalThis.{PointerLockAnswer} === 'function' && globalThis.{PointerLockAnswer}({id.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {(allow ? "true" : "false")})";

    /// <summary>Releases a locked pointer in the page (Esc, 8.4 "Esc always releases").</summary>
    public const string ExitPointerLockExpression = "document.pointerLockElement && document.exitPointerLock()";

    /// <summary>Ends a page's fullscreen (Esc or Ctrl+Shift+\, or fullscreen in an agent tab).</summary>
    public const string ExitFullscreenScript = "document.fullscreenElement && document.exitFullscreen().catch(() => {})";

    /// <summary>A binding call from the gate: "ask" with an id, or "locked" and "released". Null for anything else.</summary>
    internal static (string Kind, int Id)? ReadBindingCall(string paramsJson, out int executionContextId)
    {
        executionContextId = 0;
        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var name) || name.GetString() != PointerLockBinding) return null;
            if (root.TryGetProperty("executionContextId", out var context) && context.TryGetInt32(out var contextId)) executionContextId = contextId;
            if (!root.TryGetProperty("payload", out var payloadValue) || payloadValue.GetString() is not { Length: > 0 and <= 200 } payload) return null;
            using var message = JsonDocument.Parse(payload);
            var body = message.RootElement;
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("kind", out var kindValue)) return null;
            var kind = kindValue.GetString();
            var id = body.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var parsed) ? parsed : 0;
            return kind switch
            {
                "ask" when id > 0 => ("ask", id),
                "locked" or "released" => (kind, 0),
                _ => null,
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A <c>Page.fileChooserOpened</c> event's frame, mode and element.</summary>
    internal static WebFileChooser? ReadFileChooser(string paramsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            var root = document.RootElement;
            var frame = root.TryGetProperty("frameId", out var f) ? f.GetString() ?? "" : "";
            var mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? "selectSingle" : "selectSingle";
            int? node = root.TryGetProperty("backendNodeId", out var n) && n.TryGetInt32(out var id) ? id : null;
            return new WebFileChooser(frame, mode, node);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
