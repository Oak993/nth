// PORT-ONLY: MA-3b-E1. Production provider adapter; job W supplies the live thread/context/cap state and atomic timeline append.
using Marrow.Core.Port;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

/// <summary>Invoke on MainActor, like the provider sessions. All required app boundaries are explicit constructor arguments.</summary>
public sealed class AccountConversationHeadHost(
    ProviderRegistry registry,
    Func<Guid, Guid> active,
    Func<Guid, long> revision,
    Func<Guid, IReadOnlyList<ConversationHeadAccount>> accounts,
    Func<Guid, CancellationToken, Task<string>> context,
    Func<Guid, Guid, SessionConfiguration> configuration,
    ConversationTransferConsent consent,
    Func<Guid, Guid, string, string, CancellationToken, Task> post,
    Action<ProviderSession, Guid>? bindKeys = null) : IConversationHeadHost
{
    public event Action<Guid>? AccountsChanged;
    public void NotifyAccountsChanged(Guid thread) { MainActor.AssumeIsolated(); AccountsChanged?.Invoke(thread); }
    public Guid ActiveAccount(Guid thread) { MainActor.AssumeIsolated(); return active(thread); }
    public long Revision(Guid thread) { MainActor.AssumeIsolated(); return revision(thread); }
    public IReadOnlyList<ConversationHeadAccount> Accounts(Guid thread)
    {
        MainActor.AssumeIsolated();
        // E1's link/opinion target selectors read this head-only projection. Preserve the primary:
        // an unsafe-to-launch author can still supply its completed answer to a safe checking head.
        var source = active(thread);
        return Array.AsReadOnly(accounts(thread).Where(row => row.Id == source || TextOnlyAccountHead.Supports(row.Service)).ToArray());
    }
    public Task<string> ContextAsync(Guid thread, CancellationToken ct) { MainActor.AssumeIsolated(); ct.ThrowIfCancellationRequested(); return context(thread, ct); }

    public Task<bool> ConfirmTransferAsync(Guid thread, ConversationHeadAccount source, IReadOnlyList<ConversationHeadAccount> targets, CancellationToken ct)
    {
        MainActor.AssumeIsolated();
        return consent.AuthorizeAsync(thread, source.Service, targets.Select(target => target.Service).ToArray(), ct);
    }

    public async Task<string> RunAsync(ConversationHeadRequest request, Action<string> text, Func<bool> permitted, CancellationToken ct)
    {
        MainActor.AssumeIsolated();
        ct.ThrowIfCancellationRequested();
        if (!TextOnlyAccountHead.Supports(request.Account.Service))
            throw new NotSupportedException(request.Account.Name + ": this service has no verified text-only checking or opinion turn.");
        var expectedRevision = Revision(request.Thread);
        var expectedSource = ActiveAccount(request.Thread);
        OpenAICompatibleSession? apiHead = null;
        bool CostKnown(ConversationHeadAccount row) => row.MonthlyCapUsd is null || apiHead is null
            || double.IsFinite(row.MonthlyCapUsd.Value) && row.MonthlyCapUsd >= 0
                && (row.Service == ProviderKind.OpenRouter || apiHead.KeySpendEstimator is not null);
        bool CanSend() => !ct.IsCancellationRequested && permitted() && Revision(request.Thread) == expectedRevision
            && ActiveAccount(request.Thread) == expectedSource && Accounts(request.Thread)
            .Any(row => row.Id == request.Account.Id && row.Service == request.Account.Service && row.Enabled && row.HasRoom && CostKnown(row));
        if (!CanSend()) throw new InvalidOperationException("The account is no longer available.");
        // Fresh private heads never inherit a working session, tool-using Hydra launch or executable skill generation.
        var source = configuration(request.Thread, request.Account.Id) with
        {
            Provider = request.Account.Service, Hydra = null, IsHydraHead = false, SkillGeneration = null, SkillMcpExecutable = null,
        };
        var launch = AccountSessionFactory.Create(registry, source, request.Account.Id, freshContext: true);
        try
        {
            // The app supplies ApiKeys.BindPrivate: the shared pool charges each head's own key and checks its cap before each request.
            // Fail closed if a real API session was given no pool; a cached HasRoom alone is not a dispatch-time cap check.
            if (launch.Session is OpenAICompatibleSession api && request.Account.Service.IsAPIKeyBased)
            {
                apiHead = api;
                api.UsesPrivateHeadPricingContract = true;
                bindKeys?.Invoke(api, request.Account.Id);
                api.KeyRotation = new PinnedConversationKeyRotation(api.KeyRotation
                    ?? throw new InvalidOperationException("A checking or opinion API head requires the shared private key routing."), request.Account.Id);
                // OpenRouter reports charged USD. Other capped APIs need caller-supplied authoritative model pricing; never invent a rate.
                if (!Accounts(request.Thread).Any(row => row.Id == request.Account.Id && CostKnown(row)))
                    throw new InvalidOperationException("This capped account has no verified price for this model.");
            }
            if (!CanSend()) throw new InvalidOperationException("Permission to use this account changed.");
            var instruction = request.Purpose == ConversationHeadPurpose.Check
                ? "Check the primary answer against the user's message and recorded conversation. Report agreement or corrections in text only. Take no actions."
                : request.Purpose == ConversationHeadPurpose.Combine
                    ? "Combine the supplied answers in text only. Take no actions."
                    : "Answer this one message using the recorded conversation. Return text only. Take no actions.";
            var prompt = "[Marrow " + request.Purpose + "] " + instruction + "\nRecorded conversation:\n" + request.Context
                + "\nUser message:\n" + request.Message
                + (request.Answer is null ? "" : "\nPrimary answer:\n" + request.Answer);
            return await TextOnlyAccountHead.RunAsync(launch.Session, launch.Configuration, prompt, text, CanSend, ct);
        }
        finally { launch.Session.OnEvent = null; launch.Session.Stop(); }
    }

    public Task PostCombinedAsync(Guid thread, Guid round, string text, string byline, CancellationToken ct)
    { MainActor.AssumeIsolated(); ct.ThrowIfCancellationRequested(); return post(thread, round, text, byline, ct); }
}
