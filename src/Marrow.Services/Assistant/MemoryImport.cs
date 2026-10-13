// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.4 "Import (local part only)"; unit AS-12; ED-NEW-dextra-as12-14). No Swift twin.
// Brings in what the owner's other agents already know: Claude Code's CLAUDE.md files and ~/.claude/projects/*/memory folders, and
// Codex's AGENTS.md and memory files, from each account's config directory (which multi-account already knows). Notes are
// de-duplicated across accounts (the same words are one note with every account as a source), keep the account they came from,
// and are checked like any other note (no secrets, nothing about third parties). The files are data: a line that reads like an
// instruction to an AI waits in quarantine, and so does every note that turns up in a watched folder after the import.
using System.Security.Cryptography;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>An agent account whose files can be imported: its config directory (CLAUDE_CONFIG_DIR or CODEX_HOME) and the project folders to look in.</summary>
public sealed record MemoryImportSource(string AccountId, string Label, ProviderKind Provider, string ConfigDirectory)
{
    /// <summary>Project folders whose CLAUDE.md (Claude) or AGENTS.md (Codex) is read too.</summary>
    public IReadOnlyList<string> ProjectFolders { get; init; } = [];

    /// <summary>What the import remembers a source by.</summary>
    public string Key => Provider.Id + ":" + AccountId;
}

/// <summary>Who asked for the import: the owner (notes are used), or the watch (new notes wait for the owner's Keep).</summary>
public enum ImportMode { Owner, Watch }

/// <summary>What an import did. <see cref="Problems"/> are lines for the Memory view.</summary>
public sealed record ImportReport(int FilesRead, int FilesUnchanged, int Kept, int Quarantined, int Merged, int Refused, int Failed, IReadOnlyList<string> Problems)
{
    public static ImportReport Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, []);

    public bool Changed => Kept + Quarantined + Merged > 0;
}

public sealed class MemoryImport
{
    public const long MaxFileBytes = 256 * 1024;
    public const int MaxFilesPerSource = 200;
    public const int MaxNotesPerRun = 1000;
    public const int MaxProjectDirectories = 200;

    private readonly MemoryStore store;
    private readonly Func<string?> mainAssistantId;

    /// <param name="store">The memory the notes go to.</param>
    /// <param name="mainAssistantId">Dextra's id: procedures and project notes go to its own memory; null puts everything in the shared set.</param>
    public MemoryImport(MemoryStore store, Func<string?>? mainAssistantId = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.mainAssistantId = mainAssistantId ?? (() => null);
    }

    private sealed record ImportFile(string Path, string Root, ImportFileKind Kind);

    // ---- finding the files ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The files an import reads for a source: CLAUDE.md and the memory folders of a Claude config directory, AGENTS.md and the memory
    /// files of a Codex one, and the project folders' own CLAUDE.md or AGENTS.md. Every path is resolved and kept strictly under the
    /// folder it was found in, links and junctions are not followed, and a source that does not exist has none.
    /// </summary>
    public static IReadOnlyList<string> FilesOf(MemoryImportSource source) => [.. Discover(source, []).Select(f => f.Path)];

    private static List<ImportFile> Discover(MemoryImportSource source, List<string> problems)
    {
        var skipped = 0;
        var files = new List<ImportFile>();
        void Add(string root, string path, ImportFileKind kind)
        {
            if (files.Count >= MaxFilesPerSource) { skipped++; return; }
            if (Resolve(root, path) is { } safe && !files.Any(f => string.Equals(f.Path, safe, StringComparison.OrdinalIgnoreCase))) files.Add(new ImportFile(safe, root, kind));
        }
        if (source.Provider.Id is not ("claude" or "codex"))
        {
            problems.Add($"{source.Label}: notes cannot be imported from this kind of account.");
            return files;
        }
        var config = SafeDirectory(source.ConfigDirectory);
        if (config is not null)
        {
            switch (source.Provider.Id)
            {
                case "claude":
                    Add(config, Path.Combine(config, "CLAUDE.md"), ImportFileKind.Instructions);
                    var projects = SafeDirectory(Path.Combine(config, "projects"), config);
                    if (projects is not null)
                    {
                        foreach (var project in Children(projects, MaxProjectDirectories))
                        {
                            var memory = SafeDirectory(Path.Combine(project, "memory"), projects);
                            if (memory is null) continue;
                            foreach (var path in MarkdownFiles(memory)) Add(config, path, ImportFileKind.MemoryFolder);
                        }
                    }
                    break;
                case "codex":
                    Add(config, Path.Combine(config, "AGENTS.md"), ImportFileKind.Instructions);
                    Add(config, Path.Combine(config, "AGENTS.override.md"), ImportFileKind.Instructions);
                    var memories = SafeDirectory(Path.Combine(config, "memories"), config);
                    if (memories is not null)
                    {
                        foreach (var path in MarkdownFiles(memories)) Add(config, path, ImportFileKind.MemoryFolder);
                        foreach (var child in Children(memories, 20))
                            foreach (var path in MarkdownFiles(child)) Add(config, path, ImportFileKind.MemoryFolder);
                    }
                    break;
            }
        }
        var projectFile = source.Provider.Id == "claude" ? "CLAUDE.md" : "AGENTS.md";
        foreach (var folder in source.ProjectFolders.Take(MaxProjectDirectories))
        {
            if (SafeDirectory(folder) is not { } root) continue;
            Add(root, Path.Combine(root, projectFile), ImportFileKind.Instructions);
            if (source.Provider.Id == "claude") Add(root, Path.Combine(root, ".claude", projectFile), ImportFileKind.Instructions);
        }
        if (skipped > 0) problems.Add($"{source.Label}: more than {MaxFilesPerSource} files were found; the rest were not read.");
        return files;
    }

    private static string? SafeDirectory(string? path, string? under = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            var info = new DirectoryInfo(full);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null) return null;
            if (under is not null && !IsUnder(under, full)) return null;
            return full;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Children(string directory, int max)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase).Take(max)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> MarkdownFiles(string directory)
    {
        try
        {
            return [.. Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsUnder(string root, string path)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    // A file is read only when it is a regular file strictly under its root, and is not a link (a junction or symbolic link could
    // point anywhere: the file would be one the owner never meant to import).
    private static string? Resolve(string root, string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!IsUnder(root, full)) return null;
            var info = new FileInfo(full);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null) return null;
            return full;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // ---- importing -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Imports the sources' files. A file the import already read and that has not changed (size, modified time and content hash) is
    /// skipped. Notes are proposed to the store in one batch, so the same words from two accounts are one note with both accounts as
    /// sources, and a rerun adds nothing. A file's record is written only after its notes are, so a crash between the two reads the
    /// file again and joins its notes into themselves.
    /// </summary>
    public ImportReport Import(IReadOnlyList<MemoryImportSource> sources, ImportMode mode = ImportMode.Owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var problems = new List<string>();
        var proposals = new List<NoteProposal>();
        var owners = new List<(string SourceKey, string Path, ImportedFile Print, int From, int To)>();
        var read = 0;
        var unchanged = 0;
        var main = mainAssistantId();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in Discover(source, problems))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (proposals.Count >= MaxNotesPerRun)
                {
                    problems.Add("There are more notes than one import takes. Import again to bring in the rest.");
                    break;
                }
                var print = Fingerprint(file.Path, out var bytes, out var problem);
                if (problem is not null) { problems.Add(problem); continue; }
                if (print is null) continue;
                if (store.ImportedFileOf(source.Key, file.Path) == print) { unchanged++; continue; }
                var text = System.Text.Encoding.UTF8.GetString(bytes!);
                var directory = Path.GetDirectoryName(file.Path)!;
                var candidates = MemoryImportReader.Read(Path.GetFileName(file.Path), text, file.Kind, relative => Resolve(file.Root, Path.Combine(directory, relative)) is not null);
                var from = proposals.Count;
                foreach (var candidate in candidates)
                    proposals.Add(Proposal(source, file, candidate, mode, main));
                owners.Add((source.Key, file.Path, print, from, proposals.Count));
                read++;
            }
        }
        var outcomes = proposals.Count == 0 ? [] : store.ProposeMany(proposals);
        var recorded = new Dictionary<string, Dictionary<string, ImportedFile>>(StringComparer.Ordinal);
        foreach (var (key, path, print, from, to) in owners)
        {
            if (Enumerable.Range(from, to - from).Any(i => outcomes[i].Result == NoteResult.Failed)) continue;
            if (!recorded.TryGetValue(key, out var files)) recorded[key] = files = new Dictionary<string, ImportedFile>(StringComparer.Ordinal);
            files[path] = print;
        }
        foreach (var (key, files) in recorded) store.RememberImportedFiles(key, files);
        return new ImportReport(read, unchanged,
            outcomes.Count(o => o.Result == NoteResult.Kept), outcomes.Count(o => o.Result == NoteResult.Quarantined), outcomes.Count(o => o.Result == NoteResult.Merged),
            outcomes.Count(o => o.Result == NoteResult.Refused), outcomes.Count(o => o.Result == NoteResult.Failed), problems);
    }

    private NoteProposal Proposal(MemoryImportSource source, ImportFile file, ImportCandidate candidate, ImportMode mode, string? main)
    {
        var detail = source.Label + " · " + Path.GetFileName(file.Path) + (candidate.Heading.Length > 0 ? " · " + candidate.Heading : "");
        // The same words go where they already are; otherwise what is about the owner goes to the shared set and the rest to Dextra.
        var scope = store.ScopeHolding(candidate.Text)
            ?? (candidate.Kind is NoteKind.Preference or NoteKind.Fact || main is null || !AssistantStore.IsId(main) ? MemoryScope.Shared : main);
        string? quarantine = null;
        if (InstructionScanner.Find(candidate.Text) is { } finding)
            quarantine = $"A line in {Path.GetFileName(file.Path)} of {source.Label} reads like an instruction to an AI (“{finding.Quote}”). Not used until you keep it.";
        else if (mode == ImportMode.Watch)
            quarantine = $"Found in {Path.GetFileName(file.Path)} of {source.Label} after your import. nth cannot tell who wrote it, so it waits for you.";
        return new NoteProposal(candidate.Text, candidate.Kind, scope, new NoteSource(NoteSourceKind.Import, file.Path, source.AccountId, detail))
        {
            Topic = candidate.Topic,
            Author = NoteAuthor.Import,
            AuthorName = source.Label,
            ForceQuarantine = quarantine,
            // An import has no words of the owner's to state a note about someone else: such notes are refused (8.2).
            AboutPerson = false,
        };
    }

    // The file's size, modified time and content hash, with its bytes; null with a problem line when it is too big or cannot be read.
    private static ImportedFile? Fingerprint(string path, out byte[]? bytes, out string? problem)
    {
        bytes = null;
        problem = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                problem = $"{info.Name} is larger than {MaxFileBytes / 1024} KB and was not read.";
                return null;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
            return new ImportedFile(bytes.Length, info.LastWriteTimeUtc.Ticks, Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            problem = $"{Path.GetFileName(path)} could not be read: {error.Message}";
            return null;
        }
    }
}
