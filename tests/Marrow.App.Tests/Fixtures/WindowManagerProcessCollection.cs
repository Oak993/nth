namespace Marrow.App.Tests.Fixtures;

// No Headless/native collection fixture is created in the parent. The sole selected child Fact
// creates its own HeadlessSession after the parent has pinned its OS process handle.
[CollectionDefinition("WindowManager process proof", DisableParallelization = true)]
public sealed class WindowManagerProcessCollection;
