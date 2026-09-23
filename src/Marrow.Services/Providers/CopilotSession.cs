using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e), :1-1072. Skeleton: the private type
// PendingRequest (:31-38), the stored state (:40-73) and the private members (:87, :121-158, :179-230, :328-372,
// :382-400, :470-716, :736-872, :904-1033, :1049, :1064-1072) are left to the implementing unit, as is every
// member with behaviour.

namespace Marrow.Services.Providers;

// :27-1072. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class CopilotSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; } // :29

    // :75-80
    public CopilotSession(SessionConfiguration configuration) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:75");

    // :82-85
    public bool IsRunning =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:82");

    // :91-115
    public Task<string> Start() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:91");

    // :160-177
    public Task Send(TurnInput input) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:160");

    // :232-236. Swift: slashCommand(in:).
    public static (string Name, string Input)? SlashCommand(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:232");

    // :238-243
    public Task Interrupt() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:238");

    // :245-248
    public Task Compact() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:245");

    // :252-268. Swift: rollback(turns:).
    public Task Rollback(long turns) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:252");

    // :270-303
    public void ResolveApproval(string requestID, string optionID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:270");

    // :305-310
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:305");

    // :312-324
    public void Stop() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:312");

    // :374-380
    public static string PermissionModeName(RuntimeMode mode) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:374");

    // :402-425
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:402");

    // :428-441
    public static Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:428");

    // :444-468
    public static Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:444");

    // :718-732
    public static string ErrorMessage(JSONValue data) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:718");

    // :874-902. Swift: sessionApproval(for:).
    public static JSONValue? SessionApproval(JSONValue request) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:874");

    // :1035-1045
    public static ValueList<string> PatchPaths(string patch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:1035");

    // :1051-1062. Swift: todos(from:).
    public static ValueList<TodoStep> Todos(string markdown) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:1051");
}
