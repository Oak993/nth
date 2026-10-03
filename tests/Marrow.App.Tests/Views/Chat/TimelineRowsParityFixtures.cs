using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Helper types for the parity closing pass's tests (TimelineRowsCaptureTests, TimelineRowsTimelineScenarioTests,
// ChatBadgeTests), kept out of the test files so req_verify reads their test names (R-TEST-2, R-TEST-3).

/// <summary>A rendered headless frame's RGBA pixels (TimelineRowsCaptureTests).</summary>
internal readonly record struct TimelineRowsCaptureFrame(int Width, int Height, byte[] Pixels)
{
    public (int R, int G, int B) At(int x, int y) => (Pixels[(y * Width + x) * 4], Pixels[(y * Width + x) * 4 + 1], Pixels[(y * Width + x) * 4 + 2]);
}

/// <summary>A sent bubble measured on a rendered frame the way the reference video frame was (TimelineRowsCaptureTests).</summary>
internal readonly record struct TimelineRowsBubbleMeasure(int Top, int Bottom, int BodyLeft, int BodyRight, int TipRight, int InkLeft, int InkRight,
    int TopRowLeft, int ThirdRowLeft, int TopRowRight, bool Flares, bool Notch, (int R, int G, int B) Fill, int[] InkRows);

/// <summary>An observable presented flag (PlanCard's @State isShowingPlan, TimelineRows.swift:2261): the badge's body tracks it
/// (ChatBadgeTests).</summary>
internal sealed class PresentedFlag : MainActorModel
{
    private bool shown;
    public bool Value { get => Read(shown); set => Set(ref shown, value); }
}

/// <summary>The real timeline under test (TimelineRowsTimelineScenarioTests).</summary>
internal sealed record TimelineRowsScene(ThreadTimeline View, ThreadRuntime Runtime, AppModel Model, Border Host, Avalonia.Controls.Window Window,
    ManualFrameSource Frames, RecordingPresenter Presenter);
