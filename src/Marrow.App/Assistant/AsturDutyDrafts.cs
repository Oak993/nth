// PORT-ONLY: DESIGN.md 7.1, 9.2; ED-NEW-dextra-open-4 (LEAD-REVIEW).
// A kept idea becomes a duty only on the owner's review. No inferred standing schedule.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public static class AsturDutyDrafts
{
    public static (Duty? Duty, string? Problem) Build(AssistantProfile profile, string instructions, string schedule, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (profile.Retired) return (null, "This Astur is retired.");
        DutyTrigger trigger = new ManualTrigger();
        if (!string.IsNullOrWhiteSpace(schedule))
        {
            var read = ScheduleText.Read(schedule, now, zone);
            if (read.Schedule is null) return (null, read.Problem);
            trigger = new TimeTrigger(read.Schedule, zone.Id);
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profile.Id + "\n" + instructions)));
        var duty = new Duty("astur-" + hash[..24], instructions.Length > Duty.NameLimit ? instructions[..Duty.NameLimit] : instructions, profile.Id, trigger, instructions)
        { CreatedBy = "owner", Report = ReportStyle.SilentUnlessChanged, Since = now };
        return duty.Problem() is { } problem ? (null, problem) : (duty, null);
    }
}
