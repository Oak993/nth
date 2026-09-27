using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A working twin of Marrow.App.Tests.Fixtures.FakeGeniePlatform (RowGlideFixtures.cs), whose RenderImage throws by
/// design (RowGlideAnimator.Render is not exercised by that unit's own records). SidebarViewTests part 4
/// (PR-SIDEBAR-081 Glide, PR-SIDEBAR-084 ArchiveWithGenie) is the unit that does exercise it, so this one actually
/// renders, off a real RenderTargetBitmap, with no OS read (deterministic: Reduce Motion is a test-set property, not
/// SystemParametersInfo).
/// </summary>
public sealed class RenderingGeniePlatform : IGeniePlatform
{
    public bool AccessibilityDisplayShouldReduceMotion { get; set; }

    public double? MainScreenBackingScaleFactor => 1;

    public IImage? RenderImage(Control content, double scale)
    {
        content.Measure(Size.Infinity);
        content.Arrange(new Rect(content.DesiredSize));
        var size = content.DesiredSize;
        var pixels = new PixelSize((int)Math.Ceiling(Math.Max(1, size.Width) * scale), (int)Math.Ceiling(Math.Max(1, size.Height) * scale));
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(content);
        return bitmap;
    }
}

/// <summary>
/// The OpenPanel seam, recording (R-VIEW-9, R-TEST-10): what each panel was configured with when it ran, and a fixed
/// answer, the chosen URLs or null for Cancel. SidebarViewTests uses it for SidebarView.swift:500-511.
/// </summary>
public sealed class RecordingOpenPanelRunner(IReadOnlyList<SwiftURL>? answer) : IOpenPanelRunner
{
    public List<(bool Directories, bool Files, bool Multiple, string Prompt, string Message)> Shown { get; } = [];

    public IReadOnlyList<SwiftURL>? RunModal(OpenPanel panel)
    {
        Shown.Add((panel.CanChooseDirectories, panel.CanChooseFiles, panel.AllowsMultipleSelection, panel.Prompt, panel.Message));
        return answer;
    }
}
