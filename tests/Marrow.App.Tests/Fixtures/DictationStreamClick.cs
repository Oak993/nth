namespace Marrow.App.Tests.Fixtures;

/// <summary>A second click at <see cref="Tl"/> seconds, the final pass the design plans for it (times in <see cref="DictationStreamEvent.T"/> are the design's absolute <c>at</c>) and the length of that schedule.</summary>
public sealed record DictationStreamClick(double Tl, IReadOnlyList<DictationStreamEvent> Final, double Duration);
