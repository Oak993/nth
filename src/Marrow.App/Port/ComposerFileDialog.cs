// PORT-ONLY: NSOpenPanel at ComposerView.swift:526-533, with an exact accept-button label and multi-file selection.
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Marrow.Core.Port;

namespace Marrow.App.Port;

public interface IComposerFileDialog
{
    Task<ValueList<string>?> Choose(Avalonia.Controls.Window? owner, bool allowsMultipleSelection, bool canChooseDirectories, string prompt);
}

public static class ComposerFileDialog
{
    public static Ambient<IComposerFileDialog> Current { get; } = new(new WindowsComposerFileDialog());
}

internal sealed class WindowsComposerFileDialog : IComposerFileDialog
{
    public Task<ValueList<string>?> Choose(Avalonia.Controls.Window? owner, bool allowsMultipleSelection, bool canChooseDirectories, string prompt) =>
        throw new NotImplementedException("MARROW_STUB ComposerView.swift:526-533 native file chooser");
}
