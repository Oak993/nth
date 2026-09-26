using Marrow.App.Port;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

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
