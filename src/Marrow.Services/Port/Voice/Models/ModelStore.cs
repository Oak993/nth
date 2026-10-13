// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): the model store (voice-SPEC V-9.3, V-9.4, Q-V-13 B).
using System.Security.Cryptography;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Support;

namespace Marrow.Services.Port.Voice.Models;

/// <summary>
/// <c>&lt;data root&gt;\models\whisper\</c> with ggml-&lt;id&gt;.bin files and installed.json (V-9.3). Opening the store reads installed.json and
/// nothing else: no model is read, hashed or repaired at start-up (V-9.4). <see cref="VerifyForLoad"/> is what runs before a load.
/// </summary>
public sealed class ModelStore
{
    private static readonly Logger Log = new("voice", "models");

    private readonly Lock gate = new();
    private readonly string folder;
    private readonly Func<string?> selectedModel;
    private readonly TimeProvider clock;
    private readonly List<InstalledModelRecord> records;
    private readonly Dictionary<string, Func<double>> downloading = new(StringComparer.Ordinal);

    /// <param name="dataRoot">The data root (StoragePaths.Root; a test's folder under MARROW_PORT_DATA_DIR).</param>
    /// <param name="selectedModel">The id of the model selected in voiceTyping.model, or null: the one in use, which Remove refuses.</param>
    public ModelStore(string dataRoot, Func<string?> selectedModel, TimeProvider? clock = null)
    {
        folder = Path.Combine(dataRoot, "models", "whisper");
        this.selectedModel = selectedModel;
        this.clock = clock ?? TimeProvider.System;
        records = InstalledModelsFile.Read(InstalledJsonPath);
    }

    /// <summary>The store on the app's data root (StoragePaths.Root).</summary>
    public static ModelStore CreateDefault(Func<string?> selectedModel) => new(StoragePaths.Root, selectedModel);

    public string Folder => folder;

    public string PathFor(ModelFile file) => Path.Combine(folder, file.FileName);

    /// <summary>V-9.5 item 1's name for a partial download, <c>&lt;file&gt;.partial</c> beside the model (ED-NEW-dlresume-1: <see cref="ModelDownloader"/> takes one found there into its own partial slot).</summary>
    public string PartialPathFor(ModelFile file) => Path.Combine(folder, file.FileName + ".partial");

    public string InstalledJsonPath => Path.Combine(folder, "installed.json");

    public IReadOnlyList<InstalledModelRecord> Records
    {
        get { lock (gate) return [.. records]; }
    }

    /// <summary>The bytes of the installed models (the list footer, V-9.7): the records whose file is still there.</summary>
    public long InstalledBytes
    {
        get
        {
            lock (gate) return records.Where(r => File.Exists(Path.Combine(folder, "ggml-" + r.Id + ".bin"))).Sum(r => r.Bytes);
        }
    }

    /// <summary>V-9.4. Installed means the file is there at the manifest's size (a stat, nothing read); InUse when it is the selected model.</summary>
    public ModelInstallState State(ModelFile file)
    {
        lock (gate)
            if (downloading.TryGetValue(file.Id, out var fraction)) return new ModelInstallState(ModelInstallKind.Downloading, fraction());
        var info = new FileInfo(PathFor(file));
        if (!info.Exists || info.Length != file.Bytes) return new ModelInstallState(ModelInstallKind.NotInstalled);
        return new ModelInstallState(string.Equals(selectedModel(), file.Id, StringComparison.Ordinal) ? ModelInstallKind.InUse : ModelInstallKind.Installed);
    }

    /// <summary>
    /// V-9.4: the file is trusted without being read only when installed.json records it at this size, this last-write time and the manifest's hash;
    /// otherwise it is hashed (off the calling thread) before it may be loaded. A wrong hash deletes the file and its record.
    /// </summary>
    public async Task<ModelVerification> VerifyForLoad(ModelFile file, CancellationToken cancellationToken)
    {
        var path = PathFor(file);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            Drop(file.Id);
            return ModelVerification.Missing;
        }
        var length = info.Length;
        var time = info.LastWriteTimeUtc;
        lock (gate)
        {
            var record = records.FirstOrDefault(r => r.Id == file.Id);
            if (record is not null && record.Bytes == length && length == file.Bytes && record.FileTimeUtc == time
                && string.Equals(record.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                return ModelVerification.Trusted;
        }
        var hash = await Task.Run(() => HashFile(path, cancellationToken), cancellationToken);
        if (length == file.Bytes && string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Upsert(file, length, time);
            return ModelVerification.Rehashed;
        }
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // held open: it is still never loaded, and the record is gone
        }
        Drop(file.Id);
        return ModelVerification.Corrupt;
    }

    /// <summary>
    /// Q-V-13 B, store side: the model in use is never removed. Returns what happened. A file another handle holds open (a load or a hash in progress,
    /// a scanner: Windows refuses the delete) is in use too, and keeps its record (ED-305 (7)).
    /// </summary>
    public ModelRemoveResult Remove(ModelFile file)
    {
        var path = PathFor(file);
        bool recorded;
        lock (gate) recorded = records.Any(r => r.Id == file.Id);
        if (!File.Exists(path) && !recorded) return ModelRemoveResult.NotInstalled;
        if (string.Equals(selectedModel(), file.Id, StringComparison.Ordinal)) return ModelRemoveResult.InUse;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ModelRemoveResult.InUse;
        }
        Drop(file.Id);
        return ModelRemoveResult.Removed;
    }

    /// <summary>The file was verified just now (the download checked its hash): record it.</summary>
    public void RecordInstalled(ModelFile file)
    {
        var info = new FileInfo(PathFor(file));
        Upsert(file, info.Length, info.LastWriteTimeUtc);
    }

    internal void SetDownloading(ModelFile file, Func<double>? fraction)
    {
        lock (gate)
        {
            if (fraction is null) downloading.Remove(file.Id);
            else downloading[file.Id] = fraction;
        }
    }

    private void Upsert(ModelFile file, long length, DateTime fileTimeUtc)
    {
        lock (gate)
        {
            records.RemoveAll(r => r.Id == file.Id);
            records.Add(new InstalledModelRecord(file.Id, length, file.Sha256, clock.GetUtcNow().UtcDateTime, fileTimeUtc));
            Save();
        }
    }

    private void Drop(string id)
    {
        lock (gate)
        {
            if (records.RemoveAll(r => r.Id == id) > 0) Save();
        }
    }

    /// <summary>
    /// Writes installed.json; a write Windows refuses (a reader holds the file) is skipped, not thrown (ED-305 (8)): the next write carries the
    /// whole list again, and until then the file on disk vouches for no more than before, so the next process re-hashes what it lacks.
    /// </summary>
    private void Save()
    {
        try
        {
            InstalledModelsFile.Write(InstalledJsonPath, records);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error("installed.json could not be written: " + error.GetType().Name);
        }
    }

    private static string HashFile(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }
}
