// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 `ask_user`; ED-NEW-web-w25-1). The seam between the ask_user tool and the chat that
// shows its card: the tool (Marrow.Services) builds a card and waits; the chat's runtime (Marrow.App) puts the card in the
// thread, waits for the person, and answers. The tool never learns how the card looks; the runtime never learns which provider
// asked. A directory finds the host of a chat; it is unbound (no host, no tool offered) until the app binds it at start-up, so a
// test or a tool that never binds it offers no ask_user and launches every provider exactly as before. No Swift counterpart.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Web;

/// <summary>How an ask_user wait ended.</summary>
public enum AskOutcomeKind
{
    /// <summary>The person answered; the card holds the answers.</summary>
    Answered,
    /// <summary>The person chose not to answer.</summary>
    Skipped,
    /// <summary>The person stopped the turn.</summary>
    Stopped,
    /// <summary>Nobody is waiting any more (the provider's call or session ended); the card stays for the person to answer later.</summary>
    Detached,
}

/// <summary>The end of one wait, with the card as it then stood.</summary>
public sealed record AskOutcome(AskOutcomeKind Kind, AskCard Card);

/// <summary>The chat's side of ask_user.</summary>
public interface IAskUserHost
{
    /// <summary>
    /// Puts <paramref name="card"/> in the chat and waits for the person. Never throws for a cancelled wait: a cancelled
    /// <paramref name="cancellation"/> (the provider's call ended) is <see cref="AskOutcomeKind.Detached"/>, and the card stays waiting.
    /// </summary>
    Task<AskOutcome> Ask(AskCard card, CancellationToken cancellation);
}

/// <summary>Finds the host of a chat. Unbound until the app binds it.</summary>
public sealed class AskUserDirectory(Func<Guid, IAskUserHost?>? find)
{
    /// <summary>The app's directory; the default is unbound, so ask_user is off.</summary>
    public static Ambient<AskUserDirectory> Shared { get; } = new(new AskUserDirectory(null));

    public bool IsBound => find is not null;

    /// <summary>The chat's host; null for a chat that has none (or while unbound).</summary>
    public IAskUserHost? Find(Guid thread) => find?.Invoke(thread);
}
