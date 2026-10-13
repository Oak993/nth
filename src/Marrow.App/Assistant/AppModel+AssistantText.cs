// PORT-ONLY: DESIGN.md 4.3 and 13.2; ED-NEW-dextra-open-1, -2, -3.
// Dextra uses the owner's named provider/model, independent of title generation preferences.
using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App;

public sealed partial class AppModel
{
    private DelegateCall? PrepareSuggestionText(Marrow.Core.Port.Assistant.BrainModel chosen) => PrepareSuggestionText(chosen, PrepareAssistantText, Settings.Accounts.Accounts);

    // Pure selection binding; the production factory below resolves a provider default and rechecks authorization on MainActor.
    internal static DelegateCall? PrepareSuggestionText(Marrow.Core.Port.Assistant.BrainModel chosen, Func<DelegateWho, Guid?, DelegateCall?> prepare, IEnumerable<Account> accounts)
    {
        if (!chosen.Usable || SwiftEnum<ProviderKind>.FromRawValue(chosen.ProviderId) is not { } kind) return null;
        var who = new DelegateWho(kind, string.IsNullOrWhiteSpace(chosen.Model) ? null : chosen.Model);
        if (chosen.AccountKey == kind.Id + ":default")
        {
            // A legacy quota key names that account; account priority must not change its identity.
            var legacy = accounts.FirstOrDefault(a => a.Service == kind && a.UsesLegacyCredentials);
            return legacy is null ? null : prepare(who, legacy.Id);
        }
        return chosen.AccountKey.StartsWith(kind.Id + ":", StringComparison.Ordinal) && Guid.TryParse(chosen.AccountKey[(kind.Id.Length + 1)..], out var id)
            ? prepare(who, id) : null;
    }

    internal DelegateCall? PrepareAssistantText(DelegateWho who, Guid? accountId = null)
    {
        var kind = who.Provider;
        // Other CLIs keep their own allow rules; never substitute a different provider for a named delegate.
        if (!DextraTextRoutes.Supports(kind)) return null;
        var account = accountId is { } pinned ? Settings.Accounts.Accounts.FirstOrDefault(a => a.Id == pinned && a.Service == kind) : Settings.Accounts.Choose(kind);
        if (account is null || !account.Enabled || !IsAccountConnected(account) || !CanUseAccount(account.Id)) return null;
        var model = who.Model ?? Providers.DefaultModel(kind)?.Id;
        if (string.IsNullOrWhiteSpace(model)) return null;
        var source = new SessionConfiguration
        {
            Provider = kind, Executable = Providers.Executable(kind),
            // ED-NEW-secfinal-2: a native folder URL (FileURLWithPath takes only a POSIX path); each Claude, Pi or API answer then
            // runs in its own private folder (DextraTextRoutes.PrivateAnswerAsync), and Command Code in its own (CommandCodeTextHead).
            WorkingDirectory = Marrow.App.Runtime.ThreadRuntime.SessionDirectoryUrl(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty, Model = model,
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
            AntiSlopEnabled = false,
        };
        AssistantModelCall? receipt = null;
        Func<string, CancellationToken, Task<string?>> ask;
        if (kind == ProviderKind.Commandcode)
        {
            var configured = Providers.PrepareSession(source, account.Id, useAccountDefaults: false);
            if (configured.Executable is null) return null;
            ask = (prompt, cancel) => MainActor.Invoke<string?>(async () =>
            {
                return await CommandCodeTextHead.RunAsync(configured, prompt,
                    () => MainActor.IsIsolated && CanUseAccount(account.Id) && IsAccountConnected(account), cancel,
                    spend => { if (MainActor.IsIsolated) receipt?.Observe(spend); else MainActor.Post(() => receipt?.Observe(spend)); });
            });
        }
        else ask = (prompt, cancel) => MainActor.Invoke<string?>(async () =>
        {
            cancel.ThrowIfCancellationRequested();
            // ED-NEW-secfinal-2: each answer in its own new, empty folder as a no-tools launch, never in the shared temp root.
            return await DextraTextRoutes.PrivateAnswerAsync(source, async isolated =>
            {
                var launch = AccountSessionFactory.Create(Providers, isolated, account.Id, freshContext: true);
                return await TextOnlyAccountHead.RunAsync(launch.Session, launch.Configuration, prompt,
                    text => { if (text.Length > 32_000) throw new InvalidOperationException("The answer was too long."); },
                    () => MainActor.IsIsolated && CanUseAccount(account.Id) && IsAccountConnected(account), cancel,
                    spend => { if (MainActor.IsIsolated) receipt?.Observe(spend); else MainActor.Post(() => receipt?.Observe(spend)); });
            });
        });
        var onAccount = AssistantTaps.AccountRef(account.UsesLegacyCredentials ? UsageAccountKey.Default(kind) : new UsageAccountKey(kind, account.Id.ToString("D")));
        return new DelegateCall(kind, ask)
        {
            Account = onAccount, Model = model,
            Admit = thread =>
            {
                var controller = Dextra;
                var mission = controller?.Runner.MissionOf(thread);
                receipt = controller?.Reserve?.BeginModelCall(onAccount, mission?.Mission.AssistantId ?? AssistantHost.AssistantId, model, mission);
                return receipt is not null;
            },
            Release = () => receipt?.Dispose(),
        };
    }
}
