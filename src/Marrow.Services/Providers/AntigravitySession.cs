using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/Services/Providers/AntigravitySession.swift (B2 1fbf81e), :1-730. Skeleton: efforts (:53) and
// the ParsedModelRow shape (:394-398) are complete; the stored state (:18-50) and the private members (:66,
// :126-149, :194-392, :519-525, :567-730) are left to the implementing unit, as is every member with behaviour.

namespace Marrow.Services.Providers;

// :14-730. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class AntigravitySession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; } // :16

    // :53
    public static readonly ValueList<string> Efforts = new(new[] { "low", "medium", "high" });

    // :55-62
    public AntigravitySession(SessionConfiguration configuration) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:55");

    // :64
    public bool IsRunning =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:64");

    // :70-120
    public Task<string> Start() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:70");

    // :151-164
    public Task Send(TurnInput input) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:151");

    // :166-178
    public Task Interrupt() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:166");

    // :180-182
    public Task Compact() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:180");

    // :184
    public void ResolveApproval(string requestID, string optionID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:184");

    // :185
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:185");

    // :187-190
    public void Stop() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:187");

    // :394-398
    public sealed record ParsedModelRow(string BaseSlug, string BaseName, string? Effort);

    // :400-413
    public static ParsedModelRow ParseModelRow(string slug, string name) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:400");

    // :418-444
    public static (string? Model, string? Effort) Normalize(string? model, string? effort) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:418");

    // :452-517
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:452");

    // :527-540
    public static Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:527");

    // :547-562
    public static Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/AntigravitySession.swift:547");
}
