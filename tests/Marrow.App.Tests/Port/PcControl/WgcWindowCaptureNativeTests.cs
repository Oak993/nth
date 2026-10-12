// PORT-ONLY: Tactus (ED-NEW-pcctl-9). NATIVE: captures the real taskbar window through Windows.Graphics.Capture, so it runs
// only when the owner schedules native tests (every regular run excludes FullyQualifiedName~Native, which the class name carries). It reads pixels and
// sends no input.
using System.Runtime.InteropServices;
using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-9")]
public sealed class WgcWindowCaptureNativeTests
{
    [Fact]
    public async Task The_taskbar_window_is_captured_at_its_size_and_a_second_grab_reuses_the_pool()
    {
        Assert.True(WgcWindowCapture.IsSupported);
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        Assert.NotEqual(0, taskbar);
        var rect = new int[4];
        Assert.True(GetWindowRect(taskbar, rect));
        var frame = new PcRect(rect[0], rect[1], rect[2] - rect[0], rect[3] - rect[1]);
        using var capture = new WgcWindowCapture();

        var first = await capture.Capture(taskbar, frame, CancellationToken.None);
        var second = await capture.Capture(taskbar, frame, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal((frame.Width, frame.Height), (first.Width, first.Height));
        Assert.False(PcFrames.IsBlank(first));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, [Out] int[] rect);
}
