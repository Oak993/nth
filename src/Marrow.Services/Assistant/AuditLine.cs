// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-3: the audit line, its keyed chain and the day files.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Services.Assistant;

/// <summary>
/// The audit line (ED-NEW-dextra-as3-3). A line is one compact JSON object, its properties in a fixed order, ending
/// with <c>"prev"</c> (the previous line's HMAC) and then <c>"hmac"</c>:
/// <code>{"v":1,"seq":7,"time":"…Z",["event":…,]"assistant":…,…,"prev":"&lt;64 hex&gt;","hmac":"&lt;64 hex&gt;"}</code>
/// The line's core is the line without its <c>,"hmac":"…"</c> (closed with <c>}</c>), and
/// <c>hmac = HMAC-SHA256(key, UTF-8(core) ‖ previous HMAC's 32 bytes)</c> (12.5: "over the line and the previous line's
/// HMAC"); the first line's previous HMAC is 32 zero bytes. Lines are numbered from 1 across every day file.
/// </summary>
internal static partial class AuditLine
{
    public const int Version = 1;
    public const string LockFileName = "writer.lock";

    /// <summary>At most this many inputs are written; the count of the rest is (a line stays bounded).</summary>
    public const int MaxInputs = 64;
    public static readonly string ZeroHmac = new('0', 64);

    private const string HmacOpen = ",\"hmac\":\"";
    private const int SuffixLength = 9 + 64 + 2; // ,"hmac":" + 64 hex + "}

    [GeneratedRegex(@"\A\d{4}-\d{2}-\d{2}\.jsonl\z")] private static partial Regex DayFileShape();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z")] private static partial Regex HexShape();

    /// <summary>System records the log writes about itself.</summary>
    public const string KeyReplaced = "keyReplaced";
    public const string TornLine = "tornLine";
    public const string LogCut = "logCut";

    public static string DayFileName(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";

    public static bool IsDayFile(string name) => DayFileShape().IsMatch(name);

    public static bool IsHmac(string? value) => value is not null && HexShape().IsMatch(value);

    /// <summary>The day files in chain order (their names are ISO dates).</summary>
    public static IReadOnlyList<string> DayFiles(string directory) =>
        !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory)
            .Where(path => IsDayFile(Path.GetFileName(path)))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The core of a line: every field, masked, ending with <c>prev</c>.</summary>
    public static string Core(long sequence, DateTimeOffset time, AuditEntry entry, string previousHmac, Action<Utf8JsonWriter>? systemFields)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteNumber("seq", sequence);
            writer.WriteString("time", time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            systemFields?.Invoke(writer);
            writer.WriteString("assistant", AuditRedactor.Redact(entry.Assistant));
            if (entry.Mission is not null) writer.WriteString("mission", AuditRedactor.Redact(entry.Mission));
            if (entry.Agent is not null) writer.WriteString("agent", AuditRedactor.Redact(entry.Agent));
            writer.WriteString("tool", AuditRedactor.Redact(entry.Tool));
            writer.WriteString("class", AuditRedactor.Redact(entry.ActionClass));
            if (entry.Target is not null) writer.WriteString("target", AuditRedactor.Redact(entry.Target));
            writer.WriteStartArray("inputs");
            foreach (var input in entry.Inputs.Take(MaxInputs)) WriteInput(writer, input);
            writer.WriteEndArray();
            if (entry.Inputs.Count > MaxInputs) writer.WriteNumber("inputsNotLogged", entry.Inputs.Count - MaxInputs);
            if (entry.Result is not null) writer.WriteString("result", AuditRedactor.Redact(entry.Result));
            if (entry.CostUsd is { } cost) writer.WriteNumber("costUsd", cost);
            if (entry.Tokens is { } tokens) writer.WriteNumber("tokens", tokens);
            if (entry.AllowedBy is not null) writer.WriteString("allowedBy", AuditRedactor.Redact(entry.AllowedBy));
            writer.WriteString("prev", previousHmac);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteInput(Utf8JsonWriter writer, AuditInput input)
    {
        writer.WriteStartObject();
        writer.WriteString("name", AuditRedactor.Redact(input.Name));
        switch (input.Kind)
        {
            case AuditInputKind.Text:
                writer.WriteString("text", AuditRedactor.Redact(input.Text));
                break;
            case AuditInputKind.Typed:
                writer.WriteNumber("typedLength", input.TypedLength);
                break;
            case AuditInputKind.Secret:
                writer.WriteString("secret", AuditRedactor.Mask);
                break;
            case AuditInputKind.Number:
                if (double.IsFinite(input.Number)) writer.WriteNumber("number", input.Number);
                else writer.WriteString("number", input.Number.ToString(CultureInfo.InvariantCulture));
                break;
            case AuditInputKind.Flag:
                writer.WriteBoolean("flag", input.Flag);
                break;
        }
        writer.WriteEndObject();
    }

    public static string Mac(ReadOnlySpan<byte> key, string core, string previousHmac)
    {
        var coreBytes = Encoding.UTF8.GetBytes(core);
        var data = new byte[coreBytes.Length + 32];
        coreBytes.CopyTo(data, 0);
        Convert.FromHexString(previousHmac).CopyTo(data, coreBytes.Length);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }

    /// <summary>The whole line (no newline) for a core and its HMAC.</summary>
    public static string Seal(string core, string hmac) => core[..^1] + HmacOpen + hmac + "\"}";

    /// <summary>Splits a line into its core and HMAC; false when the line does not end in the HMAC field.</summary>
    public static bool TryOpen(string line, out string core, out string hmac)
    {
        core = "";
        hmac = "";
        if (line.Length < SuffixLength + 2 || !line.EndsWith("\"}", StringComparison.Ordinal)) return false;
        var start = line.Length - SuffixLength;
        if (string.CompareOrdinal(line, start, HmacOpen, 0, HmacOpen.Length) != 0) return false;
        var hex = line.Substring(start + HmacOpen.Length, 64);
        if (!IsHmac(hex)) return false;
        core = line[..start] + "}";
        hmac = hex;
        return true;
    }

    /// <summary>
    /// A line's core as JSON, or null when it does not parse to one object with distinct property names (a repeated
    /// name anywhere is a damaged line, never an exception; ED-NEW-dextra-as3-6).
    /// </summary>
    public static JsonObject? ParseCore(string core) => AssistantJson.ParseObject(core);

    /// <summary>The largest line number a line may carry (2^53 - 1, exact in every JSON reader), so seq + 1 never overflows.</summary>
    public const long MaxSequence = (1L << 53) - 1;

    /// <summary>A line's number: its <c>seq</c> when that is a whole number from 1 to <see cref="MaxSequence"/>, else null.</summary>
    public static long? Sequence(JsonObject fields) => Long(fields, "seq") is { } sequence && sequence is >= 1 and <= MaxSequence ? sequence : null;

    /// <summary>
    /// Whether a line is sealed under <paramref name="key"/>: its version, number and <c>prev</c> are well formed and its
    /// HMAC is the keyed hash of its core and its own <c>prev</c> (fixed-time compare). A line that does not seal under the
    /// key was not written by the writer holding it, so the writer never takes it as the chain's head (ED-NEW-dextra-as3-6).
    /// </summary>
    public static bool Seals(ReadOnlySpan<byte> key, string core, JsonObject fields, string hmac)
    {
        if (Long(fields, "v") != Version || Sequence(fields) is null || Text(fields, "prev") is not { } previous || !IsHmac(previous)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Mac(key, core, previous)), Convert.FromHexString(hmac));
    }

    /// <summary>The local day a day file is named for, or null when the name is not a day file's.</summary>
    public static DateOnly? DayOf(string fileName) =>
        IsDayFile(fileName) && DateOnly.TryParseExact(fileName[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day : null;

    /// <summary>
    /// The SHA-256 of the piece after the last newline of a file (a line cut off before its newline), or null when the
    /// file is empty or ends with a newline. Reads backwards in blocks and hashes forwards, so a huge piece costs no memory.
    /// </summary>
    public static string? PieceHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        if (length == 0) return null;
        var block = new byte[65536];
        var start = length;
        while (start > 0)
        {
            var size = (int)Math.Min(block.Length, start);
            stream.Position = start - size;
            stream.ReadExactly(block, 0, size);
            var newline = Array.LastIndexOf(block, (byte)'\n', size - 1, size);
            if (newline >= 0)
            {
                start = start - size + newline + 1;
                break;
            }
            start -= size;
        }
        if (start == length) return null;
        stream.Position = start;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int read;
        while ((read = stream.Read(block, 0, block.Length)) > 0) hash.AppendData(block, 0, read);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static long? Long(JsonObject fields, string name) =>
        fields[name] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    public static string? Text(JsonObject fields, string name) =>
        fields[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>A day file's complete lines (each ended by '\n') as raw bytes, and the piece after the last newline.</summary>
    public static (List<byte[]> Lines, byte[] Tail) SplitLines(byte[] bytes)
    {
        var lines = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            lines.Add(bytes[start..i]);
            start = i + 1;
        }
        return (lines, bytes[start..]);
    }

    /// <summary>Reads a file another handle may be writing (the writer shares it for reading only).</summary>
    public static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
