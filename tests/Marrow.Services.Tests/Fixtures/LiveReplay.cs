namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test fixture (web surfaces W24, ED-NEW-web-w24-5). What a replay through the live-answer flow made: the timeline it left, how many
/// live rows it shows, and the text of the rows as they grew (one entry each time a row's text changed).
/// </summary>
internal sealed record LiveReplay(LiveTimelineSim Sim, int Rows, IReadOnlyList<string> Growth);
