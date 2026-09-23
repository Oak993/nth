using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/CodexSession.swift (B2 1fbf81e), :1-1052. Skeleton: the private types
// Target, PendingRequest and PolicySettings (:17-33), the stored state (:13, :35-53) and the private members
// (:110-127, :224-301, :439-474, :494-1051) are left to the implementing unit, as is every member with behaviour.

namespace Marrow.Services.Providers;

// :11-1052. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class CodexSession : ProviderSession
{
    // :55-57
    public CodexSession(SessionConfiguration configuration) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:55");

    public Action<ProviderEvent>? OnEvent { get; set; } // :14

    // :59-62
    public bool IsRunning =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:59");

    // :66-108
    public Task<string> Start() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:66");

    // :129-160
    public Task Send(TurnInput input) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:129");

    // :162-168
    public Task Interrupt() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:162");

    // :170-173
    public Task Compact() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:170");

    // :175-222. Swift: rollback(from:).
    public Task<string?> Rollback(string targetID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:175");

    // :303-318
    public void ResolveApproval(string requestID, string optionID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:303");

    // :320-328
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:320");

    // :330-333
    public void Stop() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:330");

    // :335-341
    public Task<bool> StopAgent(string id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:335");

    // :345-353
    public static Task<ValueList<SlashCommand>> ListCommands(SwiftURL executable, SwiftURL directory, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:345");

    // :355-385
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:355");

    // :388-437
    public static Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:388");

    // :476-491
    public static Task<PlanLimits.ResetOutcome> ConsumeResetCredit(SwiftURL executable, IReadOnlyDictionary<string, string> environment, string? creditID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:476");

    // :910-918
    public static string ErrorMessage(JSONValue? error) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:910");
}
