using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views;

// RootViewTestsColumnsPaths' tests on the default path (the chat left fades out for 90 ms, gated at once).
[Collection("Headless")]
public sealed class RootViewTestsColumns(HeadlessSession headless) : RootViewTestsColumnsPaths(headless)
{
    protected override bool Animated => true;
}
