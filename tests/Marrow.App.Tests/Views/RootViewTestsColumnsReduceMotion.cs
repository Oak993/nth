using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views;

// RootViewTestsColumnsPaths' tests on Reduce Motion's path (the columns flip at once, as the source does).
[Collection("Headless")]
public sealed class RootViewTestsColumnsReduceMotion(HeadlessSession headless) : RootViewTestsColumnsPaths(headless)
{
    protected override bool Animated => false;
}
