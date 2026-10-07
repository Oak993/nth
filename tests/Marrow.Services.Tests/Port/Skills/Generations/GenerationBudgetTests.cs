// PORT-ONLY: integ-sk8-sk9. skills-PLAN 2.7 step 2: Marrow's entries use only the room left after the user's, with a 10% margin, and never more than half.
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Spec", "DL-9")]
public sealed class GenerationBudgetTests
{
    [Fact]
    [Trait("Feature", "SK-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Marrow_never_uses_more_than_half_and_leaves_a_tenth_after_the_users_entries()
    {
        var budget = new GenerationBudget(1000);
        Assert.Equal(500, budget.Room([])); // Half caps an empty provider: 1000 - 0 - 100 would allow 900.
        Assert.Equal(500, budget.Room([new ExistingSkill(new string('u', 97), "")])); // 100 characters used: still the half.
        Assert.Equal(300, budget.Room([new ExistingSkill(new string('u', 597), "")])); // 600 used: 1000 - 600 - 100.
        Assert.Equal(0, budget.Room([new ExistingSkill(new string('u', 997), "")])); // Never negative; the user's entries keep the room.
        Assert.Equal(500, budget.Room([new ExistingSkill("marrow:" + new string('m', 900), "")])); // Marrow's own earlier entries are not the user's.
    }
}
