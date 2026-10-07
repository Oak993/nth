// PORT-ONLY: OD-0114 / ED-404; MA-0 §1 selectors. This separates CLI state, not Windows security identities.
using System.Security.AccessControl;
using System.Security.Principal;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;

namespace Marrow.Services.Port.Accounts;

public sealed class AccountEnvironment
{
    private readonly Lazy<string> rootValue;

    public AccountEnvironment(string dataRoot)
    {
        var full = Path.GetFullPath(dataRoot);
        rootValue = new Lazy<string>(() => full);
    }

    /// <summary>PORT-ONLY: the root is asked for only when a folder is, so a registry built before storage is bound (a test, or start-up order) never reads it.</summary>
    public AccountEnvironment(Func<string> dataRoot)
    {
        rootValue = new Lazy<string>(() => Path.GetFullPath(dataRoot()));
    }

    private string root => rootValue.Value;

    // Do not let an inherited key/profile override CLI-owned sign-in for a newly created account.
    private static readonly string[] CredentialVariables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "ANTHROPIC_PROFILE", "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "CLAUDE_CODE_API_KEY_HELPER", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
        "OPENAI_API_KEY", "CODEX_API_KEY", "CURSOR_API_KEY", "XAI_API_KEY", "WINDSURF_API_KEY", "COMMAND_CODE_API_KEY",
        "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN", "GEMINI_API_KEY", "GOOGLE_API_KEY", "OPENROUTER_API_KEY",
        "GROQ_API_KEY", "MISTRAL_API_KEY", "DEEPSEEK_API_KEY", "MODEL_API_KEY", "ZAI_API_KEY",
    ];

    public string Folder(Account account) => Path.Combine(root, "accounts", account.Service.Id, account.Id.ToString("N"));

    public ValueMap<string, string> Build(Account account, IReadOnlyDictionary<string, string> inherited)
    {
        if (!account.Enabled) throw new InvalidOperationException("A disabled account cannot launch a service.");
        var env = new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
        if (account.UsesLegacyCredentials) return new(env, StringComparer.OrdinalIgnoreCase);
        foreach (var key in CredentialVariables) env.Remove(key);
        env["MARROW_ACCOUNT_ID"] = account.Id.ToString("N");
        var folder = Folder(account);
        switch (account.Service)
        {
            case ProviderKind.Claude: env["CLAUDE_CONFIG_DIR"] = folder; break;
            case ProviderKind.Codex: env["CODEX_HOME"] = folder; break;
            case ProviderKind.Cursor: env["CURSOR_CONFIG_DIR"] = folder; break;
            case ProviderKind.Grok: env["GROK_HOME"] = folder; break;
            case ProviderKind.Pi: env["PI_CODING_AGENT_DIR"] = folder; break;
            case ProviderKind.Opencode:
            case ProviderKind.OpencodeGo:
                env["XDG_DATA_HOME"] = Path.Combine(folder, "data");
                env["XDG_CONFIG_HOME"] = Path.Combine(folder, "config");
                env["XDG_STATE_HOME"] = Path.Combine(folder, "state");
                env["XDG_CACHE_HOME"] = Path.Combine(folder, "cache");
                env.Remove("OPENCODE_CONFIG");
                env["OPENCODE_CONFIG_DIR"] = Path.Combine(folder, "config", "opencode");
                break;
            default:
                env["HOME"] = env["USERPROFILE"] = Path.Combine(folder, "home");
                env["APPDATA"] = Path.Combine(folder, "home", "AppData", "Roaming");
                env["LOCALAPPDATA"] = Path.Combine(folder, "home", "AppData", "Local");
                // Copilot/Antigravity remain single-account even with private config/cache folders.
                if (account.Service == ProviderKind.Copilot)
                {
                    env["COPILOT_HOME"] = Path.Combine(folder, "config");
                    env["COPILOT_CACHE_HOME"] = Path.Combine(folder, "cache");
                }
                break;
        }
        return new(env, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Create only owned folders. Migrated credential/session trees are never read, copied, or moved.</summary>
    public void EnsureFolder(Account account)
    {
        if (!account.Enabled) throw new InvalidOperationException("A disabled account cannot launch a service.");
        var folder = Folder(account);
        CreateOrdinary(folder);
        Protect(folder);
        if (account.UsesLegacyCredentials) return;
        var env = Build(account, new Dictionary<string, string>());
        foreach (var path in env.Values.Where(v => v.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            CreateOrdinary(path);
    }

    private void CreateOrdinary(string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal)) throw new InvalidOperationException("Account folder escaped the data root.");
        var current = root;
        CheckOrdinary(current);
        Directory.CreateDirectory(current);
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            CheckOrdinary(current);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(current);
            else Directory.CreateDirectory(current, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            CheckOrdinary(current);
        }
    }

    private static void CheckOrdinary(string path)
    {
        if (new DirectoryInfo(path).LinkTarget is not null ||
            (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("An account folder cannot be a symbolic link or junction.");
    }

    private static void Protect(string folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("The Windows user has no SID.");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(folder).SetAccessControl(acl);
    }
}
