using Marrow.UI.Common;

namespace Marrow.UI.Tests.Fixtures;

// UI/Common/RenderBudget.swift:74; counts watchdog reads without retaining a real timeline.
public sealed class CountingReporter : ScrollActivityReporter
{
    public bool Active = true;
    public int Reads;
    public bool IsScrollingNow { get { Reads++; return Active; } }
}
