using Marrow.App.Assistant.Views;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-17: a team of profiles, their tasks and cards, in the shapes the org chart and the sidebar tree read.</summary>
public static class TeamSamples
{
    public const string Root = "root";

    public static AssistantProfile Dextra { get; } = new(Root, "Dextra");

    public static AssistantProfile Astur(string id, string name, AsturShape shape, AsturBudget? budget = null) =>
        new(id, name) { ParentId = Root, Shape = shape, Area = name + " looks after its own area.", Budget = budget ?? new AsturBudget(15) };

    public static AssistantProfile ChannelA { get; } = Astur("a", "Channel A", AsturShape.Squircle);

    public static AssistantProfile ChannelB { get; } = Astur("b", "Channel B", AsturShape.Hexagon, new AsturBudget(10, 5m));

    public static AssistantProfile Store { get; } = Astur("s", "Store", AsturShape.Diamond);

    /// <summary>Dextra and the Asturs named, in the order given.</summary>
    public static IReadOnlyList<AssistantProfile> Team(params AssistantProfile[] asturs) => [Dextra, .. asturs];

    public static (string AssistantId, MissionJob Job) Task(string owner, string title, MissionState state, int minutesAgo = 5, params HeadJob[] heads) =>
        (owner, HomeSamples.Job(title, state, minutesAgo, heads));

    public static DecisionItem Card(string assistant, string title) =>
        new(Guid.NewGuid(), DecisionKind.Approval, DecisionWeight.Reversible, title) { AssistantId = assistant, Raised = HomeSamples.Now };

    public static TeamUsage Usage(params TeamUsageLine[] lines) => new(lines, lines.Sum(l => l.Dollars), lines.Length == 0 ? 0 : lines.Max(l => l.WorstWindowPercent));

    public static TeamUsageLine Line(AssistantProfile who, double worst, double? fraction, decimal dollars = 0m) =>
        new(who.Id, who.Name, dollars, worst, who.Budget) { Fraction = fraction };
}
