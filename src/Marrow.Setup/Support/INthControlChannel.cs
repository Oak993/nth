using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>Why a request to nth got no answer.</summary>
public enum NthControlFailure
{
    /// <summary>It was answered.</summary>
    None,

    /// <summary>The control pipe was not there (nth is not running, is starting, or is an older nth without one) within the connect time.</summary>
    NoPipe,

    /// <summary>The pipe belongs to another user or to an elevated nth, and this setup may not talk to it.</summary>
    NotAllowed,

    /// <summary>The pipe was there but nth sent nothing back in time, or closed it without an answer: it is hung or going down.</summary>
    NoAnswer,

    /// <summary>Something answered that is not nth's version-1 answer.</summary>
    BadAnswer,
}

/// <summary>One request's outcome: an answer, or why there is none. Never both.</summary>
public sealed record NthControlExchange(NthControlResponse? Response, NthControlFailure Failure)
{
    public static NthControlExchange Answered(NthControlResponse response) => new(response, NthControlFailure.None);

    public static NthControlExchange Failed(NthControlFailure failure) => new(null, failure);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a and 10, unit U4, ED-NEW-inst-u4-1). One request to a running nth and its answer:
/// everything the handshake says to nth goes through here. Production: <c>Port/PipeNthControlChannel.cs</c> (the named pipe).
/// Tests: a fake pipe server (a real pipe with a scripted nth behind it) and an in-memory channel.
/// </summary>
public interface INthControlChannel
{
    /// <summary>Sends one request and waits for its answer. A request that gets none returns the failure; it does not throw, except when cancelled.</summary>
    Task<NthControlExchange> SendAsync(NthControlRequest request, CancellationToken cancellationToken);
}
