// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-4; DESIGN.md 3.1, 3.3). Reads windows
// through UI Automation's COM client: one cache request for the whole control-view subtree (the properties the tree line
// needs and every pattern's availability), RuntimeIds as dotted text, bounds in physical pixels. Several apps at once
// (ED-NEW-pcctl-6): each window gets its own MTA worker thread (per-monitor DPI aware v2), so a slow or hung app never
// holds up another; the one UIA client object is free-threaded and shared. IUIAutomation2's connection and transaction
// timeouts are 2 s, and the reader gives up on a call after 4 s; a worker still stuck in a hung app is replaced. Never
// used by automated tests; the pure mapping from UIA values to a PcUiNode (UiaNodes) is.
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class UiaReader : IUiaReader, IDisposable
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(4);
    public const int NodeBudget = 6000;

    private static readonly int[] cachedProperties =
    [
        UiaIds.RuntimeId, UiaIds.BoundingRectangle, UiaIds.ControlType, UiaIds.LocalizedControlType, UiaIds.Name, UiaIds.AcceleratorKey,
        UiaIds.HasKeyboardFocus, UiaIds.IsKeyboardFocusable, UiaIds.IsEnabled, UiaIds.AutomationId, UiaIds.IsPassword,
        UiaIds.IsOffscreen, UiaIds.IsExpandCollapsePatternAvailable, UiaIds.IsInvokePatternAvailable,
        UiaIds.IsRangeValuePatternAvailable, UiaIds.IsScrollPatternAvailable, UiaIds.IsScrollItemPatternAvailable,
        UiaIds.IsSelectionItemPatternAvailable, UiaIds.IsTextPatternAvailable, UiaIds.IsTogglePatternAvailable,
        UiaIds.IsValuePatternAvailable, UiaIds.IsWindowPatternAvailable, UiaIds.ValueValue, UiaIds.ValueIsReadOnly,
        UiaIds.RangeValueValue, UiaIds.RangeValueIsReadOnly, UiaIds.ScrollHorizontallyScrollable,
        UiaIds.ScrollVerticallyScrollable, UiaIds.ExpandCollapseState, UiaIds.SelectionItemIsSelected, UiaIds.ToggleState,
    ];

    private readonly PcWorkerPool workers = new("nth Tactus UIA");
    private readonly Lock gate = new();
    private UiaClient? client;

    // The client object and its two cache requests, made once and shared by every worker (all MTA).
    private sealed record UiaClient(IUIAutomation2 Automation, IUIAutomationCacheRequest Tree, IUIAutomationCacheRequest Element);

    public Task<PcUiNode?> Tree(nint window, CancellationToken cancellationToken) => workers.For(window).Run(() =>
    {
        var root = Check(Client().Automation.ElementFromHandleBuildCache(window, Client().Tree, out var element), element);
        if (root is null) return null;
        var budget = NodeBudget;
        return Build(root, ref budget, withChildren: true);
    }, CallTimeout, cancellationToken);

    public Task<PcUiNode?> Element(nint window, string runtimeId, CancellationToken cancellationToken) => workers.For(window).Run(() =>
        Find(window, runtimeId, Client().Element) is { } element ? Build(element) : null, CallTimeout, cancellationToken);

    public Task<PcUiNode?> Focused(nint window, CancellationToken cancellationToken) => workers.For(window).Run(() =>
    {
        if (Check(Client().Automation.GetFocusedElement(out var focused), focused) is not { } element) return null;
        if (Check(element.BuildUpdatedCache(Client().Element, out var cached), cached) is not { } updated) return null;
        Win32.GetWindowThreadProcessId(window, out var windowProcess);
        updated.GetCurrentPropertyValue(UiaIds.ProcessId, out var process);
        return process is int pid && pid == windowProcess ? Build(updated) : null;
    }, CallTimeout, cancellationToken);

    public Task<PcUiaOutcome> Perform(nint window, string runtimeId, PcElementCommand command, CancellationToken cancellationToken) => workers.For(window).Run(() =>
        Find(window, runtimeId, Client().Element) is { } element
            ? UiaPatterns.Perform(element, Build(element), command)
            : PcUiaOutcome.Fail(PcErrorCode.StaleElement, "the element is gone"), CallTimeout, cancellationToken);

    public Task<string?> ReadText(nint window, string runtimeId, CancellationToken cancellationToken) => workers.For(window).Run(() =>
    {
        if (Find(window, runtimeId, Client().Tree) is not { } element) return null;
        var budget = NodeBudget;
        return UiaPatterns.ReadText(element, Build(element, ref budget, withChildren: true));
    }, CallTimeout, cancellationToken);

    public void Dispose() => workers.Dispose();

    private UiaClient Client()
    {
        lock (gate) return client ??= Create();
    }

    private static UiaClient Create()
    {
        var type = Type.GetTypeFromCLSID(UiaIds.CUIAutomation8, throwOnError: true)!;
        var created = (IUIAutomation2)Activator.CreateInstance(type)!;
        created.put_ConnectionTimeout(2000);
        created.put_TransactionTimeout(2000);
        Marshal.ThrowExceptionForHR(created.get_ControlViewCondition(out var controlView));
        return new UiaClient(created, Request(created, controlView!, UiaIds.TreeScopeSubtree), Request(created, controlView!, UiaIds.TreeScopeElement));
    }

    private static IUIAutomationCacheRequest Request(IUIAutomation2 automation, IUIAutomationCondition filter, int scope)
    {
        Marshal.ThrowExceptionForHR(automation.CreateCacheRequest(out var request));
        foreach (var property in cachedProperties) Marshal.ThrowExceptionForHR(request!.AddProperty(property));
        Marshal.ThrowExceptionForHR(request!.put_TreeScope(scope));
        Marshal.ThrowExceptionForHR(request.put_TreeFilter(filter));
        Marshal.ThrowExceptionForHR(request.put_AutomationElementMode(UiaIds.ElementModeFull));
        return request;
    }

    private IUIAutomationElement? Find(nint window, string runtimeId, IUIAutomationCacheRequest cache)
    {
        if (UiaNodes.ParseRuntimeId(runtimeId) is not { } id) return null;
        if (Check(Client().Automation.ElementFromHandle(window, out var root), root) is not { } windowElement) return null;
        if (Client().Automation.CreatePropertyCondition(UiaIds.RuntimeId, id, out var condition) != 0 || condition is null) return null;
        return Check(windowElement.FindFirstBuildCache(UiaIds.TreeScopeSubtree, condition, cache, out var found), found);
    }

    private static PcUiNode Build(IUIAutomationElement element)
    {
        var budget = 1;
        return Build(element, ref budget, withChildren: false);
    }

    private static PcUiNode Build(IUIAutomationElement element, ref int budget, bool withChildren)
    {
        budget--;
        var node = UiaNodes.Node(id => element.GetCachedPropertyValue(id, out var value) == 0 ? value : null);
        if (!withChildren || budget <= 0 || element.GetCachedChildren(out var array) != 0 || array is null) return node;
        array.get_Length(out var length);
        var children = new List<PcUiNode>(Math.Max(0, length));
        for (var i = 0; i < length && budget > 0; i++)
        {
            if (array.GetElement(i, out var child) == 0 && child is not null) children.Add(Build(child, ref budget, withChildren: true));
        }
        return node with { Children = children };
    }

    private static T? Check<T>(int hresult, T? value) where T : class => hresult == 0 ? value : null;
}

/// <summary>The pure mapping from cached UIA values to a node; tested without UIA.</summary>
public static class UiaNodes
{
    public static string RuntimeIdText(int[]? id) => id is null ? "" : string.Join('.', id.Select(n => n.ToString(CultureInfo.InvariantCulture)));

    public static int[]? ParseRuntimeId(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split('.');
        var id = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id[i])) return null;
        return id;
    }

    /// <summary>UIA's BoundingRectangle (left, top, width, height as doubles) as whole physical pixels; empty when missing.</summary>
    public static PcRect Bounds(object? value) => value is double[] { Length: 4 } r && double.IsFinite(r[0]) && double.IsFinite(r[1]) && r[2] > 0 && r[3] > 0
        ? new PcRect((int)Math.Round(r[0]), (int)Math.Round(r[1]), (int)Math.Round(r[2]), (int)Math.Round(r[3]))
        : default;

    /// <summary>The node from one element's values; <paramref name="read"/> returns a UIA property's value or null.</summary>
    public static PcUiNode Node(Func<int, object?> read)
    {
        bool Flag(int id) => read(id) is bool b && b;
        string? Text(int id) => read(id) is string s && s.Length > 0 ? s : null;
        int? Number(int id) => read(id) is int n ? n : null;

        var actions = PcActions.None;
        if (Flag(UiaIds.IsInvokePatternAvailable)) actions |= PcActions.Invoke;
        if (Flag(UiaIds.IsTogglePatternAvailable)) actions |= PcActions.Toggle;
        if (Flag(UiaIds.IsSelectionItemPatternAvailable)) actions |= PcActions.Select;
        if (Flag(UiaIds.IsExpandCollapsePatternAvailable)) actions |= PcActions.ExpandCollapse;
        if (Flag(UiaIds.IsValuePatternAvailable)) actions |= PcActions.Value;
        if (Flag(UiaIds.IsRangeValuePatternAvailable)) actions |= PcActions.RangeValue;
        if (Flag(UiaIds.IsScrollPatternAvailable)) actions |= PcActions.Scroll;
        if (Flag(UiaIds.IsScrollItemPatternAvailable)) actions |= PcActions.ScrollIntoView;
        if (Flag(UiaIds.IsTextPatternAvailable)) actions |= PcActions.Text;
        if (Flag(UiaIds.IsWindowPatternAvailable)) actions |= PcActions.Window;
        if (Flag(UiaIds.IsKeyboardFocusable)) actions |= PcActions.Focus;

        var states = PcStates.None;
        if (Flag(UiaIds.HasKeyboardFocus)) states |= PcStates.Focused;
        if (read(UiaIds.IsEnabled) is false) states |= PcStates.Disabled;
        if (Flag(UiaIds.IsPassword)) states |= PcStates.Password;
        if (Flag(UiaIds.IsOffscreen)) states |= PcStates.Offscreen;
        if ((actions & PcActions.Select) != 0 && Flag(UiaIds.SelectionItemIsSelected)) states |= PcStates.Selected;
        if ((actions & PcActions.Toggle) != 0)
        {
            var toggle = Number(UiaIds.ToggleState);
            if (toggle == UiaIds.ToggleOn) states |= PcStates.Checked;
            else if (toggle == UiaIds.ToggleIndeterminate) states |= PcStates.Mixed;
        }
        if ((actions & PcActions.ExpandCollapse) != 0)
        {
            var expand = Number(UiaIds.ExpandCollapseState);
            if (expand == UiaIds.Collapsed) states |= PcStates.Collapsed;
            else if (expand is UiaIds.Expanded or UiaIds.PartiallyExpanded) states |= PcStates.Expanded;
        }
        if (((actions & PcActions.Value) != 0 && Flag(UiaIds.ValueIsReadOnly)) || ((actions & PcActions.Value) == 0 && (actions & PcActions.RangeValue) != 0 && Flag(UiaIds.RangeValueIsReadOnly)))
            states |= PcStates.ReadOnly;
        if ((actions & PcActions.Scroll) != 0 && (Flag(UiaIds.ScrollVerticallyScrollable) || Flag(UiaIds.ScrollHorizontallyScrollable)))
            states |= PcStates.Scrollable;

        string? value = null;
        if ((actions & PcActions.Value) != 0) value = Text(UiaIds.ValueValue);
        else if ((actions & PcActions.RangeValue) != 0 && read(UiaIds.RangeValueValue) is double range && double.IsFinite(range))
            value = range.ToString("0.###", CultureInfo.InvariantCulture);

        return new PcUiNode(RuntimeIdText(read(UiaIds.RuntimeId) as int[]), Role(Text(UiaIds.LocalizedControlType), Number(UiaIds.ControlType)), Text(UiaIds.Name) ?? "")
        {
            AutomationId = Text(UiaIds.AutomationId),
            Shortcut = Text(UiaIds.AcceleratorKey),
            Value = (states & PcStates.Password) != 0 ? null : value,
            States = states,
            Actions = actions,
            Bounds = Bounds(read(UiaIds.BoundingRectangle)),
        };
    }

    // The localized control type is what UIA shows people; a missing one falls back to the control type's English name.
    private static string Role(string? localized, int? controlType) => localized ?? controlType switch
    {
        50000 => "button", 50001 => "calendar", 50002 => "check box", 50003 => "combo box", 50004 => "edit",
        50005 => "hyperlink", 50006 => "image", 50007 => "list item", 50008 => "list", 50009 => "menu", 50010 => "menu bar",
        50011 => "menu item", 50012 => "progress bar", 50013 => "radio button", 50014 => "scroll bar", 50015 => "slider",
        50016 => "spinner", 50017 => "status bar", 50018 => "tab", 50019 => "tab item", 50020 => "text", 50021 => "tool bar",
        50022 => "tool tip", 50023 => "tree", 50024 => "tree item", 50025 => "custom", 50026 => "group", 50027 => "thumb",
        50028 => "data grid", 50029 => "data item", 50030 => "document", 50031 => "split button", 50032 => "window",
        50033 => "pane", 50034 => "header", 50035 => "header item", 50036 => "table", 50037 => "title bar",
        50038 => "separator", 50039 => "semantic zoom", 50040 => "app bar",
        _ => "element",
    };
}

/// <summary>
/// One dedicated MTA thread, per-monitor DPI aware v2, that runs posted work in order. A caller stops waiting after the
/// timeout (the work may still finish later); a caller's cancellation stops it waiting at once.
/// </summary>
internal sealed class PcWorkerThread : IDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread thread;

    public PcWorkerThread(string name)
    {
        thread = new Thread(Loop) { IsBackground = true, Name = name };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private int pending;
    private long lastDone = Environment.TickCount64;
    private volatile bool stuck;

    /// <summary>True while a call that timed out is still running here (a hung app); the pool then uses a fresh thread.</summary>
    public bool Stuck => stuck;

    /// <summary>How long the thread has had nothing to do.</summary>
    public TimeSpan IdleFor(long now) => Volatile.Read(ref pending) > 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(now - Interlocked.Read(ref lastDone));

    public async Task<T> Run<T>(Func<T> work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref pending);
        queue.Add(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) done.TrySetCanceled(cancellationToken);
                else done.TrySetResult(work());
            }
            catch (COMException error) when (error.HResult == unchecked((int)0x80131505)) { done.TrySetException(new PcUiaTimeoutException("the app did not answer in time")); }
            catch (Exception error) { done.TrySetException(error); }
            finally
            {
                Interlocked.Exchange(ref lastDone, Environment.TickCount64);
                Interlocked.Decrement(ref pending);
                stuck = false;
            }
        }, CancellationToken.None);
        try { return await done.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            stuck = !done.Task.IsCompleted;
            throw new PcUiaTimeoutException("the app did not answer in time");
        }
    }

    private void Loop()
    {
        Win32.SetThreadDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        foreach (var work in queue.GetConsumingEnumerable()) work();
    }

    public void Dispose() => queue.CompleteAdding();
}

/// <summary>
/// One worker thread per window, so apps are read and driven side by side (ED-NEW-pcctl-6). A thread left stuck in a hung
/// app is replaced for the next call; a thread idle for a minute is retired.
/// </summary>
internal sealed class PcWorkerPool : IDisposable
{
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(1);

    private readonly Lock gate = new();
    private readonly Dictionary<nint, PcWorkerThread> workers = [];
    private readonly string name;

    public PcWorkerPool(string name) => this.name = name;

    public PcWorkerThread For(nint window)
    {
        lock (gate)
        {
            var now = Environment.TickCount64;
            foreach (var (handle, idle) in workers.Where(p => p.Key != window && p.Value.IdleFor(now) > Idle).ToList())
            {
                idle.Dispose();
                workers.Remove(handle);
            }
            if (workers.TryGetValue(window, out var worker) && !worker.Stuck) return worker;
            worker?.Dispose();
            worker = new PcWorkerThread(name + " " + PcWindowRef.Format(window));
            workers[window] = worker;
            return worker;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var worker in workers.Values) worker.Dispose();
            workers.Clear();
        }
    }
}
