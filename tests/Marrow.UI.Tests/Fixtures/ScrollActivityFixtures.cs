using Marrow.UI.Common;

namespace Marrow.UI.Tests.Fixtures;

// Helper for tests/Marrow.UI.Tests/Common/RenderBudgetTests.cs
// (DroppyCode/UI/Common/RenderBudget.swift, B2 1fbf81e, :71-75 protocol ScrollActivityReporter).

/// <summary>
/// A scripted <see cref="ScrollActivityReporter"/> (:74 <c>var isScrollingNow: Bool { get }</c>) for
/// ScrollActivity tests: its <see cref="IsScrollingNow"/> is set directly by the test instead of being
/// driven by a real timeline.
/// </summary>
public sealed class MockScrollActivityReporter : ScrollActivityReporter
{
    public bool IsScrollingNow { get; set; }
}
