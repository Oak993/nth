using Marrow.App.Port;
using System.Reflection;
using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Sidebar;

// DroppyCode/App/Views/Sidebar/RowGlide.swift (B3 52bc792), RowGlideLayer and RowGlideView (:140-243).
[Collection("Headless")]
public sealed class RowGlideLayerTests(HeadlessSession headless)
{
    private static readonly Guid Thread = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    [Fact]
    [Trait("Req", "PR-SIDEBAR-141")] // App/Views/Sidebar/RowGlide.swift:15,140-154,228-236 the layer's own animator receives arrive and finish
    public void A_layer_over_an_injected_animator_hands_its_ghosts_over_on_that_animator() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        RowGlideAnimator animator = null!;
        RowGlideView view = null!;
        runtime.OnMain(() =>
        {
            animator = new RowGlideAnimator(new FakeGeniePlatform());
            var layer = new RowGlideLayer(animator);
            animator.Launch(
                threadID: Thread, direction: RowGlideAnimator.Glide.Direction.Reopen, from: new Rect(10, 40, 240, 28), fallback: new Rect(10, 200, 240, 28), bounds: new Rect(0, 0, 300, 150),
                departure: new FaceSample(240, 28), arrival: new FaceSample(240, 28), departureFill: 0.1, arrivalFill: 0);
            // The layer builds one view per glide when it is in a window; the handover needs no drawing, so it is driven without one.
            typeof(RowGlideLayer).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(layer, [animator.Glides]);
            view = layer.Children.OfType<RowGlideView>().Single();
            _ = (Task)typeof(RowGlideView).GetMethod("RunHandover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [true])!;
        });
        runtime.AdvanceMilliseconds((long)((RowGlideAnimator.Spring.SettlingDuration + 0.2) * 1000));
        runtime.OnMain(() =>
        {
            Assert.Empty(animator.Glides);
            Assert.False(animator.IsHiding(Thread));
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-135")] // App/Views/Sidebar/RowGlide.swift:131-137 the face is wrapped in a new frame view: the caller's control keeps its own size
    public void Rendering_a_face_leaves_the_callers_control_sized_as_it_was() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = new RowGlideAnimator(new RenderingGeniePlatform());
            var face = new Avalonia.Controls.Border { Width = 10, Height = 12 };
            var image = animator.Render(face, new Size(50, 20), Avalonia.Styling.ThemeVariant.Light);
            Assert.NotNull(image);
            Assert.Equal(10, face.Width);
            Assert.Equal(12, face.Height);
        });
    });
}
