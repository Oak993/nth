// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2, 7.3; ED-NEW-web-w12-1). The nth-ui server's entry for a CLI
// provider: an MCP server that launches nth's own executable as a stdio relay (McpPipeRelay) into this process. Every
// chat session registers once at launch and gets its own token, so the server knows the chat, provider and model. A
// session with no chat (an advisor's or a handshake's), or a process whose artifact host is off, gets no entry, so it
// launches exactly as before. No Swift counterpart.
using System.Collections.Concurrent;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Web;

public static class UiMcpBridge
{
    public const string Command = "--nth-ui-mcp";
    private const string Prefix = "NthUi-";
    private static readonly ConcurrentDictionary<string, UiRegistration> registrations = new(StringComparer.Ordinal);
    private static readonly McpPipeRelay relay = new(Prefix, token => new UiMcpServer(Registration(token)).Handle);

    /// <summary>The executable the relay entry launches: the app's own (its Main hands <see cref="Command"/> to <see cref="RunClient"/>).</summary>
    public static Ambient<Func<string>> Executable { get; } = new(new Func<string>(() => AppExecutable.Path));

    /// <summary>
    /// The session's registration: its chat, provider and model; null for a session of no chat, or while artifacts are off and no
    /// ask_user host is bound (W25: ask_user needs only the host).
    /// </summary>
    public static UiRegistration? For(SessionConfiguration configuration)
    {
        var host = ArtifactHost.Shared.Current;
        return configuration.IgnisThreadID is { } thread && (host.IsOn || AskUserDirectory.Shared.Current.IsBound)
            ? new UiRegistration(thread, configuration.Provider, configuration.Model, host) : null;
    }

    /// <summary>The nth-ui server entry for a CLI session, registered for its chat; null when <see cref="For"/> is.</summary>
    public static MCPResolvedServer? Server(SessionConfiguration configuration)
    {
        if (For(configuration) is not { } registration) return null;
        var token = Guid.NewGuid().ToString("N");
        registrations[token] = registration;
        return new MCPResolvedServer(UiMcpServer.ServerName, UiMcpServer.ServerName)
        {
            Command = Executable.Current(),
            Args = new ValueList<string>([Command, relay.PipeName, token]),
        };
    }

    /// <summary>The registration a token stands for; null when it stands for none.</summary>
    public static UiRegistration? Registration(string? token) =>
        token is not null && registrations.TryGetValue(token, out var registration) ? registration : null;

    /// <summary>The relay child's entry point (Program.Main).</summary>
    public static Task<int> RunClient(string[] args, Stream input, Stream output) => McpPipeRelay.RunClient(Command, Prefix, args, input, output);
}
