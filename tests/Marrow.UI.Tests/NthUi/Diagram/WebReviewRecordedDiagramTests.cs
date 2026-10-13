namespace Marrow.UI.Tests.NthUi.Diagram;

[Trait("Decision", "ED-NEW-web-review-25")]
public sealed class WebReviewRecordedDiagramTests
{
    [Fact]
    public void Checks_requiring_the_real_renderer_recordings_are_classified_for_the_leads_scheduled_pass()
    {
        var recorded = GetType().Assembly.GetTypes().Single(t => t.Name.StartsWith("NthSvgRecordedMermaid", StringComparison.Ordinal));
        Assert.Contains("Native", recorded.FullName!, StringComparison.Ordinal);
    }
}
