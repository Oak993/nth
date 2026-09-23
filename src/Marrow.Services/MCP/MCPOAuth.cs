using Marrow.Core.Port;

// Source: DroppyCode/Services/MCP/MCPOAuth.swift (B2 1fbf81e), :1-930. Skeleton: the file-private types
// AuthServerMetadata, RegisteredClient, OAuthRefreshGate, CallbackResult, CallbackState and CallbackWait (:48-126) and
// MCPOAuth's private members (:129-130, :200-834, :854-929) are left to the implementing unit.

namespace Marrow.Services.MCP;

// :7-46. An error enum keeps its cases as a nested record union (R-ERR-3); each unlabeled String is named after the
// binding errorDescription gives it (`let detail`).
public sealed class MCPOAuthError(MCPOAuthError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record DiscoveryFailed(string Detail) : Case;    // :8
        public sealed record MetadataFailed(string Detail) : Case;     // :9
        public sealed record RegistrationUnsupported : Case;           // :10
        public sealed record RegistrationFailed(string Detail) : Case; // :11
        public sealed record TokenFailed(string Detail) : Case;        // :12
        public sealed record CallbackFailed(string Detail) : Case;     // :13
        public sealed record StateMismatch : Case;                     // :14
        public sealed record TimedOut : Case;                          // :15
        public sealed record Cancelled : Case;                         // :16
        public sealed record SignedOut : Case;                         // :17
        public sealed record Network(string Detail) : Case;            // :18
    }

    public Case ErrorCase { get; } = errorCase;

    // :20-45
    public override string? ErrorDescription =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:20");
}

// :128-930
public static class MCPOAuth
{
    // :134-160
    public static Task SignIn(string serverID, string serverName, string url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:134");

    // :164-183
    public static Task<string> AccessToken(string serverID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:164");

    // :185-187
    public static bool IsSignedIn(string serverID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:185");

    // :189-192
    public static void SignOut(string serverID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:189");

    // :194-196
    public static Task RefreshIfNeeded(string serverID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:194");

    // :838-844
    public static string SuccessPage(string serverName) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:838");

    // :846-852
    public static string FailurePage(string serverName, string message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPOAuth.swift:846");
}
