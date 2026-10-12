using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Support;

/// <summary>What a request to a running nth asks for (<see cref="NthControlRequest"/>).</summary>
public enum NthControlCommand
{
    /// <summary><c>status</c>: who answers and what it is doing. Changes nothing.</summary>
    Status,

    /// <summary><c>quit</c>: nth saves its drafts and settings and quits (<see cref="NthQuitMode"/> says when).</summary>
    Quit,

    /// <summary><c>withdraw</c>: the installer gave up. A quit the installer started and a merge still defers is called off.</summary>
    Withdraw,
}

/// <summary>When a <see cref="NthControlCommand.Quit"/> may go ahead.</summary>
public enum NthQuitMode
{
    /// <summary><c>if-idle</c>: only when nothing is running. Otherwise nth answers busy and changes nothing.</summary>
    IfIdle,

    /// <summary><c>stop</c>: now. nth stops what is running the way closing its window does, saves and quits.</summary>
    Stop,
}

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). One request to a running nth over its
/// control pipe: <c>{"v":1,"cmd":"status"}</c>, <c>{"v":1,"cmd":"quit","mode":"if-idle"}</c>,
/// <c>{"v":1,"cmd":"quit","mode":"stop"}</c> or <c>{"v":1,"cmd":"withdraw"}</c>. Anything else (another version, another
/// command, a quit without its mode, trailing content) is not a request.
/// </summary>
public sealed record NthControlRequest(NthControlCommand Command, NthQuitMode Mode = NthQuitMode.IfIdle)
{
    public static NthControlRequest Status { get; } = new(NthControlCommand.Status);

    public static NthControlRequest QuitIfIdle { get; } = new(NthControlCommand.Quit, NthQuitMode.IfIdle);

    public static NthControlRequest QuitAndStop { get; } = new(NthControlCommand.Quit, NthQuitMode.Stop);

    public static NthControlRequest Withdraw { get; } = new(NthControlCommand.Withdraw);

    public string ToJson() => JsonSerializer.Serialize(
        new NthControlRequestJson
        {
            Version = NthControl.ProtocolVersion,
            Command = Command switch
            {
                NthControlCommand.Status => "status",
                NthControlCommand.Quit => "quit",
                _ => "withdraw",
            },
            Mode = Command == NthControlCommand.Quit ? (Mode == NthQuitMode.Stop ? "stop" : "if-idle") : null,
        },
        NthControlJsonContext.Default.NthControlRequestJson);

    /// <summary>The request in these UTF-8 bytes, or null when they are not a version-1 request.</summary>
    public static NthControlRequest? Parse(ReadOnlySpan<byte> utf8)
    {
        NthControlRequestJson? document;
        try
        {
            document = JsonSerializer.Deserialize(utf8, NthControlJsonContext.Default.NthControlRequestJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (document is null || document.Version != NthControl.ProtocolVersion) return null;
        return document.Command switch
        {
            "status" => Status,
            "withdraw" => Withdraw,
            "quit" => document.Mode switch
            {
                "if-idle" => QuitIfIdle,
                "stop" => QuitAndStop,
                _ => null,
            },
            _ => null,
        };
    }
}

/// <summary>How nth answered (<see cref="NthControlResponse.Reply"/>).</summary>
public enum NthControlReply
{
    /// <summary><c>status</c>: the answer to <see cref="NthControlCommand.Status"/>.</summary>
    Status,

    /// <summary><c>ok</c>: the request was taken. For a quit, nth is quitting now (it saves, then exits).</summary>
    Ok,

    /// <summary><c>busy</c>: a quit if idle found work running. Nothing changed; the work is listed.</summary>
    Busy,

    /// <summary><c>refused</c>: nth heard the request and will not do it (<see cref="NthControlResponse.Reason"/> says why).</summary>
    Refused,
}

/// <summary>What a running item is (<see cref="NthWorkItem.Kind"/>).</summary>
public enum NthWorkKind
{
    /// <summary><c>thread</c>: a chat that is answering, or whose helpers are working.</summary>
    Thread,

    /// <summary><c>merge</c>: a team's work going out. nth waits for it before it quits (AppDelegate.swift:44-54).</summary>
    Merge,
}

/// <summary>One thing nth is working on: its name as the sidebar shows it, its kind and how long it has run.</summary>
public sealed record NthWorkItem(string Name, NthWorkKind Kind, int Seconds);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). nth's answer:
/// <c>{"v":1,"reply":"ok","pid":4242,"exe":"C:\\...\\current\\nth.exe"}</c>, plus <c>work</c> (the running items, at most
/// <see cref="NthControl.MaxWorkItems"/>) and <c>count</c> (how many in all) for <c>status</c> and <c>busy</c>,
/// <c>deferred</c> (<c>merge</c>) when a quit is told to go ahead but will wait for a merge, and <c>reason</c> (a short
/// word) for <c>refused</c>. <see cref="Pid"/> and <see cref="Exe"/> say which process answered, so the installer can tell
/// an nth inside the folder it is about to replace from a portable or development copy elsewhere.
/// </summary>
/// <param name="Reply">The kind of answer.</param>
/// <param name="Pid">The process that answered.</param>
/// <param name="Exe">That process's executable (full path).</param>
/// <param name="Work">The running items (status and busy).</param>
/// <param name="WorkCount">How many items are running in all (the list is capped).</param>
/// <param name="Deferred">Set on a quit that was taken but will wait for this kind of work first.</param>
/// <param name="Reason">Why a request was refused.</param>
public sealed record NthControlResponse(
    NthControlReply Reply,
    int Pid,
    string Exe,
    IReadOnlyList<NthWorkItem> Work,
    int WorkCount,
    NthWorkKind? Deferred = null,
    string? Reason = null)
{
    public static NthControlResponse Refused(int pid, string exe, string reason) =>
        new(NthControlReply.Refused, pid, exe, [], 0, Reason: reason);

    /// <summary>Exactly what goes on the pipe (compact, no extra fields).</summary>
    public string ToJson() => JsonSerializer.Serialize(
        new NthControlResponseJson
        {
            Version = NthControl.ProtocolVersion,
            Reply = Reply switch
            {
                NthControlReply.Status => "status",
                NthControlReply.Ok => "ok",
                NthControlReply.Busy => "busy",
                _ => "refused",
            },
            Pid = Pid,
            Exe = Exe,
            Work = Reply is NthControlReply.Status or NthControlReply.Busy
                ? Work.Take(NthControl.MaxWorkItems).Select(item => new NthWorkItemJson
                {
                    Name = item.Name,
                    Kind = item.Kind == NthWorkKind.Merge ? "merge" : "thread",
                    Seconds = item.Seconds,
                }).ToList()
                : null,
            Count = Reply is NthControlReply.Status or NthControlReply.Busy ? WorkCount : null,
            Deferred = Deferred is { } kind ? (kind == NthWorkKind.Merge ? "merge" : "thread") : null,
            Reason = Reason,
        },
        NthControlJsonContext.Default.NthControlResponseJson);

    /// <summary>
    /// The response in these UTF-8 bytes, or null when they are not a version-1 response. Names are made safe to show
    /// (<see cref="NthControl.CleanName"/>), the list is capped, and negative numbers read as zero: nothing on the wire is trusted.
    /// </summary>
    public static NthControlResponse? Parse(ReadOnlySpan<byte> utf8)
    {
        NthControlResponseJson? document;
        try
        {
            document = JsonSerializer.Deserialize(utf8, NthControlJsonContext.Default.NthControlResponseJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (document is null || document.Version != NthControl.ProtocolVersion) return null;
        NthControlReply? reply = document.Reply switch
        {
            "status" => NthControlReply.Status,
            "ok" => NthControlReply.Ok,
            "busy" => NthControlReply.Busy,
            "refused" => NthControlReply.Refused,
            _ => null,
        };
        if (reply is null) return null;
        var work = new List<NthWorkItem>();
        foreach (var entry in document.Work ?? [])
        {
            NthWorkKind? kind = entry.Kind switch
            {
                "thread" => NthWorkKind.Thread,
                "merge" => NthWorkKind.Merge,
                _ => null,
            };
            if (kind is null || entry.Name is null) return null;
            if (work.Count >= NthControl.MaxWorkItems) continue;
            var name = NthControl.CleanName(entry.Name);
            work.Add(new NthWorkItem(name.Length == 0 ? NthControl.UnnamedWork : name, kind.Value, Math.Max(0, entry.Seconds ?? 0)));
        }
        NthWorkKind? deferred = document.Deferred switch
        {
            null => null,
            "merge" => NthWorkKind.Merge,
            "thread" => NthWorkKind.Thread,
            _ => null,
        };
        if (document.Deferred is not null && deferred is null) return null;
        return new NthControlResponse(
            reply.Value,
            Math.Max(0, document.Pid ?? 0),
            document.Exe ?? string.Empty,
            work,
            Math.Max(work.Count, document.Count ?? work.Count),
            deferred,
            document.Reason is { } reason ? NthControl.CleanName(reason) : null);
    }
}

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). The name and the wire of the pipe
/// nth Setup uses to ask a running nth whether it may quit: <c>\\.\pipe\nth-&lt;user SID&gt;-control</c> (a development build
/// adds <c>dev-</c>, so it never answers an installer). One request and one reply per connection, each a 4-byte
/// little-endian length and that many bytes of UTF-8 JSON (the framing of the toast pipe), at most
/// <see cref="MaxMessageBytes"/>. The pipe is for the signed-in user alone (<c>PipeOptions.CurrentUserOnly</c> on both
/// sides). This file is also compiled into src/Marrow.Setup (like InstallerGate.cs), so it uses nothing else from
/// Marrow.Core, and its JSON goes through a source-generated context (AOT-safe). The listening side is
/// <c>NthControlServer.cs</c> (Marrow.Core only); the asking side is src/Marrow.Setup.
/// </summary>
public static class NthControl
{
    /// <summary>The only protocol version; any other number is not understood.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>A message is a few hundred bytes; anything over this is not ours and ends the connection.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>The running items one answer lists. <see cref="NthControlResponse.WorkCount"/> still counts them all.</summary>
    public const int MaxWorkItems = 20;

    /// <summary>Longest name or reason the installer shows (longer ones are cut with an ellipsis).</summary>
    public const int MaxNameLength = 120;

    /// <summary>What a thread with no usable name is called (the sidebar's own word for it, <c>ChatThread.Untitled</c>).</summary>
    public const string UnnamedWork = "New thread";

    /// <summary>The pipe's name (without <c>\\.\pipe\</c>) for this user's release nth, or for a development build.</summary>
    public static string PipeName(string userSid, bool development = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        return "nth-" + (development ? "dev-" : string.Empty) + userSid + "-control";
    }

    /// <summary>The signed-in user's SID as text, or null when it cannot be read (not Windows).</summary>
    public static string? CurrentUserSid()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
    }

    /// <summary>
    /// A name made safe to put in a window: control characters and the characters that reorder text are dropped, runs of
    /// white space become one space, the ends are trimmed and the length is capped. Empty stays empty.
    /// </summary>
    public static string CleanName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = new StringBuilder(Math.Min(name.Length, MaxNameLength + 1));
        var space = false;
        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                space = text.Length > 0;
                continue;
            }
            if (char.IsControl(c) || IsInvisible(c)) continue;
            if (space) text.Append(' ');
            space = false;
            text.Append(c);
        }
        var clean = text.ToString();
        if (clean.Length <= MaxNameLength) return clean;
        var cut = MaxNameLength - 1;
        if (char.IsHighSurrogate(clean[cut - 1])) cut--;
        return clean[..cut] + "\u2026";
    }

    // Zero-width characters and the ones that reorder or override text direction: a name must show as it reads.
    private static bool IsInvisible(char c) =>
        c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069' or >= '\u200B' and <= '\u200F' or '\u061C' or '\u2060' or '\uFEFF';

    /// <summary>One message on the wire: the 4-byte little-endian length, then the UTF-8 JSON.</summary>
    public static byte[] Frame(string json)
    {
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        if (body.Length is 0 or > MaxMessageBytes) throw new InvalidDataException("An nth control message must be between 1 byte and 64 KiB.");
        var frame = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, sizeof(int));
        return frame;
    }

    /// <summary>Writes one message (<see cref="Frame"/>) in a single write and flushes it.</summary>
    public static async Task WriteFrameAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Frame(json), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The next message on <paramref name="stream"/>, or null when the other side closed first or sent a length that is
    /// not 1 byte to <see cref="MaxMessageBytes"/> (nothing more is read then).
    /// </summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        try
        {
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is < 1 or > MaxMessageBytes) return null;
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
            return body;
        }
        catch (Exception error) when (error is EndOfStreamException or IOException)
        {
            return null;
        }
    }
}

internal sealed class NthControlRequestJson
{
    [JsonPropertyName("v")]
    public int? Version { get; set; }

    [JsonPropertyName("cmd")]
    public string? Command { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

internal sealed class NthWorkItemJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("seconds")]
    public int? Seconds { get; set; }
}

internal sealed class NthControlResponseJson
{
    [JsonPropertyName("v")]
    public int? Version { get; set; }

    [JsonPropertyName("reply")]
    public string? Reply { get; set; }

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    [JsonPropertyName("exe")]
    public string? Exe { get; set; }

    [JsonPropertyName("work")]
    public List<NthWorkItemJson>? Work { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("deferred")]
    public string? Deferred { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>Source-generated, so the trimmed AOT setup and the app read and write the same bytes without reflection.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NthControlRequestJson))]
[JsonSerializable(typeof(NthControlResponseJson))]
internal sealed partial class NthControlJsonContext : JsonSerializerContext
{
}
