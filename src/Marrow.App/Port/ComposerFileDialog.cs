// PORT-ONLY: NSOpenPanel at ComposerView.swift:526-533, with an exact accept-button label and multi-file selection.
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
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
    public Task<ValueList<string>?> Choose(Avalonia.Controls.Window? owner, bool allowsMultipleSelection, bool canChooseDirectories, string prompt)
    {
        var handle = owner?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var completion = new TaskCompletionSource<ValueList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Show(handle, allowsMultipleSelection, canChooseDirectories, prompt)); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    // PR-COMPOSER-063 windows_path: multi-select, no FOS_PICKFOLDERS, accept label set separately from title.
    private static ValueList<string>? Show(IntPtr owner, bool multiple, bool directories, string prompt)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
        try
        {
            dialog.GetOptions(out var options);
            const uint allowMultiSelect = 0x200;
            const uint pickFolders = 0x20;
            var flags = (uint)options | (uint)(FOS.FORCEFILESYSTEM | FOS.FILEMUSTEXIST | FOS.NOCHANGEDIR);
            flags = multiple ? flags | allowMultiSelect : flags & ~allowMultiSelect;
            flags = directories ? flags | pickFolders : flags & ~pickFolders;
            dialog.SetOptions((FOS)flags);
            dialog.SetOkButtonLabel(prompt);
            var result = dialog.Show(owner);
            if (result == unchecked((int)0x800704C7)) return null;
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            dialog.GetResults(out var pointer);
            var items = (IComposerShellItemArray)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            try
            {
                items.GetCount(out var count);
                var paths = new System.Collections.Generic.List<string>();
                for (uint index = 0; index < count; index++)
                {
                    items.GetItemAt(index, out var item);
                    try { item.GetDisplayName(SIGDN.FILESYSPATH, out var path); paths.Add(path); }
                    finally { Marshal.ReleaseComObject(item); }
                }
                return new ValueList<string>(paths);
            }
            finally { Marshal.ReleaseComObject(items); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }
}

[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IComposerShellItemArray
{
    void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
    void GetPropertyStore(uint flags, ref Guid iid, out IntPtr value);
    void GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr value);
    void GetAttributes(uint flags, uint mask, out uint attributes);
    void GetCount(out uint count);
    void GetItemAt(uint index, out IShellItem item);
    void EnumItems(out IntPtr items);
}
