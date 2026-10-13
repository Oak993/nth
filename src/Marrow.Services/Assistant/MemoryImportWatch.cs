// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.4 "watches those folders for new notes"; unit AS-12; ED-NEW-dextra-as12-15). No Swift twin.
// Watches the folders the import reads. A change to a markdown file there starts a short wait (changes come in bursts); when
// it ends, the import runs once, off the UI thread, in Watch mode, so every note that is new waits for the owner's Keep. The clock
// is the injected TimeProvider, and Notify is the seam a test drives; the file system watchers only call it.
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Accounts;

namespace Marrow.Services.Assistant;

/// <summary>The sources of an import, from the accounts multi-account knows (8.4: "from each account's config directory").</summary>
public static class MemoryImportSources
{
    /// <summary>
    /// A source for each enabled Claude or Codex account: its own config folder (<see cref="AccountEnvironment.Folder"/>), or for the
    /// migrated first account (which keeps the original CLI environment) the home folder's <c>.claude</c> or <c>.codex</c>. Accounts
    /// of other providers have no notes to bring in.
    /// </summary>
    public static IReadOnlyList<MemoryImportSource> FromAccounts(IEnumerable<Account> accounts, AccountEnvironment environment, string homeDirectory, IReadOnlyList<string>? projectFolders = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(environment);
        var sources = new List<MemoryImportSource>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var account in accounts.Where(a => a.Enabled && a.Service.Id is "claude" or "codex").OrderBy(a => a.Priority))
        {
            counts[account.Service.Id] = counts.GetValueOrDefault(account.Service.Id) + 1;
            var folder = account.UsesLegacyCredentials
                ? Path.Combine(homeDirectory, account.Service.Id == "claude" ? ".claude" : ".codex")
                : environment.Folder(account);
            var label = string.IsNullOrWhiteSpace(account.CustomName) ? $"{(account.Service.Id == "claude" ? "Claude" : "Codex")} {counts[account.Service.Id]}" : account.CustomName.Trim();
            sources.Add(new MemoryImportSource(account.Id.ToString("N"), label, account.Service, folder) { ProjectFolders = projectFolders ?? [] });
        }
        return sources;
    }
}

public sealed class MemoryImportWatch : IDisposable
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(2);

    private readonly MemoryImport import;
    private readonly Func<IReadOnlyList<MemoryImportSource>> sources;
    private readonly TimeProvider clock;
    private readonly TimeSpan wait;
    private readonly Func<Action, Task> run;
    private readonly Func<Task<IReadOnlyList<MemoryImportSource>>>? refreshSources;
    private readonly object gate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private ITimer? timer;
    private bool running;
    private bool again;
    private bool disposed;

    /// <param name="import">The import to run.</param>
    /// <param name="sources">The sources now (read at every run, so an account added later is seen).</param>
    /// <param name="time">The clock the wait runs on.</param>
    /// <param name="wait">How long after the last change the import runs.</param>
    /// <param name="runner">Runs the import away from the caller; the default is the thread pool.</param>
    public MemoryImportWatch(MemoryImport import, Func<IReadOnlyList<MemoryImportSource>> sources, TimeProvider? time = null, TimeSpan? wait = null, Func<Action, Task>? runner = null,
        Func<Task<IReadOnlyList<MemoryImportSource>>>? refreshSources = null)
    {
        this.import = import ?? throw new ArgumentNullException(nameof(import));
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        clock = time ?? TimeProvider.System;
        this.wait = wait ?? DefaultWait;
        run = runner ?? (work => Task.Run(work));
        this.refreshSources = refreshSources;
    }

    /// <summary>An import the watch started finished.</summary>
    public event Action<ImportReport>? Imported;

    /// <summary>An import the watch started threw: the line for the board. The watch goes on.</summary>
    public event Action<string>? Faulted;

    /// <summary>Watches the folders that exist now. Call it again to follow an account that was added.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (disposed) return;
            StopWatchers();
            foreach (var folder in sources().SelectMany(Folders).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    var watcher = new FileSystemWatcher(folder, "*.md") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
                    watcher.Created += (_, e) => Notify(e.FullPath);
                    watcher.Changed += (_, e) => Notify(e.FullPath);
                    watcher.Renamed += (_, e) => Notify(e.FullPath);
                    watcher.Error += (_, e) => Faulted?.Invoke("The notes folder could not be watched: " + e.GetException().Message);
                    watchers.Add(watcher);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
                {
                    Faulted?.Invoke($"The folder {folder} could not be watched: {error.Message}");
                }
            }
        }
    }

    private static IEnumerable<string> Folders(MemoryImportSource source) =>
        new[] { source.ConfigDirectory }.Concat(source.ProjectFolders).Where(f => !string.IsNullOrWhiteSpace(f));

    /// <summary>A markdown file changed: the import runs once the changes stop. A file that is not markdown is ignored.</summary>
    public void Notify(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)) return;
        lock (gate)
        {
            if (disposed) return;
            if (timer is null) timer = clock.CreateTimer(_ => Fire(), null, wait, Timeout.InfiniteTimeSpan);
            else timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (gate)
        {
            if (disposed) return;
            timer?.Dispose();
            timer = null;
            if (running) { again = true; return; }
            running = true;
        }
        if (refreshSources is null) _ = run(RunImport);
        else _ = Task.Run(RunImportAsync);
    }

    // ED-NEW-dextra-open-6: capture live account/project metadata on its actor, then import on the worker.
    private async Task RunImportAsync()
    {
        while (true)
        {
            try
            {
                var current = await refreshSources!().ConfigureAwait(false);
                lock (gate) if (disposed) { running = false; again = false; return; }
                Imported?.Invoke(import.Import(current, ImportMode.Watch));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Faulted?.Invoke("Notes could not be imported: " + error.Message);
            }
            lock (gate)
            {
                if (!again || disposed) { running = false; again = false; return; }
                again = false;
            }
        }
    }

    private void RunImport()
    {
        while (true)
        {
            try
            {
                Imported?.Invoke(import.Import(sources(), ImportMode.Watch));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Faulted?.Invoke("Notes could not be imported: " + error.Message);
            }
            lock (gate)
            {
                if (!again || disposed) { running = false; again = false; return; }
                again = false;
            }
        }
    }

    private void StopWatchers()
    {
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer?.Dispose();
            timer = null;
            StopWatchers();
        }
    }
}
