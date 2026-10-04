using Marrow.App.Port;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The production composer chooser (WindowsComposerFileDialog: OpenPanel with the composer's options) shown by the production
/// Windows runner. A native child's AppModelHarness replaces OpenPanel.Runner with a recording runner for its whole flow
/// (R-TEST-10: no test opens a real dialog by accident); a native test that means to drive the real common dialog installs
/// this as the process's composer chooser, which lifts that replacement for the one call only (OpenPanelNativeTests).
/// </summary>
public sealed class SystemOpenPanelComposerFileDialog : IComposerFileDialog
{
    public Task<ValueList<string>?> Choose(Avalonia.Controls.Window? owner, bool allowsMultipleSelection, bool canChooseDirectories, string prompt)
    {
        using var runner = OpenPanel.Runner.Override(new WindowsOpenPanelRunner());
        return new WindowsComposerFileDialog().Choose(owner, allowsMultipleSelection, canChooseDirectories, prompt);
    }
}
