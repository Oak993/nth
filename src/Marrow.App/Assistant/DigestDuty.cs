// PORT-ONLY: Dextra (DESIGN.md rev 5, 6, 7.1, 7.4; canvas 5 "Morning digest" duty; unit AS-11; ED-NEW-dextra-as11-8). No Swift twin.
// The Morning digest as a standing duty of Dextra's own, the way the canvas lists it: every day at 08:00 by default, reporting to the digest only, changeable in
// the duty editor like any duty (its time, off, run now). The scheduler fires it with everything a duty has (catch up once if nth was closed at 08:00, held under
// Stop all and Pause duties, DST-safe). It is nth's own, so it starts no mission: the launcher below composes the digest where a mission would be started, and the
// run ends inside its launch. It is made once; a duty the owner deletes is not made again.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public static class DigestDuty
{
    /// <summary>The digest duty's id in the duties file.</summary>
    public const string Id = "morning-digest";

    public const string Name = "Morning digest";

    /// <summary>What the duty card says it does (canvas 5).</summary>
    public const string Instructions = "Compose the morning digest from the feed, missions, usage and duties.";

    /// <summary>08:00 by default, changeable (6).</summary>
    public static readonly TimeOnly DefaultTime = new(8, 0);

    public static bool Is(Duty duty) => string.Equals(duty.Id, Id, StringComparison.Ordinal);

    /// <summary>The duty as it is first made: every day at 08:00 in the PC's time zone, reporting to the digest only. nth made it, not an assistant.</summary>
    public static Duty Make(string assistantId, string zoneId) =>
        new(Id, Name, assistantId, new TimeTrigger(TimeSchedule.Daily(DefaultTime), zoneId), Instructions)
        {
            Report = ReportStyle.DigestOnly,
            CreatedBy = "nth",
        };
}

/// <summary>
/// The scheduler's launcher with the digest in front of it: the digest duty is composed in place and ends inside its launch; every other duty goes to the launcher
/// behind it, a mission as before. The digest service is bound after the scheduler exists; until it is, the digest duty is refused ("not started"), so a run is
/// never lost silently.
/// </summary>
public sealed class DigestLauncher(IDutyLauncher inner) : IDutyLauncher
{
    private DigestService? digest;

    public void Bind(DigestService? service) => Volatile.Write(ref digest, service);

    public async Task<DutyLaunch> Launch(DutyFire fire)
    {
        ArgumentNullException.ThrowIfNull(fire);
        if (!DigestDuty.Is(fire.Duty)) return await inner.Launch(fire).ConfigureAwait(false);
        if (Volatile.Read(ref digest) is not { } service) return DutyLaunch.Refusal("The digest is not ready yet.");
        var run = Guid.NewGuid();
        try
        {
            // Run now is the owner asking for a digest (it says "Digest", not "Morning digest"); the clock, a catch-up and a wake are the morning one.
            var kind = fire.Cause == DutyCause.Manual ? DigestKind.Asked : DigestKind.Scheduled;
            var composed = await service.Compose(kind, preview: fire.Dry, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            if (composed is null) return DutyLaunch.Completed(run, DutyRunOutcome.Failed, "The digest could not be composed.");
            return DutyLaunch.Completed(run, DutyRunOutcome.Done, fire.Dry
                ? "Dry run: reads are real; nothing is written, sent or clicked. The digest would be pinned at the top of the home; no toast.\n\n" + composed.Text
                : "Digest composed and pinned; no toast.");
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return DutyLaunch.Completed(run, DutyRunOutcome.Failed, "The digest could not be composed: " + error.Message);
        }
    }
}
