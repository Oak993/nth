// PORT-ONLY: OD-0114 / ED-404. Account launch/sign-in seams; no UI, quota scheduler or account handoff.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Support;
using Marrow.Services.Port.Accounts;

namespace Marrow.Services.Providers;

public sealed partial class ProviderRegistry
{
    private readonly AccountStore? accountStore;
    private readonly AccountStore? recoveredAccounts;
    // The data root is read when an account folder is first needed, never while a registry is only being built (parity fixtures build one with no Storage bound).
    private Lazy<AccountEnvironment>? accountEnvironmentSource;
    private AccountEnvironment? accountEnvironment => accountEnvironmentSource?.Value;
    private readonly Dictionary<Guid, Task<string?>> accountSignIns = [];
    private readonly object accountSignInGate = new();

    // Tests replace the entire launch boundary with a recorder; production runs the unmodified CLI.
    internal Func<ShellInvocation, Task<ShellResult>> AccountLoginRunner { get; set; } = call =>
        Shell.Run(call.Executable, call.Arguments, call.Directory, call.Environment, timeout: call.Timeout);

    private bool HasEnabledAccount(ProviderKind service) => accountStore is null || accountStore.Choose(service) is not null;

    private string SelectedApiKey(ProviderKind service) => accountStore is null ? settings.ApiKey(service)
        : accountStore.Choose(service) is { } account ? ApiKey(account.Id) : "";

    private Account RequireAccount(ProviderKind service, Guid? id = null) => (accountStore ?? recoveredAccounts)?.Choose(service, id)
        ?? throw new InvalidOperationException("No enabled account is available for this service.");

    private Account RequireAccount(Guid id)
    {
        var account = (accountStore ?? recoveredAccounts)?.Get(id) ?? throw new InvalidOperationException("Accounts are not configured.");
        return RequireAccount(account.Service, id);
    }

    public ValueMap<string, string> Environment(Guid accountId)
    {
        var account = RequireAccount(accountId);
        return Environment(account, LegacyEnvironment(account.Service));
    }

    private ValueMap<string, string> Environment(Account account, ValueMap<string, string> inherited)
    {
        if (accountEnvironment is null && account.UsesLegacyCredentials) return inherited;
        var env = accountEnvironment!.Build(account, inherited);
        if (account.Service == ProviderKind.Commandcode && !account.UsesLegacyCredentials && APIKeychain.Key(account, "") is { Length: > 0 } key)
            env = env.Mutating(d => d["COMMAND_CODE_API_KEY"] = key);
        return env;
    }

    public string ApiKey(Guid accountId)
    {
        var account = RequireAccount(accountId);
        return account.UsesLegacyCredentials ? settings.ApiKey(account.Service) : APIKeychain.Key(account, "");
    }

    /// <summary>Compose every CLI/API launch from one current account snapshot. Composer choices can override defaults.</summary>
    public SessionConfiguration PrepareSession(SessionConfiguration configuration, Guid accountId, bool useAccountDefaults = true)
    {
        var account = RequireAccount(configuration.Provider, accountId);
        accountEnvironment?.EnsureFolder(account);
        var ownsResume = OwnsResume(account, configuration);
        return configuration with
        {
            AccountId = account.Id,
            AccountHandshakePoint = (accountStore ?? recoveredAccounts)!.EffectiveHandshakePoint(account.Id),
            Environment = Environment(account.Id),
            ApiKey = account.Service.IsAPIKeyBased ? ApiKey(account.Id) : configuration.ApiKey,
            Model = useAccountDefaults ? account.Defaults.Model : configuration.Model,
            Effort = useAccountDefaults ? account.Defaults.Effort : configuration.Effort,
            FastMode = useAccountDefaults ? account.Defaults.FastMode : configuration.FastMode,
            // Native IDs and Pi absolute paths are account-owned; an unbound ID is legacy-only.
            ResumeID = ownsResume ? configuration.ResumeID : null,
            ResumeAt = ownsResume ? configuration.ResumeAt : null,
            ResumePath = ownsResume ? configuration.ResumePath : null,
        };
    }

    private bool OwnsResume(Account account, SessionConfiguration configuration)
    {
        if (configuration.AccountId != account.Id && !(configuration.AccountId is null && account.UsesLegacyCredentials)) return false;
        if (account.Service != ProviderKind.Pi || account.UsesLegacyCredentials || configuration.ResumeID is null) return true;
        var path = Path.GetFullPath(configuration.ResumeID);
        var sessions = Path.Combine(accountEnvironment!.Folder(account), "sessions");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(sessions + Path.DirectorySeparatorChar, comparison)) return false;
        for (var current = path; current is not null && !string.Equals(current, sessions, comparison); current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null) return false;
        return new DirectoryInfo(sessions).LinkTarget is null;
    }

    public Task<string?> SignIn(Guid accountId)
    {
        lock (accountSignInGate)
        {
            var account = RequireAccount(accountId);
            if (account.Service.IsAPIKeyBased || account.HasSignedIn) return Task.FromResult<string?>(null);
            if (accountSignIns.TryGetValue(accountId, out var pending)) return pending;
            // Register before executing: even a synchronously completed runner cannot leave a stale in-flight entry.
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountSignIns.Add(accountId, completion.Task);
            _ = CompleteSignIn(accountId, completion);
            return completion.Task;
        }
    }

    internal Func<Task> AccountLoginLoad { get; set; } = LoginEnvironment.Load;

    private async Task CompleteSignIn(Guid id, TaskCompletionSource<string?> completion)
    {
        try
        {
            await AccountLoginLoad();
            var account = RequireAccount(id);
            if (Executable(account.Service) is not { } executable)
            {
                completion.SetResult($"{account.Service.DisplayName} was not found.");
                return;
            }
            accountEnvironment!.EnsureFolder(account);
            var arguments = account.Service.LoginCommand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            var result = await AccountLoginRunner(new ShellInvocation(executable.Path, arguments,
                accountEnvironment.Folder(account), Environment(id), null, 300, null));
            if (result.Succeeded)
            {
                // Account-local success only; provider-keyed status/quota/catalog caches must not be attributed to another account.
                accountStore!.MarkSignedIn(id);
                completion.SetResult(null);
            }
            else completion.SetResult(TextCleanup.StripANSI(result.Output + result.ErrorOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim() ?? $"{account.Service.DisplayName} did not finish signing in.");
        }
        catch (Exception error) { completion.SetException(error); }
        finally { lock (accountSignInGate) accountSignIns.Remove(id); }
    }
}
