namespace Marrow.Core.Support;

/// <summary>The folders the shortcut code needs, as <c>SHGetKnownFolderPath(..., KF_FLAG_DONT_VERIFY)</c> gives them.</summary>
public enum ShellKnownFolder
{
    /// <summary><c>FOLDERID_Desktop</c>: the user's own desktop, including a OneDrive-redirected one. Never the Public desktop.</summary>
    Desktop,

    /// <summary><c>FOLDERID_Programs</c>: the user's Start menu Programs folder, where Velopack writes <c>nth.lnk</c>.</summary>
    Programs,

    /// <summary><c>FOLDERID_LocalAppData</c>: the default install root's parent and the folder <c>%LOCALAPPDATA%</c> names.</summary>
    LocalAppData,

    /// <summary><c>FOLDERID_RoamingAppData</c>.</summary>
    RoamingAppData,

    /// <summary><c>FOLDERID_Profile</c>: the user's profile folder (its <c>Desktop</c> is the unredirected desktop).</summary>
    Profile,
}

/// <summary>What Explorer is told after a change (<c>SHChangeNotify</c> with <c>SHCNF_PATHW</c>).</summary>
public enum ShellChange
{
    /// <summary><c>SHCNE_CREATE</c>.</summary>
    Created,

    /// <summary><c>SHCNE_DELETE</c>.</summary>
    Deleted,

    /// <summary><c>SHCNE_UPDATEITEM</c>.</summary>
    Updated,
}

/// <summary>A <c>.lnk</c> file as a folder listing sees it, without opening it (so a cloud placeholder is not downloaded).</summary>
/// <param name="Path">The link's full path.</param>
/// <param name="Attributes">Its file attributes, including the cloud recall bits .NET has no names for.</param>
/// <param name="LastWriteTimeUtc">Its last write time, which the Settings scan caches by.</param>
public sealed record ShellLinkFile(string Path, FileAttributes Attributes, DateTime LastWriteTimeUtc)
{
    /// <summary>A OneDrive Files On-Demand placeholder: no scan reads, counts or removes it (PLAN.md 8a).</summary>
    public bool IsCloudPlaceholder => (Attributes & (ShellLinkKeys.RecallOnOpen | ShellLinkKeys.RecallOnDataAccess)) != 0;
}

/// <summary>
/// Everything a shortcut holds that nth reads or writes. <see cref="Target"/> is the raw stored path
/// (<c>IShellLinkW::GetPath(SLGP_RAWPATH)</c>), so a path written with <c>%LOCALAPPDATA%</c> reads back with it; expand it
/// with <see cref="IShellLinks.ExpandEnvironment"/> before comparing. The two property-store values are
/// <see cref="AppUserModelId"/> (<c>PKEY_AppUserModel_ID</c>) and <see cref="ToastActivatorClsid"/>
/// (<c>PKEY_AppUserModel_ToastActivatorCLSID</c>); <see cref="CreatedBy"/> is nth's own marker property.
/// </summary>
public sealed record ShellLink(
    string Target,
    string Arguments = "",
    string WorkingDirectory = "",
    string IconPath = "",
    int IconIndex = 0,
    string Description = "",
    int ShowCommand = ShellLinkKeys.ShowNormal,
    string? AppUserModelId = null,
    Guid? ToastActivatorClsid = null,
    string? CreatedBy = null)
{
    /// <summary>A shortcut nth made: it carries nth's marker (any <c>nth-setup/</c> version). Only these are repaired or removed by the Settings switch.</summary>
    public bool IsMadeByNth => CreatedBy is not null && CreatedBy.StartsWith(ShellLinkKeys.CreatedByPrefix, StringComparison.Ordinal);
}

/// <summary>
/// A change to an existing link: every non-null field is set, everything else (including properties nth does not know,
/// such as a <c>ToastActivatorCLSID</c>) survives, because the link is loaded, changed and saved, never rebuilt (PLAN.md 8a).
/// </summary>
public sealed record ShellLinkChange(
    string? Target = null,
    string? WorkingDirectory = null,
    string? IconPath = null,
    int? IconIndex = null,
    string? AppUserModelId = null)
{
    public bool IsEmpty => Target is null && WorkingDirectory is null && IconPath is null && IconIndex is null && AppUserModelId is null;
}

/// <summary>The fixed names, keys and limits of nth's shortcut contract (PLAN.md 8a).</summary>
public static class ShellLinkKeys
{
    /// <summary>The desktop shortcut's exact file name: never "nth - Shortcut.lnk", never "nth (2).lnk".</summary>
    public const string LinkFileName = "nth.lnk";

    /// <summary>The shortcut's description (its tooltip).</summary>
    public const string Description = "nth";

    /// <summary>
    /// The AppUserModelID nth gives its own process (<c>ToastBuildIdentity.ForBuild(false, false).AppId</c>, ED-220 as renamed
    /// by OD-0135). Velopack's Start shortcut and nth's desktop shortcut must carry it, or the taskbar splits nth into two
    /// buttons and pins and toasts disagree. A constant here because the setup re-stamps Start without referencing the app.
    /// </summary>
    public const string NthAppUserModelId = "nth.App";

    /// <summary><c>SW_SHOWNORMAL</c>.</summary>
    public const int ShowNormal = 1;

    /// <summary>The marker nth writes into the shortcuts it makes (format version 1).</summary>
    public const string CreatedByValue = "nth-setup/1";

    /// <summary>Any marker version counts as nth's.</summary>
    public const string CreatedByPrefix = "nth-setup/";

    /// <summary>One writer at a time: create, repair and remove take this mutex (PLAN.md 8a).</summary>
    public const string WriterMutexName = @"Local\nth-desktop-shortcut";

    /// <summary>How long a writer waits for the mutex before it skips the step and records that it did.</summary>
    public static readonly TimeSpan WriterWait = TimeSpan.FromSeconds(2);

    /// <summary>A scan inside a Velopack hook stops after this many links (then records <c>scanCapped</c>).</summary>
    public const int ScanCapLinks = 2000;

    /// <summary>... or after this long, whichever comes first.</summary>
    public static readonly TimeSpan ScanCapTime = TimeSpan.FromSeconds(3);

    /// <summary><c>PKEY_AppUserModel_ID</c> and <c>PKEY_AppUserModel_ToastActivatorCLSID</c> share this format id (propkey.h).</summary>
    public static readonly Guid AppUserModelFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary><c>PKEY_AppUserModel_ID</c>'s property id.</summary>
    public const int AppUserModelIdPid = 5;

    /// <summary><c>PKEY_AppUserModel_ToastActivatorCLSID</c>'s property id.</summary>
    public const int ToastActivatorClsidPid = 26;

    /// <summary>nth's own property format (<c>PKEY_Nth_CreatedBy</c> lives under it). Fixed for good: changing it orphans every shortcut nth made.</summary>
    public static readonly Guid NthFormat = new("C514CBA9-8E86-4C38-94E1-87E24D26BDD3");

    /// <summary><c>PKEY_Nth_CreatedBy</c>'s property id under <see cref="NthFormat"/>.</summary>
    public const int CreatedByPid = 2;

    /// <summary><c>FILE_ATTRIBUTE_RECALL_ON_OPEN</c> (a cloud placeholder).</summary>
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary><c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c> (a Files On-Demand placeholder).</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, unit U0, ED-NEW-inst-u0-1). The only way nth's shortcut logic
/// (<c>DesktopShortcut</c>, unit U5) touches the shell, so it is tested on <c>FakeShellLinks</c> without Windows. The
/// production implementation is <c>src/Marrow.App/Port/ShellLinks.cs</c> (real COM through <c>[GeneratedComInterface]</c>,
/// on an STA thread it owns), constructed only in the app's and the setup's <c>Program.cs</c>. A call that fails throws an
/// exception whose <see cref="Exception.HResult"/> is the Windows error (a <c>COMException</c> for a COM call, an
/// <c>IOException</c> or <c>UnauthorizedAccessException</c> for a file), which the caller records as
/// <c>failed:&lt;HRESULT&gt;</c>. No member ever calls <c>IShellLink::Resolve</c> (it can search the disk and show UI).
/// This file is also compiled into src/Marrow.Setup, so it uses nothing else from Marrow.Core.
/// </summary>
public interface IShellLinks
{
    /// <summary>A known folder's path, without creating or verifying it.</summary>
    string KnownFolder(ShellKnownFolder folder);

    /// <summary>The path with its <c>%VARIABLE%</c>s expanded the way the shell expands a stored link path.</summary>
    string ExpandEnvironment(string path);

    /// <summary>Whether a file exists at this full (expanded) path.</summary>
    bool FileExists(string path);

    /// <summary>A folder's last write time, or null when it does not exist (the Settings scan re-reads only changed folders).</summary>
    DateTime? FolderWriteTimeUtc(string folder);

    /// <summary>
    /// The <c>.lnk</c> files in <paramref name="folder"/> (not its subfolders) whose names match
    /// <paramref name="searchPattern"/> (<c>*</c> and <c>?</c>), with their attributes and write times, read from the
    /// directory listing only: no link is opened, so a cloud placeholder is not downloaded. A missing folder lists nothing.
    /// </summary>
    IEnumerable<ShellLinkFile> EnumerateLinks(string folder, string searchPattern = "*.lnk");

    /// <summary>Opens a link and reads every field of <see cref="ShellLink"/>. Opening a cloud placeholder downloads it.</summary>
    ShellLink ReadLink(string path);

    /// <summary>Writes a whole link to <paramref name="path"/> (<c>IShellLinkW</c> plus <c>IPersistFile::Save</c>), replacing any file there.</summary>
    void WriteLink(string path, ShellLink link);

    /// <summary>Loads the link, sets only the change's non-null fields, commits and saves; every other field and property survives.</summary>
    void UpdateLink(string path, ShellLinkChange change);

    /// <summary>Deletes the link file. A file that is already gone is not an error.</summary>
    void DeleteLink(string path);

    /// <summary>
    /// Moves a link file to a new path as a plain file move, so every field and property moves with it (unit U5: 1.0.0's
    /// <c>Programs\nth\nth.lnk</c> becomes <c>Programs\nth.lnk</c>, VELOPACK_CHECK.md check 14). Throws when the source is
    /// missing or the destination already exists; it never replaces a file.
    /// </summary>
    void MoveLink(string from, string to);

    /// <summary>Deletes the folder only when it holds nothing at all; returns whether it was deleted. A missing folder returns false.</summary>
    bool DeleteEmptyFolder(string folder);

    /// <summary>Explorer's "Show desktop icons" is off (<c>SHGetSetSettings(SSF_HIDEICONS)</c>).</summary>
    bool DesktopIconsHidden();

    /// <summary>Tells Explorer a link appeared, went or changed, so the desktop updates at once.</summary>
    void Notify(ShellChange change, string path);

    /// <summary>
    /// Takes the one-writer lock (<see cref="ShellLinkKeys.WriterMutexName"/>), waiting at most <paramref name="wait"/>.
    /// Returns the lock to dispose, or null when another writer kept it for the whole wait.
    /// </summary>
    IDisposable? TryEnterWriter(TimeSpan wait);
}
