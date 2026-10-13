// Source: DroppyCode/App/AppModel.swift (B3 52bc792), :1249-1270;
// DroppyCode/App/AppDelegate.swift (B3 52bc792), :15, :63-92;
// DroppyCode/App/UpdateChecker.swift (B3 52bc792), :245-261.
// PORT-ONLY: OD-0028 / ED-220. Windows calls are behind IWindowsToastPlatform; only Program binds production.
using Marrow.Core.Port;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;

namespace Marrow.App.Port;

public sealed record ToastBuildIdentity(string AppId, string Scheme)
{
    public string? DevRoot { get; init; }
    public static ToastBuildIdentity ForLaunch(bool debug, bool dev, IReadOnlyList<string> arguments)
    {
        var identity = ForBuild(debug, dev);
        if (debug || dev) return identity;
        // ED-220: a release process originally launched with the dev variable owns the Dev scheme. Explorer's
        // later protocol process may lack that variable, so its validated URI retains the original identity.
        var protocolIdentity = ForBuild(debug: false, dev: true);
        return arguments.Any(argument => ToastActivation.Decode(protocolIdentity, argument) is not null) ? protocolIdentity : identity;
    }
    public static ToastBuildIdentity ForBuild(bool debug, bool dev) => debug || dev
        ? new("nth.App.Dev", "nth-dev")
        : new("nth.App", "nth-release"); // ED-220 (2), (6); OD-0135 renames both from MarrowCode.Marrow / marrow-code-*

    /// <summary>
    /// OD-0135: the AppUserModelID and URL scheme keys this app registered under its old name (HKCU\Software\Classes\...),
    /// removed at registration. Only these exact keys, which no other app writes.
    /// </summary>
    public static IReadOnlyList<string> LegacyRegistryKeys { get; } =
    [
        @"Software\Classes\AppUserModelId\MarrowCode.Marrow",
        @"Software\Classes\AppUserModelId\MarrowCode.Marrow.Dev",
        @"Software\Classes\marrow-code-release",
        @"Software\Classes\marrow-code-dev",
    ];
}

public interface IWindowsToastPlatform
{
    void SetProcessAppId(string appId);
    string? ReadRegistry(string key, string name);
    void WriteRegistry(string key, string name, string value);
    /// <summary>OD-0135: deletes the HKCU key and everything under it; nothing when it is not there; never throws.</summary>
    void DeleteRegistryTree(string key);
    void Show(string appId, string tag, string xml);
    /// <summary>PORT-ONLY (AS-9, ED-NEW-dextra-as9-6): takes a toast back from the action center by its tag. Best effort; a platform without it does nothing.</summary>
    void Remove(string appId, string tag) { }
}

public static class WindowsToastRegistration
{
    public static void Register(IWindowsToastPlatform platform, ToastBuildIdentity identity, string executable, string icon, string? devRoot = null)
    {
        // ED-220 (2): set before any top level is constructed. Only our own keys are read.
        platform.SetProcessAppId(identity.AppId);
        var appKey = "Software\\Classes\\AppUserModelId\\" + identity.AppId;
        WriteIfDifferent(appKey, "DisplayName", "nth"); // OD-0135
        WriteIfDifferent(appKey, "IconUri", icon);
        var schemeKey = "Software\\Classes\\" + identity.Scheme;
        WriteIfDifferent(schemeKey, "", "URL:nth"); // OD-0135
        WriteIfDifferent(schemeKey, "URL Protocol", "");
        // Explorer's second launch must retain ED-212's root, hence its instance lock and payload pipe.
        var rootArgument = devRoot is null ? "" : " --notification-data-root " + Quote(devRoot);
        WriteIfDifferent(schemeKey + "\\shell\\open\\command", "", Quote(executable) + rootArgument + " \"%1\"");
        // OD-0135: best effort, the keys of the old name go (a toast posted under the old name no longer opens this app).
        foreach (var legacy in ToastBuildIdentity.LegacyRegistryKeys) platform.DeleteRegistryTree(legacy);

        void WriteIfDifferent(string key, string name, string value)
        {
            if (!string.Equals(platform.ReadRegistry(key, name), value, StringComparison.Ordinal))
                platform.WriteRegistry(key, name, value);
        }
    }

    private static string Quote(string path)
    {
        // A double quote is not a valid Windows path character. Reject it rather than changing the command shape.
        if (path.Contains('"', StringComparison.Ordinal)) throw new ArgumentException("Invalid protocol command path.", nameof(path));
        // CommandLineToArgvW treats backslashes before the closing quote specially; double only that suffix.
        return "\"" + path + new string('\\', path.Length - path.TrimEnd('\\').Length) + "\"";
    }
}

/// <summary>What a toast is about. Thread is every toast nth had before the Assistant (a chat's, an update's).</summary>
public enum ToastKind { Thread, AssistantDecision }

/// <summary>One button on a toast: its words, and the userInfo its activation carries (a protocol launch, as the toast's own click).</summary>
public sealed record ToastButton(string Content, IReadOnlyDictionary<string, object> UserInfo);

public sealed record ToastRequest(string Identifier, string Title, string Body, bool Sound, IReadOnlyDictionary<string, object> UserInfo)
{
    /// <summary>PORT-ONLY (AS-9, 13.1): the non-thread payload. AssistantDecision is about <see cref="Decision"/>, not about a chat.</summary>
    public ToastKind Kind { get; init; } = ToastKind.Thread;

    /// <summary>The decision card a click on an AssistantDecision toast opens.</summary>
    public Guid? Decision { get; init; }

    /// <summary>Buttons under the text; each one launches nth with its own userInfo. None for every Thread toast.</summary>
    public IReadOnlyList<ToastButton> Buttons { get; init; } = [];

    /// <summary>A small line the toast ends with (the canvas's "Dextra"), or null.</summary>
    public string? Attribution { get; init; }

    /// <summary>The absolute path of a PNG shown as the toast's picture (Armilla, 2.5), or null.</summary>
    public string? Logo { get; init; }
}

public interface IToastCenter
{
    void InstallDelegate(AppDelegate appDelegate);
    void RequestPermission();
    Task Add(ToastRequest request);
    /// <summary>PORT-ONLY (AS-9, ED-NEW-dextra-as9-6): takes a toast back by the identifier it was added with. The shell's refusal is not an error; a caller reports any other.</summary>
    Task Remove(string identifier) => Task.CompletedTask;
}

/// <summary>PORT-ONLY (AS-9): a surface of nth that is not a chat. A toast about what it shows is not posted while it is on screen.</summary>
public enum AppSurface { AssistantHome }

public static class ToastNotifications
{
    private sealed class PermissionState { public bool Requested { get; set; } }
    private static readonly ConditionalWeakTable<AppModel, PermissionState> permissions = new();
    public static Ambient<IToastCenter> Center { get; } = new(DisabledToastCenter.Instance);
    // :1249-1255 flag belongs to each model, and is set before the request, including a failed request.
    // Stored here because the task authorizes only the two methods in AppModel.cs.
    public static void RequestPermissionOnce(AppModel model)
    {
        var state = permissions.GetOrCreateValue(model);
        if (state.Requested) return;
        state.Requested = true;
        try { Center.Current.RequestPermission(); }
        catch (Exception) { } // :1253 try?; ED-220 (4) production is a once-only no-prompt step.
    }

    /// <summary>
    /// PORT-ONLY (AS-9, DESIGN.md 13.1, ED-NEW-dextra-as9-3): the non-thread twin of <see cref="IsOnScreen"/>. True when the app is active and the
    /// surface is in front. The Assistant home is the home view in the main area; the pane workspace is not in the tree yet (16.2), so an
    /// Assistant pane joins this one expression when it lands.
    /// </summary>
    public static bool IsSurfaceOnScreen(AppModel model, AppSurface surface)
    {
        ArgumentNullException.ThrowIfNull(model);
        return surface switch
        {
            AppSurface.AssistantHome => NSApplication.IsActive.Current() && model.AssistantHomeShown,
            _ => false,
        };
    }

    // :1158-1165, :1259: selected, nonarchived panel, or inline child of a visible unfolded/unsettled parent.
    public static bool IsOnScreen(AppModel model, Marrow.Core.Models.ChatThread thread)
    {
        if (model.SelectedThreadID == thread.Id) return true;
        if (thread.ParentThreadID is not { } parent || parent != model.SelectedThreadID) return false;
        if (thread.IsInPanel) return !thread.IsArchived;
        if (thread.IsArchived || model.Thread(parent) is not { } parentThread) return false;
        return !parentThread.IsArchived && !parentThread.IsInPanel && !parentThread.FoldsHelpers && !parentThread.IsSettled;
    }
}

public sealed class DisabledToastCenter : IToastCenter
{
    public static readonly DisabledToastCenter Instance = new();
    private DisabledToastCenter() { }
    public void InstallDelegate(AppDelegate appDelegate) { }
    public void RequestPermission() { }
    public Task Add(ToastRequest request) => Task.CompletedTask;
}

public sealed class WindowsToastCenter(IWindowsToastPlatform platform, ToastBuildIdentity identity) : IToastCenter, IUpdateToastCenter
{
    private AppDelegate? appDelegate;
    public void InstallDelegate(AppDelegate appDelegate) => this.appDelegate = appDelegate; // AppDelegate.swift:15
    public void RequestPermission() { } // ED-220 (4), PR-UPDATE-023: Windows has no runtime permission prompt.
    public async Task Add(ToastRequest request)
    {
        try
        {
            // ED-220 (5): source's willPresent gate immediately precedes the Windows call.
            var options = appDelegate is { } installed
                ? await installed.UserNotificationCenter(new UNNotification(request.UserInfo))
                : UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound;
            if (options == UNNotificationPresentationOptions.None) return;
            platform.Show(identity.AppId, request.Identifier, Payload(request, identity));
        }
        catch (Exception) { } // AppModel.swift:1268; UpdateChecker.swift:260, try? add (also a denied toast).
    }
    /// <summary>
    /// PORT-ONLY (AS-9): the toast leaves the action center. The shell's own refusals are answered by the platform (a toast that stays is only
    /// stale, and nth refuses its buttons); anything else reaches the caller, which reports it once (DecisionToasts).
    /// </summary>
    public Task Remove(string identifier)
    {
        platform.Remove(identity.AppId, identifier);
        return Task.CompletedTask;
    }

    // UpdateChecker.swift:249-261: the content has a title, a body and userInfo and no sound (:249-252), so the banner is
    // silent, as a thread banner whose sound is nil is (AppModel.swift:1224; ED-227 (3)). willPresent's .sound (AppDelegate.swift:74)
    // plays nothing without a content.sound (ED-271; PR-UPDATE-025 and UI-LAUNCH-005 are amended to say so).
    public void Show(string identifier, string title, string body, string version) =>
        MainActor.Spawn(() => Add(new ToastRequest(identifier, title, body, false,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["update"] = version })));

    public static string Payload(ToastRequest request, ToastBuildIdentity identity)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"));
        // PORT-ONLY (AS-9): the picture comes first, as the shell lays it out; a request without one is the text alone, as before.
        if (request.Logo is { Length: > 0 } logo && Path.IsPathFullyQualified(logo))
            binding.Add(new XElement("image", new XAttribute("placement", "appLogoOverride"), new XAttribute("src", new Uri(logo).AbsoluteUri)));
        binding.Add(new XElement("text", request.Title), new XElement("text", request.Body));
        if (request.Attribution is { Length: > 0 } attribution)
            binding.Add(new XElement("text", new XAttribute("placement", "attribution"), attribution));
        var toast = new XElement("toast",
            new XAttribute("activationType", "protocol"),
            new XAttribute("launch", ToastActivation.Encode(identity, request.UserInfo)),
            new XElement("visual", binding));
        if (request.Buttons.Count > 0)
            toast.Add(new XElement("actions", request.Buttons.Select(button => new XElement("action",
                new XAttribute("content", button.Content),
                new XAttribute("activationType", "protocol"),
                new XAttribute("arguments", ToastActivation.Encode(identity, button.UserInfo))))));
        toast.Add(request.Sound ? new XElement("audio", new XAttribute("src", "ms-winsoundevent:Notification.Default"))
            : new XElement("audio", new XAttribute("silent", "true")));
        return toast.ToString(SaveOptions.DisableFormatting); // ED-220 (3): XML escapes text/arguments, never interpolates markup.
    }
}

public static class ToastActivation
{
    // ED-212: a Dev scheme is shared by dev instances, while their locks/pipes are isolated by root. Carry the
    // origin in the URI so the last instance to register the scheme cannot redirect a previous instance's click.
    public static string? DevRoot(ToastBuildIdentity identity, string argument)
    {
        if (Decode(identity, argument) is null) return null;
        var query = new Uri(argument).Query[1..].Split('&');
        return query.Length == 2 ? Uri.UnescapeDataString(query[1][9..]) : null;
    }
    public static string Encode(ToastBuildIdentity identity, IReadOnlyDictionary<string, object> userInfo) =>
        identity.Scheme + "://notification?payload=" + Uri.EscapeDataString(JsonSerializer.Serialize(userInfo))
        + (identity.DevRoot is { } root ? "&dataRoot=" + Uri.EscapeDataString(root) : "");

    public static UNNotification? Decode(ToastBuildIdentity identity, string argument)
    {
        // Only this build's protocol and complete object payload can enter didReceive. UUID validity is its own
        // source-defined branch, not an activation error (AppDelegate.swift:85-87).
        if (argument.Length > 64 * 1024 || !Uri.TryCreate(argument, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, identity.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "notification", StringComparison.Ordinal)
            || uri.AbsolutePath is not ("" or "/") || uri.Fragment.Length != 0
            || !uri.Query.StartsWith("?payload=", StringComparison.Ordinal)) return null;
        try
        {
            var query = uri.Query[1..].Split('&');
            if (query.Length > 2 || (query.Length == 2 && !query[1].StartsWith("dataRoot=", StringComparison.Ordinal))) return null;
            using var document = JsonDocument.Parse(Uri.UnescapeDataString(query[0][8..]));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()! : property.Value.Clone();
            }
            return new UNNotification(values);
        }
        catch (JsonException) { return null; }
        catch (UriFormatException) { return null; }
    }
}

public interface IToastActivationWindows
{
    void ShowMain();
    void Activate();
    void ShowAbout();
}

public static class ToastActivationWindows
{
    public static Ambient<IToastActivationWindows> Current { get; } = new(AvaloniaToastActivationWindows.Instance);
}

public sealed class UNNotificationResponse(UNNotification notification)
{
    public UNNotification Notification { get; } = notification;
}

public interface IToastLaunchTransport
{
    bool Send(string name, string payload);
    IDisposable Listen(string name, Action<string> received);
}

public static class ToastSecondLaunch
{
    public static bool Forward(IToastLaunchTransport transport, string name, ToastBuildIdentity identity, IReadOnlyList<string> arguments)
    {
        var argument = arguments.FirstOrDefault(a => ToastActivation.Decode(identity, a) is not null);
        return argument is not null && transport.Send(name, argument);
    }

    public static async Task<bool> Route(AppDelegate appDelegate, ToastBuildIdentity identity, string payload)
    {
        if (ToastActivation.Decode(identity, payload) is not { } notification) return false;
        await appDelegate.UserNotificationCenter(new UNNotificationResponse(notification));
        return true;
    }
}
