// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; PLAN unit U3). The two provider-session actions Ignis needs that
// unit U2 adds to the sessions (OpenAICompatibleSession.KeepWarm() and Tidy(keepTurns); ClaudeSession
// Compact(instructions)). The runtime reaches them only through these seams, so this unit builds without U2; the
// process defaults below are what the sessions can do today: no silent keep-warm (CLIs use hidden pings instead) and a
// plain compact. IgnisRuntimeStartup.Bind is where the lead points them at U2's methods.
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public static class IgnisSessionHooks
{
    /// <summary>A session's silent keep-warm request (API sessions: same payload plus one user message, max_tokens 1, not added to history); null: the session has none.</summary>
    public static Ambient<Func<ProviderSession, Func<Task>?>> KeepWarm { get; } = new(new Func<ProviderSession, Func<Task>?>(_ => null));

    /// <summary>A session's tidy-up keeping the last n turns word for word (API: exact; Claude: a /compact instruction; Codex: plain compact).</summary>
    public static Ambient<Func<ProviderSession, int, Task>> Tidy { get; } = new(new Func<ProviderSession, int, Task>((session, _) => session.Compact()));
}
