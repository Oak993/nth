using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-c9b-1: the frame parking rule has the same owner-origin semantics as presence.
[Collection("Headless")]
public sealed class FrameClockCloakOwnerTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Decision", "ED-NEW-lean-7")]
    [InlineData(false, 0, null, false)]
    [InlineData(true, 0, null, true)]
    [InlineData(false, 0x1, null, false)]
    [InlineData(false, 0x2, null, true)]
    [InlineData(false, 0x3, null, true)]
    [InlineData(false, 0x4, 0x1, false)]
    [InlineData(false, 0x4, 0x2, true)]
    [InlineData(false, 0x4, 0x3, true)]
    [InlineData(false, 0x4, null, true)]
    [InlineData(false, 0x4, 0, true)]
    [InlineData(true, 0x4, 0x1, true)]
    public void HiddenBy_keeps_minimisation_and_resolves_inherited_cloak_origin(bool iconic, int mask, int? ownerMask, bool hidden) =>
        Assert.Equal(hidden, Win32FrameHost.HiddenBy(iconic, mask, 1, _ => ownerMask, handle => handle == 1 ? 2 : 0));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_frame_source_parks_an_inherited_shell_cloak_and_resumes_for_an_app_origin() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = new Window { Width = 100, Height = 80, Content = new Border() };
        window.Show();
        try
        {
            var origin = 0x2;
            var host = new FakeFrameHost { Probe = () => Win32FrameHost.HiddenBy(false, 0x4, 1, _ => origin, handle => handle == 1 ? 2 : 0) };
            var frames = new AvaloniaFrameSource(window, host);
            var ran = 0;
            frames.RequestFrameAt(null, _ => ran++);
            for (var tick = 0; tick < 3; tick++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(0, ran);
            Assert.Equal(1, frames.Pending);
            origin = 0x1;
            AvaloniaFrameSource.WindowsChanged();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, ran);
            Assert.Equal(0, frames.Pending);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
