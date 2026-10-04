// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>Character budget measured for the provider's smallest offered context window (WU-11), never the selected model.</summary>
public sealed record GenerationBudget(int Characters)
{
    public static GenerationBudget ForModels(IEnumerable<int> measuredCharacterBudgets)
    {
        var budgets = measuredCharacterBudgets.ToArray();
        if (budgets.Length == 0 || budgets.Any(b => b <= 0)) throw new ArgumentException("Every offered model needs a measured budget.");
        return new(budgets.Min());
    }
    public int Room(IEnumerable<ExistingSkill> existing)
    {
        if (Characters <= 0) throw new ArgumentOutOfRangeException(nameof(Characters));
        var used = existing.Where(s => !s.Name.StartsWith("marrow:", StringComparison.OrdinalIgnoreCase)).Sum(s => s.Name.Length + s.Description.Length + 3);
        return Math.Max(0, Math.Min(Characters / 2, Characters - used - (int)Math.Ceiling(Characters * 0.1)));
    }
}
