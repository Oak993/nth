using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Marrow.Core.Support;

namespace Marrow.Testing;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, unit U0, ED-NEW-inst-u0-1). An in-memory <see cref="IShellLinks"/>: link records
/// instead of files, on a fake user's machine whose profile path is long and non-ASCII and whose desktop is redirected to
/// OneDrive. No test that uses it touches the real Desktop or Start menu. It keeps attributes (cloud placeholders
/// included) and write times, opens a placeholder only when <see cref="ReadLink"/> is called (and then "downloads" it,
/// recorded in <see cref="Hydrated"/>), can fail any call with a chosen HRESULT or delay it, and counts reads, writes,
/// deletes and notifications. Delays go through the wait the test gives (for example a ManualTimeProvider's Advance), so a
/// budget test runs in no real time. This file is also compiled into tests/Marrow.Setup.Tests against the setup's linked
/// copy of the contract, so it uses nothing from Marrow.Core but <c>IShellLinks.cs</c>.
/// </summary>
public sealed partial class FakeShellLinks : IShellLinks
{
    /// <summary>Every member of <see cref="IShellLinks"/>, for <see cref="Fail"/>, <see cref="Delay"/> and <see cref="CallCount"/>.</summary>
    public enum Call
    {
        KnownFolder,
        ExpandEnvironment,
        FileExists,
        FolderWriteTime,
        EnumerateLinks,
        ReadLink,
        WriteLink,
        UpdateLink,
        DeleteLink,
        DesktopIconsHidden,
        Notify,
        TryEnterWriter,
    }

    public const string UserName = "Ana-María Østergård-Fernández";

    public const string Profile = @"C:\Users\" + UserName;

    /// <summary>FOLDERID_Desktop on this fake machine: redirected to OneDrive.</summary>
    public const string OneDriveDesktop = Profile + @"\OneDrive\Desktop";

    /// <summary>The unredirected desktop folder, which still exists beside the OneDrive one.</summary>
    public const string ProfileDesktop = Profile + @"\Desktop";

    public const string LocalAppData = Profile + @"\AppData\Local";

    public const string RoamingAppData = Profile + @"\AppData\Roaming";

    public const string Programs = RoamingAppData + @"\Microsoft\Windows\Start Menu\Programs";

    /// <summary>nth's default install root (<c>%LOCALAPPDATA%\Programs\nth</c>).</summary>
    public const string DefaultRoot = LocalAppData + @"\Programs\nth";

    /// <summary>Velopack's Start shortcut (<c>--shortcuts StartMenuRoot</c>).</summary>
    public const string StartLink = Programs + @"\" + ShellLinkKeys.LinkFileName;

    /// <summary>Where nth's desktop shortcut goes on this fake machine.</summary>
    public const string DesktopLink = OneDriveDesktop + @"\" + ShellLinkKeys.LinkFileName;

    private static readonly DateTime Start = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> links = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Call, int> calls = [];
    private readonly Dictionary<Call, TimeSpan> delays = [];
    private readonly List<Fault> faults = [];
    private readonly List<(ShellChange Change, string Path)> notifications = [];
    private readonly List<string> readPaths = [];
    private readonly List<string> hydrated = [];
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly Action<TimeSpan> wait;
    private DateTime now = Start;
    private string desktop = OneDriveDesktop;
    private bool iconsHidden;
    private int reads, writes, deletes, writerTimeouts;

    /// <param name="wait">How a delayed call waits; the default sleeps the calling thread.</param>
    public FakeShellLinks(Action<TimeSpan>? wait = null)
    {
        this.wait = wait ?? Thread.Sleep;
        foreach (var folder in new[] { Profile, OneDriveDesktop, ProfileDesktop, LocalAppData, RoamingAppData, Programs })
            folders[folder] = Start;
        Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOCALAPPDATA"] = LocalAppData,
            ["APPDATA"] = RoamingAppData,
            ["USERPROFILE"] = Profile,
            ["SystemRoot"] = @"C:\Windows",
            ["ProgramFiles"] = @"C:\Program Files",
        };
    }

    /// <summary>The fake user's environment, as <see cref="ExpandEnvironment"/> sees it.</summary>
    public Dictionary<string, string> Environment { get; }

    /// <summary>FOLDERID_Desktop. Set it to <see cref="ProfileDesktop"/> for a machine without OneDrive.</summary>
    public string Desktop
    {
        get { lock (gate) return desktop; }
        set { lock (gate) { desktop = value; folders.TryAdd(value, now); } }
    }

    /// <summary>Explorer's "Show desktop icons" is off.</summary>
    public bool IconsHidden
    {
        get { lock (gate) return iconsHidden; }
        set { lock (gate) iconsHidden = value; }
    }

    /// <summary>Links opened by <see cref="ReadLink"/>.</summary>
    public int Reads { get { lock (gate) return reads; } }

    /// <summary>Links written by <see cref="WriteLink"/> or changed by <see cref="UpdateLink"/>.</summary>
    public int Writes { get { lock (gate) return writes; } }

    /// <summary>Links deleted by <see cref="DeleteLink"/>.</summary>
    public int Deletes { get { lock (gate) return deletes; } }

    /// <summary>Every write and delete: zero means the shell's state was not touched.</summary>
    public int Changes { get { lock (gate) return writes + deletes; } }

    /// <summary>Writer-lock waits that ran out.</summary>
    public int WriterTimeouts { get { lock (gate) return writerTimeouts; } }

    public IReadOnlyList<(ShellChange Change, string Path)> Notifications { get { lock (gate) return [.. notifications]; } }

    /// <summary>Every path <see cref="ReadLink"/> opened, in order (a placeholder in this list was downloaded).</summary>
    public IReadOnlyList<string> ReadPaths { get { lock (gate) return [.. readPaths]; } }

    /// <summary>Cloud placeholders that a read downloaded.</summary>
    public IReadOnlyList<string> Hydrated { get { lock (gate) return [.. hydrated]; } }

    /// <summary>How many times a member was called, failed calls included.</summary>
    public int CallCount(Call call)
    {
        lock (gate) return calls.GetValueOrDefault(call);
    }

    // ---- setting the machine up (none of these count as calls, reads or writes) ----

    /// <summary>Places a link as if it had always been there; its folder is created if needed.</summary>
    public void Seed(string path, ShellLink link, FileAttributes attributes = FileAttributes.Archive)
    {
        ArgumentNullException.ThrowIfNull(link);
        lock (gate)
        {
            folders.TryAdd(Folder(path), now);
            links[path] = new Entry(link, attributes, Tick());
        }
    }

    /// <summary>Places a OneDrive Files On-Demand placeholder: listed with its recall attribute, downloaded only when read.</summary>
    public void SeedPlaceholder(string path, ShellLink link) =>
        Seed(path, link, FileAttributes.Archive | FileAttributes.Offline | ShellLinkKeys.RecallOnDataAccess);

    /// <summary>Places <paramref name="count"/> unrelated links named <c>link-00001.lnk</c> onwards in <paramref name="folder"/>.</summary>
    public void SeedMany(string folder, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            var name = "link-" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
            Seed(Path.Combine(folder, name + ".lnk"), new ShellLink(@"C:\Apps\" + name + ".exe", Description: name));
        }
    }

    /// <summary>Makes a non-link file exist (a launch target, a stub, Update.exe).</summary>
    public void AddFile(string path)
    {
        lock (gate)
        {
            folders.TryAdd(Folder(path), now);
            files.Add(path);
        }
    }

    public void RemoveFile(string path)
    {
        lock (gate) files.Remove(path);
    }

    public void AddFolder(string path)
    {
        lock (gate) folders.TryAdd(Norm(path), now);
    }

    /// <summary>Renames a link the way a user would in Explorer; every field and property moves with it.</summary>
    public void Rename(string from, string to)
    {
        lock (gate)
        {
            if (!links.Remove(from, out var entry)) throw new FileNotFoundException("No link to rename.", from);
            links[to] = entry;
            folders[Folder(from)] = Tick();
            folders[Folder(to)] = now;
        }
    }

    public void SetAttributes(string path, FileAttributes attributes)
    {
        lock (gate)
        {
            if (!links.TryGetValue(path, out var entry)) throw new FileNotFoundException("No link.", path);
            entry.Attributes = attributes;
        }
    }

    /// <summary>A link's record without opening it (no read is counted and a placeholder stays a placeholder).</summary>
    public ShellLink? Peek(string path)
    {
        lock (gate) return links.TryGetValue(path, out var entry) ? entry.Link : null;
    }

    public FileAttributes? AttributesOf(string path)
    {
        lock (gate) return links.TryGetValue(path, out var entry) ? entry.Attributes : null;
    }

    /// <summary>The full paths of the links in a folder, by name.</summary>
    public IReadOnlyList<string> LinksIn(string folder)
    {
        lock (gate)
            return [.. links.Keys.Where(p => string.Equals(Folder(p), Norm(folder), StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
    }

    // ---- faults and delays ----

    /// <summary>
    /// Makes <paramref name="call"/> throw a <see cref="COMException"/> with <paramref name="hresult"/>, for the next
    /// <paramref name="times"/> calls (every call by default), only for <paramref name="path"/> when one is given.
    /// </summary>
    public void Fail(Call call, int hresult, int times = int.MaxValue, string? path = null)
    {
        lock (gate) faults.Add(new Fault(call, hresult, path) { Remaining = times });
    }

    /// <summary>Makes every <paramref name="call"/> wait <paramref name="delay"/> first (through the wait given to the constructor).</summary>
    public void Delay(Call call, TimeSpan delay)
    {
        lock (gate) delays[call] = delay;
    }

    public void ClearFaults()
    {
        lock (gate)
        {
            faults.Clear();
            delays.Clear();
        }
    }

    public void ResetCounters()
    {
        lock (gate)
        {
            calls.Clear();
            notifications.Clear();
            readPaths.Clear();
            hydrated.Clear();
            reads = writes = deletes = writerTimeouts = 0;
        }
    }

    // ---- IShellLinks ----

    public string KnownFolder(ShellKnownFolder folder)
    {
        Enter(Call.KnownFolder, null);
        lock (gate)
        {
            return folder switch
            {
                ShellKnownFolder.Desktop => desktop,
                ShellKnownFolder.Programs => Programs,
                ShellKnownFolder.LocalAppData => LocalAppData,
                ShellKnownFolder.RoamingAppData => RoamingAppData,
                ShellKnownFolder.Profile => Profile,
            };
        }
    }

    public string ExpandEnvironment(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Enter(Call.ExpandEnvironment, path);
        lock (gate)
            return VariablePattern().Replace(path, m => Environment.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    public bool FileExists(string path)
    {
        Enter(Call.FileExists, path);
        lock (gate) return files.Contains(path) || links.ContainsKey(path);
    }

    public DateTime? FolderWriteTimeUtc(string folder)
    {
        Enter(Call.FolderWriteTime, folder);
        lock (gate) return folders.TryGetValue(Norm(folder), out var time) ? time : null;
    }

    public IEnumerable<ShellLinkFile> EnumerateLinks(string folder, string searchPattern = "*.lnk")
    {
        Enter(Call.EnumerateLinks, folder);
        var pattern = new Regex("\\A" + Regex.Escape(searchPattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "\\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        lock (gate)
        {
            return [.. links
                .Where(p => string.Equals(Folder(p.Key), Norm(folder), StringComparison.OrdinalIgnoreCase)
                            && p.Key.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                            && pattern.IsMatch(Path.GetFileName(p.Key)))
                .OrderBy(p => Path.GetFileName(p.Key), StringComparer.OrdinalIgnoreCase)
                .Select(p => new ShellLinkFile(p.Key, p.Value.Attributes, p.Value.WriteTime))];
        }
    }

    public ShellLink ReadLink(string path)
    {
        Enter(Call.ReadLink, path);
        lock (gate)
        {
            if (!links.TryGetValue(path, out var entry)) throw new FileNotFoundException("No link.", path);
            if ((entry.Attributes & (ShellLinkKeys.RecallOnDataAccess | ShellLinkKeys.RecallOnOpen)) != 0)
            {
                entry.Attributes &= ~(ShellLinkKeys.RecallOnDataAccess | ShellLinkKeys.RecallOnOpen | FileAttributes.Offline);
                hydrated.Add(path);
            }
            reads++;
            readPaths.Add(path);
            return entry.Link;
        }
    }

    public void WriteLink(string path, ShellLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        Enter(Call.WriteLink, path);
        lock (gate)
        {
            var folder = Folder(path);
            if (!folders.ContainsKey(folder)) throw new DirectoryNotFoundException("No folder: " + folder);
            if (links.TryGetValue(path, out var old) && (old.Attributes & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException("Read-only link: " + path);
            links[path] = new Entry(link, FileAttributes.Archive, Tick());
            folders[folder] = now;
            writes++;
        }
    }

    public void UpdateLink(string path, ShellLinkChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Enter(Call.UpdateLink, path);
        lock (gate)
        {
            if (!links.TryGetValue(path, out var entry)) throw new FileNotFoundException("No link.", path);
            if ((entry.Attributes & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("Read-only link: " + path);
            if (change.IsEmpty) return;
            entry.Link = entry.Link with
            {
                Target = change.Target ?? entry.Link.Target,
                WorkingDirectory = change.WorkingDirectory ?? entry.Link.WorkingDirectory,
                IconPath = change.IconPath ?? entry.Link.IconPath,
                IconIndex = change.IconIndex ?? entry.Link.IconIndex,
                AppUserModelId = change.AppUserModelId ?? entry.Link.AppUserModelId,
            };
            entry.WriteTime = Tick();
            writes++;
        }
    }

    public void DeleteLink(string path)
    {
        Enter(Call.DeleteLink, path);
        lock (gate)
        {
            if (!links.Remove(path)) return;
            folders[Folder(path)] = Tick();
            deletes++;
        }
    }

    public bool DesktopIconsHidden()
    {
        Enter(Call.DesktopIconsHidden, null);
        lock (gate) return iconsHidden;
    }

    public void Notify(ShellChange change, string path)
    {
        Enter(Call.Notify, path);
        lock (gate) notifications.Add((change, path));
    }

    public IDisposable? TryEnterWriter(TimeSpan wait)
    {
        Enter(Call.TryEnterWriter, null);
        if (writer.Wait(wait)) return new Releaser(writer);
        lock (gate) writerTimeouts++;
        return null;
    }

    private void Enter(Call call, string? path)
    {
        TimeSpan delay;
        lock (gate)
        {
            calls[call] = calls.GetValueOrDefault(call) + 1;
            delay = delays.GetValueOrDefault(call);
        }
        if (delay > TimeSpan.Zero) wait(delay);
        lock (gate)
        {
            var fault = faults.FirstOrDefault(f => f.Call == call && f.Remaining > 0
                && (f.Path is null || string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)));
            if (fault is null) return;
            fault.Remaining--;
            throw new COMException($"FakeShellLinks.{call} failed for {path ?? "(no path)"}.", fault.HResult);
        }
    }

    private DateTime Tick() => now = now.AddSeconds(1);

    private static string Norm(string folder) => Path.TrimEndingDirectorySeparator(folder);

    private static string Folder(string path) => Path.GetDirectoryName(path) ?? throw new ArgumentException("A path with no folder.", nameof(path));

    [GeneratedRegex("%([^%]+)%")] private static partial Regex VariablePattern();

    private sealed class Entry(ShellLink link, FileAttributes attributes, DateTime writeTime)
    {
        public ShellLink Link { get; set; } = link;

        public FileAttributes Attributes { get; set; } = attributes;

        public DateTime WriteTime { get; set; } = writeTime;
    }

    private sealed record Fault(Call Call, int HResult, string? Path)
    {
        public int Remaining { get; set; }
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) semaphore.Release();
        }
    }
}
