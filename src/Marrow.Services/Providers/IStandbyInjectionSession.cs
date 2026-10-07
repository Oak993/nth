// PORT-ONLY: OD-0116, OD-0122, ED-439. A CLI that can hold the next account's context as history, with no model turn at all.
namespace Marrow.Services.Providers;

/// <summary>
/// A provider session that can wait for the switch holding injected history. With no model turn there is nothing that can act, so the standby is safe by construction:
/// no sandbox or prompt has to hold. Any turn, item, hook or approval on the waiting thread is a violation: the thread is discarded and the context goes the deferred way.
/// </summary>
public interface IStandbyInjectionSession
{
    /// <summary>
    /// Whether this CLI can hold injected history, checked at run time (version and schema, cached per executable). It lets the Handshake know before it asks the user
    /// whether to finish a reply, because a destination that cannot hold history will not continue one.
    /// </summary>
    Task<bool> SupportsStandbyInjection(CancellationToken ct);

    /// <summary>Starts the destination's own process and a thread that runs no turn. Throws <see cref="StandbyUnavailableException"/> when this CLI cannot hold injected history.</summary>
    Task<string> StartInjectedStandby(CancellationToken ct);

    /// <summary>Appends one text item to the waiting thread's history without a model turn.</summary>
    Task InjectStandbyHistory(string text, CancellationToken ct);

    /// <summary>The first event that showed activity on the waiting thread (its method name), or null.</summary>
    string? StandbyViolation { get; }

    /// <summary>Raised once, with the same text, when <see cref="StandbyViolation"/> is set. The session has already begun discarding the thread.</summary>
    Action<string>? OnStandbyViolation { get; set; }

    /// <summary>Cancel: deletes the waiting thread and stops the process. No turn is ever sent.</summary>
    Task DiscardStandby();

    /// <summary>The handover: from here the next turn is the user's, in their real mode. A CLI that has to open its own session for the user's turns does it here; a failure falls back.</summary>
    Task EndStandbyAsync(CancellationToken ct);
}

/// <summary>The destination CLI cannot hold injected history safely (too old, schema without the call, the call failed); the caller falls back.</summary>
public sealed class StandbyUnavailableException(string message) : Exception(message);
