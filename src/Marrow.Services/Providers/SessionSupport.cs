using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/SessionSupport.swift (B3 52bc792), :1-124. New at B3 (unit N7): code
// moved verbatim out of the Pi, Command Code and Antigravity sessions (Pi PiSession.swift:37-44, 57, 310-316,
// 346-405, 496-500; CommandCodeSession.swift:42-50, 293-337, 438-439, 629-646; AntigravitySession.swift:47, 232,
// 259-273). Those three files still carry their own copies until their own lanes switch them over to this one;
// this unit only adds the shared type.

namespace Marrow.Services.Providers;

// :3-41. ENGINEERING DECISION: a sealed class, not an immutable record (R-VAL-1 covers model values, the
// RecentCache.cs precedent). Every Swift construction site holds this in a stored `private var` field
// (PiSession.swift, CommandCodeSession.swift, AntigravitySession.swift) and mutates it in place through that
// variable, never taking a copy, so a class instance behaves identically.
/// <summary>Each edited file's text from before the tool ran, for the finished edit's diff.</summary>
public sealed class EditSnapshots
{
    private readonly Dictionary<string, string> contents = new(SwiftStringComparer.Instance); // :5

    /// <summary>Captures the file's text as the call starts; a missing or huge file reads as empty, so a write
    /// shows as a whole-file addition.</summary>
    public void Capture(string id, string path) => contents[id] = Read(path) ?? ""; // :9-11

    public void Discard(string id) => contents.Remove(id); // :13-15

    public void RemoveAll() => contents.Clear(); // :17-19

    public string Text(string id) => contents.GetValueOrDefault(id, ""); // :21-23

    /// <summary>The finished edit as the file before against after; null for an id nothing was captured for, so
    /// the row's edits stay as queued.</summary>
    public ValueList<FileEdit>? Finish(string id, string path, string root) // :27-33
    {
        if (!contents.Remove(id, out var before)) return null; // :28
        var relative = ToolTitles.RelativePath(path, root); // :29
        var after = Read(path) ?? ""; // :30
        if (SwiftString.Equal(before, after)) return new ValueList<FileEdit>([new FileEdit(relative)]); // :31
        return new ValueList<FileEdit>([new FileEdit(relative, before, after)]); // :32
    }

    /// <summary>Text files up to 1 MB; anything else (binary, huge, missing) reads as null.</summary>
    private static string? Read(string path) // :36-40
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 1_000_000) return null; // :38
        return SwiftTry.Optional(() => File.ReadAllBytes(path)) is { } bytes ? SwiftText.DecodeUtf8OrNil(bytes) : null; // :39
    }
}

// :45-124. Same ENGINEERING DECISION as EditSnapshots above: every Swift construction site holds this in a
// stored `private var stream` field, mutated in place, never copied.
/// <summary>Message and thinking ids for one streamed turn, starting a new id when a delta arrives without
/// one.</summary>
public sealed class StreamedTextTracker
{
    private readonly Func<long, string> messageID; // :46
    private readonly Func<long, string> thinkingID; // :47
    private long messageCounter; // :48
    private long thinkingCounter; // :49

    public string? CurrentMessageID { get; private set; } // :50
    public string? CurrentThinkingID { get; private set; } // :51
    // ED-NEW-lean-4: the two text buffers append in place and are flattened once when read (at message or block
    // end), not copied whole on every delta; MessageText and ThinkingText are the same plain concatenation as before.
    private readonly StringBuilder messageText = new(); // :52
    private readonly StringBuilder thinkingText = new(); // :53
    private string? messageFlat = "";
    private string? thinkingFlat = "";

    public string MessageText => messageFlat ??= messageText.ToString(); // :52
    public string ThinkingText => thinkingFlat ??= thinkingText.ToString(); // :53
    public bool StreamedText { get; private set; } // :54

    public StreamedTextTracker(Func<long, string> messageID, Func<long, string> thinkingID) // :56-59
    {
        this.messageID = messageID;
        this.thinkingID = thinkingID;
    }

    /// <summary>A `message_start`: a fresh reply, its text buffer empty.</summary>
    public void StartMessage() // :62-67
    {
        messageCounter++;
        CurrentMessageID = messageID(messageCounter);
        messageText.Clear();
        messageFlat = "";
        StreamedText = false;
    }

    /// <summary>Unlike <see cref="StartMessage"/>, starting a reply here keeps the text buffer.</summary>
    public string AppendMessage(string delta) // :70-78
    {
        if (CurrentMessageID is null)
        {
            messageCounter++;
            CurrentMessageID = messageID(messageCounter);
        }
        StreamedText = true;
        messageText.Append(delta);
        messageFlat = null;
        return CurrentMessageID ?? "";
    }

    public void StartThinking() // :80-84
    {
        thinkingCounter++;
        CurrentThinkingID = thinkingID(thinkingCounter);
        thinkingText.Clear();
        thinkingFlat = "";
    }

    /// <summary>The id a thinking delta belongs to. <paramref name="resetsText"/> follows the provider's
    /// fallback: Pi empties the buffer, Command Code keeps it across a block.</summary>
    public string AppendThinking(string delta, bool resetsText = true) // :86-99
    {
        if (CurrentThinkingID is null)
        {
            if (resetsText)
            {
                StartThinking();
            }
            else
            {
                thinkingCounter++;
                CurrentThinkingID = thinkingID(thinkingCounter);
            }
        }
        thinkingText.Append(delta);
        thinkingFlat = null;
        return CurrentThinkingID ?? "";
    }

    public void EndThinking() => CurrentThinkingID = null; // :101-103

    public void EndMessage() => CurrentMessageID = null; // :105-107

    /// <summary>A `message_start` that also drops the reasoning block it interrupted.</summary>
    public void ResetThinking() // :109-113
    {
        CurrentThinkingID = null;
        thinkingText.Clear();
        thinkingFlat = "";
    }

    /// <summary>The turn's text flag, cleared before each turn.</summary>
    public void ResetForTurn() => StreamedText = false; // :115-118

    public void ResetIDs() // :120-123
    {
        CurrentMessageID = null;
        CurrentThinkingID = null;
    }
}
