namespace Marrow.App.Tests.Fixtures;

// FinishChime's lazy cache belongs to the application's single main actor. Independent manual
// main loops are concurrent test actors, so tests using that static cache must own it exclusively.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FinishChimeCollection
{
    public const string Name = "Finish chime state";
}
