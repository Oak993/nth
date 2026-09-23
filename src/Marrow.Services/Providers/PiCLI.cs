using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/Services/Providers/PiCLI.swift (B2 1fbf81e), :1-217. Skeleton: thinkingLevels (:11) is
// complete; every member with behaviour, and the extension source text (:136-216), are left to the implementing
// unit.

namespace Marrow.Services.Providers;

// :9-217
public static class PiCLI
{
    // :11
    public static readonly ValueList<string> ThinkingLevels =
        new(new[] { "off", "minimal", "low", "medium", "high", "xhigh", "max" });

    // :14-18
    public static SwiftURL SupportDirectory =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PiCLI.swift:14");

    // :21-27
    public static SwiftURL ConfigDirectory(IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PiCLI.swift:21");

    // :31-44
    public static Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PiCLI.swift:31");

    // :47-112
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PiCLI.swift:47");

    // :117-126
    public static SwiftURL InstallExtension() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PiCLI.swift:117");

    // :136-216. A static let string; the byte-for-byte text is the implementing unit's (R-STR-9).
    public static string ExtensionSource =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PiCLI.swift:136");
}
