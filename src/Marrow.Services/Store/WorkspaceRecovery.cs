using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W3c, what happens to a saved pane layout that cannot be read (E:/Droppy/work/design/web-surfaces/
// DESIGN.md 3.11 "corrupted JSON: the thread opens with the default layout, the bad file is kept as workspace.bad.json, and
// the log records it"; ED-NEW-web-w3c-1). No Swift counterpart. The thread has already opened with the default layout (the
// decoder leaves the text on ChatThread.DamagedWorkspace); this keeps the text in the thread's own data folder, beside its
// artifacts and handoff files, and says so in the log. It never throws: a layout is not worth failing a launch for.

namespace Marrow.Services.Store;

/// <summary>Keeps the text of a thread's unreadable saved workspace.</summary>
public static class WorkspaceRecovery
{
    /// <summary>The kept file's name (the first copy; later, different bad texts take <c>workspace.bad.2.json</c> and on).</summary>
    public const string BadFileName = "workspace.bad.json";

    /// <summary>How many different bad texts one thread keeps; the last file is replaced after that.</summary>
    public const int MaxCopies = 9;

    private static readonly Logger Log = new(Logger.Subsystem, "workspace");

    /// <summary>
    /// Writes <paramref name="text"/> into the thread's data folder and logs it. The same text is not kept twice (a launch that
    /// finds the same damage again, because nothing saved over it yet). Returns the file's path, or null when it could not be kept.
    /// </summary>
    public static string? Keep(Guid threadID, string text, string? threadsDirectory = null)
    {
        var reason = PaneWorkspaceJson.Read(text).Reason;
        try
        {
            var folder = Path.Combine(threadsDirectory ?? Storage.ThreadsDirectory, SwiftUUID.UuidString(threadID));
            Directory.CreateDirectory(folder);
            string? target = null;
            for (var copy = 1; copy <= MaxCopies; copy++)
            {
                var path = Path.Combine(folder, copy == 1 ? BadFileName : $"workspace.bad.{copy}.json");
                if (!File.Exists(path))
                {
                    target = path;
                    break;
                }
                if (File.ReadAllText(path, Encoding.UTF8) == text)
                {
                    Log.Error($"nth could not read the pane layout saved for thread {SwiftUUID.UuidString(threadID)} ({Describe(reason)}). The thread opens with the default layout; the saved text is kept in {path}.");
                    return path;
                }
                if (copy == MaxCopies) target = path;
            }
            File.WriteAllText(target!, text, new UTF8Encoding(false));
            Log.Error($"nth could not read the pane layout saved for thread {SwiftUUID.UuidString(threadID)} ({Describe(reason)}). The thread opens with the default layout; the saved text is kept in {target}.");
            return target;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Error($"nth could not read the pane layout saved for thread {SwiftUUID.UuidString(threadID)} ({Describe(reason)}) and could not keep its text: {error.Message}");
            return null;
        }
    }

    private static string Describe(string reason) => string.IsNullOrEmpty(reason) ? "unreadable" : reason;
}
