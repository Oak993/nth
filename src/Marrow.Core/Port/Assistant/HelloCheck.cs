// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Windows Hello guards every change", 16.1 the Windows Hello native gate,
// 22 rows 4 and 8; ED-NEW-dextra-as20-5). No Swift twin.
// The Windows Hello check as a seam (HelloGate in Marrow.App asks UserConsentVerifier, with Windows' own password prompt
// standing in when Hello is not set up) and the guard every check goes through: one check at a time, and three failed
// checks in a row lock purchases off until the owner turns them on again with a fresh check. A cancelled prompt counts
// as a failed check (the conservative reading); a prompt that could not be shown at all, or one cut short by nth
// closing, counts as neither and passes nothing.
namespace Marrow.Core.Port.Assistant;

/// <summary>How one Windows Hello check ended.</summary>
public enum HelloResult
{
    /// <summary>The owner verified (face, fingerprint, PIN, or his Windows password).</summary>
    Verified,
    /// <summary>The owner closed the prompt.</summary>
    Cancelled,
    /// <summary>The check failed (wrong PIN or password, retries used up).</summary>
    Failed,
    /// <summary>No prompt could be shown (Hello off by policy and no password prompt, an error), or nth closed it.</summary>
    Unavailable,
    /// <summary>Another check is open: one at a time.</summary>
    Busy,
}

/// <summary>Asks Windows Hello. <paramref name="reason"/> is what the prompt says the check is for.</summary>
public interface IHelloGate
{
    Task<HelloResult> Verify(string reason, CancellationToken cancellationToken);
}

/// <summary>Every Windows Hello check of purchases: one at a time, and the lockout after three failures in a row.</summary>
public sealed class HelloGuard(IHelloGate gate, int lockAfter = HelloGuard.LockAfter)
{
    public const int LockAfter = 3;

    private readonly IHelloGate gate = gate ?? throw new ArgumentNullException(nameof(gate));
    private int failures;
    private int running;

    /// <summary>Raised on the third failed check in a row; the count starts again.</summary>
    public event Action? LockedOut;

    /// <summary>Failed checks in a row so far.</summary>
    public int Failures => Volatile.Read(ref failures);

    /// <summary>
    /// One check. Verified resets the count; Cancelled and Failed add one, and the third raises <see cref="LockedOut"/>
    /// before this returns. A gate that throws is <see cref="HelloResult.Unavailable"/>.
    /// </summary>
    public async Task<HelloResult> Check(string reason, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref running, 1) == 1) return HelloResult.Busy;
        try
        {
            HelloResult result;
            try { result = await gate.Verify(reason, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { result = HelloResult.Unavailable; }
            if (result == HelloResult.Busy) result = HelloResult.Unavailable;
            if (result == HelloResult.Verified)
            {
                Volatile.Write(ref failures, 0);
                return result;
            }
            if (result is HelloResult.Cancelled or HelloResult.Failed && Interlocked.Increment(ref failures) >= lockAfter)
            {
                Volatile.Write(ref failures, 0);
                LockedOut?.Invoke();
            }
            return result;
        }
        finally { Volatile.Write(ref running, 0); }
    }
}
