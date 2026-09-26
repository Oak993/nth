// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :526-533.
// PORT-ONLY: compatibility adapter for the existing composer seam; OpenPanel owns native presentation.
using System;
using System.Linq;
using System.Threading.Tasks;
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
    public Task<ValueList<string>?> Choose(Avalonia.Controls.Window? owner, bool allowsMultipleSelection, bool canChooseDirectories, string prompt)
    {
        // :527-532. OpenPanel shows modally on the calling main thread and owns the active-window handle.
        var panel = new OpenPanel
        {
            AllowsMultipleSelection = allowsMultipleSelection,
            CanChooseDirectories = canChooseDirectories,
            CanChooseFiles = !canChooseDirectories, // NSOpenPanel's file-choice default at this file-only site.
            Prompt = prompt,
        };
        if (panel.RunModal() != ModalResponse.OK) return Task.FromResult<ValueList<string>?>(null); // :532
        // :533. Windows platform boundary: convert chosen file URLs back to native paths for attachments.
        // Uri.LocalPath handles drive/UNC prefixes and percent escapes; SwiftURL.Path is POSIX-style.
        return Task.FromResult<ValueList<string>?>(new ValueList<string>(panel.Urls.Select(url => new Uri(url.AbsoluteString).LocalPath)));
    }
}
