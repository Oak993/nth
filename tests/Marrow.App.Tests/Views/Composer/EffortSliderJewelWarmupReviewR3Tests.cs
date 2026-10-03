using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, ED-252 warm-up v2, review round 3 on d864a73e8): each test states what the warm-up or the cache must
// do and failed on the head reviewed; both pass from review round 4 (round 3 answered), kept under plain names.
[Collection("Headless")]
public sealed class EffortSliderJewelWarmupReviewR3Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // OD-0096: the screen shows again "when the GPU ... changes": IsWarm must know which GPU the app draws with
    public void The_machine_identity_names_first_the_adapter_the_app_draws_with()
    {
        // Avalonia 12.1.2 (Avalonia.Win32 AngleWin32EglDisplay, decompiled; src sets no GraphicsAdapterSelectionCallback) makes
        // ANGLE's D3D11 device on DXGI adapter 0: the adapter the knob's programs are linked and kept for
        var drawing = DxgiAdapterZero.Ids();
        Assert.True(drawing is not null, "precondition: DXGI names an adapter 0");
        var machine = GpuProgramCache.Machine();
        Assert.False(string.IsNullOrEmpty(machine), "precondition: the machine's identity is read");
        var adapters = machine!.Split(" | ");
        // the identity is every hardware adapter, sorted: on a PC with two GPUs (this one: an AMD Radeon iGPU and an RTX 4080,
        // which DXGI also lists a second time) it reads the same whichever of them the app draws with, so after the app is
        // moved to the other GPU (Windows' per-app graphics preference, or the display cable moved) IsWarm stays true over a
        // folder of the other GPU's programs, the first-run screen is skipped and the four compiles land in the chip's idle
        // fallback; and a driver update of a GPU the app does not draw with makes it false (the screen shown, nothing to compile)
        Assert.True(adapters[0].StartsWith(drawing!, StringComparison.Ordinal),
            $"the identity names '{adapters[0]}' first; the app draws with DXGI adapter 0 '{drawing}' (identity: {machine})");
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the idle fallback steps only in frames where nothing animates: an Avalonia transition animates
    public void A_drag_handles_grip_growing_on_hover_counts_as_animating_for_the_warm_up() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var handle = new DragHandleView { Width = 10, Height = 120 };
        handle.Configure(new DragHandleView.Grip(RestLength: 12, ShownLength: 28, Thickness: 2.5)); // the terminal drawer's grip (TerminalPanel.cs:337)
        var window = new Avalonia.Controls.Window { Width = 200, Height = 200, Content = new Canvas { Children = { handle } } };
        window.Show();
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var grip = (Border)handle.Children[1];
            window.MouseMove(new Point(5, 60)); // hovered: the grip grows from 12 to 28 over 0.18 s (DragHandle.swift :160-166)
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(30);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(grip.Height > 12 && grip.Height < 28, $"precondition: the grip's transition runs (height {grip.Height})");
            // DragHandleView's grip and glow run on Avalonia's own transitions (DoubleTransition, DragHandle.cs:318-325): no
            // frame-clock request waits and nothing registers with AddActivity, so the warm-up takes these frames as quiet
            // and its compile (37-73 ms a step on a first launch) lands in the grip's 0.18 s growth
            Assert.True(GpuWarmup.Animating(frames), "the warm-up saw nothing animating while a drag handle's grip grows");
        }
        finally { window.Close(); }
    });
}
