using System.Reflection;
using System.Text.RegularExpressions;
using Marrow.Testing;

namespace Marrow.Core.Tests;

[Trait("Rule", "R-TEST-1")]
[Trait("Decision", "ED-360")]
public sealed class WebReviewDecisionIdTests
{
    [Fact]
    public void The_owners_review_decision_ids_are_accepted_without_admitting_other_unassigned_unit_names()
    {
        var pattern = (Regex)typeof(Traceability).GetMethod("DecisionId", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        Assert.Matches(pattern, "ED-NEW-web-review-1");
        Assert.Matches(pattern, "ED-NEW-web-review-24");
        foreach (var invalid in new[] { "ED-NEW-web-review", "ED-NEW-web-review-x", "ED-NEW-web-arbitrary-1", "ED-NEW-web-review-1-extra", "ED-NEW-web-review-1\n" })
            Assert.DoesNotMatch(pattern, invalid);
        Assert.Matches(pattern, "ED-NEW-web-w20-1");
        Assert.Matches(pattern, "ED-360");
    }
}
