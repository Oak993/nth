using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

public sealed class ScriptedStrikeDraws(double[] values) : IStrikeDraws
{
    public int Consumed { get; private set; }
    public double Next() => values[Consumed++];
}
