// Source: DroppyCode/Services/Store/TokenLedger.swift (B2 1fbf81e), :1-708.
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Store;

/// PORT-ONLY regex helper for CodexFileFingerprint.RolloutIdentity (:258), kept top-level so the nested nesting
/// types around it need not all be declared partial for the source generator.
internal static partial class TokenLedgerRegex
{
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    public static partial Regex UuidPattern();
}

/// Converts a cumulative token counter into per-event spend. :4-34
public sealed record TokenSpendTracker
{
    // :15-16 mutating func fields; R-VAL-1 struct -> immutable record, mutating returns the new value.
    private long LastTotal { get; init; }
    private bool Seeded { get; init; }

    /// :18-22. Returns the tracker's next value and the spend for this call (Swift's `mutating func` return).
    public (TokenSpendTracker Next, long Spent) Spend(long total, long before = 0)
    {
        var seeded = Seeded;
        var lastTotal = LastTotal;
        if (!seeded) // :19
        {
            seeded = true;
            lastTotal = Math.Max(0, Math.Min(total, before));
        }
        var spent = Math.Max(0, total - lastTotal); // :21, computed before the deferred update below
        lastTotal = Math.Max(0, total); // :20 defer
        return (this with { LastTotal = lastTotal, Seeded = seeded }, spent);
    }

    /// :29-33
    public TokenSpendTracker SeedBaseline(long total)
    {
        if (Seeded) return this; // :30
        return this with { Seeded = true, LastTotal = Math.Max(0, total) }; // :31-32
    }
}

/// Daily AI coding token spend across every provider. :36-196
public sealed class TokenLedger : MainActorModel
{
    // :64 static let shared = TokenLedger(); Ambient so tests can replace it (R-LIFE-2).
    public static Ambient<TokenLedger> Shared { get; } = new(() => new TokenLedger());

    // :67 private(set) var dailyTotals
    private ValueMap<string, long> dailyTotals;
    public ValueMap<string, long> DailyTotals
    {
        get => Read(dailyTotals);
        private set => Set(ref dailyTotals, value);
    }

    // :73 private(set) var legacyDaily
    private ValueMap<string, long> legacyDaily;
    public ValueMap<string, long> LegacyDaily
    {
        get => Read(legacyDaily);
        private set => Set(ref legacyDaily, value);
    }

    /// :78-81. Sum of legacyDaily's values whose key falls in the current year, per Calendar.current (ED-094:
    /// CultureInfo.CurrentCulture.Calendar with the local zone).
    public long UncertainTokensThisYear => Computed(() =>
    {
        var year = string.Format(CultureInfo.InvariantCulture, "{0:D4}-", CurrentYear(SwiftDate.Now)); // :79
        long total = 0;
        foreach (var (key, value) in LegacyDaily) // :80
            if (key.StartsWith(year, StringComparison.Ordinal)) total += value; // hasPrefix: ASCII digits/hyphen only
        return total;
    });

    // :85 private static let legacyLiveDefaultsKey
    private const string LegacyLiveDefaultsKey = "droppycode.tokenActivity.liveDaily";

    /// :87. `CaptureRun.defaults` is always nil: WebsiteCaptures and TourCaptures, the only callers that would
    /// ever enable a capture run, are not ported (dead_code.md #44), so capture mode never activates and the
    /// ledger always reads and writes UserDefaults.standard.
    private static ISettingsStore Defaults => UserDefaults.Standard.Current;

    // :90 private var state = LiveSpendState()
    private LiveSpendState state;
    // :91 private var historyDaily
    private ValueMap<string, long> historyDaily = ValueMap<string, long>.Empty;
    // :92 private var didStartLoad = false
    private bool didStartLoad;
    // :94 @ObservationIgnored private var liveSave: Task<Void, Never>?
    private TaskHandle? liveSave;
    // :97 private let launchDate = Date()
    private readonly DateTimeOffset launchDate;

    // :99-103
    private TokenLedger()
    {
        launchDate = SwiftDate.Now; // :97 (evaluated once, at construction)
        state = LiveSpendStateStore.Load(Defaults); // :100
        legacyDaily = state.LegacyDaily; // :101, backing-field assign: no didSet in init
        dailyTotals = MergedTotals(); // :102
    }

    /// :107-109
    public void Record(long spend, DateTimeOffset? on = null) => Record(spend, null, on ?? SwiftDate.Now);

    /// :114-116. Codex callers must use RecordCodexSpend instead.
    public void Record(TokenSpend spend, DateTimeOffset? on = null) => Record(spend.TotalTokens, null, on ?? SwiftDate.Now);

    /// :122-126
    public void RecordCodexSpend(long spend, string rolloutID, DateTimeOffset? on = null)
    {
        if (rolloutID.Length == 0) return; // :124
        Record(spend, SwiftString.Lowercased(rolloutID), on ?? SwiftDate.Now); // :125
    }

    // :128-145
    private void Record(long spend, string? codexRollout, DateTimeOffset date)
    {
        if (spend <= 0) return; // :129
        var day = DayKey(date); // :130
        state = state with { Daily = state.Daily.Mutating(d => d[day] = d.GetValueOrDefault(day) + spend) }; // :131
        DailyTotals = DailyTotals.Mutating(d => d[day] = d.GetValueOrDefault(day) + spend); // :132
        if (codexRollout is { } rollout) // :133-135
        {
            state = state with { LiveCodex = state.LiveCodex.Mutating(d => d[rollout] = d.GetValueOrDefault(rollout) + spend) };
        }
        if (liveSave is not null) return; // :138
        liveSave = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(2)); // :140
            if (SwiftTask.IsCancelled) return; // :141
            liveSave = null; // :142
            LiveSpendStateStore.Save(state); // :143
        });
    }

    /// :148-153
    public void FlushLiveSpend()
    {
        if (liveSave is not { } save) return; // :149
        save.Cancel(); // :150
        liveSave = null; // :151
        LiveSpendStateStore.Save(state); // :152
    }

    /// :157-182
    public async Task RefreshIfNeeded()
    {
        if (didStartLoad) return; // :158
        didStartLoad = true; // :159
        var liveCutoff = launchDate; // :160
        var settled = state.SettledCodex; // :165
        var history = await TaskHandle.Detached(async () =>
        {
            var result = TokenHistoryScanner.ScanCodexHistory(CodexFingerprintStore.Load(), liveCutoff, settled); // :171-174
            CodexFingerprintStore.Save(result.Fingerprints); // :175
            return result;
        }).Value; // :169-177 (priority dropped, R-TASK-1)
        historyDaily = history.Daily; // :178
        DailyTotals = MergedTotals(); // :181
    }

    // :184-189
    private ValueMap<string, long> MergedTotals()
    {
        var totals = new Dictionary<string, long>(state.LegacyDaily, SwiftStringComparer.Instance); // :185
        foreach (var (day, tokens) in state.Daily) totals[day] = totals.GetValueOrDefault(day) + tokens; // :186
        foreach (var (day, tokens) in historyDaily) totals[day] = totals.GetValueOrDefault(day) + tokens; // :187
        return new ValueMap<string, long>(totals, SwiftStringComparer.Instance); // :188
    }

    /// :191-195. Nonisolated so the background scan shares it. ED-094: Calendar.current -> CultureInfo.CurrentCulture.Calendar,
    /// with Calendar.current's own zone (the local zone, ED-034's fallback).
    public static string DayKey(DateTimeOffset date)
    {
        var zone = SwiftFormatters.CurrentTimeZone.Current;
        var local = TimeZoneInfo.ConvertTime(date, zone).DateTime;
        var calendar = CultureInfo.CurrentCulture.Calendar;
        var year = calendar.GetYear(local);
        var month = calendar.GetMonth(local);
        var day = calendar.GetDayOfMonth(local);
        return string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}-{2:D2}", year, month, day); // :194 %04d-%02d-%02d
    }

    /// Calendar.current.component(.year, from:), ED-094.
    private static int CurrentYear(DateTimeOffset date)
    {
        var zone = SwiftFormatters.CurrentTimeZone.Current;
        var local = TimeZoneInfo.ConvertTime(date, zone).DateTime;
        return CultureInfo.CurrentCulture.Calendar.GetYear(local);
    }
}

/// One Codex rollout's contribution, cached by path. :198-262
public sealed record CodexFileFingerprint
{
    public double Mtime { get; init; } // :202
    public ulong Size { get; init; } // :203
    public long Tokens { get; init; } // :204
    public string Day { get; init; } = ""; // :205
    public string Identity { get; init; } = ""; // :209

    /// :219-238 init?(dict:path:), the legacy-preference migration path.
    public static CodexFileFingerprint? FromLegacyDict(IReadOnlyDictionary<string, object> dict, string path)
    {
        if (!dict.TryGetValue("mtime", out var mtimeRaw) || Plist.AsDouble(mtimeRaw) is not { } mtime) return null; // :220
        if (!dict.TryGetValue("day", out var dayRaw) || dayRaw is not string day) return null; // :221
        if (!dict.TryGetValue("size", out var sizeRaw) || Plist.AsInt(sizeRaw) is not { } sizeValue) return null; // :222-229
        var tokens = dict.TryGetValue("tokens", out var tokensRaw) ? Plist.AsInt(tokensRaw) ?? 0 : 0; // :230
        var identity = dict.TryGetValue("identity", out var identityRaw) && identityRaw is string s ? s : ""; // :231
        return new CodexFileFingerprint
        {
            Mtime = mtime,
            Size = (ulong)Math.Max(0, sizeValue),
            Tokens = tokens,
            Day = day,
            Identity = identity.Length == 0 ? RolloutIdentity(path) : identity, // :237
        };
    }

    /// :241-248 init(from decoder:).
    public static CodexFileFingerprint Decode(JsonElement element) => new()
    {
        Mtime = SwiftDecoding.Decode(element, "mtime", SwiftDecoding.ReadDouble), // :244
        Size = SwiftDecoding.Decode(element, "size", SwiftDecoding.ReadUInt), // :245
        Tokens = Coding.Value(element, "tokens", SwiftDecoding.ReadInt, () => 0L), // :246
        Day = Coding.Value(element, "day", SwiftDecoding.ReadString, () => ""), // :247
        Identity = Coding.Value(element, "identity", SwiftDecoding.ReadString, () => ""), // :248
    };

    /// Synthesized encode(to:), declaration order.
    public static void Encode(Utf8JsonWriter writer, CodexFileFingerprint value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("mtime", value.Mtime);
        writer.WriteNumber("size", value.Size);
        writer.WriteNumber("tokens", value.Tokens);
        writer.WriteString("day", value.Day);
        writer.WriteString("identity", value.Identity);
        writer.WriteEndObject();
    }

    /// :250-261. The last UUID-shaped run in the base name, matched backwards, lowercased; falls back to the
    /// whole path when no run is found.
    public static string RolloutIdentity(string path)
    {
        var name = SwiftPath.LastPathComponent(path); // :257
        var matches = TokenLedgerRegex.UuidPattern().Matches(name); // :258 [.regularExpression, .backwards]
        if (matches.Count == 0) return path; // :259
        return SwiftString.Lowercased(matches[^1].Value); // :260, last match = backwards search
    }
}

/// Where the Codex fingerprints live. :264-320
internal static class CodexFingerprintStore
{
    private const string LegacyDefaultsKey = "droppycode.tokenActivity.codexFiles"; // :271

    private static string FileURL => SwiftPath.AppendingPathComponent(Storage.Root, "token-activity-codex-v3.json"); // :274

    /// :277-314
    public static ValueMap<string, CodexFileFingerprint> Load()
    {
        var oldURL = SwiftPath.AppendingPathComponent(Storage.Root, "token-activity-codex.json"); // :281
        if (File.Exists(oldURL)) SwiftTry.Ignore(() => FileOps.RemoveItem(oldURL)); // :282-284

        if (TryReadFingerprints(FileURL) is { } v3) return v3; // :285-288

        var v2URL = SwiftPath.AppendingPathComponent(Storage.Root, "token-activity-codex-v2.json"); // :292
        if (TryReadFingerprints(v2URL) is { } v2)
        {
            var rebuilt = new Dictionary<string, CodexFileFingerprint>(SwiftStringComparer.Instance);
            foreach (var (path, fingerprint) in v2) // :296
            {
                var migrated = fingerprint.Identity.Length == 0
                    ? fingerprint with { Identity = CodexFileFingerprint.RolloutIdentity(path) } // :298
                    : fingerprint;
                rebuilt[path] = migrated; // :299
            }
            SwiftTry.Ignore(() => FileOps.RemoveItem(v2URL)); // :301
            var result = new ValueMap<string, CodexFileFingerprint>(rebuilt, SwiftStringComparer.Instance);
            Save(result); // :302
            return result; // :303
        }

        if (UserDefaults.Standard.Current.Dictionary(LegacyDefaultsKey) is not { } legacy) // :305
            return ValueMap<string, CodexFileFingerprint>.Empty; // :306
        UserDefaults.Standard.Current.RemoveObject(LegacyDefaultsKey); // :308
        var migratedMap = new Dictionary<string, CodexFileFingerprint>(SwiftStringComparer.Instance);
        foreach (var (path, value) in legacy) // :309-311
        {
            if (value is IReadOnlyDictionary<string, object> entryDict
                && CodexFileFingerprint.FromLegacyDict(entryDict, path) is { } fingerprint)
                migratedMap[path] = fingerprint;
        }
        var migratedResult = new ValueMap<string, CodexFileFingerprint>(migratedMap, SwiftStringComparer.Instance);
        Save(migratedResult); // :312
        return migratedResult; // :313
    }

    private static ValueMap<string, CodexFileFingerprint>? TryReadFingerprints(string url)
    {
        byte[] data;
        try { data = File.ReadAllBytes(url); }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return null; }
        try
        {
            using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
            return new ValueMap<string, CodexFileFingerprint>(
                SwiftDecoding.ReadMap(document.RootElement, CodexFileFingerprint.Decode), SwiftStringComparer.Instance);
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return null; }
    }

    /// :316-319
    public static void Save(ValueMap<string, CodexFileFingerprint> fingerprints)
    {
        SwiftTry.Ignore(() =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var (key, value) in fingerprints)
                {
                    writer.WritePropertyName(key);
                    CodexFileFingerprint.Encode(writer, value);
                }
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(buffer.WrittenMemory, SwiftJson.DocumentOptions);
            var bytes = SwiftJson.Write(document.RootElement, SwiftJsonStyle.PlainEncoder);
            FileOps.WriteAtomic(FileURL, bytes);
        });
    }
}

/// Live spend, versioned. :322-352
internal sealed record LiveSpendState
{
    public ValueMap<string, long> Daily { get; init; } = ValueMap<string, long>.Empty; // :332
    public ValueMap<string, long> SettledCodex { get; init; } = ValueMap<string, long>.Empty; // :335
    public ValueMap<string, long> LiveCodex { get; init; } = ValueMap<string, long>.Empty; // :339
    public ValueMap<string, long> LegacyDaily { get; init; } = ValueMap<string, long>.Empty; // :341

    /// :345-351
    public static LiveSpendState Decode(JsonElement element) => new()
    {
        Daily = Coding.Value(element, "daily", ReadLongMap, () => ValueMap<string, long>.Empty),
        SettledCodex = Coding.Value(element, "settledCodex", ReadLongMap, () => ValueMap<string, long>.Empty),
        LiveCodex = Coding.Value(element, "liveCodex", ReadLongMap, () => ValueMap<string, long>.Empty),
        LegacyDaily = Coding.Value(element, "legacyDaily", ReadLongMap, () => ValueMap<string, long>.Empty),
    };

    private static ValueMap<string, long> ReadLongMap(JsonElement element) =>
        new(SwiftDecoding.ReadMap(element, SwiftDecoding.ReadInt), SwiftStringComparer.Instance);
}

/// :354-383
internal static class LiveSpendStateStore
{
    private static string FileURL => SwiftPath.AppendingPathComponent(Storage.Root, "token-activity-live-v2.json"); // :356

    /// :359-377
    public static LiveSpendState Load(ISettingsStore defaults)
    {
        var state = TryReadState() is { } stored
            ? stored // :363
            : new LiveSpendState { LegacyDaily = ReadLegacyDaily(defaults) }; // :365-367

        // :369-375. The process that recorded liveCodex is gone: its Codex spend is settled now.
        var settled = new Dictionary<string, long>(state.SettledCodex, SwiftStringComparer.Instance);
        foreach (var (identity, tokens) in state.LiveCodex) settled[identity] = settled.GetValueOrDefault(identity) + tokens; // :372-374
        return state with
        {
            SettledCodex = new ValueMap<string, long>(settled, SwiftStringComparer.Instance),
            LiveCodex = ValueMap<string, long>.Empty, // :375
        };
    }

    private static LiveSpendState? TryReadState()
    {
        byte[] data;
        try { data = File.ReadAllBytes(FileURL); }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return null; }
        try
        {
            using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
            return LiveSpendState.Decode(document.RootElement);
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return null; }
    }

    /// :367 defaults.dictionary(forKey:) as? [String: Int] ?? [:]. The whole-dictionary cast fails (giving [:])
    /// unless every value converts to Int.
    private static ValueMap<string, long> ReadLegacyDaily(ISettingsStore defaults)
    {
        if (defaults.Dictionary(TokenLedgerKeys.LegacyLiveDefaultsKey) is not { } raw) return ValueMap<string, long>.Empty;
        var result = new Dictionary<string, long>(SwiftStringComparer.Instance);
        foreach (var (key, value) in raw)
        {
            if (Plist.AsInt(value) is not { } number) return ValueMap<string, long>.Empty;
            result[key] = number;
        }
        return new ValueMap<string, long>(result, SwiftStringComparer.Instance);
    }

    /// :379-382
    public static void Save(LiveSpendState state)
    {
        SwiftTry.Ignore(() =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                WriteLongMap(writer, "daily", state.Daily);
                WriteLongMap(writer, "settledCodex", state.SettledCodex);
                WriteLongMap(writer, "liveCodex", state.LiveCodex);
                WriteLongMap(writer, "legacyDaily", state.LegacyDaily);
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(buffer.WrittenMemory, SwiftJson.DocumentOptions);
            var bytes = SwiftJson.Write(document.RootElement, SwiftJsonStyle.PlainEncoder);
            FileOps.WriteAtomic(FileURL, bytes);
        });
    }

    private static void WriteLongMap(Utf8JsonWriter writer, string name, ValueMap<string, long> map)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach (var (key, value) in map) writer.WriteNumber(key, value);
        writer.WriteEndObject();
    }
}

/// PORT-ONLY: the v1 preference key (:85), shared between TokenLedger.Defaults's caller and LiveSpendStateStore.
internal static class TokenLedgerKeys
{
    public const string LegacyLiveDefaultsKey = "droppycode.tokenActivity.liveDaily";
}

/// Background Codex history scan. :385-587
internal static class TokenHistoryScanner
{
    public sealed record Result(ValueMap<string, long> Daily, ValueMap<string, CodexFileFingerprint> Fingerprints, ValueSet<string> Identities); // :388-394

    private const int TailBytes = 131_072; // :398
    private const int HistoryMonths = 12; // :399
    private const int HeadBytes = 8 * 1_048_576; // :498

    /// :400-462
    public static Result ScanCodexHistory(ValueMap<string, CodexFileFingerprint> cached, DateTimeOffset liveCutoff, ValueMap<string, long> settledLive)
    {
        var fingerprints = new Dictionary<string, CodexFileFingerprint>(cached, SwiftStringComparer.Instance); // :405
        var seen = new HashSet<string>(SwiftStringComparer.Instance); // :406
        var identities = new HashSet<string>(SwiftStringComparer.Instance); // :407
        var contributions = new Dictionary<string, List<(string Day, long Tokens)>>(SwiftStringComparer.Instance); // :411
        var cutoff = AddCalendarMonths(SwiftDate.Now, -HistoryMonths); // :412, ED-094
        foreach (var directory in CodexDirectories()) // :414
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories); } // :415-419
            catch (Exception e) when (SwiftTry.IsThrownError(e)) { continue; }
            foreach (var path in files)
            {
                if (!string.Equals(SwiftPath.PathExtension(path), "jsonl", StringComparison.Ordinal)) continue; // :421
                if (IsHidden(path)) continue; // .skipsHiddenFiles
                DateTimeOffset mtime;
                ulong size;
                try
                {
                    var info = new FileInfo(path);
                    mtime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero); // :423
                    size = (ulong)Math.Max(0, info.Length); // :429
                }
                catch (Exception e) when (SwiftTry.IsThrownError(e)) { continue; }
                if (mtime < cutoff || mtime >= liveCutoff) continue; // :424-428
                seen.Add(path); // :431
                var mtimeSeconds = SwiftDate.Seconds1970(mtime);
                if (fingerprints.TryGetValue(path, out var known) && known.Mtime == mtimeSeconds && known.Size == size) // :432
                {
                    var identity = known.Identity.Length == 0 ? CodexFileFingerprint.RolloutIdentity(path) : known.Identity; // :433-435
                    identities.Add(identity); // :436
                    if (known.Tokens > 0) AddContribution(contributions, identity, known.Day, known.Tokens); // :437
                    continue; // :438
                }
                var parsed = TailContribution(path); // :440
                var newIdentity = CodexFileFingerprint.RolloutIdentity(path); // :441
                var fingerprint = new CodexFileFingerprint // :442-448
                {
                    Mtime = mtimeSeconds,
                    Size = size,
                    Tokens = parsed?.Tokens ?? 0,
                    Day = parsed?.Day ?? "",
                    Identity = newIdentity,
                };
                fingerprints[path] = fingerprint; // :449
                identities.Add(newIdentity); // :450
                if (parsed is { Tokens: > 0 } p) AddContribution(contributions, newIdentity, p.Day, p.Tokens); // :451
            }
        }
        foreach (var key in fingerprints.Keys.ToList()) // :456
            if (!seen.Contains(key)) fingerprints.Remove(key);
        return new Result(
            Reconcile(contributions, settledLive), // :458
            new ValueMap<string, CodexFileFingerprint>(fingerprints, SwiftStringComparer.Instance), // :459
            new ValueSet<string>(identities, SwiftStringComparer.Instance)); // :460
    }

    private static void AddContribution(Dictionary<string, List<(string Day, long Tokens)>> contributions, string identity, string day, long tokens)
    {
        if (!contributions.TryGetValue(identity, out var list)) contributions[identity] = list = [];
        list.Add((day, tokens));
    }

    /// :468-488. Newest day first; never negative.
    private static ValueMap<string, long> Reconcile(Dictionary<string, List<(string Day, long Tokens)>> contributions, ValueMap<string, long> settledLive)
    {
        var daily = new Dictionary<string, long>(SwiftStringComparer.Instance);
        foreach (var (identity, files) in contributions) // :473
        {
            var remaining = Math.Max(0L, settledLive.TryGetValue(identity, out var s) ? s : 0); // :474
            foreach (var file in files.OrderByDescending(f => f.Day, SwiftStringComparer.Instance)) // :479
            {
                if (file.Tokens <= 0) continue; // :480
                var cleared = Math.Min(remaining, file.Tokens); // :481
                remaining -= cleared; // :482
                var kept = file.Tokens - cleared; // :483
                if (kept > 0) daily[file.Day] = daily.GetValueOrDefault(file.Day) + kept; // :484
            }
        }
        return new ValueMap<string, long>(daily, SwiftStringComparer.Instance);
    }

    /// PORT-ONLY (R-TEST-9): the OS home directory a test points elsewhere, so a scan never walks the real
    /// user's actual %USERPROFILE%\.codex. Empty means "not overridden"; production code never overrides it.
    internal static Ambient<string> HomeOverride { get; } = new("");

    /// :490-496. The OS home directory (not the app's login-environment home).
    private static IReadOnlyList<string> CodexDirectories()
    {
        var home = HomeOverride.Current.Length > 0
            ? HomeOverride.Current
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // :491
        var codex = SwiftPath.AppendingPathComponent(home, ".codex");
        return
        [
            SwiftPath.AppendingPathComponent(codex, "sessions"), // :493
            SwiftPath.AppendingPathComponent(codex, "archived_sessions"), // :494
        ];
    }

    private static bool IsHidden(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return false; }
    }

    /// :498-540. The total the file inherited from the thread it resumed.
    private static long InheritedTotal(string url)
    {
        byte[] head;
        try
        {
            using var stream = File.OpenRead(url);
            var toRead = (int)Math.Min(HeadBytes, stream.Length);
            head = new byte[toRead];
            var readTotal = 0;
            while (readTotal < toRead)
            {
                var n = stream.Read(head, readTotal, toRead - readTotal);
                if (n <= 0) break;
                readTotal += n;
            }
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return 0; }
        var text = SwiftText.DecodeUtf8Repairing(head); // :560-ish lossy decode, matching the tail's own approach
        foreach (var line in SwiftString.Split(text, "\n"))
        {
            if (!line.Contains("\"token_count\"", StringComparison.Ordinal)) continue; // :521
            if (TryParseTokenCountLine(line) is not { } evt) continue; // :522-527
            return Math.Max(0, evt.TotalTokens - evt.LastTokenUsageTotal); // :534
        }
        return 0; // :539
    }

    /// :545-581. The file's own growth, with the event day, or nil when the tail holds no token_count event.
    private static (long Tokens, string Day)? TailContribution(string url)
    {
        try
        {
            using var handle = File.OpenRead(url);
            var fileSize = handle.Length; // :548
            if (fileSize <= 0) return null; // :549
            var offset = fileSize > TailBytes ? fileSize - TailBytes : 0; // :550
            handle.Seek(offset, SeekOrigin.Begin); // :552-555
            using var memory = new MemoryStream();
            handle.CopyTo(memory);
            var data = memory.ToArray(); // :556
            if (data.Length == 0) return null; // :557
            var text = SwiftText.DecodeUtf8Repairing(data); // :560, lossy on purpose
            var lines = SwiftString.Split(text, "\n"); // :561
            for (var i = lines.Count - 1; i >= 0; i--) // reversed()
            {
                var line = lines[i];
                if (!line.Contains("\"token_count\"", StringComparison.Ordinal)) continue; // :562
                if (TryParseTokenCountLine(line) is not { } evt) continue; // :564-569
                string day;
                if (evt.Timestamp is { } stamp && ISO8601.Date(stamp) is { } date) day = TokenLedger.DayKey(date); // :571-572
                else day = ""; // :574
                if (day.Length == 0) continue; // :576
                var own = evt.TotalTokens - InheritedTotal(url); // :577
                return (Math.Max(0, own), day); // :577-578
            }
            return null; // :580
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return null; }
    }

    private readonly record struct TokenCountEvent(long TotalTokens, long LastTokenUsageTotal, string? Timestamp);

    /// Decodes one candidate line into its total_token_usage.total_tokens, last_token_usage.total_tokens and
    /// timestamp (:522-527, :564-569). A missing or non-positive total, or a line that fails to parse, yields null.
    private static TokenCountEvent? TryParseTokenCountLine(string line)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(Encoding.UTF8.GetBytes(line)); }
        catch (Exception e) when (SwiftTry.IsThrownError(e)) { return null; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;
            if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) return null;
            if (!info.TryGetProperty("total_token_usage", out var total) || total.ValueKind != JsonValueKind.Object) return null;
            if (!total.TryGetProperty("total_tokens", out var tokensElement) || tokensElement.ValueKind != JsonValueKind.Number
                || !tokensElement.TryGetInt64(out var tokens) || tokens <= 0) return null;
            long last = 0;
            if (info.TryGetProperty("last_token_usage", out var lastUsage) && lastUsage.ValueKind == JsonValueKind.Object
                && lastUsage.TryGetProperty("total_tokens", out var lastElement) && lastElement.ValueKind == JsonValueKind.Number
                && lastElement.TryGetInt64(out var lastValue))
                last = lastValue;
            string? timestamp = root.TryGetProperty("timestamp", out var timestampElement) && timestampElement.ValueKind == JsonValueKind.String
                ? timestampElement.GetString()
                : null;
            return new TokenCountEvent(tokens, last, timestamp);
        }
    }

    /// Calendar.current.date(byAdding: .month, value:, to:) ?? .distantPast (:412), ED-094.
    private static DateTimeOffset AddCalendarMonths(DateTimeOffset date, int months)
    {
        var zone = SwiftFormatters.CurrentTimeZone.Current;
        var local = TimeZoneInfo.ConvertTime(date, zone).DateTime;
        DateTime shifted;
        try { shifted = CultureInfo.CurrentCulture.Calendar.AddMonths(local, months); }
        catch (ArgumentException) { return SwiftDate.DistantPast; }
        var unspecified = DateTime.SpecifyKind(shifted, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}

/// Which scaling the heatmap colors use. :589-602
public enum TokenActivityMode
{
    [RawValue("daily")] Daily, // :591
    [RawValue("weekly")] Weekly, // :592
    [RawValue("cumulative")] Cumulative, // :593
}

public static class TokenActivityModeExtensions
{
    /// :595-601
    public static string Title(this TokenActivityMode mode) => mode switch
    {
        TokenActivityMode.Daily => "Daily",
        TokenActivityMode.Weekly => "Weekly",
        TokenActivityMode.Cumulative => "Cumulative",
        _ => throw new UnreachableException(),
    };
}

/// One month tick's label and grid column. :616 `(label: String, column: Int)`.
public sealed record MonthTick(string Label, int Column);

/// Contribution grid over the current year. :604-707
public sealed record TokenActivityGrid
{
    public required ValueList<ValueList<DateOnly?>> Columns { get; init; } // :612
    public required ValueList<ValueList<long>> Values { get; init; } // :614
    public required long MaxValue { get; init; } // :615
    public required ValueList<MonthTick> MonthTicks { get; init; } // :616
    public required DateOnly Tomorrow { get; init; } // :618

    public const int Rows = 7; // :620
    public const int Months = 12; // :621

    private static readonly string[] MonthNames = // :625
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// :627-706. A fixed Gregorian calendar with Sunday as the first weekday (never `.current`'s identifier);
    /// `now`'s own local zone supplies "today". `daily` is looked up by TokenLedger.DayKey, which follows
    /// Calendar.current's identifier (ED-094), so the two can disagree for a non-Gregorian user, exactly as
    /// upstream's own dayKey(for:) inside build(daily:mode:now:) does.
    public static TokenActivityGrid Build(IReadOnlyDictionary<string, long> daily, TokenActivityMode mode, DateTimeOffset? now = null)
    {
        var zone = SwiftFormatters.CurrentTimeZone.Current; // Calendar(identifier: .gregorian)'s own timeZone: local
        var nowValue = now ?? SwiftDate.Now; // :627
        var localNow = TimeZoneInfo.ConvertTime(nowValue, zone).DateTime;
        var startOfToday = DateOnly.FromDateTime(localNow); // :630
        var rangeStart = new DateOnly(startOfToday.Year, 1, 1); // :631
        var rangeEnd = rangeStart.AddYears(1).AddDays(-1); // :632
        var tomorrow = startOfToday.AddDays(1); // :633

        // :636. Swift weekday: Sunday=1..Saturday=7; firstWeekday=1 (Sunday); (weekday-1+7)%7 == weekday-1, which
        // is exactly .NET's DayOfWeek (Sunday=0..Saturday=6) for rangeStart.
        var leadingGap = (int)rangeStart.DayOfWeek;
        var gridStart = rangeStart.AddDays(-leadingGap); // :637

        var totalsByDay = new Dictionary<DateOnly, long>(); // :640
        for (var cursor = rangeStart; cursor <= startOfToday; cursor = cursor.AddDays(1)) // :641-647
        {
            var key = TokenLedger.DayKey(LocalMidnight(cursor, zone)); // :643
            if (daily.TryGetValue(key, out var total)) totalsByDay[cursor] = total; // :644
        }

        var columns = new List<List<DateOnly?>>(); // :651
        var day = gridStart; // :652
        while (true) // :653-662
        {
            var column = new List<DateOnly?>();
            for (var r = 0; r < Rows; r++)
            {
                column.Add(day < rangeStart || day > rangeEnd ? null : day); // :656
                day = day.AddDays(1); // :657-658
            }
            columns.Add(column); // :660
            if (day > rangeEnd) break; // :661
        }

        var values = columns.Select(_ => Enumerable.Repeat(0L, Rows).ToList()).ToList(); // :666
        switch (mode)
        {
            case TokenActivityMode.Daily: // :668-673
                for (var c = 0; c < columns.Count; c++)
                    for (var r = 0; r < Rows; r++)
                        if (columns[c][r] is { } d0) values[c][r] = totalsByDay.GetValueOrDefault(d0, 0);
                break;
            case TokenActivityMode.Weekly: // :674-681
                for (var c = 0; c < columns.Count; c++)
                {
                    long week = 0;
                    for (var r = 0; r < Rows; r++)
                        if (columns[c][r] is { } d1) week += totalsByDay.GetValueOrDefault(d1, 0);
                    for (var r = 0; r < Rows; r++) values[c][r] = columns[c][r] is null ? 0 : week;
                }
                break;
            case TokenActivityMode.Cumulative: // :682-689
                long running = 0;
                for (var c = 0; c < columns.Count; c++)
                    for (var r = 0; r < Rows; r++)
                    {
                        if (columns[c][r] is { } d2) running += totalsByDay.GetValueOrDefault(d2, 0);
                        values[c][r] = running;
                    }
                break;
            default:
                throw new UnreachableException();
        }
        var maxValue = values.SelectMany(v => v).DefaultIfEmpty(0).Max(); // :691

        var monthTicks = new List<MonthTick>(); // :694
        for (var offset = 0; offset < Months; offset++) // :695
        {
            var monthDate = rangeStart.AddMonths(offset); // :696
            var monthStart = new DateOnly(monthDate.Year, monthDate.Month, 1); // :697
            var clamped = monthStart < rangeStart ? rangeStart : monthStart; // :698
            var daysOut = clamped.DayNumber - gridStart.DayNumber; // :699
            var column = daysOut / Rows; // :700
            var label = MonthNames[((monthDate.Month - 1) % 12 + 12) % 12]; // :701
            if (column < columns.Count) monthTicks.Add(new MonthTick(label, column)); // :702
        }

        return new TokenActivityGrid
        {
            Columns = new ValueList<ValueList<DateOnly?>>(columns.Select(c => new ValueList<DateOnly?>(c))),
            Values = new ValueList<ValueList<long>>(values.Select(v => new ValueList<long>(v))),
            MaxValue = maxValue,
            MonthTicks = new ValueList<MonthTick>(monthTicks),
            Tomorrow = tomorrow,
        }; // :705
    }

    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var unspecified = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
