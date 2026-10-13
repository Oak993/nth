// PORT-ONLY: CU-DX-OPEN2 item 1, DESIGN.md 4.3; ED-NEW-dextra-open-7.
// Dextra's private routes do not expand the generic checking-head capability contract.
using Marrow.Core.Models;
namespace Marrow.Services.Port.MultiAccount;
public static class DextraTextRoutes
{
    /// <summary>
    /// PORT-ONLY (security gate S5, ED-NEW-secfinal-2): runs one private Claude, Pi or API answer in a new, empty folder of its own
    /// (never the shared temp root, whose .claude settings, .mcp.json or CLAUDE.md anything could have written) as a no-tools launch:
    /// Claude loads no setting source, no MCP config and no CLAUDE.md and saves no session (ClaudeSession.NoToolsArguments). The
    /// folder is removed after the call, as the Command Code route's is (ED-NEW-dextra-open-7: never recursively).
    /// </summary>
    public static async Task<T> PrivateAnswerAsync<T>(Marrow.Services.Providers.SessionConfiguration source, Func<Marrow.Services.Providers.SessionConfiguration, Task<T>> run)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(run);
        var folder = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? Path.GetTempPath(), "dextra-text-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        try
        {
            return await run(source with { WorkingDirectory = NativeFolderUrl(folder), NoTools = true }).ConfigureAwait(true);
        }
        finally
        {
            try { Directory.Delete(folder); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // ED-NEW-dextra-open-7: leave child-created files; no recursive cleanup of runtime-owned paths.
            }
        }
    }

    // A native Windows folder as a session's working directory URL: percent-encoded under the non-file scheme, as
    // ThreadRuntime.SessionDirectoryUrl builds a chat's (SwiftURL.FileURLWithPath takes only a POSIX path).
    private static Marrow.Core.Port.SwiftURL NativeFolderUrl(string path)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(path.Replace('/', '\\')))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return Marrow.Core.Port.SwiftURL.Create("marrow-native-path:" + builder) ?? throw new InvalidOperationException("Invalid private answer folder.");
    }

    public static bool Supports(ProviderKind provider) => TextOnlyAccountHead.Supports(provider) || provider == ProviderKind.Commandcode;
    public static string? UnavailableReason(ProviderKind provider) => Supports(provider) ? null : provider switch
    {
        ProviderKind.Codex => "Cannot turn off all built-in tools for answers.",
        ProviderKind.Copilot => "Cannot block every tool before it runs.",
        ProviderKind.Antigravity => "Cannot turn off all built-in tools for answers.",
        _ => "Cannot turn off its built-in tools for answers.",
    };
}
