using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Services.Tests.Fixtures.DigestFixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 12.5 "with the day's last value printed in the next digest"; ED-NEW-dextra-as11-6): the contract with the activity log. The digest prints the
// final HMAC of the last closed day; the book keeps the first one printed for each day; and the log's check takes those as the finals it must still find, so a day that was
// rewritten after the digest printed it fails the check ("the day no longer ends with the HMAC the digest printed").
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-6")]
public sealed class DigestSealTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Day = new(2026, 10, 12);

    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private void Log(Action<AuditLog> write, DateTimeOffset at)
    {
        using var log = AuditLog.Open(temp.Roots, temp.Keys, new ManualTimeProvider(at));
        write(log);
    }

    private static AuditEntry Entry(string result) => new() { Assistant = "dextra", Tool = "digest", ActionClass = "ReadLocal", Result = result };

    [Fact]
    public void The_final_the_digest_prints_is_the_one_the_log_checks_and_a_day_rewritten_later_fails_the_check()
    {
        Log(log => log.Append(Entry("one")), Noon);
        var final = AuditVerifier.DayFinal(temp.AuditDirectory, Day);
        Assert.NotNull(final);

        using var store = AssistantStore.Open(temp.Roots);
        using var book = new DigestBook();
        book.Attach(store);
        book.Add(Plain("d1", Morning) with { Seal = new DigestSeal(Day, final) });
        var printed = book.PrintedDayFinals();
        Assert.Equal(final, printed![Day]);

        // Nothing changed since: the check passes with the printed value.
        Assert.Equal(AuditVerificationStatus.Verified, AuditVerifier.Verify(temp.Roots, temp.Keys, printed).Status);

        // The day gets another line after the digest printed it (something rewrote it): the check fails, naming the day.
        Log(log => log.Append(Entry("two")), Noon.AddHours(1));
        var verdict = AuditVerifier.Verify(temp.Roots, temp.Keys, printed);
        Assert.Equal(AuditVerificationStatus.Failed, verdict.Status);
        Assert.Contains("the digest printed for 2026-10-12", verdict.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_day_final_is_the_logs_own_last_hmac_for_the_day_and_a_day_with_no_line_has_none()
    {
        Log(log => log.Append(Entry("one")), Noon);
        Assert.Matches("^[0-9a-f]{64}$", AuditVerifier.DayFinal(temp.AuditDirectory, Day)!);
        Assert.Null(AuditVerifier.DayFinal(temp.AuditDirectory, Day.AddDays(-1)));
    }
}
