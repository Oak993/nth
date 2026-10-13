// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1, ED-NEW-pcctl-7; DESIGN.md 4.2, 4.3). The rules the model cannot talk its way around:
// the apps nth never drives (terminals and shells, password managers and credential prompts, security apps, the lock screen
// and UAC, nth itself and other agent apps), elevated windows, password fields, the guarded-action words that need the
// user's confirmation, and who gets the tools at all (ED-NEW-pcctl-2: only a chat the user is watching). The lists are
// nth's own, written from public knowledge of these apps' executable names.
using Marrow.Core.Localisation;

namespace Marrow.Core.Port.PcControl;

public enum PcDenyCategory
{
    None,
    Terminal,
    PasswordManager,
    Security,
    System,
    AgentApp,
    User,
}

/// <summary>What the policy says about an app.</summary>
public sealed record PcAppVerdict(PcDenyCategory Category, string? Reason)
{
    public bool Denied => Category != PcDenyCategory.None;
    public static PcAppVerdict Allowed { get; } = new(PcDenyCategory.None, null);
}

/// <summary>A process's token facts (from OpenProcessToken and GetTokenInformation).</summary>
public sealed record PcProcessSecurity(bool Elevated, bool HighIntegrity, bool UiAccess, bool Readable = true);

public static class PcPolicy
{
    private static readonly Dictionary<string, PcDenyCategory> deniedExes = Build();

    private static Dictionary<string, PcDenyCategory> Build()
    {
        var map = new Dictionary<string, PcDenyCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in new[]
                 {
                     "windowsterminal.exe", "wt.exe", "openconsole.exe", "conhost.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
                     "powershell_ise.exe", "wsl.exe", "wslhost.exe", "bash.exe", "mintty.exe", "alacritty.exe", "wezterm.exe",
                     "wezterm-gui.exe", "kitty.exe", "hyper.exe", "tabby.exe", "conemu.exe", "conemu64.exe", "cmder.exe", "putty.exe",
                 }) map[exe] = PcDenyCategory.Terminal;
        foreach (var exe in new[]
                 {
                     "1password.exe", "bitwarden.exe", "keepass.exe", "keepassxc.exe", "lastpass.exe", "dashlane.exe", "keeper.exe",
                     "keeperpasswordmanager.exe", "enpass.exe", "proton pass.exe", "protonpass.exe", "nordpass.exe", "roboform.exe",
                     "credentialuibroker.exe",
                 }) map[exe] = PcDenyCategory.PasswordManager;
        foreach (var exe in new[]
                 {
                     "sechealthui.exe", "securityhealthsystray.exe", "securityhealthhost.exe", "msascui.exe", "mpcmdrun.exe",
                     "bdagent.exe", "bdwtxag.exe", "avastui.exe", "avgui.exe", "nortonui.exe", "norton.exe", "avpui.exe", "avp.exe",
                     "mbam.exe", "mbamtray.exe", "egui.exe", "mcuicnt.exe",
                 }) map[exe] = PcDenyCategory.Security;
        foreach (var exe in new[] { "consent.exe", "lockapp.exe", "logonui.exe", "useraccountcontrolsettings.exe", "regedit.exe", "mmc.exe" })
            map[exe] = PcDenyCategory.System;
        foreach (var exe in new[] { "marrow.app.exe", "nth.exe", "codex.exe", "chatgpt.exe", "claude.exe", "codex-computer-use.exe" })
            map[exe] = PcDenyCategory.AgentApp;
        return map;
    }

    private static readonly (string Prefix, PcDenyCategory Category)[] deniedAumids =
    [
        ("microsoft.windowsterminal", PcDenyCategory.Terminal),
        ("microsoft.powershell", PcDenyCategory.Terminal),
        ("microsoft.sechealthui", PcDenyCategory.Security),
        ("microsoft.lockapp", PcDenyCategory.System),
        ("microsoft.windows.shell.rundialog", PcDenyCategory.Terminal),
        ("microsoft.windows.secureassessmentbrowser", PcDenyCategory.System),
        ("openai.codex", PcDenyCategory.AgentApp),
        ("openai.chatgpt", PcDenyCategory.AgentApp),
        ("claude", PcDenyCategory.AgentApp),
        ("anthropic.claude", PcDenyCategory.AgentApp),
        ("agilebits.1password", PcDenyCategory.PasswordManager),
        ("8bitsolutionsllc.bitwardendesktop", PcDenyCategory.PasswordManager),
    ];

    private static readonly HashSet<string> riskyExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe", "iexplore.exe",
        "outlook.exe", "olk.exe", "thunderbird.exe", "hxoutlook.exe",
        "teams.exe", "ms-teams.exe", "slack.exe", "discord.exe", "whatsapp.exe", "telegram.exe", "signal.exe", "zoom.exe",
        "explorer.exe", "totalcmd64.exe",
    };

    /// <summary>Whether the app may be driven at all. <paramref name="userDenied"/> holds exe names or AUMIDs the user added.</summary>
    public static PcAppVerdict Classify(PcAppIdentity app, IEnumerable<string>? userDenied = null)
    {
        if (deniedExes.TryGetValue(app.ExeName, out var category)) return new PcAppVerdict(category, Reason(category, app.ExeName));
        if (app.Aumid is { Length: > 0 } aumid)
        {
            foreach (var (prefix, denied) in deniedAumids)
                if (aumid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return new PcAppVerdict(denied, Reason(denied, aumid));
        }
        foreach (var entry in userDenied ?? [])
        {
            var item = entry.Trim();
            if (item.Length == 0) continue;
            if (string.Equals(item, app.ExeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item, app.ExePath, StringComparison.OrdinalIgnoreCase)
                || (app.Aumid is { } id && string.Equals(item, id, StringComparison.OrdinalIgnoreCase)))
                return new PcAppVerdict(PcDenyCategory.User, "the user blocked this app in Settings");
        }
        return PcAppVerdict.Allowed;
    }

    /// <summary>The Run dialog (a #32770 dialog titled "Run" owned by Explorer) runs commands, so it is denied like a shell.</summary>
    public static bool IsRunDialog(string exeName, string className, string title) =>
        string.Equals(exeName, "explorer.exe", StringComparison.OrdinalIgnoreCase) && className == "#32770"
        && string.Equals(title.Trim(), "Run", StringComparison.OrdinalIgnoreCase);

    private static string Reason(PcDenyCategory category, string what) => category switch
    {
        PcDenyCategory.Terminal => what + " is a terminal or shell; nth never drives those",
        PcDenyCategory.PasswordManager => what + " holds passwords; nth never drives it",
        PcDenyCategory.Security => what + " is a security app; nth never drives it",
        PcDenyCategory.System => what + " is a protected Windows screen",
        PcDenyCategory.AgentApp => what + " is nth itself or another agent app",
        PcDenyCategory.User => "the user blocked this app in Settings",
        PcDenyCategory.None => "",
    };

    private static readonly HashSet<string> messagingExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "outlook.exe", "olk.exe", "thunderbird.exe", "hxoutlook.exe", "mailbird.exe", "em client.exe", "mailclient.exe",
        "teams.exe", "ms-teams.exe", "slack.exe", "discord.exe", "whatsapp.exe", "telegram.exe", "signal.exe", "zoom.exe",
        "skype.exe", "viber.exe", "element.exe",
    };

    /// <summary>Apps where a click acts as the user towards other people or files (the approval sheet warns).</summary>
    public static bool IsRisky(PcAppIdentity app) => riskyExes.Contains(app.ExeName);

    /// <summary>Mail and chat apps, where a nameless button may well be Send (DESIGN.md 4.3, critique C13).</summary>
    public static bool IsMessaging(PcAppIdentity app) => messagingExes.Contains(app.ExeName);

    /// <summary>
    /// In a mail or chat app, pressing a button with no name or a one- or two-character name (an icon) needs the user's
    /// confirmation too: it may be Send, and Tactus cannot tell.
    /// </summary>
    public static bool UnnamedButtonNeedsConfirmation(PcAppIdentity app, PcUiNode node) =>
        IsMessaging(app) && node.Can(PcActions.Invoke) && (node.Name ?? "").Trim().Length <= 2
        && GuardedWord(node.Name, node.AutomationId) is null;

    /// <summary>Elevated, high-integrity or UIAccess windows are refused; a token nth cannot read counts as elevated.</summary>
    public static bool IsElevated(PcProcessSecurity security) => !security.Readable || security.Elevated || security.HighIntegrity || security.UiAccess;

    /// <summary>Text may never go into a password field (pc_type, pc_set_value, paste).</summary>
    public static bool RefusesText(PcUiNode? element) => element is not null && element.Has(PcStates.Password);

    /// <summary>
    /// ED-NEW-pcctl-2 (legacy Marrow pcTools.ts:39-45): the tools are offered only when PC control is on and the chat is one
    /// the user is watching. ED-NEW-pcctl-7 narrows this for Hydra heads (DESIGN.md 2.10, critique C14): a head is offered
    /// the tools while the run is attended (the host's definition: physical input within 10 minutes and the lead chat's
    /// window not minimized); actors and other background runs never are.
    /// </summary>
    public static bool OffersTools(PcSettings settings, bool attended) => settings.Enabled && attended;

    // The guarded words (DESIGN.md 4.3) live in one place for both products, one list per language (Localisation/Guarded/guarded.<language>.json,
    // ED-NEW-web-w22-1): English, Hindi and Urdu, and any language added there. Tactus and the web guard (WebAgentPolicy) ask the same lists.

    /// <summary>
    /// The guarded word an element holds, as the English word lower-cased, or null: from its name (in any listed language: English, Hindi,
    /// Urdu) or its AutomationId.
    /// </summary>
    public static string? GuardedWord(string? name, string? automationId = null) =>
        GuardedWords.Shared.Find(name) ?? GuardedWords.Shared.FindIdentifier(automationId);
}
