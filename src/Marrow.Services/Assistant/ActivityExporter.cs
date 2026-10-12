// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view": "The log exports to JSON or CSV"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-5: the two exports.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marrow.Services.Assistant;

public enum ActivityExportFormat
{
    Json,
    Csv,
}

/// <summary>What an export says about itself: the filter that chose its rows, the chain check's result, and when it was made.</summary>
public sealed record ActivityExportContext(ActivityFilter Filter, AuditVerification? Verification, DateTimeOffset ExportedAt);

/// <summary>What an export wrote.</summary>
public sealed record ActivityExportResult(string Path, int Rows, long Bytes);

/// <summary>
/// Writes rows of the Activity view to JSON or CSV (12.5). Both formats carry, for every line, the numbers and the HMAC the
/// log holds and whether the chain check vouched for it (<see cref="ActivityRow.Trust"/>), so an export is never mistaken
/// for a verified log when it is not. Text is masked once more on the way out (<see cref="AuditRedactor"/>: a log written
/// by an older build may hold a secret a newer rule would mask) and a CSV cell that a spreadsheet would run as a formula is
/// written as text. Rows are written as they are read, so an export of a long log holds one line in memory.
/// </summary>
public static class ActivityExporter
{
    public const string JsonFormat = "nth-activity-log";
    public const int JsonVersion = 1;

    /// <summary>The column names of the CSV, in order.</summary>
    public static IReadOnlyList<string> CsvColumns { get; } =
    [
        "seq", "time", "file", "line", "trust", "kind", "event", "assistant", "mission", "agent", "tool", "class", "target",
        "inputs", "result", "costUsd", "tokens", "allowedBy", "note", "prev", "hmac",
    ];

    /// <summary>A name to offer in a save dialog, e.g. <c>nth-activity-2026-10-12.json</c>.</summary>
    public static string SuggestedFileName(ActivityExportFormat format, DateOnly today) =>
        "nth-activity-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (format == ActivityExportFormat.Json ? ".json" : ".csv");

    /// <summary>Writes the rows to <paramref name="output"/> and returns how many it wrote.</summary>
    public static int Write(Stream output, ActivityExportFormat format, IEnumerable<ActivityRow> rows, ActivityExportContext context, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        return format == ActivityExportFormat.Json ? WriteJson(output, rows, context, cancellation) : WriteCsv(output, rows, cancellation);
    }

    /// <summary>
    /// Writes the export to a temporary file beside <paramref name="path"/> and renames it over the target when it is
    /// whole, so a failed or cancelled export leaves neither a half file nor the file it was to replace changed.
    /// </summary>
    public static ActivityExportResult WriteFile(string path, ActivityExportFormat format, IEnumerable<ActivityRow> rows, ActivityExportContext context,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            int count;
            long bytes;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                count = Write(stream, format, rows, context, cancellation);
                stream.Flush(flushToDisk: true);
                bytes = stream.Length;
            }
            File.Move(temporary, target, overwrite: true);
            return new ActivityExportResult(target, count, bytes);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    // ---- JSON -------------------------------------------------------------------------------------------------------

    private static int WriteJson(Stream output, IEnumerable<ActivityRow> rows, ActivityExportContext context, CancellationToken cancellation)
    {
        var count = 0;
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("format", JsonFormat);
        writer.WriteNumber("version", JsonVersion);
        writer.WriteString("exportedAt", Time(context.ExportedAt));
        WriteFilter(writer, context.Filter);
        WriteVerification(writer, context.Verification);
        writer.WriteStartArray("lines");
        foreach (var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            WriteRow(writer, row);
            count++;
        }
        writer.WriteEndArray();
        writer.WriteNumber("count", count);
        writer.WriteEndObject();
        writer.Flush();
        return count;
    }

    private static void WriteFilter(Utf8JsonWriter writer, ActivityFilter filter)
    {
        writer.WriteStartObject("filter");
        if (filter.Assistant is not null) writer.WriteString("assistant", filter.Assistant);
        if (filter.Mission is not null) writer.WriteString("mission", filter.Mission);
        if (filter.Agent is not null) writer.WriteString("agent", filter.Agent);
        if (filter.Class is not null) writer.WriteString("class", filter.Class);
        if (filter.Grant is not null) writer.WriteString("grant", filter.Grant);
        if (filter.From is { } from) writer.WriteString("from", Day(from));
        if (filter.To is { } to) writer.WriteString("to", Day(to));
        if (!string.IsNullOrWhiteSpace(filter.Text)) writer.WriteString("text", filter.Text.Trim());
        writer.WriteEndObject();
    }

    private static void WriteVerification(Utf8JsonWriter writer, AuditVerification? verification)
    {
        writer.WriteStartObject("verification");
        if (verification is null)
        {
            writer.WriteString("status", "NotChecked");
            writer.WriteString("summary", "The chain was not checked for this export.");
        }
        else
        {
            writer.WriteString("status", verification.Status.ToString());
            writer.WriteString("summary", Mask(verification.Summary));
            writer.WriteNumber("linesVerified", verification.LinesVerified);
            if (verification.Reason is not null) writer.WriteString("reason", Mask(verification.Reason));
            if (verification.FirstFailure is { } at)
            {
                writer.WriteStartObject("firstFailure");
                writer.WriteString("file", at.File);
                writer.WriteNumber("line", at.Line);
                if (at.Sequence is { } sequence) writer.WriteNumber("seq", sequence);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteRow(Utf8JsonWriter writer, ActivityRow row)
    {
        writer.WriteStartObject();
        if (row.At.Sequence is { } sequence) writer.WriteNumber("seq", sequence);
        if (row.Kind != ActivityRowKind.Unreadable) writer.WriteString("time", Time(row.Time));
        writer.WriteString("file", row.At.File);
        writer.WriteNumber("line", row.At.Line);
        writer.WriteString("trust", TrustName(row.Trust));
        writer.WriteString("kind", KindName(row.Kind));
        if (row.Event is not null) writer.WriteString("event", Mask(row.Event));
        if (row.Kind != ActivityRowKind.Unreadable)
        {
            writer.WriteString("assistant", Mask(row.Assistant));
            if (row.Mission is not null) writer.WriteString("mission", Mask(row.Mission));
            if (row.Agent is not null) writer.WriteString("agent", Mask(row.Agent));
            writer.WriteString("tool", Mask(row.Tool));
            writer.WriteString("class", Mask(row.Class));
            if (row.Target is not null) writer.WriteString("target", Mask(row.Target));
            writer.WriteStartArray("inputs");
            foreach (var input in row.Inputs) WriteInput(writer, input);
            writer.WriteEndArray();
            if (row.InputsNotLogged > 0) writer.WriteNumber("inputsNotLogged", row.InputsNotLogged);
            if (row.Result is not null) writer.WriteString("result", Mask(row.Result));
            if (row.CostUsd is { } cost) writer.WriteNumber("costUsd", cost);
            if (row.Tokens is { } tokens) writer.WriteNumber("tokens", tokens);
            if (row.AllowedBy is not null) writer.WriteString("allowedBy", Mask(row.AllowedBy));
        }
        if (row.Note is not null) writer.WriteString("note", Mask(row.Note));
        if (row.Prev.Length > 0) writer.WriteString("prev", row.Prev);
        if (row.Hmac.Length > 0) writer.WriteString("hmac", row.Hmac);
        writer.WriteEndObject();
    }

    private static void WriteInput(Utf8JsonWriter writer, ActivityInput input)
    {
        writer.WriteStartObject();
        writer.WriteString("name", Mask(input.Name));
        switch (input.Kind)
        {
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
            default:
                writer.WriteString("text", Mask(input.Text));
                break;
        }
        writer.WriteEndObject();
    }

    // ---- CSV --------------------------------------------------------------------------------------------------------

    private static int WriteCsv(Stream output, IEnumerable<ActivityRow> rows, CancellationToken cancellation)
    {
        var count = 0;
        // A byte order mark, so a spreadsheet reads the file as UTF-8; RFC 4180 line ends.
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 65536, leaveOpen: true) { NewLine = "\r\n" };
        writer.Write(string.Join(',', CsvColumns));
        writer.Write("\r\n");
        var cells = new string?[CsvColumns.Count];
        foreach (var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            Fill(cells, row);
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0) writer.Write(',');
                WriteCell(writer, cells[i], guarded: i is not (0 or 3 or 15 or 16)); // seq, line, costUsd and tokens are numbers
            }
            writer.Write("\r\n");
            count++;
        }
        writer.Flush();
        return count;
    }

    private static void Fill(string?[] cells, ActivityRow row)
    {
        var read = row.Kind != ActivityRowKind.Unreadable;
        cells[0] = row.At.Sequence?.ToString(CultureInfo.InvariantCulture);
        cells[1] = read ? Time(row.Time) : null;
        cells[2] = row.At.File;
        cells[3] = row.At.Line.ToString(CultureInfo.InvariantCulture);
        cells[4] = TrustName(row.Trust);
        cells[5] = KindName(row.Kind);
        cells[6] = row.Event is null ? null : Mask(row.Event);
        cells[7] = read ? Mask(row.Assistant) : null;
        cells[8] = row.Mission is null ? null : Mask(row.Mission);
        cells[9] = row.Agent is null ? null : Mask(row.Agent);
        cells[10] = read ? Mask(row.Tool) : null;
        cells[11] = read ? Mask(row.Class) : null;
        cells[12] = row.Target is null ? null : Mask(row.Target);
        cells[13] = read ? string.Join("; ", row.Inputs.Select(i => Mask(i.Name) + "=" + (i.Kind == AuditInputKind.Text ? Mask(i.Text) : i.Display))) : null;
        cells[14] = row.Result is null ? null : Mask(row.Result);
        cells[15] = row.CostUsd?.ToString(CultureInfo.InvariantCulture);
        cells[16] = row.Tokens?.ToString(CultureInfo.InvariantCulture);
        cells[17] = row.AllowedBy is null ? null : Mask(row.AllowedBy);
        cells[18] = row.Note is null ? null : Mask(row.Note);
        cells[19] = row.Prev.Length == 0 ? null : row.Prev;
        cells[20] = row.Hmac.Length == 0 ? null : row.Hmac;
    }

    /// <summary>
    /// One cell (RFC 4180: quoted when it holds a comma, a quote or a line break, quotes doubled). A text that begins with
    /// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a line break would run as a formula in a spreadsheet (a file path or
    /// a command an agent chose can start that way), so it is written with a leading apostrophe.
    /// </summary>
    internal static void WriteCell(TextWriter writer, string? value, bool guarded)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (guarded && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') value = "'" + value;
        if (value.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            writer.Write(value);
            return;
        }
        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        writer.Write('"');
    }

    // ---- shared -----------------------------------------------------------------------------------------------------

    private static string Mask(string? text) => string.IsNullOrEmpty(text) ? "" : AuditRedactor.Redact(text);

    private static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string TrustName(ActivityTrust trust) => trust switch
    {
        ActivityTrust.Verified => "verified",
        ActivityTrust.NotVerified => "notVerified",
        _ => "unchecked",
    };

    private static string KindName(ActivityRowKind kind) => kind switch
    {
        ActivityRowKind.Action => "action",
        ActivityRowKind.Record => "record",
        _ => "unreadable",
    };
}
