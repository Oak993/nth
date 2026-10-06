namespace Marrow.UI.Port;

// PORT-ONLY: optional diagnostic span on the shared Stopwatch timeline. Async spans are labelled explicitly.
public readonly record struct AuroraBootWorkEvent(string Name, long Started, long Finished, int ThreadId, int EndThreadId);
