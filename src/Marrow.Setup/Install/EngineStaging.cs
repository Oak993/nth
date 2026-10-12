namespace Marrow.Setup.Install;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 5, "Payload staging", and 4's first step; unit U3, ED-NEW-inst-u3-1). Writes Velopack's
/// <c>Setup.exe</c>, stored inside the setup exe, to <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;\nth Setup Engine.exe</c>, a named
/// folder rather than a GUID in Temp, in the background while Welcome or Update shows, so Install starts at once. It is
/// written to a <c>.partial</c> file and moved over the engine only when whole, so a half-written engine never runs. If
/// Install comes first, the first step reads "Preparing files" and the staged bytes ÷ the payload's size drive it. The
/// staging folder is deleted on every exit path the setup controls (<see cref="InstallRun.Finish"/>, and unit U9's sweeps).
/// </summary>
public sealed class EngineStaging
{
    public const string EngineFileName = "nth Setup Engine.exe";

    /// <summary>The embedded payload's resource name (Marrow.Setup.csproj).</summary>
    public const string PayloadResource = "VelopackSetup.exe";

    private readonly Func<Stream?> openPayload;
    private readonly Lock gate = new();
    private Task? staging;
    private long staged;
    private long total = -1;

    /// <param name="folder">The staging folder for this version.</param>
    /// <param name="openPayload">Opens the payload (the embedded resource), or null when this build has none.</param>
    public EngineStaging(string folder, Func<Stream?> openPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        this.openPayload = openPayload ?? throw new ArgumentNullException(nameof(openPayload));
    }

    public string Folder { get; }

    public string EnginePath => Path.Combine(Folder, EngineFileName);

    /// <summary>Bytes written so far.</summary>
    public long StagedBytes => Interlocked.Read(ref staged);

    /// <summary>The payload's size, or -1 until it is known.</summary>
    public long TotalBytes => Interlocked.Read(ref total);

    /// <summary>The engine is in place, whole.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Why staging failed (no payload in this build, no room, access denied), or null.</summary>
    public Exception? Error { get; private set; }

    /// <summary>Staged ÷ total, 0 to 1 (1 once complete).</summary>
    public double Fraction => IsComplete ? 1 : TotalBytes is var size and > 0 ? Math.Clamp(StagedBytes / (double)size, 0, 1) : 0;

    /// <summary>The setup's own payload stream, from its embedded resources.</summary>
    public static Stream? EmbeddedPayload() => typeof(EngineStaging).Assembly.GetManifestResourceStream(PayloadResource);

    /// <summary>Starts staging once (on a pool thread) and returns the same task however often it is asked.</summary>
    public Task StageAsync()
    {
        lock (gate) return staging ??= Task.Run(Stage);
    }

    private void Stage()
    {
        var partial = EnginePath + ".partial";
        try
        {
            using var payload = openPayload() ?? throw new InvalidOperationException("This installer was built without Velopack's Setup.exe.");
            Interlocked.Exchange(ref total, payload.CanSeek ? payload.Length : -1);
            Directory.CreateDirectory(Folder);
            using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = payload.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, read);
                    Interlocked.Add(ref staged, read);
                }
                file.Flush(flushToDisk: true);
            }
            File.Move(partial, EnginePath, overwrite: true);
            IsComplete = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            Error = error;
            try { File.Delete(partial); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }
}
