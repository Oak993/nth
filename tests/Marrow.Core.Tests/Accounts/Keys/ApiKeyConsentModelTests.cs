namespace Marrow.Core.Tests.Accounts.Keys;

// OD-0116 for API keys as an invariant: whatever the order mode, credits, caps, pins, answers and clock moves, a thread that is on a key
// does not leave it for another unless the user agreed or pinned another (a key turned off is a forced move too).
[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyConsentModelTests
{
    [Fact]
    public void A_thread_never_moves_to_another_key_without_the_users_agreement()
    {
        var failures = new List<string>();
        var moves = 0;
        var refusals = 0;
        for (var seed = 0; seed < 20000 && failures.Count < 10; seed++)
        {
            var model = new ApiKeyConsentModel(seed);
            model.Run();
            moves += model.Moves;
            refusals += model.Refusals;
            if (model.Violations.Count > 0) failures.Add(model.Describe + " => " + string.Join(" / ", model.Violations));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // The runs must reach both outcomes, or the invariant proves nothing.
        Assert.True(moves > 1000 && refusals > 1000, "moves " + moves + ", refusals " + refusals);
    }
}
