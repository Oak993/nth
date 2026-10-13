// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-5, ED-NEW-pcws-6; DESIGN.md 2.13). The wire
// between nth and its agent inside the child session (`nth.exe --tactus-workspace-agent <pipe> <token>`, started by the
// Remote Desktop control's StartProgram, connected back over a current-user named pipe): one JSON object per line, UTF-8,
// each carrying the protocol version "v" and its "type"; requests carry an "id", replies the "re" of the request they
// answer. The agent speaks first with a hello that carries the token nth gave it; nth answers welcome, or failed and
// closes. Both ends are the same nth.exe, but the version still travels: a message from a newer version is left untouched
// and never acted on, and unknown fields survive a read and a write. A line is at most 16 MB (a screenshot and its crops);
// a longer one closes the link. Log lines name the message type, its ids, the tool and the chat thread, never the token,
// a window title, typed text or a tool's arguments (Describe).
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Workspace;

public static class WorkspaceAgentProtocol
{
    public const int Version = 1;

    /// <summary>The longest line either end reads or writes.</summary>
    public const int MaxLineBytes = 16 * 1024 * 1024;

    // agent to nth
    public const string Hello = "hello";
    public const string Launched = "launched";
    public const string WindowsReply = "windows";
    public const string WindowReply = "window";
    public const string Result = "result";
    public const string Approve = "approve";
    public const string Confirm = "confirm";

    // nth to agent
    public const string Welcome = "welcome";
    public const string Launch = "launch";
    public const string WindowsAsk = "windows";
    public const string WindowAsk = "window";
    public const string Call = "call";
    public const string Cancel = "cancel";
    public const string EndTurn = "end_turn";
    public const string Approved = "approved";
    public const string Confirmed = "confirmed";

    // either way
    public const string Failed = "failed";
    public const string Bye = "bye";

    private static readonly Dictionary<string, PcErrorCode> codes = Enum.GetValues<PcErrorCode>().ToDictionary(PcErrorCodes.Wire, code => code, StringComparer.Ordinal);

    /// <summary>A new message of this version.</summary>
    public static JsonObject Message(string type) => new() { ["v"] = Version, ["type"] = type };

    /// <summary>A request: a new message with its id.</summary>
    public static JsonObject Request(string type, long id)
    {
        var message = Message(type);
        message["id"] = id;
        return message;
    }

    /// <summary>A reply to the request <paramref name="re"/>.</summary>
    public static JsonObject Reply(string type, long re)
    {
        var message = Message(type);
        message["re"] = re;
        return message;
    }

    /// <summary>One line as a message; null when it is not a JSON object with a version and a type.</summary>
    public static WorkspaceMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject body) return null;
            var message = new WorkspaceMessage(body);
            return message.Version > 0 && message.Type.Length > 0 ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the hello's token is the one nth gave the agent (compared in fixed time).</summary>
    public static bool TokenMatches(string expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
    }

    /// <summary>What a log line may say about a message: its type, ids, tool and chat thread; never a token, title, text or argument.</summary>
    public static string Describe(WorkspaceMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = new StringBuilder("type=").Append(Types.Contains(message.Type) ? message.Type : "other").Append(" v=").Append(message.Version.ToString(CultureInfo.InvariantCulture));
        if (message.Id is { } id) text.Append(" id=").Append(id.ToString(CultureInfo.InvariantCulture));
        if (message.Re is { } re) text.Append(" re=").Append(re.ToString(CultureInfo.InvariantCulture));
        if (message.Type == Call && message.Str("tool") is { } tool) text.Append(" tool=").Append(PcToolSchemas.All.Any(spec => spec.Name == tool) ? tool : "other");
        if (message.Str("thread") is { } thread) text.Append(" thread=").Append(Safe(thread));
        return text.ToString();
    }

    // The message types this version knows: anything else is logged as "other", never as its own text.
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        Hello, Launched, WindowsReply, WindowReply, Result, Approve, Confirm, Welcome, Launch, Call, Cancel, EndTurn, Approved, Confirmed, Failed, Bye,
    };

    // A word for a log: letters, digits, '-', '_' and '.', at most 64 of them.
    private static string Safe(string word)
    {
        var kept = new string([.. word.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').Take(64)]);
        return kept.Length > 0 ? kept : "?";
    }

    // ----- windows -----

    public static JsonObject ToJson(WorkspaceWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var info = window.Info;
        return new JsonObject
        {
            ["handle"] = (long)info.Handle,
            ["title"] = info.Title,
            ["class"] = info.ClassName,
            ["pid"] = info.ProcessId,
            ["exe"] = info.ExePath,
            ["aumid"] = info.Aumid,
            ["x"] = info.Frame.X,
            ["y"] = info.Frame.Y,
            ["width"] = info.Frame.Width,
            ["height"] = info.Frame.Height,
            ["minimized"] = info.Minimized,
            ["owner"] = (long)info.Owner,
            ["monitor"] = info.Monitor,
            ["start_time"] = info.StartTime,
            ["version"] = info.Version,
            ["elevated"] = window.Elevated,
        };
    }

    /// <summary>A window from the wire; null when it is missing or has no handle.</summary>
    public static WorkspaceWindow? ToWindow(JsonNode? node)
    {
        if (node is not JsonObject body || Long(body, "handle") is not { } handle || handle <= 0 || handle >= WorkspaceRefs.Bit) return null;
        var info = new PcWindowInfo((nint)handle, Str(body, "title") ?? "", Str(body, "class") ?? "", (int)(Long(body, "pid") ?? 0), Str(body, "exe") ?? "")
        {
            Aumid = Str(body, "aumid"),
            Frame = new PcRect(Int(body, "x"), Int(body, "y"), Int(body, "width"), Int(body, "height")),
            Minimized = Bool(body, "minimized") ?? false,
            Owner = (nint)(Long(body, "owner") ?? 0),
            Monitor = Int(body, "monitor"),
            StartTime = Long(body, "start_time") ?? 0,
            Version = Str(body, "version") ?? "",
        };
        // a window whose elevation is not said is treated as elevated (fail closed)
        return new WorkspaceWindow(info, Bool(body, "elevated") ?? true);
    }

    public static JsonArray ToJson(IEnumerable<WorkspaceWindow> windows) => new([.. windows.Select(w => (JsonNode)ToJson(w))]);

    public static IReadOnlyList<WorkspaceWindow> ToWindows(JsonNode? node) =>
        node is JsonArray list ? [.. list.Select(ToWindow).OfType<WorkspaceWindow>()] : [];

    // ----- results -----

    public static JsonObject ToJson(PcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new JsonObject
        {
            ["error"] = result.Error is { } code ? PcErrorCodes.Wire(code) : null,
            ["status"] = result.Status switch
            {
                PcStatus.Unverified => "unverified",
                PcStatus.WaitingForUser => "waiting_for_user",
                _ => "ok",
            },
            ["reason"] = result.Reason,
            ["window"] = result.Window is { } line ? new JsonObject
            {
                ["title"] = line.Title,
                ["exe"] = line.Exe,
                ["obs"] = line.Obs,
                ["width"] = line.Width,
                ["height"] = line.Height,
                ["scale"] = line.Scale,
            } : null,
            ["body"] = result.Body,
            ["image"] = ToJson(result.Image),
            ["details"] = new JsonArray([.. result.Details.Select(d => (JsonNode?)ToJson(d))]),
            ["token"] = result.Token,
            ["resumed"] = result.Resumed is { } resumed ? new JsonObject { ["after_ms"] = resumed.AfterMs, ["reread"] = resumed.Reread, ["changed"] = resumed.Changed } : null,
        };
    }

    /// <summary>A result from the wire; a missing or broken one is an error result, never a success.</summary>
    public static PcResult ToResult(JsonNode? node)
    {
        if (node is not JsonObject body) return PcResult.Fail(PcErrorCode.NotSupported, "the workspace sent no result");
        PcErrorCode? error = null;
        var reason = Str(body, "reason");
        if (Str(body, "error") is { } wire)
        {
            if (codes.TryGetValue(wire, out var code)) error = code;
            else
            {
                error = PcErrorCode.NotSupported;
                reason = "the workspace answered an error this version does not know (" + new string([.. wire.Where(char.IsAsciiLetterOrDigit).Take(40)]) + ")";
            }
        }
        var status = Str(body, "status") switch
        {
            "unverified" => PcStatus.Unverified,
            "waiting_for_user" => PcStatus.WaitingForUser,
            _ => PcStatus.Ok,
        };
        PcWindowLine? window = null;
        if (body["window"] is JsonObject line)
        {
            window = new PcWindowLine(Str(line, "title") ?? "", Str(line, "exe") ?? "", Str(line, "obs"), Int(line, "width"), Int(line, "height"),
                line["scale"] is JsonValue scale && scale.TryGetValue<double>(out var value) ? value : 0);
        }
        PcResumed? resumed = null;
        if (body["resumed"] is JsonObject block)
            resumed = new PcResumed(Long(block, "after_ms") ?? 0, Bool(block, "reread") ?? false, Int(block, "changed"));
        var details = body["details"] is JsonArray crops ? crops.Select(ToImage).OfType<PcImage>().ToList() : [];
        return new PcResult
        {
            Error = error,
            Status = error is null ? status : PcStatus.Ok,
            Reason = reason,
            Window = window,
            Body = Str(body, "body"),
            Image = ToImage(body["image"]),
            Details = details,
            Token = Str(body, "token"),
            Resumed = resumed,
        };
    }

    private static JsonObject? ToJson(PcImage? image) => image is null ? null : new JsonObject
    {
        ["jpeg"] = Convert.ToBase64String(image.Jpeg),
        ["width"] = image.Width,
        ["height"] = image.Height,
        ["obs"] = image.ObsId,
    };

    private static PcImage? ToImage(JsonNode? node)
    {
        if (node is not JsonObject body || Str(body, "jpeg") is not { } text) return null;
        try
        {
            return new PcImage(Convert.FromBase64String(text), Int(body, "width"), Int(body, "height"), Str(body, "obs") ?? "");
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ----- settings, grid, asks -----

    /// <summary>What the agent's engine needs of the settings: confirmations, screenshot detail, OCR, the deny list, the stop chord's name.</summary>
    public static JsonObject ToJson(PcSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new JsonObject
        {
            ["confirm"] = settings.ConfirmGuardedActions,
            ["detail"] = settings.Detail == PcDetail.High ? "high" : "standard",
            ["ocr"] = settings.ReadTextFromImages,
            ["denied"] = new JsonArray([.. settings.UserDeniedApps.Select(app => (JsonNode?)JsonValue.Create(app))]),
            ["stop_chord"] = settings.StopChord,
        };
    }

    /// <summary>
    /// The agent's settings from the wire: Tactus on, and the real mouse and keyboard on because inside the workspace they
    /// are its own (R5, never the user's; ED-NEW-pcws-7); confirmations stay on unless the wire says off.
    /// </summary>
    public static PcSettings ToSettings(JsonNode? node)
    {
        var body = node as JsonObject;
        var denied = body?["denied"] is JsonArray list ? list.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList() : [];
        return PcSettings.Default with
        {
            Enabled = true,
            RealInput = true,
            LogTypedText = false,
            ConfirmGuardedActions = body is null || (Bool(body, "confirm") ?? true),
            Detail = body is not null && Str(body, "detail") == "high" ? PcDetail.High : PcDetail.Standard,
            ReadTextFromImages = body is not null && (Bool(body, "ocr") ?? false),
            UserDeniedApps = denied,
            StopChord = body is not null && Str(body, "stop_chord") is { Length: > 0 } chord ? chord : PcSettings.Default.StopChord,
        };
    }

    public static JsonObject? ToJson(PcGridProfile? grid) => grid is null ? null : new JsonObject { ["long_edge"] = grid.LongEdge, ["max_pixels"] = grid.MaxPixels };

    public static PcGridProfile? ToGrid(JsonNode? node) =>
        node is JsonObject body && Int(body, "long_edge") is > 0 and var edge && Int(body, "max_pixels") is > 0 and var pixels ? new PcGridProfile(edge, pixels) : null;

    public static string Wire(PcCapability capability) => capability == PcCapability.Act ? "act" : "see";

    public static PcCapability ToCapability(string? wire) => wire == "act" ? PcCapability.Act : PcCapability.See;

    public static string Wire(PcApproval approval) => approval switch
    {
        PcApproval.Chat => "chat",
        PcApproval.Always => "always",
        PcApproval.TimedOut => "timed_out",
        _ => "denied",
    };

    /// <summary>An approval from the wire; anything unknown is a denial.</summary>
    public static PcApproval ToApproval(string? wire) => wire switch
    {
        "chat" => PcApproval.Chat,
        "always" => PcApproval.Always,
        "timed_out" => PcApproval.TimedOut,
        _ => PcApproval.Denied,
    };

    // ----- fields -----

    internal static string? Str(JsonObject body, string name) => body[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // a number read back as long whether it came over the pipe (text) or was built in memory as an int
    internal static long? Long(JsonObject body, string name) =>
        body[name] is not JsonValue value ? null : value.TryGetValue<long>(out var number) ? number : value.TryGetValue<int>(out var small) ? (long?)small : null;

    internal static bool? Bool(JsonObject body, string name) => body[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static int Int(JsonObject body, string name) => body[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
}

/// <summary>One message as read: its body (unknown fields kept), version, type and ids.</summary>
public sealed class WorkspaceMessage(JsonObject body)
{
    public JsonObject Body { get; } = body;

    public int Version => (int)(WorkspaceAgentProtocol.Long(Body, "v") ?? 0);

    public string Type => WorkspaceAgentProtocol.Str(Body, "type") ?? "";

    public long? Id => WorkspaceAgentProtocol.Long(Body, "id");

    public long? Re => WorkspaceAgentProtocol.Long(Body, "re");

    /// <summary>From a newer version than this one knows: left untouched, never acted on.</summary>
    public bool Newer => Version > WorkspaceAgentProtocol.Version;

    public string? Str(string name) => WorkspaceAgentProtocol.Str(Body, name);

    public long? Long(string name) => WorkspaceAgentProtocol.Long(Body, name);

    public bool? Bool(string name) => WorkspaceAgentProtocol.Bool(Body, name);

    /// <summary>The message as one line again, with every field it came with.</summary>
    public string ToLine() => Body.ToJsonString();
}

/// <summary>The link broke its rules (a line too long); the link closes.</summary>
public sealed class WorkspaceProtocolException(string message) : IOException(message);

/// <summary>Reads lines of at most <see cref="WorkspaceAgentProtocol.MaxLineBytes"/>; a longer one throws before it is kept.</summary>
public sealed class WorkspaceLineReader(Stream stream, int maxBytes = WorkspaceAgentProtocol.MaxLineBytes)
{
    private readonly byte[] buffer = new byte[64 * 1024];
    private int start;
    private int end;

    /// <summary>The next line without its end; null when the stream ended.</summary>
    public async Task<string?> ReadLine(CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (start == end)
            {
                start = 0;
                end = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (end == 0) return null;
            }
            var newline = Array.IndexOf(buffer, (byte)'\n', start, end - start);
            var take = (newline < 0 ? end : newline) - start;
            if (line.Length + take > maxBytes) throw new WorkspaceProtocolException("a message is longer than " + maxBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
            line.Write(buffer, start, take);
            if (newline >= 0)
            {
                start = newline + 1;
                return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
            }
            start = end;
        }
    }
}

/// <summary>Writes one message per line, one writer at a time.</summary>
public sealed class WorkspaceLineWriter(Stream stream) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task Write(JsonObject message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
        if (bytes.Length > WorkspaceAgentProtocol.MaxLineBytes) throw new WorkspaceProtocolException("a message is longer than the link allows");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
