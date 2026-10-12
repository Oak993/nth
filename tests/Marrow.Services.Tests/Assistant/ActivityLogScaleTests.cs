using System.Diagnostics;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: a log of 20,000 lines over three days is opened, filtered, listed in both ways and exported twice, inside a
// bound that only a method that grows with the square of the log would miss (DESIGN.md 12.5; ED-NEW-dextra-as21-1). The
// size is fixed here; nothing in the test grows with the machine.
[Trait("Decision", "ED-NEW-dextra-as21-1")]
[Trait("Feature", "AS-21")]
public sealed class ActivityLogScaleTests : IDisposable
{
    private const int Lines = 20_000;
    private readonly ActivityLogRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public void A_log_of_twenty_thousand_lines_opens_filters_lists_and_exports_in_bounded_time()
    {
        for (var day = 0; day < 3; day++)
        {
            rig.AddMany(Lines / 3 + (day == 0 ? Lines % 3 : 0), i => new AuditEntry
            {
                Assistant = i % 5 == 0 ? "astur-shop" : "dextra",
                Mission = i % 7 == 0 ? null : "m-" + (i % 40),
                Agent = "t-" + (i % 9),
                Tool = i % 2 == 0 ? "shell" : "web",
                ActionClass = i % 3 == 0 ? "Run" : "Read web",
                Target = "E:/shop/file-" + i + ".cs",
                Result = "ok " + i,
                AllowedBy = i % 4 == 0 ? "grant:g-" + (i % 8) : null,
                Inputs = [AuditInput.Plain("command", "dotnet test --filter case" + i), AuditInput.Typed("body", new string('x', i % 50))],
                CostUsd = 0.001m,
                Tokens = i,
            });
            rig.Advance(TimeSpan.FromDays(1));
        }
        var clock = Stopwatch.StartNew();

        var snapshot = ActivityLogReader.Load(rig.Source);
        Assert.Equal(AuditVerificationStatus.Verified, snapshot.Verification.Status);
        Assert.Equal(Lines, snapshot.Rows.Count);
        Assert.Equal(3, snapshot.Facets.Days.Count);
        Assert.Equal(40, snapshot.Facets.Missions.Count);

        var filter = new ActivityFilter { Assistant = "dextra", Class = "Run", Text = "case19" };
        var passed = snapshot.Rows.Where(filter.Matches).ToList();
        Assert.NotEmpty(passed);
        Assert.All(passed, r => Assert.Contains("case19", r.Inputs[0].Text, StringComparison.Ordinal));

        var log = ActivityListBuilder.Build(snapshot.Rows, ActivityViewMode.Log);
        Assert.Equal(Lines + 3, log.Count);
        var lineage = ActivityListBuilder.Build(snapshot.Rows, ActivityViewMode.Lineage);
        Assert.True(lineage.Count > Lines);

        foreach (var format in new[] { ActivityExportFormat.Json, ActivityExportFormat.Csv })
        {
            using var output = new MemoryStream();
            var written = ActivityExporter.Write(output, format, ActivityLogReader.Stream(rig.Directory, snapshot.Verification), new ActivityExportContext(new ActivityFilter(), snapshot.Verification, DateTimeOffset.UtcNow));
            Assert.Equal(Lines, written);
            Assert.True(output.Length > Lines * 100L);
        }

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(90), clock.Elapsed.ToString());
    }
}
