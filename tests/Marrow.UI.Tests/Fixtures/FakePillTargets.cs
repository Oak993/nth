// PORT-ONLY: plain controls standing in for the Tactus pill's parts (ED-NEW-pcctl-24), so the motion driver can be tested
// alone: it records each layout the driver asks for.
using Avalonia.Controls;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Fixtures;

public sealed class FakePillTargets : IPillTargets
{
    private readonly List<(PillPose Pose, PillPlaces Places)> layouts = [];

    public FakePillTargets(PillDriverKind driver = PillDriverKind.RenderThread)
    {
        Body = new PillSurface(PillSurfaceKind.Body, driver);
        StopSurface = new PillSurface(PillSurfaceKind.Stop, driver);
    }

    public Control Stage { get; } = new Canvas();

    public Control Content { get; } = new Canvas();

    public Control Below { get; } = new Canvas();

    public Control Row { get; } = new Canvas();

    public Control Group { get; } = new Canvas();

    public Control StopSlot { get; } = new Canvas();

    public Control StopClip { get; } = new Canvas();

    public Control TextClip { get; } = new Canvas();

    public PillSurface Body { get; }

    public PillSurface StopSurface { get; }

    public IReadOnlyList<(PillPose Pose, PillPlaces Places)> Layouts => layouts;

    public void Layout(PillPose pose, PillPlaces places) => layouts.Add((pose, places));
}
