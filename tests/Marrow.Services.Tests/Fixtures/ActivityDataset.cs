using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-21): six actions over two days, the data the Activity view's filter, lineage and list tests read. Day one
/// (2026-10-11) holds lines 1 and 2; day two (2026-10-12) holds lines 3 to 6. Costs and tokens are 0.01 per 100.
/// <code>
///  1 dextra      m-1  t-1  shell     Run       grant:g-1     build ok     E:/shop
///  2 dextra      m-1  t-2  web       Read web  -             14 items     https://example.test/docs
///  3 astur-shop  m-2  t-3  mail      Outward   decision:d-7  sent         Pat
///  4 astur-shop  m-2  t-3  mail      Outward   decision:d-7  held         Lee
///  5 dextra      -    -    schedule  Agents    -             duty armed
///  6 dextra      m-3  t-1  shell     Run       grant:g-1     tests ok     E:/shop
/// </code>
/// </summary>
public static class ActivityDataset
{
    public static ActivityLogRig Make()
    {
        var rig = new ActivityLogRig(new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.Zero));
        rig.Add("dextra", "m-1", "t-1", "shell", "Run", "E:/shop", "build ok", "grant:g-1", [AuditInput.Plain("command", "dotnet build")], 0.01m, 100);
        rig.Advance(TimeSpan.FromSeconds(30));
        rig.Add("dextra", "m-1", "t-2", "web", "Read web", "https://example.test/docs", "14 items", null, null, 0.02m, 200);
        rig.Advance(TimeSpan.FromHours(12));
        rig.Add("astur-shop", "m-2", "t-3", "mail", "Outward", "Pat", "sent", "decision:d-7",
            [AuditInput.Typed("body", new string('x', 148)), AuditInput.Secret("token")], 0.03m, 300);
        rig.Advance(TimeSpan.FromMinutes(1));
        rig.Add("astur-shop", "m-2", "t-3", "mail", "Outward", "Lee", "held", "decision:d-7", null, 0.04m, 400);
        rig.Advance(TimeSpan.FromMinutes(1));
        rig.Add("dextra", null, null, "schedule", "Agents", null, "duty armed");
        rig.Advance(TimeSpan.FromMinutes(1));
        rig.Add("dextra", "m-3", "t-1", "shell", "Run", "E:/shop", "tests ok", "grant:g-1", null, 0.05m, 500);
        return rig;
    }
}
