using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Runtime;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Markdown;

// PORT-ONLY: web surfaces W10, links from chats in the app (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11; ED-NEW-web-w10-2). No Swift
// counterpart. The one place a link a chat shows becomes an open: the chat's prose, the Open button of a Hydra report, a live answer's
// open effect, an artifact's or an MCP App's link after its own card. It picks the router of the chat (so a link opens Visus in that chat's
// workspace), hands the router the three places a link can go, and shows the first-time question over the chat. Sign-in, update, store and
// non-web addresses never come here: they call Workspace.Open themselves and stay in the system's hands.

namespace Marrow.App.Port.Web;

public static class ChatLinks
{
    private sealed class Holder(ChatLinkRouter router, WebSettings settings)
    {
        public ChatLinkRouter Router { get; } = router;

        public WebSettings Settings { get; } = settings;

        public Control? Anchor { get; set; }
    }

    private static readonly ConditionalWeakTable<ThreadRuntime, Holder> Holders = new();
    private static Holder? loose;

    // The chat a link is being opened for, set by a caller that knows it while it runs a default that does not (an Ambient with no chat).
    [ThreadStatic] private static ThreadRuntime? scopedRuntime;
    [ThreadStatic] private static Control? scopedAnchor;

    /// <summary>Shows the first-time question over the control the click came from. Tests replace it; the app draws <see cref="ChatLinkSheet"/>.</summary>
    public static Action<ChatLinkChoice, Control?> ShowChoice { get; set; } = ChatLinkSheet.Show;

    /// <summary>
    /// Opens a page in the PC's default browser (or an address in the app that handles its scheme): what a click on a link in a chat did before
    /// there was a setting (MarkdownLinkOpener, whose shell seam refuses what the shell must not run). Tests replace it.
    /// </summary>
    public static Action<SwiftURL> OpenExternal { get; set; } = MarkdownLinkOpener.Open;

    /// <summary>Opens a path or file link: reveals it or opens it in the editor (MarkdownLinkOpener). Tests replace it.</summary>
    public static Action<SwiftURL> OpenLocal { get; set; } = MarkdownLinkOpener.Open;

    /// <summary>Opens an address as a Visus tab in the chat's workspace. Tests replace it.</summary>
    public static Action<ThreadRuntime?, SwiftURL> OpenVisus { get; set; } = OpenInVisus;

    /// <summary>The settings a caller with no window of its own reads; the app's, through <see cref="WebSettingsHost"/>.</summary>
    public static Func<WebSettings> Settings { get; set; } = static () => WebSettingsHost.Current;

    /// <summary>
    /// A link was clicked or chosen in a chat. <paramref name="modifiers"/>: Ctrl takes the other browser for this link. Always true: the
    /// link was taken (a question being open counts; nothing else opens meanwhile). A sign-in address stays in the default browser.
    /// </summary>
    public static bool Open(SwiftURL url, ThreadRuntime? runtime, KeyModifiers modifiers = KeyModifiers.None, Control? anchor = null, bool forceExternal = false, WebSettings? settings = null)
    {
        runtime ??= scopedRuntime;
        anchor ??= scopedAnchor;
        var holder = HolderFor(runtime, settings ?? Settings());
        holder.Anchor = anchor;
        holder.Router.Open(url, control: modifiers.HasFlag(KeyModifiers.Control), forceExternal: forceExternal);
        return true;
    }

    /// <summary>Runs <paramref name="action"/> with the chat (and the control) a link opened inside it belongs to.</summary>
    public static T With<T>(ThreadRuntime? runtime, Control? anchor, Func<T> action)
    {
        var (before, beforeAnchor) = (scopedRuntime, scopedAnchor);
        (scopedRuntime, scopedAnchor) = (runtime ?? before, anchor ?? beforeAnchor);
        try { return action(); }
        finally { (scopedRuntime, scopedAnchor) = (before, beforeAnchor); }
    }

    /// <summary>A web address from somewhere with no chat of its own (an Ambient default): the scoped chat, else the window's chosen thread.</summary>
    public static bool OpenFromAnywhere(string address) => SwiftURL.Create(address) is { } url && OpenFromAnywhere(url);

    /// <summary><see cref="OpenFromAnywhere(string)"/> for an address already parsed (the process-wide link hook, InlineText.OpenUrl).</summary>
    public static bool OpenFromAnywhere(SwiftURL url) => Open(url, scopedRuntime ?? KeyWindowRuntime(), KeyModifiers.None, scopedAnchor);

    private static Holder HolderFor(ThreadRuntime? runtime, WebSettings settings)
    {
        if (runtime is null)
        {
            if (loose is null || !ReferenceEquals(loose.Settings, settings)) loose = Make(null, settings);
            return loose;
        }
        if (!Holders.TryGetValue(runtime, out var kept) || !ReferenceEquals(kept.Settings, settings))
        {
            kept = Make(runtime, settings);
            Holders.AddOrUpdate(runtime, kept);
        }
        return kept;
    }

    private static Holder Make(ThreadRuntime? runtime, WebSettings settings)
    {
        var weak = runtime is null ? null : new WeakReference<ThreadRuntime>(runtime);
        ThreadRuntime? Target() => weak is not null && weak.TryGetTarget(out var target) ? target : null;
        var router = new ChatLinkRouter(settings, url => OpenVisus(Target(), url), url => OpenExternal(url), url => OpenLocal(url));
        var holder = new Holder(router, settings);
        router.PendingChanged += choice =>
        {
            if (choice is not null) ShowChoice(choice, holder.Anchor);
        };
        return holder;
    }

    // A side chat is a thread of its own, but its pane sits in the main thread's workspace, so a page opens there.
    private static ThreadRuntime? WorkspaceOwner(ThreadRuntime? runtime)
    {
        if (runtime is null) return null;
        if (runtime.Thread is { IsSideChat: true, ParentThreadID: { } parent } && runtime.App?.ExistingRuntime(parent) is { } owner) return owner;
        return runtime;
    }

    private static ThreadRuntime? KeyWindowRuntime()
    {
        try
        {
            var model = Marrow.App.Window.WindowManager.Shared.Model;
            return model.SelectedThreadID is { } id ? model.ExistingRuntime(id) : null;
        }
        catch (InvalidOperationException)
        {
            return null; // no app model yet (a host with no window)
        }
    }

    private static void OpenInVisus(ThreadRuntime? runtime, SwiftURL url)
    {
        var address = url.AbsoluteString;
        var owner = WorkspaceOwner(runtime ?? KeyWindowRuntime());
        // Visus shows web pages in a chat's workspace; with no chat to hold the pane, or an address Visus does not load, the default browser has it.
        if (owner is null || !WebPolicy.IsTopLevelAllowed(address))
        {
            OpenExternal(url);
            return;
        }
        var session = VisusHost.SessionFor(owner);
        var kind = VisusTabRules.KindForOpened(VisusTabKind.Browse, address);
        var open = session.Tabs.FirstOrDefault(tab => tab.Kind == kind && string.Equals(tab.Url, address, StringComparison.Ordinal));
        if (open is null) session.NewTab(kind, address);
        else session.Select(open);
        owner.Workspace = owner.Workspace.Open(PaneKind.Browser);
    }
}
