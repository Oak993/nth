// PORT-ONLY: the seam behind AppKit's NSOpenPanel as SidebarView.swift:500-511 (B3 52bc792) drives it: a panel
// configured property by property, `runModal()` answering OK or Cancel, then `urls`. The Windows panel is the common
// Open dialog (IFileOpenDialog, the COM declarations in WallpaperFileDialog.cs): canChooseDirectories without
// canChooseFiles is FOS_PICKFOLDERS, allowsMultipleSelection is FOS_ALLOWMULTISELECT, `message` is the dialog's
// title (SetTitle) and `prompt` its OK button (SetOkButtonLabel), both verbatim (PR-SIDEBAR-012 windows_path, the
// same mapping WallpaperFileDialog.cs settles for :174-175 of WallpaperCard.swift). R-VIEW-9: presentation goes
// through a seam; a headless test replaces Runner so no test opens a real system dialog (R-TEST-10).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.Core.Port;

namespace Marrow.App.Port;

/// <summary>NSApplication.ModalResponse as <c>runModal()</c> answers it: the two values the source compares.</summary>
public enum ModalResponse
{
    OK,
    Cancel,
}

/// <summary>What a panel shows the user and hands back: the chosen URLs, or null when the user cancelled.</summary>
public interface IOpenPanelRunner
{
    IReadOnlyList<SwiftURL>? RunModal(OpenPanel panel);
}

/// <summary>
/// NSOpenPanel as its callers configure it. Only the properties a ported call site sets are declared; each starts at
/// the value the site overwrites, so no AppKit default is relied on.
/// </summary>
public sealed class OpenPanel
{
    /// <summary>R-LIFE-2: the panel's presentation is a process-wide seam a test replaces for its own flow.</summary>
    public static Ambient<IOpenPanelRunner> Runner { get; } = new(() => new WindowsOpenPanelRunner());

    public bool CanChooseDirectories { get; set; }
    public bool CanChooseFiles { get; set; }
    public bool AllowsMultipleSelection { get; set; }
    public string Prompt { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>The URLs the user chose, valid after <see cref="RunModal"/> answered OK.</summary>
    public IReadOnlyList<SwiftURL> Urls { get; private set; } = [];

    /// <summary><c>runModal()</c>: shows the panel and waits for it, on the calling (main) thread.</summary>
    public ModalResponse RunModal()
    {
        var chosen = Runner.Current.RunModal(this);
        if (chosen is null) return ModalResponse.Cancel;
        Urls = chosen;
        return ModalResponse.OK;
    }
}

/// <summary>
/// The Windows common Open dialog, shown modally over the app's active window on the calling thread, as
/// <c>runModal()</c> blocks its caller until the panel closes.
/// </summary>
public sealed class WindowsOpenPanelRunner : IOpenPanelRunner
{
    private const FOS PickFolders = (FOS)0x00000020; // FOS_PICKFOLDERS
    private const FOS AllowMultiSelect = (FOS)0x00000200; // FOS_ALLOWMULTISELECT
    private const FOS PathMustExist = (FOS)0x00000800; // FOS_PATHMUSTEXIST

    /// <summary>The dialog's option set for a panel: the dialog's own <paramref name="current"/> options plus what the panel's
    /// properties ask for (exposed so the native test reads the very set that is handed to <c>SetOptions</c>).</summary>
    internal static FOS Options(FOS current, OpenPanel panel)
    {
        var options = current | FOS.FORCEFILESYSTEM | FOS.NOCHANGEDIR | PathMustExist;
        // canChooseDirectories with canChooseFiles off: folders only. No ported site asks for both at once.
        if (panel.CanChooseDirectories && !panel.CanChooseFiles) options |= PickFolders;
        if (panel.AllowsMultipleSelection) options |= AllowMultiSelect;
        return options;
    }

    internal const FOS PickFoldersOption = PickFolders;
    internal const FOS AllowMultiSelectOption = AllowMultiSelect;

    public IReadOnlyList<SwiftURL>? RunModal(OpenPanel panel)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
        try
        {
            dialog.SetTitle(panel.Message);
            dialog.SetOkButtonLabel(panel.Prompt);
            dialog.GetOptions(out var options);
            dialog.SetOptions(Options(options, panel));

            var hr = dialog.Show(OwnerHandle());
            const int ErrorCancelled = unchecked((int)0x800704C7);
            if (hr == ErrorCancelled) return null;
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            dialog.GetResults(out var arrayPointer);
            var array = (IShellItemArray)Marshal.GetObjectForIUnknown(arrayPointer);
            Marshal.Release(arrayPointer);
            try
            {
                array.GetCount(out var count);
                var urls = new List<SwiftURL>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    array.GetItemAt(index, out var item);
                    try
                    {
                        item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
                        urls.Add(FileURL(path));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
                return urls;
            }
            finally
            {
                Marshal.ReleaseComObject(array);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    // The app's active window, so the dialog is modal to the app as runModal() is; no owner when none is active.
    private static IntPtr OwnerHandle()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return IntPtr.Zero;
        var owner = desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow;
        return owner?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    // A chosen path as the file URL AppModel.addProject(at:) reads: the same "file://" form and percent-encoding
    // Project.Url builds (Library.cs), since the port has no fileURLWithPath initializer.
    private static SwiftURL FileURL(string path)
    {
        var builder = new System.Text.StringBuilder("file://");
        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized[0] != '/') builder.Append('/');
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return SwiftURL.Create(builder.ToString()) ?? throw SwiftTraps.Fatal("Invalid folder path.");
    }
}

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
    void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
    void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
    void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
    void GetCount(out uint pdwNumItems);
    void GetItemAt(uint dwIndex, out IShellItem ppsi);
    void EnumItems(out IntPtr ppenumShellItems);
}
