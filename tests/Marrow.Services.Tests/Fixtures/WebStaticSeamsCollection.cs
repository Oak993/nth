namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: web surfaces W10 (ED-NEW-web-w10-3). The tests that bind a process-wide web seam (the live-view cap, the MCP Apps advertisement) run alone,
// so no other test reads the seam while it is bound.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebStaticSeamsCollection
{
    public const string Name = "Web static seams";
}
