namespace Marrow.Core.Support;

/// <summary>What one <see cref="DesktopShortcutProbe.Probe"/> found.</summary>
/// <param name="Present">A shortcut nth made (its marker) is on the desktop, as far as the probe could look.</param>
/// <param name="Capped">The probe stopped reading at its 2,000-link / 3 s cap; links it did not reach are not counted yet.</param>
/// <param name="Reads">Links opened this time (links answered from the cache cost no read).</param>
/// <param name="Failed">The shell refused a listing; <see cref="Present"/> is what was known before and the caller should keep its last answer.</param>
public sealed record DesktopShortcutProbeResult(bool Present, bool Capped, int Reads, bool Failed);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8a "nth's Settings", unit U8, ED-NEW-inst-u8-2). The scan behind
/// Settings > General > "On this PC" > "Desktop shortcut": is there a shortcut nth made on the user's desktop? It is built to
/// be asked often (the page opening, the window being activated) without costing anything while nothing changed:
/// <list type="bullet">
/// <item><b>Per file, by last-write time:</b> a link is opened only when its path is new or its write time differs from the
/// one cached; a cloud placeholder is never opened, counted or cached (PLAN.md 8a, OneDrive).</item>
/// <item><b>Per folder, by write time:</b> the folders are the user's desktop and, when it is redirected and the profile's own
/// <c>Desktop</c> still exists, that one too. A folder whose write time is unchanged is not listed again, except for one
/// cheap <c>nth*.lnk</c> listing that notices a link nth made being replaced in place (a write that does not touch the
/// folder's time). A folder whose time changed is listed in full, but still opens only the new or changed links.</item>
/// <item><b>Capped:</b> at most 2,000 opens or 3 s per probe (<c>nth*.lnk</c> first); a capped folder is not marked up to date,
/// so the next probe carries on with what the cache already holds.</item>
/// </list>
/// It reaches the shell only through <see cref="IShellLinks"/> and never writes. It is safe to call from any thread (calls
/// are serialized); the Settings model calls it on a background thread, never the UI thread.
/// </summary>
public sealed class DesktopShortcutProbe
{
    private readonly Lock gate = new();
    private readonly IShellLinks shell;
    private readonly DesktopShortcut desktop;
    private readonly TimeProvider time;
    private readonly Dictionary<string, FolderCache> folders = new(StringComparer.OrdinalIgnoreCase);

    public DesktopShortcutProbe(IShellLinks shell, string root, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        this.shell = shell;
        this.time = time ?? TimeProvider.System;
        desktop = new DesktopShortcut(shell, root, this.time);
    }

    /// <summary>Looks at the desktop folders (see the type's summary) and says whether a shortcut nth made is there.</summary>
    public DesktopShortcutProbeResult Probe()
    {
        lock (gate)
        {
            var started = time.GetTimestamp();
            var reads = 0;
            var capped = false;
            var failed = false;
            IReadOnlyList<string> desktopFolders;
            try { desktopFolders = desktop.DesktopFolders(); }
            catch (Exception error) when (DesktopShortcut.IsShellFailure(error)) { return new DesktopShortcutProbeResult(AnyMade(), Capped: false, Reads: 0, Failed: true); }
            foreach (var gone in folders.Keys.Where(f => !desktopFolders.Contains(f, StringComparer.OrdinalIgnoreCase)).ToArray()) folders.Remove(gone);
            foreach (var folder in desktopFolders)
            {
                try
                {
                    if (shell.FolderWriteTimeUtc(folder) is not { } folderTime) { folders.Remove(folder); continue; }
                    if (!folders.TryGetValue(folder, out var cache)) folders[folder] = cache = new FolderCache();
                    var nth = shell.EnumerateLinks(folder, "nth*.lnk").ToList();
                    if (cache.FolderTime == folderTime && cache.NthListing.SequenceEqual(Signature(nth))) continue;
                    ScanFolder(folder, folderTime, nth, cache, started, ref reads, ref capped);
                }
                catch (Exception error) when (DesktopShortcut.IsShellFailure(error))
                {
                    failed = true;
                    if (folders.TryGetValue(folder, out var broken)) broken.FolderTime = null; // look again next time
                }
            }
            return new DesktopShortcutProbeResult(AnyMade(), capped, reads, failed);
        }
    }

    /// <summary>
    /// Forgets every folder's write time (the per-file cache stays), so the next probe lists the folders again. Called after
    /// nth's own create or remove, whose result must show at once whatever the folder's clock says.
    /// </summary>
    public void Invalidate()
    {
        lock (gate)
            foreach (var cache in folders.Values) cache.FolderTime = null;
    }

    private bool AnyMade() => folders.Values.Any(f => f.Links.Values.Any(l => l.Made));

    /// <summary>The <c>nth*.lnk</c> files as the directory lists them (placeholders left out) with their write times, in path order.</summary>
    private static List<(string Path, DateTime WriteTimeUtc)> Signature(List<ShellLinkFile> nth) =>
        [.. nth.Where(file => !file.IsCloudPlaceholder)
            .Select(file => (file.Path, file.LastWriteTimeUtc))
            .OrderBy(pair => pair.Path, StringComparer.OrdinalIgnoreCase)];

    private void ScanFolder(string folder, DateTime folderTime, List<ShellLinkFile> nth, FolderCache cache, long started, ref int reads, ref bool capped)
    {
        var next = new Dictionary<string, CachedLink>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderCapped = false;
        var unreadable = false;
        foreach (var file in nth.Concat(shell.EnumerateLinks(folder, "*.lnk"))) // nth*.lnk first, so a cap reaches the likeliest links first
        {
            if (file.IsCloudPlaceholder || !seen.Add(file.Path)) continue;
            if (cache.Links.TryGetValue(file.Path, out var known) && known.WriteTimeUtc == file.LastWriteTimeUtc)
            {
                next[file.Path] = known; // unchanged since it was last opened: no read
                continue;
            }
            if (reads >= ShellLinkKeys.ScanCapLinks || time.GetElapsedTime(started) >= ShellLinkKeys.ScanCapTime)
            {
                folderCapped = true; // not opened yet; the next probe carries on from the cache
                continue;
            }
            reads++;
            ShellLink link;
            try { link = shell.ReadLink(file.Path); }
            catch (Exception error) when (DesktopShortcut.IsShellFailure(error))
            {
                // C4b U8-2, ED-NEW-inst-cx-11: a still-listed but unreadable link has not proved its old marker absent.
                // Keep the old timestamp too, so the changed file is retried rather than cached as successfully read.
                if (cache.Links.TryGetValue(file.Path, out var previous)) next[file.Path] = previous;
                unreadable = true; // skipped now; the folder is not marked up to date, so the next probe tries it again
                continue;
            }
            next[file.Path] = new CachedLink(file.LastWriteTimeUtc, link.IsMadeByNth);
        }
        cache.Links = next;
        cache.NthListing = Signature(nth);
        cache.FolderTime = folderCapped || unreadable ? null : folderTime;
        capped |= folderCapped;
    }

    private sealed class FolderCache
    {
        /// <summary>The folder's write time when it was last listed in full; null when it must be listed again.</summary>
        public DateTime? FolderTime { get; set; }

        public List<(string Path, DateTime WriteTimeUtc)> NthListing { get; set; } = [];

        public Dictionary<string, CachedLink> Links { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct CachedLink(DateTime WriteTimeUtc, bool Made);
}
