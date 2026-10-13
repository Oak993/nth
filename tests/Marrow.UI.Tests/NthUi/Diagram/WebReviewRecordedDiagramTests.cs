namespace Marrow.UI.Tests.NthUi.Diagram;

// ED-NEW-web-review-25 asked for the recorded-Mermaid check to require all 26 recordings; ED-NEW-web-review-30 (lead ruling 2026-10-13)
// amends where it runs: the recordings are committed and the check only reads the files (no Edge), so it is a managed class that the
// ordinary !~Native sweeps run. MermaidRunnerNativeTests re-records the 26 drawings in the lead's native pass.
[Trait("Decision", "ED-NEW-web-review-25")]
public sealed class WebReviewRecordedDiagramTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-review-30")]
    public void The_recorded_diagram_check_is_not_native_classified_so_the_managed_gate_runs_it()
    {
        var recorded = GetType().Assembly.GetTypes().Single(t => t.Name.StartsWith("NthSvgRecordedMermaid", StringComparison.Ordinal));
        Assert.DoesNotContain("Native", recorded.FullName!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-review-30")]
    public void All_26_recorded_mermaid_drawings_are_present_for_the_managed_check()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) continue;
            var folder = Path.Combine(dir.FullName, "tests", "Marrow.UI.Tests", "Fixtures", "mermaid-recorded");
            Assert.True(Directory.Exists(folder), folder);
            Assert.Equal(26, Directory.GetFiles(folder, "*.svg").Length);
            return;
        }
        Assert.Fail("Marrow.sln was not found above " + AppContext.BaseDirectory);
    }
}
