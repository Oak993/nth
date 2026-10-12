// PORT-ONLY: web surfaces W11, the agent overlay inside a driven page (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "In-page
// overlay: an injected, origin-guarded script draws the silver Tactus pointer with the model's badge, a click ring and a faint
// inner glow on the page edge. It is hidden from the model's own screenshots (removed before capture)", 8.1 "scripts ... check
// location.origin first and do nothing elsewhere", 10 "Reduce Motion: ... the agent pointer jumps instead of gliding"; canvas
// section 3 `.tpt`, `.arr`, `.bdg`, `.rp`, `.web.driven::before`, `.scan`; ED-NEW-web-w11-1). No Swift counterpart.
//
// The script runs in an isolated world of the page's main frame (Page.createIsolatedWorld), so the page's own scripts can neither
// call it nor see its functions; its drawing sits in a closed shadow root on the document element (outside the body, so it is in
// no innerText), aria-hidden and inert, so it is in no accessibility tree and takes no input. Motion is transform and opacity only:
// the pointer glides on the canvas's curve (0.5 s, cubic-bezier(.25,1.08,.35,1)) and appears with a 0.3 s fade where it first
// stands; the ring scales from 0.3 to 1 in 0.45 s; the rim breathes over 2.2 s. With Reduce Motion the pointer jumps, the ring
// only fades at full size and the rim stands still. The pointer and badge are the Tactus pointer's own shapes: the canvas's arrow
// and silver, and the Tactus mark from TactusGlyph (never a flame or nth's own mark). It also reports the user's own clicks and
// keys in the page (trusted events while no synthetic one is in flight) through a CDP binding only this world can call, which is
// how "You took over Visus" starts.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl.Mark;

namespace Marrow.Services.Web;

public sealed class WebAgentOverlay
{
    /// <summary>The isolated world's name (Page.createIsolatedWorld worldName; Runtime.addBinding executionContextName).</summary>
    public const string WorldName = "nth-agent";

    /// <summary>The binding the overlay calls when the user clicks or types in the page.</summary>
    public const string Binding = "__nthAgentUser";

    /// <summary>The pointer's glide (canvas: transition transform .5s var(--glide)).</summary>
    public const int GlideMs = 500;

    /// <summary>The click ring (canvas: animation rg .45s).</summary>
    public const int RingMs = 450;

    private int? context;
    private bool bound;

    /// <summary>The overlay's whole script; it defines <c>window.__nthAgent</c> in its world and nothing in the page's.</summary>
    public static string Source { get; } = BuildSource();

    /// <summary>
    /// Makes sure the overlay is in the page's main frame for <paramref name="origin"/> and returns whether it is (false when the page
    /// is on another origin, or has no frame yet). Never throws: the overlay is decoration and a failure never fails a step.
    /// </summary>
    public async Task<bool> EnsureAsync(CdpBroker cdp, string origin, bool reduceMotion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cdp);
        var install = "window.__nthAgent ? window.__nthAgent.install(" + Js(origin) + "," + (reduceMotion ? "true" : "false") + ") : null";
        if (context is { } known && await Evaluate(cdp, known, install, cancellationToken).ConfigureAwait(true) is { } ready) return ready;
        context = null;
        try
        {
            var tree = await cdp.CallAsync("Page.getFrameTree", cancellationToken: cancellationToken).ConfigureAwait(true);
            if (!tree.TryGetProperty("frameTree", out var frameTree) || !frameTree.TryGetProperty("frame", out var frame)
                || !frame.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } frameId) return false;
            var world = await cdp.CallAsync("Page.createIsolatedWorld", new JsonObject
            {
                ["frameId"] = frameId,
                ["worldName"] = WorldName,
                ["grantUniveralAccess"] = false,
            }, cancellationToken: cancellationToken).ConfigureAwait(true);
            if (!world.TryGetProperty("executionContextId", out var made) || !made.TryGetInt32(out var contextId)) return false;
            if (!bound)
            {
                await cdp.CallAsync("Runtime.addBinding", new JsonObject { ["name"] = Binding, ["executionContextName"] = WorldName }, cancellationToken: cancellationToken).ConfigureAwait(true);
                bound = true;
            }
            await Evaluate(cdp, contextId, Source, cancellationToken).ConfigureAwait(true);
            context = contextId;
            return await Evaluate(cdp, contextId, install, cancellationToken).ConfigureAwait(true) ?? false;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The rim and the pointer on (the model is driving) or off.</summary>
    public Task Drive(CdpBroker cdp, bool on, CancellationToken cancellationToken) => Call(cdp, "drive(" + (on ? "true" : "false") + ")", cancellationToken);

    /// <summary>The pointer to (x, y) in CSS pixels of the viewport.</summary>
    public Task Move(CdpBroker cdp, double x, double y, CancellationToken cancellationToken) =>
        Call(cdp, "move(" + Num(x) + "," + Num(y) + ")", cancellationToken);

    /// <summary>The click ring where the pointer is.</summary>
    public Task Press(CdpBroker cdp, CancellationToken cancellationToken) => Call(cdp, "press()", cancellationToken);

    /// <summary>One reading sweep across the page.</summary>
    public Task Scan(CdpBroker cdp, CancellationToken cancellationToken) => Call(cdp, "scan()", cancellationToken);

    /// <summary>Synthetic input is in flight (true): the page's events now are nth's own, not the user's.</summary>
    public Task Busy(CdpBroker cdp, bool busy, CancellationToken cancellationToken) => Call(cdp, "busy(" + (busy ? "true" : "false") + ")", cancellationToken);

    /// <summary>Before a screenshot: the drawing leaves the page (display none), so the model never sees it.</summary>
    public Task Hide(CdpBroker cdp, CancellationToken cancellationToken) => Call(cdp, "hide()", cancellationToken);

    /// <summary>After the screenshot: the drawing comes back exactly as it was.</summary>
    public Task Show(CdpBroker cdp, CancellationToken cancellationToken) => Call(cdp, "show()", cancellationToken);

    /// <summary>The overlay goes from the page (the turn ended, Stop, the user took over).</summary>
    public Task Remove(CdpBroker cdp, CancellationToken cancellationToken) => Call(cdp, "remove()", cancellationToken);

    /// <summary>The page navigated: its world is gone, so the next step installs the overlay again.</summary>
    public void Reset() => context = null;

    private async Task Call(CdpBroker cdp, string call, CancellationToken cancellationToken)
    {
        if (context is not { } id) return;
        await Evaluate(cdp, id, "window.__nthAgent ? window.__nthAgent." + call + " : null", cancellationToken).ConfigureAwait(true);
    }

    // The boolean an evaluation returned; null when the world is gone or the value is not a boolean.
    private static async Task<bool?> Evaluate(CdpBroker cdp, int contextId, string expression, CancellationToken cancellationToken)
    {
        try
        {
            var result = await cdp.CallAsync("Runtime.evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["contextId"] = contextId,
                ["returnByValue"] = true,
                ["silent"] = true,
            }, cancellationToken: cancellationToken).ConfigureAwait(true);
            if (result.TryGetProperty("exceptionDetails", out _)) return null;
            if (result.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var inner))
                return inner.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
            return null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string Num(double value) => double.IsFinite(value) ? Math.Round(value, 1).ToString(CultureInfo.InvariantCulture) : "0";

    private static string Js(string text) => JsonSerializer.Serialize(text);

    private static string BuildSource()
    {
        var mark = TactusGlyph.SmallDrawing(TactusSmallState.Ready).Mark.ToSvg();
        const string css =
            ":host{all:initial}"
            + ".rim{position:fixed;inset:0;pointer-events:none;opacity:0;transition:opacity .3s;box-shadow:inset 0 0 0 1.5px rgba(150,150,170,.55),inset 0 0 26px rgba(120,120,150,.22)}"
            + ".rim.on{opacity:1;animation:rimb 2.2s ease-in-out infinite}"
            + "@keyframes rimb{0%,100%{opacity:1}50%{opacity:.5}}"
            + ".scan{position:fixed;inset:0;pointer-events:none;overflow:hidden}"
            + ".scan::before{content:\"\";position:absolute;top:0;bottom:0;left:0;width:45%;background:linear-gradient(100deg,transparent,rgba(110,110,150,.12) 55%,transparent);transform:translateX(-110%);opacity:0}"
            + ".scan.go::before{animation:scanmv 1.4s cubic-bezier(.32,.72,0,1) forwards}"
            + "@keyframes scanmv{0%{opacity:1;transform:translateX(-110%)}100%{opacity:1;transform:translateX(260%)}}"
            + ".tpt{position:fixed;left:0;top:0;pointer-events:none;opacity:0;transition-property:transform,opacity;transition-duration:.5s,.3s;transition-timing-function:cubic-bezier(.25,1.08,.35,1),ease;will-change:transform}"
            + ".tpt.on{opacity:1}"
            + ".arr{position:absolute;left:-2px;top:-1.5px;width:22px;height:26px;overflow:visible;filter:drop-shadow(0 2px 3px rgba(0,0,0,.45))}"
            + ".arr path{fill:url(#nth-agent-silver);stroke:rgba(16,16,20,.9);stroke-width:1.2;stroke-linejoin:round}"
            + ".bdg{position:absolute;left:13px;top:17px;width:14px;height:14px;border-radius:50%;display:grid;place-items:center;background:#1c1c20;box-shadow:0 0 0 1.5px rgba(14,14,16,.92),0 1px 3px rgba(0,0,0,.5)}"
            + ".bdg svg{width:9px;height:9px}.bdg path{fill:#F4F4F6}"
            + ".rp{position:absolute;left:-16px;top:-16px;width:32px;height:32px;border-radius:50%;border:1.5px solid rgba(60,60,80,.85);opacity:0;box-sizing:border-box}"
            + ".rp.go{animation:rg .45s cubic-bezier(.2,.7,.2,1)}"
            + "@keyframes rg{from{transform:scale(.3);opacity:1}to{transform:scale(1);opacity:0}}"
            + ".calm .rp.go{animation:rgcalm .15s linear}"
            + "@keyframes rgcalm{from{opacity:1}to{opacity:0}}"
            + ".calm .rim.on{animation:none}.calm .scan.go::before{animation:none}";
        const string script = """
(() => {
  if (window.__nthAgent) return true;
  let host = null, root = null, ptr = null, ring = null, rim = null, sweep = null, frame = null;
  let origin = null, reduce = false, busy = false, driving = false, shown = false;
  const ok = () => origin !== null && location.origin === origin;
  function make() {
    if (host && host.isConnected) return;
    host = document.createElement('nth-agent-overlay');
    host.setAttribute('aria-hidden', 'true');
    host.inert = true;
    host.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483647;pointer-events:none;display:block;contain:strict';
    root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = '<style>__CSS__</style><div class="f"><div class="rim"></div><div class="scan"></div>'
      + '<div class="tpt"><i class="rp"></i><svg class="arr" viewBox="0 0 22 26"><defs><linearGradient id="nth-agent-silver" x1="0" y1="0" x2=".6" y2="1">'
      + '<stop offset="0" stop-color="#FBFBFC"/><stop offset=".55" stop-color="#D9D9DE"/><stop offset="1" stop-color="#9C9CA4"/></linearGradient></defs>'
      + '<path d="M2 1.5v19.2l4.8-4.4 3.2 7.2 3.4-1.5-3.1-7.1 6.6-.3z"/></svg><span class="bdg"><svg viewBox="0 0 16 16"><path d="__MARK__"/></svg></span></div></div>';
    frame = root.querySelector('.f'); rim = root.querySelector('.rim'); sweep = root.querySelector('.scan');
    ptr = root.querySelector('.tpt'); ring = root.querySelector('.rp');
    frame.classList.toggle('calm', reduce);
    shown = false;
    (document.documentElement || document.body).appendChild(host);
  }
  const user = (e) => {
    if (!e.isTrusted || busy || !driving || !ok()) return;
    try { if (typeof __nthAgentUser === 'function') __nthAgentUser(e.type); } catch (_) {}
  };
  window.addEventListener('pointerdown', user, true);
  window.addEventListener('keydown', user, true);
  window.addEventListener('wheel', user, { capture: true, passive: true });
  window.__nthAgent = {
    install(o, r) {
      if (location.origin !== o) return false;
      origin = o;
      reduce = !!r || (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
      make();
      return true;
    },
    drive(on) {
      if (!ok()) return false;
      make();
      driving = !!on;
      rim.classList.toggle('on', driving);
      if (!driving) { ptr.classList.remove('on'); shown = false; }
      return true;
    },
    move(x, y) {
      if (!ok()) return false;
      make();
      ptr.style.transitionDuration = (reduce || !shown) ? '0ms, 300ms' : '500ms, 300ms';
      ptr.style.transform = 'translate(' + x + 'px,' + y + 'px)';
      if (!shown) { shown = true; void ptr.offsetWidth; ptr.classList.add('on'); }
      return true;
    },
    press() {
      if (!ok() || !ring) return false;
      ring.classList.remove('go'); void ring.offsetWidth; ring.classList.add('go');
      return true;
    },
    scan() {
      if (!ok() || !sweep || reduce) return false;
      sweep.classList.remove('go'); void sweep.offsetWidth; sweep.classList.add('go');
      return true;
    },
    busy(b) { busy = !!b; return true; },
    hide() { if (host) host.style.display = 'none'; return true; },
    show() { if (host) host.style.display = 'block'; return true; },
    remove() { driving = false; shown = false; if (host) host.remove(); host = null; return true; },
  };
  return true;
})()
""";
        return script.Replace("__CSS__", css.Replace("'", "\\'", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("__MARK__", mark, StringComparison.Ordinal);
    }
}
