using System.Text;

namespace Marrow.Setup.Install;

/// <summary>
/// Reads the lines Velopack's <c>Setup.exe</c> appends to its <c>--log</c> file while it runs, without ever locking it
/// (read, write and delete shared). Only whole lines are returned; a line still being written waits for its newline. The
/// file starts at <paramref name="startOffset"/> (its length before this run), so an earlier run's lines are never read
/// as this one's; if Velopack's 1 MB rotation makes the file shorter than what was read, reading starts again at 0.
/// </summary>
public sealed class VelopackLogTail(string path, long startOffset = 0)
{
    /// <summary>Copy details carries the last 40 lines (PLAN.md 4).</summary>
    public const int KeptLines = 40;

    private readonly Queue<string> recent = new();
    private readonly StringBuilder partial = new();
    private readonly Decoder decoder = new UTF8Encoding(false, false).GetDecoder();
    private long offset = Math.Max(0, startOffset);

    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The last <see cref="KeptLines"/> whole lines read.</summary>
    public IReadOnlyList<string> Recent => [.. recent];

    /// <summary>The file's length now (0 when there is none): where a new run's lines will start.</summary>
    public static long LengthOf(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>The whole lines added since the last call (none when the file is missing or cannot be read now).</summary>
    public IReadOnlyList<string> ReadNew()
    {
        var lines = new List<string>();
        try
        {
            if (!File.Exists(Path)) return lines;
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length < offset)
            {
                offset = 0; // rotated
                partial.Clear();
                decoder.Reset();
            }
            stream.Position = offset;
            var bytes = new byte[8192];
            var chars = new char[8192 + 1];
            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                offset += read;
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                for (var i = 0; i < count; i++)
                {
                    var c = chars[i];
                    if (c == '\n')
                    {
                        Take(lines);
                        continue;
                    }
                    if (c != '\r') partial.Append(c);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Velopack holds the file between writes; the next poll reads what this one missed.
        }
        return lines;
    }

    private void Take(List<string> lines)
    {
        var line = partial.ToString();
        partial.Clear();
        if (partial.Capacity > 64 * 1024) partial.Capacity = 1024;
        if (line.Length > 4096) line = line[..4096];
        lines.Add(line);
        recent.Enqueue(line);
        while (recent.Count > KeptLines) recent.Dequeue();
    }
}

/// <summary>Why an install failed, in the order the reasons are tried (PLAN.md 4, "Error reasons").</summary>
public enum FailureReason
{
    /// <summary>"There isn't enough free space on drive …"</summary>
    NoSpace,

    /// <summary>"Some of nth's files are in use. …" (a sharing violation, or Velopack could not stop or move nth's folder).</summary>
    InUse,

    /// <summary>"Security software stopped the install. …" (access denied, or Setup.exe was ended from outside).</summary>
    Blocked,

    /// <summary>"Something went wrong while installing."</summary>
    Other,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4; VELOPACK_CHECK.md check 5; unit U3, ED-NEW-inst-u3-1). What Velopack's
/// <c>Setup.exe</c> has said in its log so far. SOURCE FACT (Velopack 1.2.161, tag 92d6a1c9, src/bins/src/commands/install.rs,
/// windows/util.rs, setup.rs, setup_errors.rs, read from GitHub raw 2026-10-12): the lines are simplelog's
/// <c>[setup:&lt;pid&gt;] [hh:mm:ss] LEVEL [target] message</c>, and the run says, in order, "Renaming existing directory to …"
/// (only when the root was not empty: an upgrade or a repair), "Preparing and cleaning installation directory...",
/// "Starting installation!", "Extracting Update.exe...", "Copying nupkg to packages directory...", "Creating shortcuts...",
/// "Starting process install hook", "Running --veloapp-install hook...", then "Hook executed successfully", "Hook exited with
/// non-zero exit code" or "Process timed out after 30s and was killed.", then "Installation completed successfully!" or
/// "Installation failed!" with "Rolling back installation..." and "An error has occurred: …" (whose Debug form runs over
/// several lines). Lines are matched by these phrases, never by position, so a changed prefix still reads.
/// </summary>
public sealed class VelopackLogSignals
{
    private readonly List<string> errors = [];
    private bool inError;

    public bool RenamedForRollback { get; private set; }

    /// <summary>Velopack has started writing into the root (or emptied it).</summary>
    public bool Writing { get; private set; }

    public bool CreatingShortcuts { get; private set; }

    public bool HookStarted { get; private set; }

    public bool HookEnded { get; private set; }

    /// <summary>The hook overran its 30 s and was killed (the install still finishes).</summary>
    public bool HookTimedOut { get; private set; }

    public bool Completed { get; private set; }

    public bool Failed { get; private set; }

    public bool RollingBack { get; private set; }

    /// <summary>The error lines (ERROR level and the error's continuation lines), first 40.</summary>
    public IReadOnlyList<string> Errors => errors;

    public void Read(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var line in lines) Read(line);
    }

    public void Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var prefixed = line.StartsWith('[');
        if (!prefixed)
        {
            if (inError && line.Trim().Length > 0) Error(line);
            return;
        }
        inError = false;
        if (Has(line, "Renaming existing directory to")) RenamedForRollback = true;
        if (Has(line, "Preparing and cleaning installation directory") || Has(line, "Starting installation!") || Has(line, "Extracting Update.exe")
            || Has(line, "Copying nupkg to packages directory")) Writing = true;
        if (Has(line, "Creating shortcuts...")) CreatingShortcuts = true;
        if (Has(line, "Starting process install hook") || (Has(line, "Running ") && Has(line, "--veloapp-install"))) HookStarted = true;
        if (Has(line, "Hook executed successfully") || Has(line, "Hook exited with non-zero exit code") || Has(line, "Was unable to wait for hook")
            || Has(line, "Failed to start hook") || Has(line, "Error waiting for process to finish")) HookEnded = true;
        if (Has(line, "Process timed out after"))
        {
            HookEnded = true;
            HookTimedOut = true;
        }
        if (Has(line, "Installation completed successfully!")) Completed = true;
        if (Has(line, "Installation failed!")) Failed = true;
        if (Has(line, "Rolling back installation")) RollingBack = true;
        if (Has(line, "An error has occurred") || Has(line, "] ERROR ") || Has(line, " ERROR ["))
        {
            if (!Has(line, "Process timed out after")) // a killed hook is logged at ERROR but does not fail the install
            {
                Error(line);
                inError = Has(line, "An error has occurred");
            }
        }
    }

    /// <summary>
    /// The reason the error lines give: no space (Velopack's own check, or Windows error 112/39), files in use (a sharing
    /// or lock violation, Windows error 32/33, or Velopack could not stop nth or move its folder), blocked (access denied,
    /// Windows error 5), else other.
    /// </summary>
    public FailureReason Reason()
    {
        var text = string.Join('\n', errors);
        if (Has(text, "disk space to be installed") || Has(text, "os error 112") || Has(text, "os error 39") || Has(text, "not enough space on the disk")) return FailureReason.NoSpace;
        if (Has(text, "os error 32") || Has(text, "os error 33") || Has(text, "being used by another process") || Has(text, "Failed to stop application")
            || Has(text, "Failed to remove existing application directory")) return FailureReason.InUse;
        if (Has(text, "os error 5)") || Has(text, "Access is denied")) return FailureReason.Blocked;
        return FailureReason.Other;
    }

    private void Error(string line)
    {
        if (errors.Count < VelopackLogTail.KeptLines) errors.Add(line);
    }

    private static bool Has(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
