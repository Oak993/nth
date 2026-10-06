using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// OD-0116 as an invariant, not a list of cases: whatever the settings, the answers and the changes made while a switch
// is being prepared, nothing carries the conversation to another account unless the user's current word allows it.
[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeConsentModelTests
{
    [Fact]
    public async Task No_call_that_carries_the_conversation_runs_without_the_users_current_agreement()
    {
        var failures = new List<string>();
        var switches = 0;
        for (var seed = 0; seed < 60000 && failures.Count < 12; seed++)
        {
            var model = new ConsentModel(seed);
            await model.RunAsync();
            switches += model.Switches;
            if (model.Violations.Count > 0) failures.Add(model.Describe + " => " + string.Join(" / ", model.Violations));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // The runs must actually carry conversations over, or the invariant proves nothing.
        Assert.True(switches > 5000, "switches " + switches);
    }
}
