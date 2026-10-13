// PORT-ONLY: web surfaces W11, model access to Visus (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "Model access (in the
// same safety model as Tactus)", 8.1; ED-NEW-web-w11-1). No Swift counterpart. One engine per chat runs the web_* tools on the
// tabs the chat's model may use. Every step goes through the same gates, in order: Visus model access on, not stopped this
// turn, the Tactus run control's watch (Stop and the chord end it at once), the queue (steps run one at a time, so a stop also
// cancels the queued ones), Pause and take-over (the step holds, up to 25 s, then answers paused or user_took_over), the
// per-site card for actions (and for reading a shared tab), the cross-origin-after-read card for model navigations, the hard
// denies and the guarded-action card, judged on the element's facts read fresh at the moment of the step. Navigations the
// model causes (a click, Enter, a redirect within 3 s of a step) are checked as they start and stopped when they would cross
// to another site after a read; the model is told to ask through web_open. Page content reaches the model only inside the
// untrusted block. Asks wait 25 s in the call, then answer waiting_for_user; the card stays up (2 minutes) and the same call
// again joins it. This file holds the gates and the tab-level tools; WebAgentEngine.Page.cs holds the page-level ones.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Localisation;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Web;

public sealed partial class WebAgentEngine : IDisposable
{
    /// <summary>How long a call waits for the user's answer before it answers waiting_for_user.</summary>
    public const int AskWaitMs = 25_000;

    /// <summary>How long an unanswered card stays up for the same call to join it again.</summary>
    public const int AskLifetimeMs = 120_000;

    /// <summary>How long a step holds for Pause or a take-over before it answers paused or user_took_over.</summary>
    public const int HoldWaitMs = 25_000;

    /// <summary>A navigation this soon after a model step on the tab counts as the model's (a click's link, a submit's redirect).</summary>
    public const int AfterStepMs = 3_000;

    /// <summary>A pop-up this soon after a model step on the tab counts as the model's (Chromium's user activation lasts 5 s).</summary>
    public const int PopupAfterStepMs = 5_000;

    /// <summary>How long web_open and web_navigate wait for the page to load.</summary>
    public const int LoadWaitMs = 15_000;

    /// <summary>How long a click or Enter waits for the navigation it may start.</summary>
    public const int SettleMs = 350;

    private readonly WebAgentHub hub;
    private readonly SemaphoreSlim queue = new(1, 1);
    private readonly Lock gate = new();
    private readonly Dictionary<string, TabState> states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> shortIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> readOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> knownUrls = new(StringComparer.Ordinal);

    // ED-NEW-web-sec-agent-6: the preview origins a chat outside any project allowed scripts in, kept only while its web tools live.
    private readonly HashSet<string> chatScripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string? Origin, string Text)> readTexts = new();

    // ED-NEW-web-sec-agent-9: the titles and addresses the model was shown, with their site (Named), and what it typed on each site (Typed).
    private readonly LinkedList<(string Origin, string Text)> shownNames = new();
    private readonly Dictionary<string, string> typedOn = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingAsk> asks = new(StringComparer.Ordinal);
    private CancellationTokenSource turn = new();
    private CancellationTokenSource? running;
    private volatile string? crashed;
    private PcRunControl? watchedRun;
    private bool stopped;
    private int nextShort;
    private bool disposed;
    private int activeCalls;
    private bool resourcesDisposed;

    internal WebAgentEngine(WebAgentHub hub, string thread)
    {
        this.hub = hub;
        Thread = thread;
    }

    public string Thread { get; }

    /// <summary>The user stopped the web steps for the rest of this turn.</summary>
    public bool StoppedForTurn
    {
        get
        {
            lock (gate) return stopped;
        }
    }

    /// <summary>The sites whose content the model has read in this chat (the cross-origin rule's sources).</summary>
    public IReadOnlyCollection<string> ReadOrigins
    {
        get
        {
            lock (gate) return [.. readOrigins];
        }
    }

    /// <summary>The asks waiting for the user now.</summary>
    public int PendingAsks
    {
        get
        {
            lock (gate) return asks.Values.Count(a => !a.Task.IsCompleted);
        }
    }

    /// <summary>Runs one web tool for <paramref name="caller"/>. Never throws for a refusal; a cancelled call throws.</summary>
    public async Task<WebToolResult> Call(string tool, JsonObject? arguments, WebAgentCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (WebToolSchemas.Spec(tool) is null) return Fail(WebErrorCodes.BadArgs, "There is no web tool " + tool + ".");
        if (!hub.Offered || hub.Host is not { } host || hub.Asker is not { } asker)
            return Fail(WebErrorCodes.Off, "Visus model access is off (Settings › Visus › Let models use Visus). Ask the user to turn it on.");
        WatchRun();
        if (StoppedForTurn) return Stopped();
        CancellationToken turnToken;
        lock (gate)
        {
            if (disposed) return Stopped();
            turnToken = turn.Token;
            activeCalls++;
        }
        try
        {
            using var watch = hub.Run.Watch(cancellationToken, Thread, WebAgentHub.App, 0);
            using var joined = hub.TactusPause?.Join(Thread, WebAgentHub.App, new PcWaitState());
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(watch.Token, turnToken);
            using var crash = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            var token = crash.Token;
            try
            {
                await queue.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (StoppedForTurn) return Stopped();
                    var step = new Step(tool, arguments ?? [], caller, host, asker, token);
                    lock (gate) running = crash;
                    try
                    {
                        // A stop ends the call at once, even while a page call it made is still on its way back.
                        return await host.OnUi(() => Dispatch(step)).WaitAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (gate) running = null;
                    }
                }
                finally
                {
                    queue.Release();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && crashed is { } why && !linked.IsCancellationRequested)
            {
                crashed = null;
                return Fail(WebErrorCodes.PageFailed, "The page failed while this step ran (" + why + "). Nothing was sent again. Wait for it to reload, then read it before you act.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (watch.Stopped) StopTurn(watch.StoppedBy ?? PcStopSource.Pill);
                return Stopped();
            }
        }
        finally
        {
            lock (gate)
            {
                activeCalls--;
                DisposeResourcesIfIdle();
            }
        }
    }

    /// <summary>The user stopped the web steps: the running one ends, the queued ones never start, and none starts this turn.</summary>
    public void StopTurn(PcStopSource source)
    {
        List<TabState> driven;
        lock (gate)
        {
            if (disposed) return;
            stopped = true;
            turn.Cancel();
            asks.Clear();
            driven = [.. states.Values.Where(s => s.Driven)];
        }
        // the chord is named only while Tactus is bound (without it Visus has no chord: Background tasks and the chat stop it)
        hub.Report(new WebAgentActivity(Thread, WebAgentPhase.Stopped, hub.StopChord is { } chord ? UiText.Format("agent.step.stopped_with_chord", chord) : UiText.Get("agent.step.stopped")));
        Release(driven);
    }

    /// <summary>The turn ended: the asks go, the overlay leaves the tabs, and the next turn may use Visus again.</summary>
    public void EndTurn()
    {
        List<TabState> driven;
        lock (gate)
        {
            if (disposed) return;
            turn.Cancel();
            turn = new CancellationTokenSource();
            stopped = false;
            asks.Clear();
            driven = [.. states.Values.Where(s => s.Driven)];
        }
        hub.Report(new WebAgentActivity(Thread, WebAgentPhase.Idle, ""));
        // The pill forgets Visus for this chat (a Tactus engine of the chat, when there is one, does the same at its turn end).
        hub.TactusPause?.EndTurn(Thread);
        Release(driven);
    }

    /// <summary>Resume: every tab the user took over goes back to the model.</summary>
    internal void HandBack()
    {
        lock (gate)
        {
            foreach (var state in states.Values) state.TookOver = false;
        }
    }

    public void Dispose()
    {
        List<TabState> all;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            turn.Cancel();
            all = [.. states.Values];
            states.Clear();
            asks.Clear();
            if (watchedRun is not null) watchedRun.Stopped -= OnRunStopped;
            watchedRun = null;
        }
        foreach (var state in all) state.Unhook();
        lock (gate) DisposeResourcesIfIdle();
    }

    // ED-NEW-web-review-24: cancellation releases the running call's queue lease first.
    // Reentrant disposal must not dispose that lease, or a queued waiter, underneath it.
    private void DisposeResourcesIfIdle()
    {
        if (!disposed || activeCalls != 0 || resourcesDisposed) return;
        resourcesDisposed = true;
        turn.Dispose();
        queue.Dispose();
    }

    private async Task<WebToolResult> Dispatch(Step s)
    {
        try
        {
            return await Run(s);
        }
        finally
        {
            foreach (var lease in s.Leases) lease.Dispose();
        }
    }

    private async Task<WebToolResult> Run(Step s)
    {
        try
        {
            return s.Tool switch
            {
                WebToolSchemas.Open => await Open(s),
                WebToolSchemas.Tabs => ListTabs(s),
                WebToolSchemas.Read => await Read(s),
                WebToolSchemas.Find => await FindOnPage(s),
                WebToolSchemas.Screenshot => await Screenshot(s),
                WebToolSchemas.Click => await Click(s),
                WebToolSchemas.Type => await TypeText(s),
                WebToolSchemas.Key => await Keys(s),
                WebToolSchemas.Scroll => await Scroll(s),
                WebToolSchemas.Select => await SelectOption(s),
                WebToolSchemas.Wait => await Wait(s),
                WebToolSchemas.Navigate => await History(s),
                WebToolSchemas.Console => await Logs(s, network: false),
                WebToolSchemas.Network => await Logs(s, network: true),
                WebToolSchemas.Device => await Device(s),
                WebToolSchemas.Eval => await Eval(s),
                _ => Fail(WebErrorCodes.BadArgs, "There is no web tool " + s.Tool + "."),
            };
        }
        catch (Refusal refusal)
        {
            return refusal.Result;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Fail(WebErrorCodes.PageFailed, "The page did not answer (" + error.GetType().Name + "). Read it again, or reload it with web_navigate.");
        }
    }

    // ---- The tab-level tools ----

    private WebToolResult ListTabs(Step s)
    {
        var tabs = s.Host.Tabs(Thread).Where(t => !t.IsClosed && t.Kind != VisusTabKind.Private).ToList();
        if (tabs.Count == 0) return new WebToolResult("No Visus tabs are yours or shared with you. web_open opens one.");
        var lines = new StringBuilder();
        foreach (var tab in tabs)
        {
            var state = StateOf(tab);
            lines.Append(state.Short).Append(" (").Append(tab.Shared ? "shared by the user" : "yours").Append(") ").Append(tab.Url);
            if (tab.Title.Length > 0) lines.Append(" | ").Append(tab.Title);
            lines.Append('\n');
            Named(WebPolicy.OriginOf(tab.Url), tab.Url + "\n" + tab.Title);
        }
        return new WebToolResult("Tabs you may use (" + N(tabs.Count) + "):\n" + WebAgentText.Untrusted(null, lines.ToString().TrimEnd()));
    }

    private async Task<WebToolResult> Open(Step s)
    {
        var url = Str(s.Args, "url")?.Trim();
        var query = Str(s.Args, "query")?.Trim();
        if (string.IsNullOrEmpty(url) == string.IsNullOrEmpty(query)) return Fail(WebErrorCodes.BadArgs, "Pass url or query (one of them).");
        var searched = !string.IsNullOrEmpty(query);
        url = searched ? s.Host.SearchUrl(query!) : WithScheme(url!);
        if (WebAgentUrls.DeniedReason(url) is { } why) return Fail(WebErrorCodes.Denied, why);
        var target = WebPolicy.OriginOf(url)!;
        TabState? state = null;
        if (Str(s.Args, "tab") is { Length: > 0 } named)
        {
            state = await Prepare(s, TabFor(s, named));
        }
        if (!searched && !IsKnown(s, url, state)) return Fail(WebErrorCodes.UnknownUrl, "nth opens only addresses from the conversation, from pages you read and from search results. Search for it with query, or ask the user for the address.");
        if (await Hold(s, state) is { } held) return held;
        var (refused, crossed) = await CrossOrigin(s, state, url);
        if (refused is not null) return refused;
        if (!crossed && state is { Tab.Shared: true } && !SameOrigin(state.Tab.Url, url) && WebPolicy.OriginOf(state.Tab.Url) is not null)
        {
            var move = WebGuardVerdict.Ask(WebGuardCategory.NewSite, "open");
            if (await Guard(s, state, move, new WebElementFacts(), "open|" + WebAgentUrls.Normalize(url), url) is { } cancelled) return cancelled;
        }
        Report(searched ? UiText.Format("agent.step.searching", Clip(query!, 40)) : UiText.Format("agent.step.opening", WebPolicy.SourceName(url)), state?.Short);
        if (state is null)
        {
            var opened = await s.Host.OpenAgentTab(Thread, url, s.Token) ?? throw new Refusal(Fail(WebErrorCodes.PageFailed, "Visus could not open a tab."));
            state = await Prepare(s, opened);
            state.Expected = target;
            using var lease = BeginStep(state);
            await WaitLoaded(state, s.Token);
        }
        else
        {
            state.Expected = target;
            using var lease = BeginStep(state);
            var before = state.Tab.Url;
            state.Tab.Navigate(url);
            await WaitStarted(state, before, s.Token);
            await WaitLoaded(state, s.Token);
        }
        lock (gate) knownUrls.Add(WebAgentUrls.Normalize(url));
        if (state.Blocked is { } blocked) return Fail(WebErrorCodes.Blocked, blocked);
        var tab = state.Tab;
        // + ED-NEW-web-sec-res-3: the page chooses its own address (a redirect, pushState), so it is page text, inside the block.
        var text = "Opened " + state.Short + (tab.Loading ? " (still loading; web_wait for load)" : "") + ".";
        Named(WebPolicy.OriginOf(tab.Url), tab.Url + "\n" + tab.Title);
        return new WebToolResult(text + "\n" + WebAgentText.Untrusted(WebPolicy.OriginOf(tab.Url), "Address: " + tab.Url + "\nTitle: " + tab.Title));
    }

    private async Task<WebToolResult> History(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var action = Str(s.Args, "action")?.Trim().ToLowerInvariant();
        if (action is not ("back" or "forward" or "reload")) return Fail(WebErrorCodes.BadArgs, "action is back, forward or reload.");
        if (await Hold(s, state) is { } held) return held;
        var view = state.Tab.View;
        if (action == "back" && view is { CanGoBack: false }) return Fail(WebErrorCodes.BadArgs, state.Short + " has no page to go back to.");
        if (action == "forward" && view is { CanGoForward: false }) return Fail(WebErrorCodes.BadArgs, state.Short + " has no page to go forward to.");
        Report(action switch { "back" => UiText.Get("agent.step.going_back"), "forward" => UiText.Get("agent.step.going_forward"), _ => UiText.Get("agent.step.reloading") }, state.Short);
        using (BeginStep(state))
        {
            var before = state.Tab.Url;
            state.Expected = action == "reload" ? WebPolicy.OriginOf(before) : null;
            if (action == "back") state.Tab.GoBack();
            else if (action == "forward") state.Tab.GoForward();
            else state.Tab.Reload();
            await WaitStarted(state, action == "reload" ? null : before, s.Token);
            await WaitLoaded(state, s.Token);
        }
        if (state.Blocked is { } blocked) return Fail(WebErrorCodes.Blocked, blocked);
        Named(WebPolicy.OriginOf(state.Tab.Url), state.Tab.Url + "\n" + state.Tab.Title);
        // + ED-NEW-web-sec-res-3: the address the page went to is page text, inside the block with its title.
        return new WebToolResult(state.Short + " is at the address below.\n"
            + WebAgentText.Untrusted(WebPolicy.OriginOf(state.Tab.Url), "Address: " + state.Tab.Url + "\nTitle: " + state.Tab.Title));
    }

    private async Task<WebToolResult> Wait(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var kind = (Str(s.Args, "for") ?? "load").Trim().ToLowerInvariant();
        var timeout = (int)Math.Clamp(Num(s.Args, "timeout") ?? 10_000, 100, WebToolSchemas.MaxWaitMs);
        if (kind is not ("load" or "ref" or "idle")) return Fail(WebErrorCodes.BadArgs, "for is load, ref or idle.");
        WebNodeKey? key = null;
        if (kind == "ref") key = RefOf(state, Str(s.Args, "ref"));
        Report(UiText.Get("agent.step.waiting"), state.Short);
        var start = hub.Clock.NowMs;
        while (true)
        {
            var done = kind switch
            {
                "load" => !state.Tab.Loading && await ReadyState(state, s.Token) is null or "complete",
                "idle" => state.Logs.InFlight == 0 && hub.Clock.NowMs - Math.Max(state.Logs.LastNetworkAt, start - 500) >= 500,
                _ => await Visible(state, key!.Value, s.Token),
            };
            if (done) return new WebToolResult(kind switch { "load" => state.Short + " finished loading.", "idle" => state.Short + " has no requests running.", _ => Str(s.Args, "ref") + " is on the page." });
            if (hub.Clock.NowMs - start >= timeout)
                return Fail(WebErrorCodes.Timeout, "Waited " + N(timeout) + " ms; " + (kind switch { "load" => "the page is still loading.", "idle" => "requests are still running.", _ => "the element is not visible." }));
            await hub.Clock.Delay(kind == "ref" ? 200 : 100, s.Token);
        }
    }

    private async Task<WebToolResult> Logs(Step s, bool network)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        if (await Hold(s, state) is { } held) return held;
        if (state.Tab.Shared && await Site(s, state) is { } refused) return refused;
        Report(network ? UiText.Get("agent.step.reading_network") : UiText.Get("agent.step.reading_console"), state.Short);
        var since = (long)Math.Max(0, Num(s.Args, "since") ?? 0);
        var (lines, next) = network ? state.Logs.Network(since) : state.Logs.Console(since);
        var origin = WebPolicy.OriginOf(state.Tab.Url);
        Saw(origin, null, []);
        if (lines.Count == 0) return new WebToolResult("Nothing new in " + state.Short + "'s " + (network ? "network" : "console") + ". next: " + N(next)) { ReadOrigin = origin };
        var body = string.Join('\n', lines.Select(l => "#" + N(l.Number) + " " + l.Text));
        Kept(origin, body);
        return new WebToolResult(N(lines.Count) + " " + (network ? "requests" : "messages") + " in " + state.Short + ". next: " + N(next) + "\n" + WebAgentText.Untrusted(origin, body)) { ReadOrigin = origin };
    }

    private async Task<WebToolResult> Device(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        if (state.Tab.Kind != VisusTabKind.Preview && !WebPolicy.IsDevUrl(state.Tab.Url))
            return Fail(WebErrorCodes.NotSupported, "web_device works on project previews (localhost and LAN dev hosts) only.");
        if (await Hold(s, state) is { } held) return held;
        WebDeviceSize? size;
        var preset = Str(s.Args, "preset");
        if (preset is not null)
        {
            if (!WebAgentDevices.Presets.TryGetValue(preset.Trim(), out size)) return Fail(WebErrorCodes.BadArgs, "preset is phone, tablet, laptop or desktop.");
        }
        else if (Num(s.Args, "width") is { } width && Num(s.Args, "height") is { } height)
        {
            if (width < WebAgentDevices.MinSide || height < WebAgentDevices.MinSide || width > WebAgentDevices.MaxSide || height > WebAgentDevices.MaxSide)
                return Fail(WebErrorCodes.BadArgs, "width and height are 240 to 4000 CSS px.");
            size = new WebDeviceSize((int)width, (int)height, 1, false);
        }
        else
        {
            return Fail(WebErrorCodes.BadArgs, "Pass preset, or width and height.");
        }
        Report(UiText.Get("agent.step.setting_device"), state.Short);
        var cdp = state.Cdp!;
        if (size is null)
        {
            await cdp.CallAsync("Emulation.clearDeviceMetricsOverride", cancellationToken: s.Token);
            await cdp.CallAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = false }, cancellationToken: s.Token);
            return new WebToolResult(state.Short + " is back at the pane's own size.");
        }
        await cdp.CallAsync("Emulation.setDeviceMetricsOverride", new JsonObject
        {
            ["width"] = size.Width,
            ["height"] = size.Height,
            ["deviceScaleFactor"] = size.Scale,
            ["mobile"] = size.Mobile,
        }, cancellationToken: s.Token);
        await cdp.CallAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = size.Mobile }, cancellationToken: s.Token);
        return new WebToolResult(state.Short + " now shows " + N(size.Width) + " x " + N(size.Height) + " CSS px" + (size.Mobile ? " as a touch device" : "") + ".");
    }

    private async Task<WebToolResult> Eval(Step s)
    {
        var state = await Prepare(s, TabFor(s, Str(s.Args, "tab")));
        var expression = Str(s.Args, "expression");
        if (string.IsNullOrWhiteSpace(expression)) return Fail(WebErrorCodes.BadArgs, "Pass expression.");
        if (!WebPolicy.IsDevUrl(state.Tab.Url) || WebPolicy.OriginOf(state.Tab.Url) is not { } origin)
            return Fail(WebErrorCodes.NotSupported, "web_eval runs only in project previews (localhost and LAN dev hosts).");
        if (await Hold(s, state) is { } held) return held;
        // ED-NEW-web-sec-agent-6 (security review L4; DESIGN 5.10 "after the user allows it per project"): the grant belongs to the
        // chat's project and the preview origin; a chat outside any project keeps its own only while its web tools live.
        var project = s.Host.ProjectOf(Thread);
        bool granted;
        if (project is null)
        {
            lock (gate) granted = chatScripts.Contains(origin);
        }
        else
        {
            granted = hub.Grants.Eval(project, origin);
        }
        if (!granted)
        {
            var ask = new WebEvalAsk(Thread, state.Short, origin, s.Model) { TabId = state.Tab.Id };
            Report(UiText.Get("agent.step.check_card"), state.Short, WebAgentPhase.Asking);
            var (answered, allowed) = await Ask("eval|" + project + "|" + origin, t => s.Asker.AskEval(ask, t), false, s);
            if (!answered) return Waiting(s);
            if (!allowed) return Fail(WebErrorCodes.NotApproved, "The user did not allow scripts in " + WebPolicy.SourceName(origin) + ". Use the other web tools.");
            if (project is null)
            {
                lock (gate) chatScripts.Add(origin);
            }
            else
            {
                hub.Grants.GrantEval(project, origin);
            }
        }
        // ED-NEW-secfinal-20 (final security gate S2): the pause or take-over (Hold) and the "Allow scripts" card above can take as long as the
        // user needs, and a dev server the model can write to may send its page elsewhere meanwhile. The script runs only while the tab still shows
        // the origin that was checked and allowed; otherwise it gets the same plain refusal as a tab that never was a preview.
        if (!WebPolicy.IsDevUrl(state.Tab.Url) || !string.Equals(WebPolicy.OriginOf(state.Tab.Url), origin, StringComparison.OrdinalIgnoreCase))
            return Fail(WebErrorCodes.NotSupported, "web_eval runs only in project previews (localhost and LAN dev hosts).");
        Report(UiText.Get("agent.step.running_script"), state.Short);
        var result = await state.Cdp!.CallAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = true,
            ["timeout"] = 10_000,
            ["userGesture"] = false,
        }, cancellationToken: s.Token);
        Saw(origin, null, []);
        if (result.TryGetProperty("exceptionDetails", out var thrown))
        {
            var message = thrown.TryGetProperty("exception", out var ex) && Text(ex, "description") is { } description ? description : Text(thrown, "text") ?? "error";
            return Fail(WebErrorCodes.PageFailed, "The expression threw.\n" + WebAgentText.Untrusted(origin, Clip(message, 4000))) with { ReadOrigin = origin };
        }
        var value = result.TryGetProperty("result", out var r)
            ? r.TryGetProperty("value", out var v) ? v.GetRawText() : Text(r, "description") ?? Text(r, "type") ?? "undefined"
            : "undefined";
        Kept(origin, value);
        return new WebToolResult("Result:\n" + WebAgentText.Untrusted(origin, Clip(value, 20_000))) { ReadOrigin = origin };
    }

    // ---- The gates ----

    // The tab a call names, among the tabs the chat's model may use now.
    private IWebAgentTab TabFor(Step s, string? shortId)
    {
        if (string.IsNullOrWhiteSpace(shortId)) throw new Refusal(Fail(WebErrorCodes.BadArgs, "Pass tab (t1) from web_tabs or web_open."));
        string? id;
        lock (gate) shortIds.TryGetValue(shortId.Trim(), out id);
        var tab = id is null ? null : s.Host.Tabs(Thread).FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        if (tab is null || tab.IsClosed || tab.Kind == VisusTabKind.Private)
            throw new Refusal(Fail(WebErrorCodes.NoTab, "There is no tab " + shortId.Trim() + " you may use. web_tabs lists them."));
        return tab;
    }

    // The tab's state, with its view live and hooked (navigation checks, take-over, logs).
    private async Task<TabState> Prepare(Step s, IWebAgentTab tab)
    {
        var state = StateOf(tab);
        // The tab is the model's for the whole call: it is never suspended or discarded meanwhile (DESIGN 2.3), and a crash
        // while the call runs fails it (2.7).
        if (!s.Leases.Any(l => l is TabLease held && ReferenceEquals(held.State, state))) s.Leases.Add(new TabLease(this, state, tab.BeginModelStep()));
        if (!await tab.MakeLiveAsync(s.Token)) throw new Refusal(Fail(WebErrorCodes.PageFailed, "The tab could not load its page."));
        if (tab.View is not { } view || tab.Cdp is not { } cdp) throw new Refusal(Fail(WebErrorCodes.PageFailed, "The tab has no page yet."));
        if (!ReferenceEquals(state.View, view) || !ReferenceEquals(state.Cdp, cdp))
        {
            state.Unhook();
            state.View = view;
            state.Cdp = cdp;
            state.Refs = new WebRefs();
            state.Overlay = new WebAgentOverlay();
            state.FramesAttached = false;
            EventHandler<WebNavigationStarting> starting = (_, e) => OnNavigationStarting(state, e);
            EventHandler<WebNavigationCompleted> completed = (_, _) => state.Overlay.Reset();
            Action input = () => OnUserInput(state);
            Action<string> failed = reason => OnStepFailed(state, reason);
            Action<WebPopupOpening> popup = e => OnPopupOpening(state, e);
            view.NavigationStarting += starting;
            view.NavigationCompleted += completed;
            tab.UserInput += input;
            tab.StepFailed += failed;
            tab.PopupOpening += popup;
            state.Hooks.Add(new Unhooking(() =>
            {
                view.NavigationStarting -= starting;
                view.NavigationCompleted -= completed;
                tab.UserInput -= input;
                tab.StepFailed -= failed;
                tab.PopupOpening -= popup;
            }));
            state.Hooks.Add(cdp.Subscribe("Runtime.bindingCalled", e => OnBinding(state, e)));
            state.Hooks.Add(state.Logs);
            await state.Logs.AttachAsync(cdp, s.Token);
            foreach (var domain in new[] { "Page.enable", "DOM.enable" }) await Quietly(cdp, domain, s.Token);
        }
        return state;
    }

    private TabState StateOf(IWebAgentTab tab)
    {
        lock (gate)
        {
            if (states.TryGetValue(tab.Id, out var state))
            {
                state.Tab = tab;
                return state;
            }
            var made = new TabState(tab, "t" + N(++nextShort), () => hub.Clock.NowMs);
            states[tab.Id] = made;
            shortIds[made.Short] = tab.Id;
            return made;
        }
    }

    // Pause and take-over: the step holds until they end, up to 25 s, then answers paused or user_took_over.
    private async Task<WebToolResult?> Hold(Step s, TabState? state)
    {
        var start = hub.Clock.NowMs;
        var held = false;
        while (true)
        {
            var paused = hub.IsPaused;
            var took = state is not null && TookOver(state);
            if (!paused && !took)
            {
                if (held) Report(UiText.Get("agent.step.resuming"), state?.Short);
                return null;
            }
            if (!held)
            {
                if (paused) Report(UiText.Get("agent.step.page_stays"), state?.Short, WebAgentPhase.Paused);
                else Report(UiText.Get("agent.step.resume_when_done"), state?.Short, WebAgentPhase.TookOver);
            }
            held = true;
            var left = HoldWaitMs - (hub.Clock.NowMs - start);
            if (left <= 0)
            {
                return paused
                    ? Fail(WebErrorCodes.Paused, "The user paused Visus; the page stays as it is. Call again after they resume.")
                    : Fail(WebErrorCodes.TookOver, "The user is working in " + state!.Short + ". Do not use that tab until they resume; other tabs still work.");
            }
            using var cut = CancellationTokenSource.CreateLinkedTokenSource(s.Token);
            await Task.WhenAny(hub.NextChange(), Swallow(hub.Clock.Delay((int)Math.Min(left, int.MaxValue), cut.Token)));
            await cut.CancelAsync();
            s.Token.ThrowIfCancellationRequested();
        }
    }

    // DESIGN 5.10: the first action on a site in a tab asks; reading a shared tab asks too. Allow once lasts while the tab stays
    // on the site. A shared tab is never offered "Always allow", and its card cannot be turned off.
    private async Task<WebToolResult?> Site(Step s, TabState state)
    {
        var origin = WebPolicy.OriginOf(state.Tab.Url);
        if (origin is null) return null;
        if (state.OnceOrigin is { } once && !string.Equals(once, origin, StringComparison.OrdinalIgnoreCase)) state.OnceOrigin = null;
        if (state.OnceOrigin is not null) return null;
        if (!state.Tab.Shared && (!hub.Settings().AskNewSites || hub.Grants.Site(origin))) return null;
        var ask = new WebSiteAsk(Thread, state.Short, origin, s.Model, state.Tab.Shared) { TabId = state.Tab.Id };
        Report(UiText.Get("agent.step.check_card"), state.Short, WebAgentPhase.Asking);
        var (answered, answer) = await Ask("site|" + state.Tab.Id + "|" + origin, t => s.Asker.AskSite(ask, t), WebSiteAnswer.Deny, s);
        if (!answered) return Waiting(s);
        if (answer == WebSiteAnswer.Deny)
            return Fail(WebErrorCodes.NotApproved, "The user did not allow " + s.Model + " to use " + WebPolicy.SourceName(origin) + " in Visus. Do not try another way to use it.");
        if (answer == WebSiteAnswer.AlwaysAllow && ask.AllowAlways) hub.Grants.GrantSite(origin);
        state.OnceOrigin = origin;
        return null;
    }

    // DESIGN 5.10 cross-origin rule: once the model read one site, a model navigation to another asks, with the full address.
    private async Task<(WebToolResult? Refused, bool Asked)> CrossOrigin(Step s, TabState? state, string url)
    {
        IReadOnlyList<string> from;
        lock (gate) from = WebAgentUrls.CrossOriginSources(readOrigins, url, hub.Grants.Pair);
        if (from.Count == 0) return (null, false);
        var ask = new WebNavigateAsk(Thread, state?.Short, from, url, WebAgentUrls.QueryFields(url, FromPage), s.Model) { TabId = state?.Tab.Id };
        Report(UiText.Get("agent.step.check_card"), state?.Short, WebAgentPhase.Asking);
        var (answered, answer) = await Ask("nav|" + url, t => s.Asker.AskNavigate(ask, t), WebNavigateAnswer.Deny, s);
        if (!answered) return (Waiting(s), true);
        if (answer == WebNavigateAnswer.Deny)
            return (Fail(WebErrorCodes.NotApproved, "The user did not allow opening " + WebPolicy.SourceName(url) + ". Do not try another way to open it."), true);
        if (answer == WebNavigateAnswer.AlwaysForPair)
        {
            var to = WebPolicy.OriginOf(url)!;
            foreach (var source in from) hub.Grants.GrantPair(source, to);
        }
        return (null, true);
    }

    // The guarded-action card ("Send invite?") or a hard deny; null when the step may run.
    private async Task<WebToolResult?> Guard(Step s, TabState state, WebGuardVerdict verdict, WebElementFacts element, string key, string? target = null,
        IReadOnlyList<KeyValuePair<string, string>>? fields = null, WebPoint? near = null, string? from = null)
    {
        if (verdict.Kind == WebGuardKind.Deny) return Fail(WebErrorCodes.Denied, verdict.Reason ?? "nth never does this for a model.");
        if (verdict.Kind != WebGuardKind.Confirm) return null;
        var origin = WebPolicy.OriginOf(target ?? state.Tab.Url) ?? state.Tab.Url;
        var name = Clip(element.Name, 60);
        var rows = new List<KeyValuePair<string, string>>(fields ?? []);
        if (target is not null) rows.Add(new(UiText.Get("agent.guard.row_address"), target));
        rows.Add(new(UiText.Get("agent.guard.row_site"), WebPolicy.SourceName(state.Tab.Url)));
        var ask = new WebGuardAsk(Thread, state.Short, origin, verdict.Category, verdict.Word ?? "", name, s.Model) { Rows = rows, Chat = s.Host.ChatTitle(Thread), TabId = state.Tab.Id, Near = near, From = from };
        Report(UiText.Get("agent.step.check_card"), state.Short, WebAgentPhase.Asking);
        var (answered, confirmed) = await Ask("guard|" + state.Tab.Id + "|" + key + "|" + verdict.Category, t => s.Asker.Confirm(ask, t), false, s);
        if (!answered) return Waiting(s);
        // ED-NEW-web-sec-agent-10: the element's name is the page's words, so it goes inside the untrusted block.
        return confirmed ? null : Fail(WebErrorCodes.Cancelled, "The user cancelled this step. Do not try another way to do it."
            + (name.Length > 0 ? "\n" + WebAgentText.Untrusted(WebPolicy.OriginOf(state.Tab.Url), "Name: " + name) : ""));
    }

    // One ask, shared by the same call made again: it waits 25 s in this call; an answer that comes later is kept for the next.
    private async Task<(bool Answered, T Value)> Ask<T>(string key, Func<CancellationToken, Task<T>> start, T refused, Step s)
    {
        PendingAsk pending;
        lock (gate)
        {
            if (!asks.TryGetValue(key, out pending!))
            {
                var life = CancellationTokenSource.CreateLinkedTokenSource(turn.Token);
                Task<T> task;
                try
                {
                    task = start(life.Token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    task = Task.FromResult(refused);
                }
                pending = new PendingAsk(task, life);
                asks[key] = pending;
                _ = Expire(key, pending);
            }
        }
        var typed = (Task<T>)pending.Task;
        if (!typed.IsCompleted)
        {
            using var cut = CancellationTokenSource.CreateLinkedTokenSource(s.Token);
            await Task.WhenAny(typed, Swallow(hub.Clock.Delay(AskWaitMs, cut.Token)));
            await cut.CancelAsync();
        }
        s.Token.ThrowIfCancellationRequested();
        if (!typed.IsCompleted) return (false, refused);
        lock (gate)
        {
            if (asks.TryGetValue(key, out var current) && ReferenceEquals(current, pending)) asks.Remove(key);
        }
        await pending.Life.CancelAsync();
        return (true, typed.IsCompletedSuccessfully ? typed.Result : refused);
    }

    // A card goes after its lifetime, answered or not (ED-NEW-web-sec-agent-3: a Confirm nobody used does not wait for a look-alike
    // call); the next call asks again.
    private async Task Expire(string key, PendingAsk pending)
    {
        try
        {
            await hub.Clock.Delay(AskLifetimeMs, pending.Life.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        lock (gate)
        {
            if (asks.TryGetValue(key, out var current) && ReferenceEquals(current, pending)) asks.Remove(key);
        }
        await pending.Life.CancelAsync().ConfigureAwait(false);
    }

    // DESIGN 5.10 "unknown URL means stop": an address the model may open comes from the conversation, a page it read, search
    // results, or the site it is on.
    private bool IsKnown(Step s, string url, TabState? state)
    {
        if (WebAgentUrls.IsSearchResults(url) || s.Host.InConversation(Thread, url)) return true;
        var origin = WebPolicy.OriginOf(url);
        lock (gate)
        {
            if (knownUrls.Contains(WebAgentUrls.Normalize(url))) return true;
            if (origin is not null && readOrigins.Contains(origin)) return true;
        }
        return state is not null && SameOrigin(state.Tab.Url, url);
    }

    // Content the model read: its site counts for the cross-origin rule, its links and addresses become known.
    private void Saw(string? origin, string? text, IEnumerable<string> links)
    {
        lock (gate)
        {
            if (origin is not null) readOrigins.Add(origin);
            foreach (var link in links.Concat(WebAgentUrls.FindAddresses(text)).Take(2000))
                if (WebPolicy.IsWeb(link)) knownUrls.Add(WebAgentUrls.Normalize(link));
        }
        Kept(origin, text);
    }

    // Text the model read and the site it read it on (the last twelve reads): for "This address carries text from the page you
    // were on", and for typing it into another site (ED-NEW-web-sec-agent-5). Console, network and script output count too.
    private void Kept(string? origin, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (gate)
        {
            readTexts.AddLast((origin, text.Length <= 200_000 ? text : text[..200_000]));
            while (readTexts.Count > 12) readTexts.RemoveFirst();
        }
    }

    // Whether a query value came from a page the model read ("This address carries text from the page you were on").
    private bool FromPage(string value)
    {
        lock (gate) return readTexts.Any(t => t.Text.Contains(value, StringComparison.OrdinalIgnoreCase)) || shownNames.Any(t => t.Text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    // ED-NEW-web-sec-agent-5 (security review M3): the sites whose text the model is about to type into a page of target. Text read on
    // the same site, and a pair of sites the user always allows (the cross-origin card's "always"), do not count.
    private List<string> CarriedFrom(string typed, string? target)
    {
        var from = new List<string>();
        if (typed.Length == 0) return from;
        List<(string? Origin, string Text)> texts;
        lock (gate) texts = [.. readTexts, .. shownNames.Select(n => ((string?)n.Origin, n.Text))];
        foreach (var (source, text) in texts)
        {
            if (source is null || from.Contains(source, StringComparer.OrdinalIgnoreCase) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) continue;
            if (target is not null && hub.Grants.Pair(source, target)) continue;
            if (WebAgentText.Carries(typed, text)) from.Add(source);
        }
        return from;
    }

    // ED-NEW-web-sec-agent-9 (security review M3, second pass): the sites a text carries text from, together with what the model typed on
    // target before (a code typed in pieces adds up there), and the text the card shows: this text, or the pieces with it.
    private (List<string> From, string Shown) Carried(string typed, string? target)
    {
        var from = CarriedFrom(typed, target);
        if (from.Count > 0 || target is null || typed.Length == 0) return (from, typed);
        string? before;
        lock (gate) before = typedOn.GetValueOrDefault(target);
        if (string.IsNullOrEmpty(before)) return (from, typed);
        var joined = before + " " + typed;
        from = CarriedFrom(joined, target);
        return (from, from.Count == 0 ? typed : joined.Length <= 120 ? joined : "\u2026" + joined[^119..]);
    }

    // What the model typed on a site: the last 2,000 characters, pieces joined by a space, for at most 64 sites; a text whose carry card
    // the user confirmed starts it again (the user saw what goes there).
    private void Typed(string? target, string typed, bool confirmed)
    {
        if (target is null || typed.Length == 0) return;
        lock (gate)
        {
            if (confirmed)
            {
                typedOn.Remove(target);
                return;
            }
            var joined = typedOn.TryGetValue(target, out var before) && before.Length > 0 ? before + " " + typed : typed;
            typedOn[target] = joined.Length <= 2_000 ? joined : joined[^2_000..];
            if (typedOn.Count > 64) typedOn.Remove(typedOn.Keys.First(k => !string.Equals(k, target, StringComparison.OrdinalIgnoreCase)));
        }
    }

    // A title or an address the model was shown, and the site it belongs to (the tab's, read or not); the last 64, kept apart from the
    // twelve page reads so a tab list never pushes a page out of them.
    private void Named(string? origin, string? text)
    {
        if (origin is null || string.IsNullOrWhiteSpace(text)) return;
        var kept = text.Length <= 4_000 ? text : text[..4_000];
        lock (gate)
        {
            for (var node = shownNames.First; node is not null; node = node.Next)
            {
                if (string.Equals(node.Value.Origin, origin, StringComparison.OrdinalIgnoreCase) && node.Value.Text == kept)
                {
                    shownNames.Remove(node);
                    break;
                }
            }
            shownNames.AddLast((origin, kept));
            while (shownNames.Count > 64) shownNames.RemoveFirst();
        }
    }

    // ---- Navigations, take-over and the step window ----

    private void OnNavigationStarting(TabState state, WebNavigationStarting e)
    {
        if (!e.IsMainFrame || e.Cancel || !ModelCaused(state)) return;
        var url = e.Url;
        if (WebPolicy.IsBlank(url)) return;
        // ED-NEW-web-sec-agent-6 (security review L4): an option whose change sends its form (an onchange submit) would be a submit
        // the card never saw; a GET form on its own site (a filter, a sort) still runs.
        WebElementFacts? choosing;
        lock (gate) choosing = state.Choosing;
        if (choosing is { InForm: true } && WebAgentPolicy.CheckSubmit(choosing with { Submits = true }, WebPolicy.OriginOf(state.Tab.Url)).Kind != WebGuardKind.None)
        {
            Block(state, e, "Choosing that option sent its form, and nth stopped it: sending this form needs the user's OK. To send it, web_click the form's submit button (nth asks the user).");
            return;
        }
        var source = WebPolicy.OriginOf(state.Tab.Url);
        if (WebAgentUrls.DeniedReason(url) is { } why)
        {
            Block(state, e, "nth stopped the page from opening the address below: " + why + "\n" + WebAgentText.Untrusted(source, url));
            return;
        }
        var target = WebPolicy.OriginOf(url)!;
        if (state.Expected is { } expected && string.Equals(expected, target, StringComparison.OrdinalIgnoreCase)) return;
        if (SameOrigin(state.Tab.Url, url)) return;
        lock (gate) knownUrls.Add(WebAgentUrls.Normalize(url));
        if (state.Tab.Shared)
        {
            Block(state, e, "nth stopped the user's shared tab from moving to another site. To go there, call web_open with the address below (it asks the user):\n"
                + WebAgentText.Untrusted(source, url));
            return;
        }
        IReadOnlyList<string> from;
        lock (gate) from = WebAgentUrls.CrossOriginSources(readOrigins, url, hub.Grants.Pair);
        if (from.Count > 0)
            Block(state, e, "nth stopped the page from opening another site: after reading " + string.Join(", ", from.Select(WebPolicy.SourceName))
                + ", opening another site needs the user's OK. Call web_open with the address below to ask them:\n" + WebAgentText.Untrusted(source, url));
    }

    // ED-NEW-web-sec-agent-4 (security review M2): a model's click is a trusted gesture, so a pop-up it opens (target=_blank,
    // window.open) would load in a new tab with none of the checks above. While the model drives the tab, and for the 5 s its gesture
    // lasts, such a pop-up is stopped (it waits on Visus's quiet chip for the user) and the model is told to ask through web_open,
    // where the deny list and the cross-origin card apply. The user's own pop-ups (before the model drives, after a take-over, after
    // the turn) are never judged.
    private void OnPopupOpening(TabState state, WebPopupOpening e)
    {
        if (e.Cancel || !ModelPopup(state)) return;
        e.Cancel = true;
        var url = e.Url;
        // ED-NEW-web-sec-agent-10 (security review M2 and L4, second pass): the address is the page's choice (its fragment can say anything),
        // so it reaches the model inside the untrusted block, never in nth's own words.
        var source = WebPolicy.OriginOf(state.Tab.Url);
        string why;
        if (WebAgentUrls.DeniedReason(url) is { } denied)
        {
            why = "nth stopped the page from opening a new tab at the address below: " + denied + "\n" + WebAgentText.Untrusted(source, url);
        }
        else if (WebPolicy.IsWeb(url))
        {
            lock (gate) knownUrls.Add(WebAgentUrls.Normalize(url));
            Named(source, url);
            why = "nth stopped the page from opening a new tab at the address below. To go there, call web_open with it (it asks the user when it must):\n"
                + WebAgentText.Untrusted(source, url);
        }
        else
        {
            why = "nth stopped the page from opening a new tab.";
        }
        lock (gate) state.Blocked = why;
    }

    private bool ModelPopup(TabState state)
    {
        lock (gate)
        {
            if (state.TookOver) return false;
            return state.Stepping > 0 || hub.Clock.NowMs - state.LastStepEnd <= PopupAfterStepMs || (state.Driven && state.Tab.Kind == VisusTabKind.Agent);
        }
    }

    private void Block(TabState state, WebNavigationStarting e, string why)
    {
        e.Cancel = true;
        state.Blocked = why;
        if (WebPolicy.IsWeb(e.Url)) Named(WebPolicy.OriginOf(state.Tab.Url), e.Url);
    }

    private bool ModelCaused(TabState state)
    {
        lock (gate)
        {
            if (state.TookOver) return false;
            return state.Stepping > 0 || hub.Clock.NowMs - state.LastStepEnd <= AfterStepMs;
        }
    }

    private void OnBinding(TabState state, WebCdpEvent e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.ParamsJson);
            if (Text(document.RootElement, "name") != WebAgentOverlay.Binding) return;
        }
        catch (JsonException)
        {
            return;
        }
        OnUserInput(state);
    }

    // DESIGN 5.10 "User takes over": the user clicked or typed in a tab the model drives; the model pauses on that tab.
    private void OnUserInput(TabState state)
    {
        lock (gate)
        {
            if (!state.Driven || state.TookOver) return;
            state.TookOver = true;
        }
        hub.Report(new WebAgentActivity(Thread, WebAgentPhase.TookOver, UiText.Get("agent.step.resume_when_done"), state.Short) { TabId = state.Tab.Id });
        if (state.Cdp is { } cdp) _ = Swallow(state.Overlay.Drive(cdp, false, CancellationToken.None));
        hub.RaiseChanged();
    }

    private bool TookOver(TabState state)
    {
        lock (gate) return state.TookOver;
    }

    /// <summary>Whether the user took over one of the tabs the model drives (the pill reads "You took over Visus").</summary>
    public bool AnyTookOver
    {
        get
        {
            lock (gate) return states.Values.Any(s => s.TookOver);
        }
    }

    // DESIGN 2.7: the page crashed (or hung, or Visus stopped) while a model step ran on it: that step fails at once and is never
    // sent again; a step on another tab goes on.
    private void OnStepFailed(TabState state, string reason)
    {
        CancellationTokenSource? cut;
        lock (gate)
        {
            if (running is not { } now || state.Busy == 0) return;
            cut = now;
        }
        crashed = reason;
        try
        {
            cut.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private StepLease BeginStep(TabState state)
    {
        var lease = state.Tab.BeginModelStep();
        lock (gate)
        {
            state.Stepping++;
            state.Driven = true;
            state.Blocked = null;
            state.Choosing = null;
        }
        return new StepLease(() =>
        {
            lock (gate)
            {
                state.Stepping--;
                state.LastStepEnd = hub.Clock.NowMs;
            }
            lease.Dispose();
        });
    }

    // The overlay leaves the tabs the model drove (Stop, the turn's end).
    private void Release(List<TabState> driven)
    {
        foreach (var state in driven)
        {
            lock (gate)
            {
                state.Driven = false;
                state.TookOver = false;
                state.Expected = null;
            }
        }
        if (driven.Count == 0 || hub.Host is not { } host) return;
        _ = Swallow(host.OnUi(async () =>
        {
            foreach (var state in driven)
                if (state.Cdp is { } cdp) await Swallow(state.Overlay.Remove(cdp, CancellationToken.None));
            return true;
        }));
    }

    private void WatchRun()
    {
        var run = hub.Run;
        lock (gate)
        {
            if (disposed || ReferenceEquals(run, watchedRun)) return;
            if (watchedRun is not null) watchedRun.Stopped -= OnRunStopped;
            watchedRun = run;
            run.Stopped += OnRunStopped;
        }
    }

    private void OnRunStopped(PcScope scope, PcStopSource source)
    {
        // A sleep stops the streams, not the turn (Tactus DESIGN 2.10).
        if (source != PcStopSource.Sleep && scope.Covers(Thread, WebAgentHub.App)) StopTurn(source);
    }

    private async Task WaitStarted(TabState state, string? before, CancellationToken token)
    {
        var start = hub.Clock.NowMs;
        while (!state.Tab.Loading && (before is null || string.Equals(state.Tab.Url, before, StringComparison.Ordinal)) && state.Blocked is null && hub.Clock.NowMs - start < 1_000)
            await hub.Clock.Delay(50, token);
    }

    private async Task WaitLoaded(TabState state, CancellationToken token)
    {
        var start = hub.Clock.NowMs;
        while (state.Tab.Loading && state.Blocked is null && hub.Clock.NowMs - start < LoadWaitMs) await hub.Clock.Delay(100, token);
    }

    private void Report(string step, string? tab, WebAgentPhase phase = WebAgentPhase.Working)
    {
        string? id = null;
        if (tab is not null)
            lock (gate) shortIds.TryGetValue(tab, out id);
        hub.Report(new WebAgentActivity(Thread, phase, step, tab) { TabId = id });
    }

    // ---- Small helpers ----

    private static WebToolResult Fail(string code, string text) => WebToolResult.Fail(code, text);

    private static WebToolResult Stopped() => Fail(WebErrorCodes.Stopped, "The user stopped Visus. Do not use the web tools again this turn.");

    private static WebToolResult Waiting(Step s) => Fail(WebErrorCodes.WaitingForUser,
        "The user has not answered the card in Visus yet. Do other work, or call " + s.Tool + " again with the same arguments to keep waiting.");

    // An address without a scheme gets https (http for a dev host); a scheme stays, so the deny check sees it.
    private static string WithScheme(string url)
    {
        if (url.Contains("://", StringComparison.Ordinal)) return url;
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && !url[..colon].Contains('.', StringComparison.Ordinal) && (colon + 1 >= url.Length || !char.IsAsciiDigit(url[colon + 1]))) return url;
        var host = url.Split('/', ':', '?', '#')[0];
        return (WebPolicy.IsDevHost(host) ? "http://" : "https://") + url;
    }

    private static bool SameOrigin(string? a, string? b)
    {
        var left = WebPolicy.OriginOf(a);
        return left is not null && string.Equals(left, WebPolicy.OriginOf(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string N(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

    private static string Clip(string? text, int max)
    {
        var value = (text ?? "").Trim();
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }

    private static string? Str(JsonObject args, string name) => args[name] switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    private static double? Num(JsonObject args, string name)
    {
        if (args[name] is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<decimal>(out var m)) return (double)m;
        if (value.TryGetValue<float>(out var f)) return f;
        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number) return element.GetDouble();
        if (value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static bool Bool(JsonObject args, string name)
    {
        if (args[name] is not JsonValue value) return false;
        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<JsonElement>(out var element)) return element.ValueKind == JsonValueKind.True;
        return value.TryGetValue<string>(out var text) && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task Quietly(CdpBroker cdp, string method, CancellationToken token, JsonObject? parameters = null, string? session = null)
    {
        try
        {
            await cdp.CallAsync(method, parameters, session, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
        }
    }

    private static async Task Swallow(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private sealed record Step(string Tool, JsonObject Args, WebAgentCaller Caller, IWebAgentHost Host, IWebAgentAsker Asker, CancellationToken Token)
    {
        public string Model => Host.ModelName(Caller.Model);

        /// <summary>The tabs this call holds (released when it ends).</summary>
        public List<IDisposable> Leases { get; } = [];
    }

    // One call's hold on a tab: the tab's model step, and the count a crash looks at.
    private sealed class TabLease : IDisposable
    {
        private readonly WebAgentEngine engine;
        private IDisposable? step;

        public TabLease(WebAgentEngine engine, TabState state, IDisposable step)
        {
            this.engine = engine;
            State = state;
            this.step = step;
            lock (engine.gate) state.Busy++;
        }

        public TabState State { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref step, null) is not { } held) return;
            lock (engine.gate) State.Busy--;
            held.Dispose();
        }
    }

    private sealed record PendingAsk(Task Task, CancellationTokenSource Life);

    private sealed class Refusal(WebToolResult result) : Exception(result.Text)
    {
        public WebToolResult Result { get; } = result;
    }

    private sealed class StepLease(Action end) : IDisposable
    {
        private Action? end = end;

        public void Dispose() => Interlocked.Exchange(ref end, null)?.Invoke();
    }

    private sealed class Unhooking(Action undo) : IDisposable
    {
        private Action? undo = undo;

        public void Dispose() => Interlocked.Exchange(ref undo, null)?.Invoke();
    }

    private sealed class TabState(IWebAgentTab tab, string shortId, Func<long> clock)
    {
        public IWebAgentTab Tab { get; set; } = tab;
        public string Short { get; } = shortId;
        public WebAgentLogs Logs { get; private set; } = new(clock);
        public WebRefs Refs { get; set; } = new();
        public WebAgentOverlay Overlay { get; set; } = new();
        public IWebEngineView? View { get; set; }
        public CdpBroker? Cdp { get; set; }
        public List<IDisposable> Hooks { get; } = [];
        public bool FramesAttached { get; set; }

        /// <summary>"Allow once": the site this tab may be used on until it leaves it.</summary>
        public string? OnceOrigin { get; set; }

        /// <summary>The site a model navigation (web_open, reload) may load without a check.</summary>
        public string? Expected { get; set; }

        /// <summary>Why the last model-caused navigation was stopped.</summary>
        public string? Blocked { get; set; }

        /// <summary>The select web_select changed in its last step (a navigation it causes is judged as its form's submit).</summary>
        public WebElementFacts? Choosing { get; set; }

        public int Stepping { get; set; }

        /// <summary>The calls that hold the tab now (a crash fails the running one).</summary>
        public int Busy { get; set; }
        public long LastStepEnd { get; set; } = long.MinValue / 2;
        public bool Driven { get; set; }
        public bool TookOver { get; set; }

        public void Unhook()
        {
            foreach (var hook in Hooks) hook.Dispose();
            Hooks.Clear();
            Logs = new WebAgentLogs(clock);
            View = null;
            Cdp = null;
            OverlayReady = false;
        }

        /// <summary>The overlay is in the page now (its last install answered).</summary>
        public bool OverlayReady { get; set; }
    }
}
