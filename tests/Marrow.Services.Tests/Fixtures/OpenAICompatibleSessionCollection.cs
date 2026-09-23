namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin. MCPHub.Shared (src/Marrow.Services/MCP/MCPHub.cs :45) is one
// process-wide actor: its `specs` field is cached across calls ("specs ??= ReadSpecs()") until Reload()
// clears it, and OpenAICompatibleSessionFourthTests/FifthTests seed and reload it against a real stdio
// subprocess. Every OpenAICompatibleSession*Tests class can reach MCPHub.Shared.Tools() too, through
// StreamOneRound's real fallback (OpenAICompatibleSession.cs :628-631) whenever a test does not call
// SeedMcpToolsForTests first. Left in separate xUnit collections (the default: one collection per
// class), these run in parallel and race on that shared singleton: one test's Reload() tears down
// another's freshly-seeded server or live process mid-call, which is exactly what a prior merge attempt
// saw under `dotnet test Marrow.sln` (a real MCP call answering "The server didn't answer in time"
// instead of returning its tool's text, and an Unknown-tool error text substituted by a live server's).
// Putting all five classes in one named, non-parallel collection makes xUnit run their tests one at a
// time, so no two ever call into MCPHub.Shared at once (ED-065, R-TEST-9; same convention as
// FileWatcherCollection, GitCaptureCollection, GitIndexPathCacheCollection in this fixtures folder).
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OpenAICompatibleSessionCollection
{
    public const string Name = "OpenAICompatibleSession MCPHub.Shared";
}
