// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-1): the stored result of the hardware check and
// self-test, the voiceHardware settings (voice-SPEC V-16.4), and its key (V-10.5).
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Port.Voice;

/// <summary>Who ran the check (V-16.4): the first-run check by itself, or the user's Check my hardware.</summary>
public enum HardwareOrigin { Auto, Manual }

/// <summary>Which inference backend the check found working (V-16.4, V-10.1): Vulkan, CUDA, or the processor alone.</summary>
public enum VoiceBackend { Vulkan, Cuda, Cpu }

/// <summary>
/// The <c>voiceHardware</c> settings (V-16.4): the check's key and what it found and chose. <see cref="LiveWords"/> is the
/// <c>LiveWords</c> switch of V-7.7, read from the record at the start of a session.
/// </summary>
public sealed record VoiceHardwareRecord
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "voiceHardware";

    public string Key { get; init; } = "";
    public HardwareOrigin Origin { get; init; } = HardwareOrigin.Auto;
    public string Cpu { get; init; } = "";
    public int Cores { get; init; }
    public int RamGB { get; init; }
    public string Gpu { get; init; } = "";
    public double VramGB { get; init; }
    public double FreeVramGB { get; init; }
    public VoiceBackend Backend { get; init; } = VoiceBackend.Cpu;
    public string Candidate { get; init; } = "";
    public string Model { get; init; } = "";
    public double Rtf { get; init; }
    public bool LiveWords { get; init; }

    /// <summary>When the check ran (UTC, whole seconds).</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UnixEpoch;

    /// <summary>The app's version and build the check ran under.</summary>
    public string App { get; init; } = "";

    private static readonly string[] OriginNames = ["auto", "manual"];
    private static readonly string[] BackendNames = ["vulkan", "cuda", "cpu"];

    /// <summary>Compact JSON in V-16.4's key order; <c>at</c> as <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, VoiceJson.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("key", Key);
            w.WriteString("origin", VoiceJson.WireName(OriginNames, Origin, HardwareOrigin.Auto));
            w.WriteString("cpu", Cpu);
            w.WriteNumber("cores", Cores);
            w.WriteNumber("ramGB", RamGB);
            w.WriteString("gpu", Gpu);
            VoiceJson.WriteNumberOrNull(w, "vramGB", VramGB);
            VoiceJson.WriteNumberOrNull(w, "freeVramGB", FreeVramGB);
            w.WriteString("backend", VoiceJson.WireName(BackendNames, Backend, VoiceBackend.Cpu));
            w.WriteString("candidate", Candidate);
            w.WriteString("model", Model);
            VoiceJson.WriteNumberOrNull(w, "rtf", Rtf);
            w.WriteBoolean("liveWords", LiveWords);
            w.WriteString("at", At.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture));
            w.WriteString("app", App);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Lenient read (V-16.6): null when nothing readable is stored (the check runs); otherwise a missing or unreadable field keeps
    /// its default, which for the key is empty, so the record is stale (V-10.5).
    /// </summary>
    public static VoiceHardwareRecord? FromJson(ReadOnlySpan<byte> json) => VoiceJson.ReadObject<VoiceHardwareRecord?>(json, null, o =>
    {
        var d = new VoiceHardwareRecord();
        var at = DateTimeOffset.UnixEpoch;
        if (VoiceJson.Text(o, "at") is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            at = parsed;
        return new VoiceHardwareRecord
        {
            Key = VoiceJson.Text(o, "key") ?? d.Key,
            Origin = VoiceJson.Name(o, "origin", OriginNames, d.Origin),
            Cpu = VoiceJson.Text(o, "cpu") ?? d.Cpu,
            Cores = VoiceJson.Int(o, "cores", d.Cores),
            RamGB = VoiceJson.Int(o, "ramGB", d.RamGB),
            Gpu = VoiceJson.Text(o, "gpu") ?? d.Gpu,
            VramGB = VoiceJson.Number(o, "vramGB", d.VramGB),
            FreeVramGB = VoiceJson.Number(o, "freeVramGB", d.FreeVramGB),
            Backend = VoiceJson.Name(o, "backend", BackendNames, d.Backend),
            Candidate = VoiceJson.Text(o, "candidate") ?? d.Candidate,
            Model = VoiceJson.Text(o, "model") ?? d.Model,
            Rtf = VoiceJson.Number(o, "rtf", d.Rtf),
            LiveWords = VoiceJson.Bool(o, "liveWords", d.LiveWords),
            At = at,
            App = VoiceJson.Text(o, "app") ?? d.App,
        };
    });
}

/// <summary>What the key of a stored result is made of (V-10.5): adapter description and driver version, CPU name, RAM GB, app version and build, Whisper.net runtime version.</summary>
public sealed record HardwareKeyInputs(string AdapterDescription, string DriverVersion, string CpuName, int RamGB, string App, string RuntimeVersion);

/// <summary>The key of a stored hardware result and the stale test (V-10.5).</summary>
public static class HardwareKey
{
    /// <summary>
    /// SHA-256 (lower-case hex) over the inputs, each written as its name, its length in UTF-16 units and its text, so values that
    /// run together cannot collide (ED: V-10.5 names the inputs, not the byte layout).
    /// </summary>
    public static string Compute(HardwareKeyInputs inputs)
    {
        var builder = new StringBuilder();
        Append(builder, "adapter", inputs.AdapterDescription);
        Append(builder, "driver", inputs.DriverVersion);
        Append(builder, "cpu", inputs.CpuName);
        Append(builder, "ramGB", inputs.RamGB.ToString(CultureInfo.InvariantCulture));
        Append(builder, "app", inputs.App);
        Append(builder, "runtime", inputs.RuntimeVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// The HARDWARE part of the key (lead ruling R5, 2026-10-04, from the OD-0099 Q-V-1 C amendment): adapter description, driver version,
    /// processor name and memory GB, without the app version and build or the Whisper.net runtime version. The slow-PC answer is stored
    /// with it (<c>voiceTyping.liveWordsChoiceKey</c>), so the question comes back on new hardware only, never after an app update.
    /// Same encoding as <see cref="Compute(HardwareKeyInputs)"/>, under its own leading name so it never equals a full key.
    /// </summary>
    public static string ComputeHardwarePart(HardwareKeyInputs inputs)
    {
        var builder = new StringBuilder();
        Append(builder, "part", "hardware");
        Append(builder, "adapter", inputs.AdapterDescription);
        Append(builder, "driver", inputs.DriverVersion);
        Append(builder, "cpu", inputs.CpuName);
        Append(builder, "ramGB", inputs.RamGB.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>The hardware part of a probe's profile (R5).</summary>
    public static string ComputeHardwarePart(HardwareProfile profile) =>
        ComputeHardwarePart(new HardwareKeyInputs(profile.GpuDescription, profile.DriverVersion, profile.CpuName, profile.MemoryGB, "", ""));

    /// <summary>The key of a probe's profile under this app build and Whisper.net runtime.</summary>
    public static string Compute(HardwareProfile profile, string app, string runtimeVersion) =>
        Compute(new HardwareKeyInputs(profile.GpuDescription, profile.DriverVersion, profile.CpuName, profile.MemoryGB, app, runtimeVersion));

    /// <summary>V-10.5: a stored result with another key than today's, or no stored result, is stale: the check runs again at the next first-run moment.</summary>
    public static bool IsStale(VoiceHardwareRecord? stored, string currentKey) =>
        stored is null || !string.Equals(stored.Key, currentKey, StringComparison.Ordinal);

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append(':').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
}
