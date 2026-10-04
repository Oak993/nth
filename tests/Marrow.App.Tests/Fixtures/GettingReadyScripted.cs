using Marrow.App.Port.FirstRun;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-2: a scripted preparation (no GPU, no cache folder) and the log of what it was asked.</summary>
public sealed record GettingReadyScripted(FirstRunPreparation Preparation, List<string> Log, Func<bool> Prepared);
