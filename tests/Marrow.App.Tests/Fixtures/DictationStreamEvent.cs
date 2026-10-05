namespace Marrow.App.Tests.Fixtures;

/// <summary>One event of the design's live stream or final plan: its time (seconds from the first click), <c>add</c> or <c>rev</c>, the word's place and its text.</summary>
public sealed record DictationStreamEvent(double T, string Kind, int Index, string Text);
