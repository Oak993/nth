// PORT-ONLY: SK-13 / DL-16, skills-ONDEMAND-PLAN.md 3.8 "Uninstall" and skills-PLAN.md 2.1, WU-18 r3 (a). There is no Swift twin.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Uninstall;

/// <summary>
/// The body of Velopack's before-uninstall hook (<c>OnBeforeUninstallFastCallback</c>; its limit is 30 seconds). It unlocks everything under <c>addons\</c> and <c>skills\</c> first
/// (the PLAN 2.1 deny entry and read-only files, entry by entry, so one entry that will not unlock does not stop the rest), and only then deletes <c>addons\</c> whole and everything
/// in <c>skills\</c> but <c>enabled.json</c> (the store, the views, the condemned list, the reset marker and the generation records). A folder a running process holds is left
/// unlocked, so a plain delete removes it once the handle closes. It never touches a provider's home or any other Marrow data file. ENGINEERING DECISIONS (ED-NEW-f-sk-13):
/// (1) the budget is 20 seconds, so the hook ends well inside Velopack's 30; after it no new delete starts, and everything is unlocked by then; (2) a failed delete falls back to the
/// folder's children one by one, so a single held file costs only its own ancestors; (3) <c>skills\</c> children other than <c>enabled.json</c> go too, because each describes a
/// tree this hook deletes; the switches in <c>enabled.json</c> stay, as the plan says.
/// </summary>
public static class SkillsUninstall
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(20);

    public static UninstallReport BeforeUninstall(string dataRoot, IStoreProtection? protection = null, IStoreDeletion? deletion = null, TimeSpan? budget = null, TimeProvider? clock = null)
    {
        var paths = new StorePaths(dataRoot);
        var acl = protection ?? new WindowsStoreProtection();
        var remover = deletion ?? StoreDeletion.Instance;
        var time = clock ?? TimeProvider.System;
        var deadline = time.GetUtcNow() + (budget ?? DefaultBudget);
        var left = new List<string>();
        var outOfTime = false;

        // 1. Everything unlocked before the first delete.
        var entries = new List<string>();
        foreach (var root in new[] { paths.Addons, paths.Skills }) Walk(root, entries);
        foreach (var entry in entries)
            Try(() => { if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0) acl.Unlock(entry); });
        foreach (var entry in entries)
            Try(() =>
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            });

        // 2. Delete, as much as can be deleted inside the budget.
        Remove(paths.Addons);
        if (Directory.Exists(paths.Skills))
        {
            string[] children = [];
            Try(() => children = Directory.GetFileSystemEntries(paths.Skills));
            foreach (var child in children.Where(c => !StorePaths.Comparer.Equals(c, paths.EnabledFile)).Order(StringComparer.Ordinal)) Remove(child);
        }
        return new UninstallReport(left, outOfTime);

        void Remove(string path)
        {
            if (!Path.Exists(path)) return;
            if (time.GetUtcNow() > deadline)
            {
                outOfTime = true;
                left.Add(path);
                return;
            }
            if (TryDelete(path)) return;
            var before = left.Count;
            if (IsPlainDirectory(path))
            {
                string[] children = [];
                if (!Try(() => children = Directory.GetFileSystemEntries(path))) { left.Add(path); return; }
                foreach (var child in children) Remove(child);
                if (!Path.Exists(path) || TryDelete(path)) return;
            }
            if (left.Count == before) left.Add(path);
        }

        bool TryDelete(string path)
        {
            try
            {
                remover.Delete(path);
                return !Path.Exists(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private static bool IsPlainDirectory(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Every entry under (and including) a folder, never following a link; an unreadable folder contributes itself only.</summary>
    private static void Walk(string folder, List<string> into)
    {
        if (!Path.Exists(folder)) return;
        into.Add(folder);
        if (!IsPlainDirectory(folder)) return;
        string[] children = [];
        if (!Try(() => children = Directory.GetFileSystemEntries(folder))) return;
        foreach (var child in children)
        {
            if (IsPlainDirectory(child)) Walk(child, into);
            else into.Add(child);
        }
    }
}
