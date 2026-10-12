// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-45; DESIGN.md 2.6 "Self-check (capability learning)", 2.7 steps 5
// and 6, critiques C9 and C16). Where what Tactus learned is kept between runs: one JSON file in nth's data folder
// (pc-control\capabilities.json), in the capability store's export format, loaded when Tactus starts and saved shortly
// after each change (a pass, a refusal, a focus-moving way, a clear), written whole to a temporary file and moved over the
// old one. Loading keeps only well-formed entries: a known rung and state, a real exe, a time not in the future, and for
// the borrow's ways a version and a framework, so a proof never carries over to another exe, version or framework. A
// missing or broken file loads nothing, and the borrow's ways then start unproven. The NP-4 smoke file
// (%TEMP%\tactus-smoke\np4-proofs.json) is only an optional import for development builds: its borrow passes alone, at
// most 30 days old, never "*", and never over anything learned here.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed class PcCapabilityFile : IDisposable
{
    public const string FileName = "capabilities.json";

    /// <summary>How long an imported NP-4 proof is trusted.</summary>
    public static readonly TimeSpan ProofsFor = TimeSpan.FromDays(30);

    /// <summary>How long a change waits before it is saved (changes close together are saved once).</summary>
    public static readonly TimeSpan SaveAfter = TimeSpan.FromMilliseconds(500);

    private readonly PcCapabilities capabilities;
    private readonly string path;
    private readonly Func<DateTimeOffset> now;
    private readonly object writing = new();
    private int pending;
    private int disposed;

    private PcCapabilityFile(PcCapabilities capabilities, string path, Func<DateTimeOffset> now)
    {
        this.capabilities = capabilities;
        this.path = path;
        this.now = now;
    }

    public string Path => path;

    /// <summary>
    /// Loads <paramref name="path"/> into <paramref name="capabilities"/>, then the development import when one is named,
    /// and saves every later change to <paramref name="path"/>.
    /// </summary>
    public static PcCapabilityFile Open(PcCapabilities capabilities, string path, Func<DateTimeOffset> now, string? devProofs = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(now);
        var file = new PcCapabilityFile(capabilities, path, now);
        if (Read(path) is { } saved) capabilities.Import(Valid(saved, now()));
        if (devProofs is not null && Read(devProofs) is { } proofs) capabilities.Import(Proofs(proofs, now(), capabilities));
        capabilities.Changed += file.OnChanged;
        return file;
    }

    /// <summary>The entries of a saved file that may be loaded, as an export; "[]" when it is not a JSON array.</summary>
    public static string Valid(string json, DateTimeOffset now) => Filter(json, item => Entry(item, now) is not null);

    /// <summary>
    /// The entries of an NP-4 proofs file that may be imported, as an export: passes of the borrow's ways only, each for a
    /// real exe with a version and a framework, at most <see cref="ProofsFor"/> old, and only where nothing is known yet.
    /// </summary>
    public static string Proofs(string json, DateTimeOffset now, PcCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return Filter(json, item => Entry(item, now) is { } entry
            && entry.State == PcLearned.Passed
            && entry.Key.Rung == PcRung.R4
            && PcBorrowProofs.IsProofMethod(entry.Key.Method)
            && now - entry.At <= ProofsFor
            && capabilities.State(entry.Key) == PcLearned.Unknown);
    }

    /// <summary>Saves now when a change is waiting.</summary>
    public void Flush()
    {
        if (Interlocked.Exchange(ref pending, 0) == 1) Save();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        capabilities.Changed -= OnChanged;
        Flush();
    }

    private void OnChanged()
    {
        if (Volatile.Read(ref disposed) == 1 || Interlocked.Exchange(ref pending, 1) == 1) return;
        _ = Task.Delay(SaveAfter).ContinueWith(_ => Flush(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // The whole store, to a temporary file first, then moved over the old one; a failed write keeps learning in memory.
    private void Save()
    {
        lock (writing)
        {
            var temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                File.WriteAllText(temporary, capabilities.Export());
                File.Move(temporary, path, overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? Read(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Filter(string json, Func<JsonObject, bool> keep)
    {
        var kept = new JsonArray();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray items) return kept.ToJsonString();
            foreach (var item in items.OfType<JsonObject>())
            {
                if (keep(item)) kept.Add(item.DeepClone());
            }
        }
        catch (JsonException)
        {
            return new JsonArray().ToJsonString();
        }
        return kept.ToJsonString();
    }

    // One well-formed entry, or null: a real exe, a known rung, a method, a known state other than Unknown, a time not in
    // the future; and for the borrow's ways (rung R4) a version and a framework name.
    private static (PcCapabilityKey Key, PcLearned State, DateTimeOffset At)? Entry(JsonObject item, DateTimeOffset now)
    {
        if (Text(item, "exe") is not { Length: > 0 } exe || exe == PcBorrowProofs.AnyApp) return null;
        if (Text(item, "method") is not { Length: > 0 } method) return null;
        if (Number(item, "rung") is not { } rung || !Enum.IsDefined((PcRung)rung)) return null;
        if (Text(item, "state") is not { } stateText || !Enum.TryParse<PcLearned>(stateText, out var state) || !Enum.IsDefined(state) || state == PcLearned.Unknown) return null;
        if (Number(item, "at") is not { } at) return null;
        DateTimeOffset when;
        try
        {
            when = DateTimeOffset.FromUnixTimeMilliseconds(at);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        if (when > now) return null;
        var version = Text(item, "version") ?? "";
        var control = Text(item, "control") ?? "";
        if ((PcRung)rung == PcRung.R4 && PcBorrowProofs.IsProofMethod(method)
            && (version.Length == 0 || !Enum.TryParse<PcFramework>(control, out var framework) || !Enum.IsDefined(framework) || framework.ToString() != control))
            return null;
        return (new PcCapabilityKey(exe, version, (PcRung)rung, method, control), state, when);
    }

    private static string? Text(JsonObject item, string name) =>
        item[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? Number(JsonObject item, string name) =>
        item[name] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
}
