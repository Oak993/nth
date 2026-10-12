// PORT-ONLY: web surfaces W19, MCP Apps in the app (DESIGN.md 7.6; ED-NEW-web-w19-2, -3). No Swift counterpart. Program.Main's
// one Bind line: the host over the process's MCP hub and the grants file beside nth's settings, the cards it asks for drawn in
// the chat's approval stack, the app messages and context chips handed to the chat, links through the link setting, the App
// pane registered, and the list of app tools read at start and whenever a server says its tools changed.
//
// The cards (DESIGN 7.6, 5.5): the domains card says "shipyard's Release board wants to reach: ..." with Allow for shipyard /
// Not now, and Allow broad access only when the declaration holds wildcards or 'unsafe-eval' (its detail names the risk); the
// first message asks "Let shipyard post messages in this chat?"; a tool call from the app asks "shipyard wants to run
// promote_release" with Allow once / Always for shipyard / Deny, unless the chat runs with full access; the camera, the
// microphone and the location ask each time. An app's card is never answered by anything but the user's click.
// + ED-NEW-web-w26-3 (W26 security review): a link the app opens by itself asks "shipyard wants to open shipyard.dev" with
// Open / Cancel, the full address in the detail; a link the user clicks in the view still opens at once.
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.MCP;
using Marrow.Services.Web;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Port.Web.McpApps;

public static class McpAppsStartup
{
    public static void Bind()
    {
        var hub = MCPHub.Shared;
        var ui = new McpAppHostUi(thread => Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(thread));
        McpAppsHost.Bind(new McpAppsHost(new McpHubAppServers(hub), new McpAppGrantStore(McpAppGrantStore.DefaultPath()), ui, MainActor.Post,
            log: line => System.Diagnostics.Trace.WriteLine(line)));
        AppPane.Register();
        // W24's HTML live answers (html_widget, a fenced nth-html block) are drawn by the inline widget host.
        Marrow.App.Views.Chat.LiveAnswerHtml.Host.SetProcessDefault(Marrow.App.Views.Chat.InlineWebWidget.ForLiveAnswer);
        hub.ListChanged += change => { _ = McpAppCatalog.RefreshAsync(hub); };
        _ = McpAppCatalog.RefreshAsync(hub);
    }
}

/// <summary>What the MCP Apps host asks of the app: the chat's cards, its messages, its context chips, links and the theme.</summary>
public sealed class McpAppHostUi : IMcpAppHostUi
{
    private static readonly Dictionary<McpAppMount, (ThreadRuntime Runtime, HashSet<string> Ids)> Pending = [];
    private readonly Func<Guid, ThreadRuntime?> runtimes;

    private Action? themeChanged;
    private bool themeHooked;

    public McpAppHostUi(Func<Guid, ThreadRuntime?> runtimes) => this.runtimes = runtimes;

    public const string AllowOnceText = "Allow once";
    public const string DenyText = "Deny";
    public const string AllowText = "Allow";

    public static string AlwaysText(string server) => "Always for " + server;

    public const string OpenText = "Open";
    public const string CancelText = "Cancel";
    private const int MaxLinkDetail = 500;

    /// <summary>ED-NEW-web-w26-3: "shipyard wants to open shipyard.dev".</summary>
    public static string LinkTitle(string server, string url) => server + " wants to open " + WebPolicy.SourceName(url);

    /// <summary>The whole address, so the user sees what the link carries (cut at 500 characters).</summary>
    public static string LinkDetail(string url) => url.Length <= MaxLinkDetail ? url : url[..MaxLinkDetail] + "…";

    public static string ToolTitle(string server, string tool) => server + " wants to run " + tool;

    public static string ToolDetail(string title) => "From its " + title + ", in this chat.";

    public static string PermissionTitle(string server, WebPermissionKind kind) => server + " wants to use " + kind switch
    {
        WebPermissionKind.Camera => "the camera",
        WebPermissionKind.Microphone => "the microphone",
        WebPermissionKind.Location => "your location",
        _ => kind.ToString(),
    };

    public bool Dark => Avalonia.Application.Current is { } application && PlainControls.IsDark(application.ActualThemeVariant);

    /// <summary>nth's theme changed (hooked on the application when the first app listens, once it runs).</summary>
    public event Action? ThemeChanged
    {
        add
        {
            themeChanged += value;
            if (themeHooked || Avalonia.Application.Current is not { } application) return;
            themeHooked = true;
            application.ActualThemeVariantChanged += (_, _) => themeChanged?.Invoke();
        }
        remove => themeChanged -= value;
    }

    public async Task<McpAppDomainDecision> AskDomainsAsync(McpAppMount app, McpAppDomainRequest request)
    {
        var options = new List<ApprovalRequest.Option> { new("allow", request.AllowText, ApprovalRequest.Option.Role.Approve) };
        if (request.HasBroad) options.Add(new ApprovalRequest.Option("broad", McpAppDomains.BroadAccessTitle, ApprovalRequest.Option.Role.ApproveAlways));
        options.Add(new ApprovalRequest.Option("not-now", McpAppDomains.NotNowText, ApprovalRequest.Option.Role.Decline));
        var chosen = await Ask(app, ApprovalRequest.Kind.Permissions, request.Sentence, options, request.HasBroad ? McpAppDomains.BroadAccessRisk : null).ConfigureAwait(false);
        return chosen?.Id switch
        {
            "allow" => McpAppDomainDecision.Allow,
            "broad" => McpAppDomainDecision.AllowBroad,
            _ => McpAppDomainDecision.NotNow,
        };
    }

    public async Task<bool> AskMessagesAsync(McpAppMount app)
    {
        var options = new List<ApprovalRequest.Option>
        {
            new("allow", AllowText, ApprovalRequest.Option.Role.Approve),
            new("not-now", McpAppDomains.NotNowText, ApprovalRequest.Option.Role.Decline),
        };
        var chosen = await Ask(app, ApprovalRequest.Kind.Permissions, McpAppUntrusted.MessagePermission(app.Server), options, null).ConfigureAwait(false);
        return chosen?.Id == "allow";
    }

    public async Task<McpAppToolDecision> AskToolAsync(McpAppMount app, string tool, string? title)
    {
        var options = new List<ApprovalRequest.Option>
        {
            new("once", AllowOnceText, ApprovalRequest.Option.Role.Approve),
            new("always", AlwaysText(app.Server), ApprovalRequest.Option.Role.ApproveAlways),
            new("deny", DenyText, ApprovalRequest.Option.Role.Decline),
        };
        var chosen = await Ask(app, ApprovalRequest.Kind.Tool, ToolTitle(app.Server, tool), options, ToolDetail(title ?? app.Title)).ConfigureAwait(false);
        return chosen?.Id switch
        {
            "once" => McpAppToolDecision.AllowOnce,
            "always" => McpAppToolDecision.AlwaysForServer,
            _ => McpAppToolDecision.Deny,
        };
    }

    public async Task<bool> AskPermissionAsync(McpAppMount app, WebPermissionKind kind)
    {
        var options = new List<ApprovalRequest.Option>
        {
            new("once", AllowOnceText, ApprovalRequest.Option.Role.Approve),
            new("deny", DenyText, ApprovalRequest.Option.Role.Decline),
        };
        var chosen = await Ask(app, ApprovalRequest.Kind.Permissions, PermissionTitle(app.Server, kind), options, ToolDetail(app.Title)).ConfigureAwait(false);
        return chosen?.Id == "once";
    }

    public async Task<bool> AskOpenLinkAsync(McpAppMount app, string url)
    {
        var options = new List<ApprovalRequest.Option>
        {
            new("open", OpenText, ApprovalRequest.Option.Role.Approve),
            new("cancel", CancelText, ApprovalRequest.Option.Role.Decline),
        };
        var chosen = await Ask(app, ApprovalRequest.Kind.Permissions, LinkTitle(app.Server, url), options, LinkDetail(url)).ConfigureAwait(false);
        return chosen?.Id == "open";
    }

    // Shows the card in the chat's approval stack, on the main actor; a chat that is not open answers as dismissed.
    private Task<ApprovalRequest.Option?> Ask(McpAppMount app, ApprovalRequest.Kind kind, string title, IReadOnlyList<ApprovalRequest.Option> options, string? detail) =>
        MainActor.Invoke(async () =>
        {
            if (runtimes(app.Launch.Thread) is not { } runtime) return null;
            var (id, answer) = runtime.AskForApp(kind, title, options, detail);
            if (!Pending.TryGetValue(app, out var pending)) Pending[app] = pending = (runtime, []);
            var ids = pending.Ids;
            ids.Add(id);
            try
            {
                return await answer;
            }
            finally
            {
                ids.Remove(id);
                if (ids.Count == 0) Pending.Remove(app);
            }
        });

    /// <summary>The app went away: every card it still waits on leaves the stack (each answers as dismissed).</summary>
    public static void Withdraw(McpAppMount app)
    {
        if (!Pending.Remove(app, out var pending)) return;
        pending.Runtime.WithdrawAppPrompts(pending.Ids);
    }

    public bool ToolsNeedApproval(Guid thread) =>
        !MainActor.IsIsolated || runtimes(thread) is not { } runtime || runtime.AppToolsNeedApproval;

    public string? PostMessage(McpAppMount app, string text)
    {
        if (!MainActor.IsIsolated) return ThreadRuntime.AppMessageBusy;
        return runtimes(app.Launch.Thread) is { } runtime ? runtime.PostAppMessage(app.Server, text) : "This chat is not open.";
    }

    public void SetModelContext(McpAppMount app, string text) => OnMain(() =>
    {
        if (runtimes(app.Launch.Thread) is not { } runtime) return;
        if (text.Length == 0) runtime.AppContexts.Remove(app.Server);
        else runtime.AppContexts.Set(app.Server, app.Title, text);
    });

    public void OpenLink(McpAppMount app, string url) => OnMain(() =>
    {
        if (SwiftURL.Create(url) is { } link) InlineText.OpenUrl?.Invoke(link);
    });

    private static void OnMain(Action action)
    {
        if (MainActor.IsIsolated) action();
        else MainActor.Post(action);
    }
}
