// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-5; DESIGN.md 2.13). The names that connect nth to
// its agent: a fresh current-user pipe name and a fresh token for every workspace, and the program line the Remote Desktop
// control starts inside the child session (`"<nth.exe>" --tactus-workspace-agent <pipe> <token>`). The agent accepts only
// names and tokens of exactly this shape. The token is a secret of that one workspace: it is never logged and never shown.
using System.Security.Cryptography;

namespace Marrow.Services.Port.PcControl.Workspace;

public static class WorkspacePipe
{
    public const string Prefix = "nth-workspace-";
    public const int NameHexLength = 32;
    public const int TokenHexLength = 64;

    public static string NewName() => Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(NameHexLength / 2));

    public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenHexLength / 2));

    public static bool IsName(string? text) =>
        text is { Length: > 0 } && text.StartsWith(Prefix, StringComparison.Ordinal) && IsHex(text.AsSpan(Prefix.Length), NameHexLength);

    public static bool IsToken(string? text) => text is { Length: > 0 } && IsHex(text.AsSpan(), TokenHexLength);

    /// <summary>What the Remote Desktop control starts in the child session: nth's own exe as the agent.</summary>
    public static string StartProgram(string exe, string name, string token)
    {
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathFullyQualified(exe) || exe.Contains('"', StringComparison.Ordinal))
            throw new ArgumentException("nth's exe must be a full path without quotes", nameof(exe));
        if (!IsName(name)) throw new ArgumentException("not a workspace pipe name", nameof(name));
        if (!IsToken(token)) throw new ArgumentException("not a workspace token", nameof(token));
        return "\"" + exe + "\" " + WorkspaceSetup.AgentCommand + " " + name + " " + token;
    }

    private static bool IsHex(ReadOnlySpan<char> text, int length)
    {
        if (text.Length != length) return false;
        foreach (var c in text)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        return true;
    }
}
