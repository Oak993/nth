// PORT-ONLY: web surfaces W11, the page-level web tools (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "How it acts: CDP
// events are trusted inside the renderer and need no OS cursor and no focus. The user's mouse and keys are never touched.
// Coordinates come from DOM.getContentQuads in CSS pixels. iframes are reached with Target.setAutoAttach {flatten: true} and
// per-session calls."; ED-NEW-web-w11-1). No Swift counterpart. Reads come from the accessibility tree (refs are stable per tab)
// or the page's text; every action reads the element's facts again at the moment it runs (the browser's accessible name, the
// DOM's own attributes and its form), so a guard judges what is on the page now, never what the model remembers; the element
// under the click point is judged too, so a cover over the target cannot carry a click past a guard. The overlay is hidden
// around every screenshot. ED-NEW-web-sec-agent-1 (security review H1): another site's frame runs in its own process, and the page
// around it sees only an <iframe>; the element under a point and the focused field are read inside that frame through its own
// session, and a frame nth cannot reach is sealed: nothing is pressed or typed into it.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Localisation;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public sealed partial class WebAgentEngine
{
    private const string Group = "nth-agent";

    /// <summary>How many frames deep the hit test and the focus are followed (deeper is sealed).</summary>
    private const int MaxFrameDepth = 6;

    private const string FrameRefusal = "That point or the focused field is inside a frame of another site that nth cannot read, so nth does not act there. "
        + "Read the page again (web_read shows the elements of the frames it can reach) and act on a ref.";

    // ED-NEW-web-sec-agent-2 (security review H2; DESIGN 8.4 "Clipboard read: agent tabs denied").
    private const string ClipboardRefusal = "nth never uses the clipboard for a model (paste, copy or cut): it would read or replace the user's own clipboard. "
        + "Use web_read to read text and web_type to type it.";

    private const string SecretSelector =
        "input[type=password],input[autocomplete~=current-password],input[autocomplete~=new-password],input[autocomplete~=one-time-code],input[autocomplete*=cc-]";

    // The facts of the element a node belongs to (its closest control), read with the DOM's own methods so a page's own
    // properties (a field named "action", a clobbered form) cannot change them.
    private const string FactsJs = """
        function () {
          const ga = (el, n) => Element.prototype.getAttribute.call(el, n);
          const close = (el, s) => Element.prototype.closest.call(el, s);
          const start = this.nodeType === 1 ? this : this.parentElement;
          if (!start) return null;
          const e = close(start, 'button,a[href],input,select,textarea,summary,iframe,[role=button],[role=link],[role=menuitem],[role=menuitemcheckbox],[role=menuitemradio],[role=tab],[role=checkbox],[role=radio],[role=switch],[role=option],[role=combobox],[role=textbox],[contenteditable=""],[contenteditable=true]') || start;
          const tag = e.tagName.toLowerCase();
          const type = (ga(e, 'type') || '').toLowerCase() || null;
          const form = close(e, 'form') || (e.form instanceof HTMLFormElement ? e.form : null);
          const submits = !!form && ((tag === 'button' && (!type || type === 'submit')) || (tag === 'input' && (type === 'submit' || type === 'image')));
          const clean = (t) => String(t || '').replace(/\s+/g, ' ').trim().slice(0, 200);
          const abs = (u) => { try { return new URL(u, document.baseURI).href; } catch (x) { return null; } };
          let label = null;
          try { if (e.labels && e.labels.length) label = clean(Array.from(e.labels).map(l => l.innerText).join(' ')) || null; } catch (x) {}
          const by = ga(e, 'aria-labelledby');
          if (!label && by) label = clean(by.split(/\s+/).map(i => { const t = document.getElementById(i); return t ? t.innerText : ''; }).join(' ')) || null;
          const field = tag === 'input' || tag === 'textarea' || tag === 'select';
          const text = clean(tag === 'input' && ['submit', 'button', 'reset'].includes(type || '') ? e.value : field ? '' : (e.innerText || e.textContent));
          let action = null, method = null;
          if (form) {
            const fa = submits ? ga(e, 'formaction') : null;
            const a = fa ? fa : ga(form, 'action');
            action = a ? abs(a) : location.href;
            method = String((submits ? ga(e, 'formmethod') : null) || ga(form, 'method') || 'get').toLowerCase();
          }
          const link = close(e, 'a[href]');
          const role = (ga(e, 'role') || '').toLowerCase();
          const editable = !!(e.isContentEditable || tag === 'textarea' || (tag === 'input' && !['button', 'submit', 'reset', 'checkbox', 'radio', 'file', 'image', 'hidden', 'range', 'color'].includes(type || 'text')));
          const box = tag === 'input' && (type === 'checkbox' || type === 'radio');
          const rows = [];
          if (form) {
            for (const f of Array.from(form.querySelectorAll('input,select,textarea')).slice(0, 40)) {
              if (rows.length >= 6) break;
              const ft = (ga(f, 'type') || '').toLowerCase(), fac = (ga(f, 'autocomplete') || '').toLowerCase();
              if (['password', 'hidden', 'submit', 'button', 'reset', 'file', 'image'].includes(ft) || /password|one-time-code|cc-/.test(fac)) continue;
              const ftag = f.tagName.toLowerCase();
              let v = ftag === 'select' ? (f.selectedOptions && f.selectedOptions[0] ? f.selectedOptions[0].text : '') : (ft === 'checkbox' || ft === 'radio') ? (f.checked ? 'on' : '') : f.value;
              v = clean(v);
              if (!v) continue;
              let n = null;
              try { if (f.labels && f.labels.length) n = clean(f.labels[0].innerText); } catch (x) {}
              n = n || clean(ga(f, 'aria-label')) || clean(ga(f, 'placeholder')) || clean(ga(f, 'name')) || 'Field';
              rows.push([n.slice(0, 40), v.slice(0, 80)]);
            }
          }
          return {
            tag, type, role, name: text || clean(ga(e, 'aria-label')) || clean(ga(e, 'title')) || clean(ga(e, 'alt')) || clean(ga(e, 'placeholder')),
            ariaLabel: ga(e, 'aria-label'), id: e.id || null, nameAttr: ga(e, 'name'), label, title: ga(e, 'title'),
            autocomplete: ga(e, 'autocomplete'), inForm: !!form, submits, formAction: action, formMethod: method,
            href: link ? link.href : null, className: typeof e.className === 'string' ? e.className.slice(0, 200) : null,
            frameUrl: tag === 'iframe' ? (abs(ga(e, 'src') || '') || location.href) : location.href,
            checked: box ? !!e.checked : ['checkbox', 'radio', 'switch', 'menuitemcheckbox', 'menuitemradio'].includes(role) ? ga(e, 'aria-checked') === 'true' : null,
            editable, contentEditable: !!e.isContentEditable, isSelect: tag === 'select',
            options: tag === 'select' ? Array.from(e.options).slice(0, 30).map(o => clean(o.text)) : null, rows
          };
        }
        """;

    // The focused element, through open shadow roots and same-site frames.
    private const string ActiveJs = """
        (() => { let a = document.activeElement; for (let i = 0; i < 8 && a; i++) { if (a.shadowRoot && a.shadowRoot.activeElement) a = a.shadowRoot.activeElement; else if (a.tagName === 'IFRAME') { try { const d = a.contentDocument; if (!d || !d.activeElement) break; a = d.activeElement; } catch (x) { break; } } else break; } return a; })()
        """;

    // Focus, then select what a text field holds (typing replaces it); a rich-text field gets its caret at the end.
    private const string FocusJs = """
        function () {
          this.focus();
          const tag = this.tagName;
          if ((tag === 'INPUT' || tag === 'TEXTAREA') && typeof this.select === 'function') { this.select(); return 'field'; }
          if (this.isContentEditable) { const r = document.createRange(); r.selectNodeContents(this); r.collapse(false); const s = getSelection(); s.removeAllRanges(); s.addRange(r); return 'rich'; }
          return 'other';
        }
        """;

    private const string SelectJs = """
        function (want) {
          const w = String(want).trim().toLowerCase();
          const opts = Array.from(this.options || []).filter(o => !o.disabled);
          const o = opts.find(o => o.value === want) || opts.find(o => o.text.trim().toLowerCase() === w) || opts.find(o => o.text.trim().toLowerCase().includes(w));
          if (!o) return null;
          this.value = o.value;
          o.selected = true;
          this.dispatchEvent(new Event('input', { bubbles: true }));
          this.dispatchEvent(new Event('change', { bubbles: true }));
          return o.text.trim();
        }
        """;

    // The page's text (or one element's) and its links.
    private const string TextJs = """
        function () { const r = this.nodeType === 9 ? (this.body || this.documentElement) : this; return { text: (r && r.innerText) || '', links: Array.from(r && r.querySelectorAll ? r.querySelectorAll('a[href]') : []).slice(0, 500).map(a => a.href) }; }
        """;

    // The page's HTML (or one element's) without field values that hold secrets, tokens or what the user typed, and without scripts.
    private const string HtmlJs = """
        function () {
          const r = this.nodeType === 9 ? this.documentElement : this;
          const c = r.cloneNode(true);
          for (const i of c.querySelectorAll('input')) { const t = (i.getAttribute('type') || '').toLowerCase(), a = (i.getAttribute('autocomplete') || '').toLowerCase(); if (t === 'password' || t === 'hidden' || /password|one-time-code|cc-/.test(a)) i.removeAttribute('value'); }
          for (const s of c.querySelectorAll('script,style,noscript,template')) s.textContent = '';
          return { text: c.outerHTML || '', links: Array.from(r.querySelectorAll ? r.querySelectorAll('a[href]') : []).slice(0, 500).map(a => a.href) };
        }
        """;

    private async Task<WebToolResult> Read(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var mode = (Str(s.Args, "mode") ?? "ax").Trim().ToLowerInvariant();
        if (mode is not ("ax" or "text" or "html")) return Fail(WebErrorCodes.BadArgs, "mode is ax, text or html.");
        WebNodeKey? root = Str(s.Args, "ref") is { Length: > 0 } reference ? RefOf(state, reference) : null;
        if (await Hold(s, state) is { } held) return held;
        if (state.Tab.Shared && await Site(s, state) is { } refused) return refused;
        var budget = (int)Math.Clamp(Num(s.Args, "max_tokens") ?? 4000, 200, 20_000) * 4;
        Report(UiText.Get("agent.step.reading_page"), state.Short);
        if (await Ensure(state, s)) await state.Overlay.Scan(state.Cdp!, s.Token);
        var origin = WebPolicy.OriginOf(state.Tab.Url);
        var cdp = state.Cdp!;
        string body;
        IReadOnlyList<string> links;
        string? more = null;
        try
        {
            if (mode == "ax")
            {
                var secrets = await Secrets(state, s.Token);
                var nodes = WebAgentTree.Parse(await cdp.CallAsync("Accessibility.getFullAXTree", null, root?.Session, s.Token));
                var tree = WebAgentTree.Format(nodes, state.Refs, root?.Session, root?.BackendId, secrets, budget, state.Tab.Url);
                var text = new StringBuilder(tree.Text);
                var all = new List<string>(tree.Links);
                if (root is null)
                {
                    foreach (var frame in await Frames(state, s.Token))
                    {
                        var left = budget - text.Length;
                        if (left < 400) break;
                        var frameNodes = WebAgentTree.Parse(await cdp.CallAsync("Accessibility.getFullAXTree", null, frame.SessionId, s.Token));
                        var part = WebAgentTree.Format(frameNodes, state.Refs, frame.SessionId, null, null, left, frame.Url);
                        if (part.Text.Length == 0) continue;
                        text.Append("\nframe ").Append(frame.Url).Append(":\n").Append(part.Text);
                        all.AddRange(part.Links);
                    }
                }
                body = text.ToString();
                links = all;
            }
            else
            {
                var page = await PageText(state, root, mode == "html" ? HtmlJs : TextJs, s.Token);
                body = page.Text.Length <= budget ? page.Text : page.Text[..budget];
                if (page.Text.Length > budget) more = N(page.Text.Length - budget) + " more characters are not shown: raise max_tokens, or read one ref.";
                links = page.Links;
            }
        }
        finally
        {
            await ReleaseObjects(state);
        }
        Saw(origin, body, links);
        var header = state.Short + " " + state.Tab.Url + (state.Tab.Loading ? " (still loading)" : "");
        return new WebToolResult(header + "\n" + WebAgentText.Untrusted(origin, "Title: " + state.Tab.Title + "\n" + body) + (more is null ? "" : "\n" + more)) { ReadOrigin = origin };
    }

    private async Task<WebToolResult> FindOnPage(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var query = Str(s.Args, "query")?.Trim();
        if (string.IsNullOrEmpty(query)) return Fail(WebErrorCodes.BadArgs, "Pass query.");
        if (await Hold(s, state) is { } held) return held;
        if (state.Tab.Shared && await Site(s, state) is { } refused) return refused;
        Report(UiText.Format("agent.step.finding", Clip(query, 30)), state.Short);
        var cdp = state.Cdp!;
        var secrets = await Secrets(state, s.Token);
        var lines = new List<string>(WebAgentTree.Find(WebAgentTree.Parse(await cdp.CallAsync("Accessibility.getFullAXTree", null, null, s.Token)), state.Refs, query, null, secrets));
        foreach (var frame in await Frames(state, s.Token))
        {
            if (lines.Count >= 30) break;
            var nodes = WebAgentTree.Parse(await cdp.CallAsync("Accessibility.getFullAXTree", null, frame.SessionId, s.Token));
            lines.AddRange(WebAgentTree.Find(nodes, state.Refs, query, frame.SessionId, null, 30 - lines.Count));
        }
        var origin = WebPolicy.OriginOf(state.Tab.Url);
        var body = string.Join('\n', lines);
        Saw(origin, body, []);
        if (lines.Count == 0) return new WebToolResult("Nothing on " + state.Short + " matches that. Try other words, or web_read the page.") { ReadOrigin = origin };
        return new WebToolResult(N(lines.Count) + " matches on " + state.Short + ":\n" + WebAgentText.Untrusted(origin, body)) { ReadOrigin = origin };
    }

    private async Task<WebToolResult> Screenshot(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        WebNodeKey? key = Str(s.Args, "ref") is { Length: > 0 } reference ? RefOf(state, reference) : null;
        var full = Bool(s.Args, "full");
        if (await Hold(s, state) is { } held) return held;
        if (state.Tab.Shared && await Site(s, state) is { } refused) return refused;
        Report(UiText.Get("agent.step.screenshot"), state.Short);
        var cdp = state.Cdp!;
        var metrics = await cdp.CallAsync("Page.getLayoutMetrics", cancellationToken: s.Token);
        var visual = Prop(metrics, "cssVisualViewport");
        var content = Prop(metrics, "cssContentSize");
        double cssWidth = Dbl(visual, "clientWidth") ?? 0, cssHeight = Dbl(visual, "clientHeight") ?? 0;
        var shot = new JsonObject { ["format"] = "jpeg", ["quality"] = 75 };
        if (key is { } node)
        {
            if (await Box(state, node, scroll: true, s.Token) is not { } box) return Fail(WebErrorCodes.StaleRef, Str(s.Args, "ref") + " is not visible on the page. Read it again.");
            metrics = await cdp.CallAsync("Page.getLayoutMetrics", cancellationToken: s.Token);
            visual = Prop(metrics, "cssVisualViewport");
            cssWidth = Math.Max(1, box.Width);
            cssHeight = Math.Max(1, Math.Min(box.Height, 8000));
            shot["clip"] = new JsonObject { ["x"] = box.X + (Dbl(visual, "pageX") ?? 0), ["y"] = box.Y + (Dbl(visual, "pageY") ?? 0), ["width"] = cssWidth, ["height"] = cssHeight, ["scale"] = 1 };
            shot["captureBeyondViewport"] = true;
        }
        else if (full)
        {
            cssWidth = Math.Max(1, Dbl(content, "width") ?? cssWidth);
            cssHeight = Math.Max(1, Math.Min(Dbl(content, "height") ?? cssHeight, 8000));
            shot["clip"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = cssWidth, ["height"] = cssHeight, ["scale"] = 1 };
            shot["captureBeyondViewport"] = true;
        }
        JsonElement result;
        await state.Overlay.Hide(cdp, s.Token);
        try
        {
            result = await cdp.CallAsync("Page.captureScreenshot", shot, cancellationToken: s.Token);
        }
        finally
        {
            await Swallow(state.Overlay.Show(cdp, CancellationToken.None));
        }
        if (Text(result, "data") is not { Length: > 0 } data) return Fail(WebErrorCodes.PageFailed, "The page gave no picture.");
        var jpeg = Convert.FromBase64String(data);
        var (width, height) = JpegSize(jpeg) ?? ((int)Math.Round(cssWidth), (int)Math.Round(cssHeight));
        var origin = WebPolicy.OriginOf(state.Tab.Url);
        Saw(origin, null, []);
        var text = "Screenshot of " + state.Short + ": a " + N(width) + " x " + N(height) + " px picture of " + N(cssWidth) + " x " + N(cssHeight) + " CSS px. web_click x, y are CSS px of the visible page.";
        if (!s.Caller.Vision) text += " This model cannot see pictures: use web_read for the page's content.";
        return new WebToolResult(text) { Jpeg = jpeg, ImageWidth = width, ImageHeight = height, ReadOrigin = origin };
    }

    private async Task<WebToolResult> Click(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var reference = Str(s.Args, "ref")?.Trim();
        WebNodeKey? key = reference is { Length: > 0 } ? RefOf(state, reference) : null;
        var x = Num(s.Args, "x");
        var y = Num(s.Args, "y");
        if (key is null && (x is null || y is null)) return Fail(WebErrorCodes.BadArgs, "Pass ref, or x and y.");
        if (await Hold(s, state) is { } held) return held;
        if (await Site(s, state) is { } refused) return refused;
        try
        {
            var origin = WebPolicy.OriginOf(state.Tab.Url);
            Element? element = null;
            (double X, double Y) point;
            if (key is { } node)
            {
                element = await Facts(state, node, s.Token) ?? throw new Refusal(Gone(reference!));
                point = await Point(state, node, scroll: true, s.Token)
                    ?? throw new Refusal(Fail(WebErrorCodes.StaleRef, reference + " is not visible on the page, so it cannot be clicked. Scroll to it, or read the page again."));
            }
            else
            {
                point = (x!.Value, y!.Value);
            }
            var hit = await Hit(state, point, s.Token);
            if (hit is { Sealed: true }) return Fail(WebErrorCodes.Denied, FrameRefusal);
            element ??= hit ?? throw new Refusal(Fail(WebErrorCodes.BadArgs, "Nothing is at that point of the visible page."));
            var covered = hit is not null && key is not null && !SameElement(hit.Facts, element.Facts);
            var (verdict, judged) = Strictest((WebAgentPolicy.CheckPress(element.Facts, origin), element), covered ? (WebAgentPolicy.CheckPress(hit!.Facts, origin), hit) : (WebGuardVerdict.Pass, hit!));
            var what = reference ?? N(point.X) + ", " + N(point.Y);
            if (await Guard(s, state, verdict, judged.Facts, "click|" + Identity(judged.Facts), null, judged.Rows, new WebPoint(point.X, point.Y)) is { } stop) return stop;
            Report(UiText.Format("agent.step.pressing", PillName(element, false)), state.Short);
            using (BeginStep(state))
            {
                await Drive(state, s);
                await ClickAt(state, point, s);
                await Settle(state, s);
            }
            var said = (key is null ? "Clicked at " + what : "Clicked " + what) + " in " + state.Short + ".";
            if (covered) said += " Another element covers its centre, and the click went to it.";
            return Acted(state, said, NameBlock(origin, element, covered ? hit : null));
        }
        finally
        {
            await ReleaseObjects(state);
        }
    }

    private async Task<WebToolResult> TypeText(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var reference = Str(s.Args, "ref")?.Trim();
        var key = RefOf(state, reference);
        var text = Str(s.Args, "text");
        if (text is null) return Fail(WebErrorCodes.BadArgs, "Pass text.");
        if (text.Length > 20_000) return Fail(WebErrorCodes.BadArgs, "text is at most 20,000 characters.");
        var submit = Bool(s.Args, "submit");
        if (await Hold(s, state) is { } held) return held;
        if (await Site(s, state) is { } refused) return refused;
        try
        {
            var origin = WebPolicy.OriginOf(state.Tab.Url);
            var element = await Facts(state, key, s.Token) ?? throw new Refusal(Gone(reference!));
            var typing = WebAgentPolicy.CheckType(element.Facts);
            if (typing.Kind == WebGuardKind.Deny) return Fail(WebErrorCodes.Denied, typing.Reason!);
            if (element.IsSelect) return Fail(WebErrorCodes.BadArgs, reference + " is a list of options: use web_select.");
            if (!element.Editable) return Fail(WebErrorCodes.BadArgs, reference + " is not a text field: use web_click or web_select.");
            if (submit && await Guard(s, state, EnterVerdict(element, origin), element.Facts, "enter|" + Identity(element.Facts), null, element.Rows) is { } stop) return stop;
            Report(UiText.Format("agent.step.filling_in", PillName(element, true)), state.Short);
            using (BeginStep(state))
            {
                await Drive(state, s);
                if (await Point(state, key, scroll: true, s.Token) is { } point) await Pointer(state, point, s);
                await CallOn(state, key, FocusJs, null, s.Token);
                // The field that has focus now is the one that gets the text: a page that moves focus cannot steer it into a secret.
                if (await ActiveFacts(state, s.Token) is { } active)
                {
                    if (active.Sealed) return Fail(WebErrorCodes.Denied, FrameRefusal);
                    if (WebAgentPolicy.CheckType(active.Facts) is { Kind: WebGuardKind.Deny } moved) return Fail(WebErrorCodes.Denied, moved.Reason!);
                }
                await state.Overlay.Busy(state.Cdp!, true, s.Token);
                try
                {
                    if (text.Length > 0) await state.Cdp!.CallAsync("Input.insertText", new JsonObject { ["text"] = text }, cancellationToken: s.Token);
                    if (submit) await Press(state.Cdp!, WebAgentKeys.Parse("enter").Presses[0], s.Token);
                }
                finally
                {
                    await Swallow(state.Overlay.Busy(state.Cdp!, false, CancellationToken.None));
                }
                if (submit) await Settle(state, s);
            }
            var said = "Typed " + N(text.Length) + " characters into " + reference + " in " + state.Short + (submit ? " and pressed Enter" : "") + ".";
            return Acted(state, said, NameBlock(origin, element, null));
        }
        finally
        {
            await ReleaseObjects(state);
        }
    }

    private async Task<WebToolResult> Keys(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var (presses, error) = WebAgentKeys.Parse(Str(s.Args, "keys"));
        if (error is not null) return Fail(WebErrorCodes.BadArgs, error);
        if (await Hold(s, state) is { } held) return held;
        if (await Site(s, state) is { } refused) return refused;
        try
        {
            var origin = WebPolicy.OriginOf(state.Tab.Url);
            Report(UiText.Get("agent.step.pressing_keys"), state.Short);
            var pressedEnter = false;
            using (BeginStep(state))
            {
                await Drive(state, s);
                for (var i = 0; i < presses.Count; i++)
                {
                    var press = presses[i];
                    var done = i == 0 ? "" : "Pressed the first " + N(i) + " of " + N(presses.Count) + " keys. ";
                    if (TookOver(state)) return Fail(WebErrorCodes.TookOver, done + "The user took over " + state.Short + ". Do not use that tab until they resume.");
                    // ED-NEW-web-sec-agent-2: a modifier never takes a key past the checks. A clipboard chord never runs; Enter and Space press
                    // what has focus, whatever the modifiers; anything that can change a field (a character, Backspace, Delete, a chord on a
                    // character key) is judged on the focused field.
                    if (WebAgentKeys.TouchesClipboard(press)) return Fail(WebErrorCodes.Denied, done + ClipboardRefusal);
                    var commands = (press.Modifiers & (WebAgentKeys.Ctrl | WebAgentKeys.Alt | WebAgentKeys.Meta)) != 0;
                    if (press.IsEnter || WebAgentKeys.Activates(press) || WebAgentKeys.Edits(press))
                    {
                        var active = await ActiveFacts(state, s.Token);
                        if (active is { Sealed: true }) return Fail(WebErrorCodes.Denied, done + FrameRefusal);
                        if (active is not null)
                        {
                            var verdict = press.IsEnter
                                ? commands ? Strictest((EnterVerdict(active, origin), active), (WebGuardVerdict.Ask(WebGuardCategory.Send, "send"), active)).Verdict : EnterVerdict(active, origin)
                                : WebAgentKeys.Activates(press) && Pressable(active.Facts) ? WebAgentPolicy.CheckPress(active.Facts, origin)
                                : WebAgentPolicy.CheckType(active.Facts);
                            if (verdict.Kind == WebGuardKind.Deny) return Fail(WebErrorCodes.Denied, done + verdict.Reason);
                            if (await Guard(s, state, verdict, active.Facts, "press|" + Identity(active.Facts), null, active.Rows) is { } stop)
                            {
                                if (stop.Error == WebErrorCodes.WaitingForUser && i > 0)
                                    return Fail(WebErrorCodes.WaitingForUser, done + "The next key waits for the user's OK in Visus: call web_key again with only the rest: \"" + Remaining(presses, i) + "\".");
                                return i > 0 ? stop with { Text = done + stop.Text } : stop;
                            }
                        }
                    }
                    await state.Overlay.Busy(state.Cdp!, true, s.Token);
                    try
                    {
                        await Press(state.Cdp!, press, s.Token);
                    }
                    finally
                    {
                        await Swallow(state.Overlay.Busy(state.Cdp!, false, CancellationToken.None));
                    }
                    pressedEnter |= press.IsEnter || press.Key == " ";
                }
                if (pressedEnter) await Settle(state, s);
            }
            return Acted(state, "Pressed " + N(presses.Count) + (presses.Count == 1 ? " key" : " keys") + " in " + state.Short + ".", null);
        }
        finally
        {
            await ReleaseObjects(state);
        }
    }

    private async Task<WebToolResult> Scroll(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        if (Num(s.Args, "dy") is not { } dy || double.IsNaN(dy)) return Fail(WebErrorCodes.BadArgs, "Pass dy in CSS px (negative scrolls up).");
        dy = Math.Clamp(dy, -20_000, 20_000);
        WebNodeKey? key = Str(s.Args, "ref") is { Length: > 0 } reference ? RefOf(state, reference) : null;
        if (await Hold(s, state) is { } held) return held;
        (double X, double Y) point;
        if (key is { } node) point = await Point(state, node, scroll: false, s.Token) ?? await Centre(state, s.Token);
        else point = await Centre(state, s.Token);
        Report(dy < 0 ? UiText.Get("agent.step.scrolling_up") : UiText.Get("agent.step.scrolling_down"), state.Short);
        using (BeginStep(state))
        {
            await Drive(state, s);
            await Pointer(state, point, s);
            await state.Overlay.Busy(state.Cdp!, true, s.Token);
            try
            {
                await state.Cdp!.CallAsync("Input.dispatchMouseEvent", new JsonObject
                {
                    ["type"] = "mouseWheel",
                    ["x"] = point.X,
                    ["y"] = point.Y,
                    ["deltaX"] = 0,
                    ["deltaY"] = dy,
                    ["pointerType"] = "mouse",
                }, cancellationToken: s.Token);
            }
            finally
            {
                await Swallow(state.Overlay.Busy(state.Cdp!, false, CancellationToken.None));
            }
            await hub.Clock.Delay(150, s.Token);
        }
        return new WebToolResult("Scrolled " + state.Short + (dy < 0 ? " up" : " down") + " by " + N(Math.Abs(dy)) + " px.");
    }

    private async Task<WebToolResult> SelectOption(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var reference = Str(s.Args, "ref")?.Trim();
        var key = RefOf(state, reference);
        var value = Str(s.Args, "value") ?? (s.Args["value"] is JsonValue raw && raw.TryGetValue<bool>(out var flag) ? (flag ? "true" : "false") : null);
        if (value is null) return Fail(WebErrorCodes.BadArgs, "Pass value.");
        if (await Hold(s, state) is { } held) return held;
        if (await Site(s, state) is { } refused) return refused;
        try
        {
            var origin = WebPolicy.OriginOf(state.Tab.Url);
            var element = await Facts(state, key, s.Token) ?? throw new Refusal(Gone(reference!));
            var typing = WebAgentPolicy.CheckType(element.Facts);
            if (typing.Kind == WebGuardKind.Deny) return Fail(WebErrorCodes.Denied, typing.Reason!);
            if (element.IsSelect)
            {
                Report(UiText.Format("agent.step.choosing", PillName(element, true)), state.Short);
                JsonElement chosen;
                using (BeginStep(state))
                {
                    await Drive(state, s);
                    if (await Point(state, key, scroll: true, s.Token) is { } point) await Pointer(state, point, s);
                    chosen = await CallOn(state, key, SelectJs, value, s.Token);
                }
                if (chosen.ValueKind != JsonValueKind.String)
                    return Fail(WebErrorCodes.BadArgs, reference + " has no option like that. Its options:\n" + WebAgentText.Untrusted(origin, string.Join(" | ", element.Options)));
                return new WebToolResult("Chose an option in " + reference + " of " + state.Short + ":\n" + WebAgentText.Untrusted(origin, chosen.GetString() ?? ""));
            }
            if (element.Checked is { } isChecked)
            {
                bool? want = value.Trim().ToLowerInvariant() switch
                {
                    "true" or "on" or "yes" or "checked" or "1" => true,
                    "false" or "off" or "no" or "unchecked" or "0" => false,
                    _ => null,
                };
                if (want is null) return Fail(WebErrorCodes.BadArgs, reference + " is a checkbox or radio button: value is true or false.");
                if (want == isChecked) return new WebToolResult(reference + " is already " + (isChecked ? "on" : "off") + ".");
                if (await Guard(s, state, WebAgentPolicy.CheckPress(element.Facts, origin), element.Facts, "click|" + Identity(element.Facts), null, element.Rows) is { } stop) return stop;
                var point = await Point(state, key, scroll: true, s.Token) ?? throw new Refusal(Fail(WebErrorCodes.StaleRef, reference + " is not visible on the page. Scroll to it, or read the page again."));
                Report(UiText.Format("agent.step.choosing", PillName(element, true)), state.Short);
                using (BeginStep(state))
                {
                    await Drive(state, s);
                    await ClickAt(state, point, s);
                    await Settle(state, s);
                }
                return Acted(state, "Turned " + reference + (want.Value ? " on" : " off") + " in " + state.Short + ".", NameBlock(origin, element, null));
            }
            return Fail(WebErrorCodes.BadArgs, reference + " is not a list of options, a checkbox or a radio button.");
        }
        finally
        {
            await ReleaseObjects(state);
        }
    }

    // ---- What an action is judged on ----

    // Enter in a field: a press of the control it is (a button, a link), a form submit in a form field, and a possible send in
    // a rich-text field (chat composers send on Enter); a new line in a text area otherwise.
    private static WebGuardVerdict EnterVerdict(Element element, string? origin)
    {
        var facts = element.Facts;
        if (Pressable(facts)) return WebAgentPolicy.CheckPress(facts, origin);
        var tag = facts.Tag.ToLowerInvariant();
        if (facts.InForm && element.Editable && tag == "input") return WebAgentPolicy.CheckSubmit(facts with { Submits = true }, origin);
        if (element.ContentEditable)
        {
            var named = WebAgentPolicy.CheckPress(facts, origin);
            return named.Kind == WebGuardKind.None ? WebGuardVerdict.Ask(WebGuardCategory.Send, "send") : named;
        }
        return WebGuardVerdict.Pass;
    }

    private static bool Pressable(WebElementFacts facts)
    {
        var tag = facts.Tag.ToLowerInvariant();
        var type = (facts.Type ?? "").ToLowerInvariant();
        return tag is "button" or "a" or "summary"
            || (tag == "input" && type is "submit" or "button" or "image" or "reset" or "checkbox" or "radio")
            || facts.Role.ToLowerInvariant() is "button" or "link" or "menuitem" or "tab" or "checkbox" or "radio" or "switch" or "option";
    }

    private static (WebGuardVerdict Verdict, Element Element) Strictest((WebGuardVerdict Verdict, Element Element) a, (WebGuardVerdict Verdict, Element Element) b) =>
        Rank(b.Verdict) > Rank(a.Verdict) ? b : a;

    private static int Rank(WebGuardVerdict verdict) => verdict.Kind switch
    {
        WebGuardKind.Deny => 2,
        WebGuardKind.Confirm => 1,
        _ => 0,
    };

    private static bool SameElement(WebElementFacts a, WebElementFacts b) =>
        string.Equals(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase) && a.Id == b.Id && a.NameAttribute == b.NameAttribute && a.AriaLabel == b.AriaLabel
        && a.Href == b.Href && a.ClassName == b.ClassName && a.Label == b.Label && a.FormAction == b.FormAction;

    // A key for the card of one element, so the same call again joins the same card.
    private static string Identity(WebElementFacts facts) => facts.Tag + "#" + facts.Id + "|" + facts.NameAttribute + "|" + Clip(facts.Name, 60);

    private static string Display(Element element) =>
        element.Facts.Name.Length > 0 ? element.Facts.Name
        : element.Facts.Label is { Length: > 0 } label ? label
        : element.Facts.Role.Length > 0 ? WebAgentTree.RoleWord(element.Facts.Role)
        : element.Facts.Tag.ToLowerInvariant() is "input" or "textarea" ? "field" : "element";

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    // The element's name in the pill's step line: the page's own words (first letter lowered for "Filling in the email"), else its role, else "field" or "element".
    private static string PillName(Element element, bool lowerFirst)
    {
        var own = element.Facts.Name.Length > 0 ? element.Facts.Name : element.Facts.Label is { Length: > 0 } label ? label : null;
        if (own is null) return Clip(PillWord(element), 40);
        var clipped = Clip(own, 40);
        return lowerFirst ? Lower(clipped) : clipped;
    }

    private static string PillWord(Element element) =>
        element.Facts.Role.Length > 0 ? RoleLabel(element.Facts.Role)
        : element.Facts.Tag.ToLowerInvariant() is "input" or "textarea" ? UiText.Get("agent.step.element_field") : UiText.Get("agent.step.element_other");

    // The common roles in the user's language; any other role shows as the browser names it, the word the model reads.
    private static string RoleLabel(string role) => role.ToLowerInvariant() switch
    {
        "button" => UiText.Get("agent.step.role.button"),
        "link" => UiText.Get("agent.step.role.link"),
        "textbox" => UiText.Get("agent.step.role.textbox"),
        "searchbox" => UiText.Get("agent.step.role.searchbox"),
        "checkbox" => UiText.Get("agent.step.role.checkbox"),
        "radio" => UiText.Get("agent.step.role.radio"),
        "combobox" => UiText.Get("agent.step.role.combobox"),
        "menuitem" => UiText.Get("agent.step.role.menuitem"),
        "tab" => UiText.Get("agent.step.role.tab"),
        "switch" => UiText.Get("agent.step.role.switch"),
        "option" => UiText.Get("agent.step.role.option"),
        "listbox" => UiText.Get("agent.step.role.listbox"),
        _ => WebAgentTree.RoleWord(role),
    };

    // The element's name for the model: page text, so inside the untrusted block.
    private static string? NameBlock(string? origin, Element element, Element? cover)
    {
        var name = element.Facts.Name.Length > 0 ? element.Facts.Name : element.Facts.Label;
        if (string.IsNullOrWhiteSpace(name) && cover is null) return null;
        var text = "Name: " + Clip(name, 120) + (cover is null ? "" : "\nCovered by: " + Clip(Display(cover), 120));
        return WebAgentText.Untrusted(origin, text);
    }

    // An action's answer: what it did, the page's name for it, and where the page went (or why nth stopped it from going).
    private WebToolResult Acted(TabState state, string said, string? names)
    {
        var text = said + (names is null ? "" : "\n" + names);
        if (state.Blocked is { } blocked) return Fail(WebErrorCodes.Blocked, text + "\n" + blocked);
        if (state.Tab.Loading) text += "\nThe page is loading " + state.Tab.Url + ": web_wait for load, then read it.";
        return new WebToolResult(text);
    }

    private static string Remaining(IReadOnlyList<WebKeyPress> presses, int from) =>
        string.Join(' ', presses.Skip(from).Select(KeyName));

    private static string KeyName(WebKeyPress press)
    {
        var name = new StringBuilder();
        if ((press.Modifiers & WebAgentKeys.Ctrl) != 0) name.Append("ctrl+");
        if ((press.Modifiers & WebAgentKeys.Alt) != 0) name.Append("alt+");
        if ((press.Modifiers & WebAgentKeys.Shift) != 0 && press.Key.Length != 1) name.Append("shift+");
        if ((press.Modifiers & WebAgentKeys.Meta) != 0) name.Append("meta+");
        name.Append(press.Key switch
        {
            " " => "space",
            "ArrowUp" => "up",
            "ArrowDown" => "down",
            "ArrowLeft" => "left",
            "ArrowRight" => "right",
            _ => press.Key.Length == 1 ? press.Key : press.Key.ToLowerInvariant(),
        });
        return name.ToString();
    }

    // ---- The page through CDP ----

    private WebNodeKey RefOf(TabState state, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new Refusal(Fail(WebErrorCodes.BadArgs, "Pass ref (e12) from web_read or web_find."));
        return state.Refs.Resolve(reference.Trim())
            ?? throw new Refusal(Fail(WebErrorCodes.StaleRef, reference.Trim() + " is not an element of " + state.Short + ". Call web_read or web_find again."));
    }

    private static WebToolResult Gone(string reference) =>
        Fail(WebErrorCodes.StaleRef, reference + " is gone from the page. Read it again (web_read or web_find).");

    private async Task<Element?> Facts(TabState state, WebNodeKey key, CancellationToken token)
    {
        if (await Resolve(state, key, token) is not { } objectId) return null;
        var element = await FactsOf(state, objectId, key.Session, token);
        if (element is null) return null;
        element = element with { Node = key };
        try
        {
            var nodes = WebAgentTree.Parse(await state.Cdp!.CallAsync("Accessibility.getPartialAXTree", new JsonObject
            {
                ["backendNodeId"] = key.BackendId,
                ["fetchRelatives"] = false,
            }, key.Session, token));
            var own = nodes.FirstOrDefault(n => n.BackendId == key.BackendId) ?? nodes.FirstOrDefault(n => !n.Ignored);
            if (own is { Name.Length: > 0 })
            {
                var facts = element.Facts;
                var label = string.Join(' ', new[] { facts.Label, string.Equals(facts.Name, own.Name, StringComparison.Ordinal) ? null : facts.Name }.Where(t => !string.IsNullOrWhiteSpace(t)));
                element = element with { Facts = facts with { Name = own.Name.Trim(), Role = own.Role.Length > 0 ? own.Role : facts.Role, Label = label.Length > 0 ? label : null } };
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
        }
        return element;
    }

    private async Task<Element?> FactsOf(TabState state, string objectId, string? session, CancellationToken token)
    {
        JsonElement result;
        try
        {
            result = await state.Cdp!.CallAsync("Runtime.callFunctionOn", new JsonObject
            {
                ["objectId"] = objectId,
                ["functionDeclaration"] = FactsJs,
                ["returnByValue"] = true,
                ["silent"] = true,
            }, session, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
        if (result.TryGetProperty("exceptionDetails", out _) || !result.TryGetProperty("result", out var r) || !r.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Object)
            return null;
        var rows = new List<KeyValuePair<string, string>>();
        if (v.TryGetProperty("rows", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in list.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == 2 && row[0].GetString() is { } name && row[1].GetString() is { } value)
                    rows.Add(new(name, value));
            }
        }
        var options = v.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array ? o.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
        var facts = new WebElementFacts
        {
            Role = Text(v, "role") ?? "",
            Name = Text(v, "name") ?? "",
            Tag = Text(v, "tag") ?? "",
            Type = Text(v, "type"),
            AriaLabel = Text(v, "ariaLabel"),
            Id = Text(v, "id"),
            NameAttribute = Text(v, "nameAttr"),
            Label = Text(v, "label"),
            Title = Text(v, "title"),
            Autocomplete = Text(v, "autocomplete"),
            InForm = Flag(v, "inForm") == true,
            Submits = Flag(v, "submits") == true,
            FormAction = Text(v, "formAction"),
            FormMethod = Text(v, "formMethod"),
            Href = Text(v, "href"),
            FrameUrl = Text(v, "frameUrl"),
            ClassName = Text(v, "className"),
        };
        return new Element(facts, Flag(v, "editable") == true, Flag(v, "contentEditable") == true, Flag(v, "isSelect") == true, Flag(v, "checked"), rows, options);
    }

    // The focused element. Focus inside another site's frame is read in that frame's own session; a frame nth cannot reach (no
    // session, a frame of the page's own process that the page's script cannot enter, more than six frames deep) is sealed.
    private async Task<Element?> ActiveFacts(TabState state, CancellationToken token)
    {
        string? session = null;
        Element? owner = null;
        for (var depth = 0; depth <= MaxFrameDepth; depth++)
        {
            string objectId;
            try
            {
                var result = await state.Cdp!.CallAsync("Runtime.evaluate", new JsonObject
                {
                    ["expression"] = ActiveJs,
                    ["returnByValue"] = false,
                    ["objectGroup"] = Group,
                    ["silent"] = true,
                }, session, token);
                if (!result.TryGetProperty("result", out var r) || Text(r, "objectId") is not { } id) return owner is null ? null : owner with { Sealed = true };
                objectId = id;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return owner is null ? null : owner with { Sealed = true };
            }
            if (await FactsOf(state, objectId, session, token) is not { } element) return owner is null ? null : owner with { Sealed = true };
            var described = await Describe(state, session, new JsonObject { ["objectId"] = objectId }, token);
            if (described.Backend is { } backend) element = element with { Node = new WebNodeKey(backend, session) };
            if (!IsFrameOwner(element.Facts)) return element;
            if (!described.Ok || described.Local) return element with { Sealed = true };
            if (described.FrameId is null) return element;
            if (await FrameSession(state, described.FrameId, token) is not { } inner) return element with { Sealed = true };
            owner = element;
            session = inner;
        }
        return owner! with { Sealed = true };
    }

    // The element at a point of the visible page (what a click there reaches). The hit test of a page stops at another site's frame,
    // so the point is hit-tested again inside that frame (its session, the point moved by the frame's place); a frame of the page's
    // own process was already entered by the hit test, so an <iframe> hit there is its edge. A frame nth cannot reach is sealed.
    private async Task<Element?> Hit(TabState state, (double X, double Y) point, CancellationToken token)
    {
        string? session = null;
        Element? owner = null;
        var (dx, dy) = (0d, 0d);
        for (var depth = 0; depth <= MaxFrameDepth; depth++)
        {
            long backend;
            try
            {
                var result = await state.Cdp!.CallAsync("DOM.getNodeForLocation", new JsonObject
                {
                    ["x"] = (int)Math.Round(point.X - dx),
                    ["y"] = (int)Math.Round(point.Y - dy),
                    ["includeUserAgentShadowDOM"] = false,
                    ["ignorePointerEventsNone"] = false,
                }, session, token);
                if (!result.TryGetProperty("backendNodeId", out var id) || !id.TryGetInt64(out backend)) return owner is null ? null : owner with { Sealed = true };
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return owner is null ? null : owner with { Sealed = true };
            }
            var key = new WebNodeKey(backend, session);
            if (await Facts(state, key, token) is not { } element) return owner is null ? null : owner with { Sealed = true };
            if (!IsFrameOwner(element.Facts)) return element;
            var described = await Describe(state, session, new JsonObject { ["backendNodeId"] = backend }, token);
            if (!described.Ok) return element with { Sealed = true };
            if (described.Local || described.FrameId is null) return element;
            if (await FrameSession(state, described.FrameId, token) is not { } inner) return element with { Sealed = true };
            (dx, dy) = await FrameOffset(state, inner, token, 0);
            owner = element;
            session = inner;
        }
        return owner! with { Sealed = true };
    }

    private static bool IsFrameOwner(WebElementFacts facts) => facts.Tag.ToLowerInvariant() is "iframe" or "frame" or "object" or "embed";

    /// <summary>What DOM.describeNode says of a node: its backend id, the frame it holds, and whether that frame is in the page's own process.</summary>
    private readonly record struct Described(bool Ok, long? Backend, string? FrameId, bool Local);

    private static async Task<Described> Describe(TabState state, string? session, JsonObject node, CancellationToken token)
    {
        try
        {
            var result = await state.Cdp!.CallAsync("DOM.describeNode", node, session, token);
            if (!result.TryGetProperty("node", out var n) || n.ValueKind != JsonValueKind.Object) return default;
            long? backend = n.TryGetProperty("backendNodeId", out var b) && b.TryGetInt64(out var value) ? value : null;
            return new Described(true, backend, Text(n, "frameId") is { Length: > 0 } frame ? frame : null, n.TryGetProperty("contentDocument", out _));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return default;
        }
    }

    // The session of another site's frame (its target id is the frame's id); auto-attach is turned on the first time, and a frame
    // that has just appeared gets a moment to be announced.
    private async Task<string?> FrameSession(TabState state, string frameId, CancellationToken token)
    {
        await Frames(state, token);
        for (var attempt = 0; ; attempt++)
        {
            var found = state.Cdp!.Sessions.FirstOrDefault(s => string.Equals(s.TargetId, frameId, StringComparison.Ordinal));
            if (found is not null) return found.SessionId;
            if (attempt == 1) return null;
            await hub.Clock.Delay(100, token);
        }
    }

    private async Task<string?> Resolve(TabState state, WebNodeKey key, CancellationToken token)
    {
        try
        {
            var resolved = await state.Cdp!.CallAsync("DOM.resolveNode", new JsonObject { ["backendNodeId"] = key.BackendId, ["objectGroup"] = Group }, key.Session, token);
            return resolved.TryGetProperty("object", out var o) ? Text(o, "objectId") : null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<JsonElement> CallOn(TabState state, WebNodeKey key, string function, string? argument, CancellationToken token)
    {
        if (await Resolve(state, key, token) is not { } objectId) throw new Refusal(Fail(WebErrorCodes.StaleRef, "The element is gone from the page. Read it again."));
        var call = new JsonObject
        {
            ["objectId"] = objectId,
            ["functionDeclaration"] = function,
            ["returnByValue"] = true,
            ["silent"] = true,
        };
        if (argument is not null) call["arguments"] = new JsonArray { new JsonObject { ["value"] = argument } };
        var result = await state.Cdp!.CallAsync("Runtime.callFunctionOn", call, key.Session, token);
        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v) ? v.Clone() : default;
    }

    private async Task<(string Text, IReadOnlyList<string> Links)> PageText(TabState state, WebNodeKey? root, string function, CancellationToken token)
    {
        JsonElement value;
        if (root is { } node)
        {
            value = await CallOn(state, node, function, null, token);
        }
        else
        {
            var result = await state.Cdp!.CallAsync("Runtime.evaluate", new JsonObject
            {
                ["expression"] = "(" + function + ").call(document)",
                ["returnByValue"] = true,
                ["silent"] = true,
            }, cancellationToken: token);
            value = result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v) ? v.Clone() : default;
        }
        if (value.ValueKind != JsonValueKind.Object) return ("", []);
        var links = value.TryGetProperty("links", out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
        return (Text(value, "text") ?? "", links);
    }

    // The DOM nodes whose values never reach the model (password, one-time-code and card fields of the page's own document).
    private static async Task<IReadOnlySet<long>> Secrets(TabState state, CancellationToken token)
    {
        var found = new HashSet<long>();
        try
        {
            var cdp = state.Cdp!;
            var document = await cdp.CallAsync("DOM.getDocument", new JsonObject { ["depth"] = 0 }, cancellationToken: token);
            if (!document.TryGetProperty("root", out var root) || !root.TryGetProperty("nodeId", out var rootId)) return found;
            var matches = await cdp.CallAsync("DOM.querySelectorAll", new JsonObject { ["nodeId"] = rootId.GetInt32(), ["selector"] = SecretSelector }, cancellationToken: token);
            if (!matches.TryGetProperty("nodeIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return found;
            foreach (var id in ids.EnumerateArray().Take(50))
            {
                var described = await cdp.CallAsync("DOM.describeNode", new JsonObject { ["nodeId"] = id.GetInt32() }, cancellationToken: token);
                if (described.TryGetProperty("node", out var node) && node.TryGetProperty("backendNodeId", out var backend) && backend.TryGetInt64(out var value)) found.Add(value);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
        }
        return found;
    }

    // The iframe sessions of the tab (attached once; flattened, so their calls go through the same channel).
    private static async Task<IReadOnlyList<CdpSession>> Frames(TabState state, CancellationToken token)
    {
        var cdp = state.Cdp!;
        if (!state.FramesAttached)
        {
            state.FramesAttached = true;
            try
            {
                await cdp.AttachToFramesAsync(token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
            }
        }
        return [.. cdp.Sessions.Where(s => string.Equals(s.Type, "iframe", StringComparison.OrdinalIgnoreCase)).Take(6)];
    }

    private readonly record struct Rect(double X, double Y, double Width, double Height);

    // The element's box in CSS px of the main page's viewport (iframe content moved by its frame's place).
    private static async Task<Rect?> Box(TabState state, WebNodeKey key, bool scroll, CancellationToken token)
    {
        var cdp = state.Cdp!;
        if (scroll) await Quietly(cdp, "DOM.scrollIntoViewIfNeeded", token, new JsonObject { ["backendNodeId"] = key.BackendId }, key.Session);
        JsonElement quads;
        try
        {
            quads = await cdp.CallAsync("DOM.getContentQuads", new JsonObject { ["backendNodeId"] = key.BackendId }, key.Session, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
        if (!quads.TryGetProperty("quads", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        foreach (var quad in list.EnumerateArray())
        {
            var v = quad.EnumerateArray().Select(n => n.GetDouble()).ToArray();
            if (v.Length < 8) continue;
            double minX = Math.Min(Math.Min(v[0], v[2]), Math.Min(v[4], v[6])), maxX = Math.Max(Math.Max(v[0], v[2]), Math.Max(v[4], v[6]));
            double minY = Math.Min(Math.Min(v[1], v[3]), Math.Min(v[5], v[7])), maxY = Math.Max(Math.Max(v[1], v[3]), Math.Max(v[5], v[7]));
            if (maxX - minX < 1 || maxY - minY < 1) continue;
            var (dx, dy) = key.Session is null ? (0d, 0d) : await FrameOffset(state, key.Session, token, 0);
            return new Rect(minX + dx, minY + dy, maxX - minX, maxY - minY);
        }
        return null;
    }

    private static async Task<(double X, double Y)?> Point(TabState state, WebNodeKey key, bool scroll, CancellationToken token) =>
        await Box(state, key, scroll, token) is { } box ? (box.X + box.Width / 2, box.Y + box.Height / 2) : null;

    private static async Task<(double X, double Y)> FrameOffset(TabState state, string session, CancellationToken token, int depth)
    {
        var cdp = state.Cdp!;
        var frame = cdp.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, session, StringComparison.Ordinal));
        if (frame is null || depth > 6) return (0, 0);
        try
        {
            var owner = await cdp.CallAsync("DOM.getFrameOwner", new JsonObject { ["frameId"] = frame.TargetId }, frame.ParentSessionId, token);
            if (!owner.TryGetProperty("backendNodeId", out var id) || !id.TryGetInt64(out var backend)) return (0, 0);
            // The frame's document starts at its owner's content box (inside the border and padding); its quads when there is no box model.
            var v = await ContentBox(cdp, backend, frame.ParentSessionId, token);
            if (v is null)
            {
                var quads = await cdp.CallAsync("DOM.getContentQuads", new JsonObject { ["backendNodeId"] = backend }, frame.ParentSessionId, token);
                if (!quads.TryGetProperty("quads", out var list) || list.GetArrayLength() == 0) return (0, 0);
                v = [.. list[0].EnumerateArray().Select(n => n.GetDouble())];
            }
            var (px, py) = frame.ParentSessionId is null ? (0d, 0d) : await FrameOffset(state, frame.ParentSessionId, token, depth + 1);
            return (Math.Min(Math.Min(v[0], v[2]), Math.Min(v[4], v[6])) + px, Math.Min(Math.Min(v[1], v[3]), Math.Min(v[5], v[7])) + py);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return (0, 0);
        }
    }

    private static async Task<double[]?> ContentBox(CdpBroker cdp, long backend, string? session, CancellationToken token)
    {
        try
        {
            var box = await cdp.CallAsync("DOM.getBoxModel", new JsonObject { ["backendNodeId"] = backend }, session, token);
            if (!box.TryGetProperty("model", out var model) || !model.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() < 8) return null;
            return [.. content.EnumerateArray().Select(n => n.GetDouble())];
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<(double X, double Y)> Centre(TabState state, CancellationToken token)
    {
        try
        {
            var metrics = await state.Cdp!.CallAsync("Page.getLayoutMetrics", cancellationToken: token);
            var layout = Prop(metrics, "cssLayoutViewport");
            if (Dbl(layout, "clientWidth") is { } w && Dbl(layout, "clientHeight") is { } h && w > 0 && h > 0) return (w / 2, h / 2);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
        }
        return (400, 300);
    }

    private static async Task<string?> ReadyState(TabState state, CancellationToken token)
    {
        try
        {
            var result = await state.Cdp!.CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = "document.readyState", ["returnByValue"] = true, ["silent"] = true }, cancellationToken: token);
            return result.TryGetProperty("result", out var r) ? Text(r, "value") : null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<bool> Visible(TabState state, WebNodeKey key, CancellationToken token) => await Box(state, key, scroll: false, token) is not null;

    private static Task ReleaseObjects(TabState state) =>
        state.Cdp is { } cdp ? Quietly(cdp, "Runtime.releaseObjectGroup", CancellationToken.None, new JsonObject { ["objectGroup"] = Group }) : Task.CompletedTask;

    // ---- Input and the overlay ----

    private async Task<bool> Ensure(TabState state, Step s)
    {
        if (WebPolicy.OriginOf(state.Tab.Url) is not { } origin || state.Cdp is not { } cdp) return false;
        state.OverlayReady = await state.Overlay.EnsureAsync(cdp, origin, hub.Settings().ReduceMotion, s.Token);
        return state.OverlayReady;
    }

    private async Task Drive(TabState state, Step s)
    {
        if (await Ensure(state, s)) await state.Overlay.Drive(state.Cdp!, true, s.Token);
    }

    // The silver pointer glides to the point first (it jumps with Reduce Motion), so the click lands where the user sees it.
    private async Task Pointer(TabState state, (double X, double Y) point, Step s)
    {
        if (!state.OverlayReady) return;
        await state.Overlay.Move(state.Cdp!, point.X, point.Y, s.Token);
        if (!hub.Settings().ReduceMotion) await hub.Clock.Delay(WebAgentOverlay.GlideMs, s.Token);
    }

    private async Task ClickAt(TabState state, (double X, double Y) point, Step s)
    {
        var cdp = state.Cdp!;
        await Pointer(state, point, s);
        await state.Overlay.Busy(cdp, true, s.Token);
        try
        {
            await Mouse(cdp, "mouseMoved", point, "none", 0, 0, s.Token);
            await Mouse(cdp, "mousePressed", point, "left", 1, 1, s.Token);
            await Mouse(cdp, "mouseReleased", point, "left", 0, 1, s.Token);
        }
        finally
        {
            await Swallow(state.Overlay.Busy(cdp, false, CancellationToken.None));
        }
        await state.Overlay.Press(cdp, s.Token);
    }

    private static Task<JsonElement> Mouse(CdpBroker cdp, string type, (double X, double Y) point, string button, int buttons, int clicks, CancellationToken token) =>
        cdp.CallAsync("Input.dispatchMouseEvent", new JsonObject
        {
            ["type"] = type,
            ["x"] = point.X,
            ["y"] = point.Y,
            ["button"] = button,
            ["buttons"] = buttons,
            ["clickCount"] = clicks,
            ["pointerType"] = "mouse",
        }, cancellationToken: token);

    private static async Task Press(CdpBroker cdp, WebKeyPress press, CancellationToken token)
    {
        var commands = (press.Modifiers & (WebAgentKeys.Ctrl | WebAgentKeys.Alt | WebAgentKeys.Meta)) != 0;
        var typed = press.Text is not null && !commands;
        var down = new JsonObject
        {
            ["type"] = typed ? "keyDown" : "rawKeyDown",
            ["key"] = press.Key,
            ["code"] = press.Code,
            ["windowsVirtualKeyCode"] = press.KeyCode,
            ["nativeVirtualKeyCode"] = press.KeyCode,
            ["modifiers"] = press.Modifiers,
        };
        if (typed)
        {
            down["text"] = press.Text;
            down["unmodifiedText"] = press.Text;
        }
        await cdp.CallAsync("Input.dispatchKeyEvent", down, cancellationToken: token);
        await cdp.CallAsync("Input.dispatchKeyEvent", new JsonObject
        {
            ["type"] = "keyUp",
            ["key"] = press.Key,
            ["code"] = press.Code,
            ["windowsVirtualKeyCode"] = press.KeyCode,
            ["nativeVirtualKeyCode"] = press.KeyCode,
            ["modifiers"] = press.Modifiers,
        }, cancellationToken: token);
    }

    // A click or Enter may start a navigation: give it the moment to start, so a blocked one is reported with the step.
    private async Task Settle(TabState state, Step s) => await hub.Clock.Delay(SettleMs, s.Token);

    // ---- Small helpers ----

    private static JsonElement Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static double? Dbl(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static bool? Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    // The pixel size of a JPEG (its start-of-frame marker), or null.
    private static (int Width, int Height)? JpegSize(byte[] jpeg)
    {
        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }
            var marker = jpeg[i + 1];
            if (marker is 0xD8 or 0x01 or 0xFF || marker is >= 0xD0 and <= 0xD7)
            {
                i += marker == 0xFF ? 1 : 2;
                continue;
            }
            var length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
            i += 2 + length;
        }
        return null;
    }

    /// <summary>One element as an action sees it: its facts, what kind of field it is, its form's visible fields.</summary>
    private sealed record Element(WebElementFacts Facts, bool Editable, bool ContentEditable, bool IsSelect, bool? Checked, IReadOnlyList<KeyValuePair<string, string>> Rows, IReadOnlyList<string> Options)
    {
        /// <summary>The DOM node the facts were read from (its backend id in its session), when known.</summary>
        public WebNodeKey? Node { get; init; }

        /// <summary>An element inside a frame nth could not reach: only the frame is known, so nothing is pressed or typed there.</summary>
        public bool Sealed { get; init; }
    }
}
