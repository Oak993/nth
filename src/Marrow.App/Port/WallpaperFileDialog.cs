// PORT-ONLY: the seam behind WallpaperCard.swift:168-189's NSOpenPanel. The packet's UNK item on this file (part1.md
// line 412) settles both of the panel's exact strings on IFileDialog: SetTitle carries :174's message and
// SetOkButtonLabel carries :175's prompt verbatim, both available on every Windows version this port supports, so
// neither string is dropped or reworded. R-VIEW-9: presentation goes through a seam; a headless test overrides
// Current so no test opens a real system dialog (R-TEST-10).
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Marrow.Core.Port;

namespace Marrow.App.Port;

/// <summary>The one call WallpaperCard.choose() needs: a chosen picture path, or null on cancel.</summary>
public interface IWallpaperFileDialog
{
    Task<string?> Choose(Avalonia.Controls.Window? owner);
}

/// <summary>
/// WallpaperCard.swift:168-189 as a Windows common Open dialog (IFileOpenDialog): title "Choose a picture for the
/// window" (:174), OK button "Use as Wallpaper" (:175), single selection (:172), files only (:173), filtered to the
/// same extensions WallpaperStore.Accepts recognises (Wallpaper.swift:98-108). Runs off the UI thread so the sheet
/// does not block (:169 comment); the caller installs the result back on the main actor.
/// </summary>
public sealed class WindowsWallpaperFileDialog : IWallpaperFileDialog
{
    // UI-THEME-020: the two byte-for-byte strings the dialog carries, named so a test can assert them without
    // opening the real system dialog (R-TEST-10).
    internal const string Title = "Choose a picture for the window"; // :174
    internal const string OkButtonLabel = "Use as Wallpaper"; // :175

    public Task<string?> Choose(Avalonia.Controls.Window? owner) => Task.Run(() => ShowDialog(owner));

    private static string? ShowDialog(Avalonia.Controls.Window? owner)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
        try
        {
            dialog.SetTitle(Title); // :174
            dialog.SetOkButtonLabel(OkButtonLabel); // :175
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS.FORCEFILESYSTEM | FOS.FILEMUSTEXIST | FOS.NOCHANGEDIR); // :172-173 files, single selection (default, no ALLOWMULTISELECT)
            var spec = new COMDLG_FILTERSPEC
            {
                pszName = "Pictures",
                pszSpec = "*.bmp;*.dib;*.gif;*.hdp;*.ico;*.jfif;*.jpe;*.jpeg;*.jpg;*.jxr;*.png;*.tif;*.tiff;*.wdp",
            }; // Wallpaper.swift:98-108 IsSupportedImageExtension
            dialog.SetFileTypes(1, [spec]);
            dialog.SetFileTypeIndex(1);

            var ownerHandle = owner is not null ? owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero : IntPtr.Zero;
            var hr = dialog.Show(ownerHandle); // :176-181 sheet on the key window, or standalone when there is none
            const int ErrorCancelled = unchecked((int)0x800704C7);
            if (hr == ErrorCancelled) return null; // :182-187 cancel: nothing
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }
}

/// <summary>R-LIFE-2: the dialog is a process-wide seam a test replaces for its own async flow.</summary>
public static class WallpaperFileDialog
{
    public static Ambient<IWallpaperFileDialog> Current { get; } = new(() => new WindowsWallpaperFileDialog());
}

[Flags]
internal enum FOS : uint
{
    FORCEFILESYSTEM = 0x00000040,
    FILEMUSTEXIST = 0x00001000,
    NOCHANGEDIR = 0x00000008,
}

internal enum SIGDN : uint
{
    FILESYSPATH = 0x80058000,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct COMDLG_FILTERSPEC
{
    [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
}

[ComImport]
[Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
internal class FileOpenDialogCoClass;

[ComImport]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOpenDialog
{
    // IModalWindow
    [PreserveSig]
    int Show(IntPtr parent);

    // IFileDialog
    void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
    void SetFileTypeIndex(uint iFileType);
    void GetFileTypeIndex(out uint piFileType);
    void Advise(IntPtr pfde, out uint pdwCookie);
    void Unadvise(uint dwCookie);
    void SetOptions(FOS fos);
    void GetOptions(out FOS pfos);
    void SetDefaultFolder(IShellItem psi);
    void SetFolder(IShellItem psi);
    void GetFolder(out IShellItem ppsi);
    void GetCurrentSelection(out IShellItem ppsi);
    void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
    void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
    void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
    void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
    void GetResult(out IShellItem ppsi);
    void AddPlace(IShellItem psi, int fdap);
    void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
    void Close(int hr);
    void SetClientGuid(ref Guid guid);
    void ClearClientData();
    void SetFilter(IntPtr pFilter);

    // IFileOpenDialog
    void GetResults(out IntPtr ppenum);
    void GetSelectedItems(out IntPtr ppsai);
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}
