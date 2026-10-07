// PORT-ONLY: OD-0104 / OD-0106 CUDA A. Downloads pinned NVIDIA archives via the shared downloader; publishes every companion in one folder swap.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

public sealed class NvidiaCompanionInstaller(string dataRoot, Downloader downloader, IReadOnlyList<NvidiaCompanionArchive>? pins = null, Func<string, long>? freeSpace = null)
{
    private sealed record InstallReceipt(Dictionary<string, string> Archives, Dictionary<string, string> Files);
    private const string Receipt = "nvidia-companions.json";
    private readonly IReadOnlyList<NvidiaCompanionArchive> archives = pins ?? NvidiaCompanionArchive.All;
    public long Bytes => archives.Sum(a => a.Bytes);

    // Hashes are derived only from the verified archive, then rechecked on restart/repair. Presence alone does not certify an install.
    public bool IsInstalled(string folder)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<InstallReceipt>(File.ReadAllText(Path.Combine(folder, Receipt)));
            if (receipt?.Archives is null || receipt.Files is null || receipt.Archives.Count != archives.Count) return false;
            foreach (var archive in archives)
                if (!receipt.Archives.TryGetValue(archive.Id, out var hash) || !string.Equals(hash, archive.Sha256, StringComparison.Ordinal)) return false;
            foreach (var name in archives.SelectMany(a => a.Entries).Select(Path.GetFileName))
            {
                if (name is null || !receipt.Files.TryGetValue(name, out var expected)) return false;
                using var file = File.OpenRead(Path.Combine(folder, name));
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(file)), expected, StringComparison.Ordinal)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public async Task<NvidiaCompanionResult> InstallAsync(string folder, Action<double> progress, CancellationToken token)
    {
        var staging = folder + ".tmp-nvidia-" + Guid.NewGuid().ToString("N");
        var aside = folder + ".old-nvidia-" + Guid.NewGuid().ToString("N");
        var moved = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (IsInstalled(folder)) { progress(1); return NvidiaCompanionResult.Installed; }
            var downloads = archives.Select(a => new DownloadRequest(new Uri(a.Url), Path.Combine(dataRoot, "runtimes", "downloads", a.Id + ".zip"), a.Bytes, a.Sha256, "NVIDIA")).ToArray();
            var job = downloader.StartSet(downloads, p => progress(Math.Clamp(p.BytesDone / (double)Bytes, 0, 1)));
            DownloadOutcome outcome;
            using (token.Register(job.Cancel)) outcome = await job.Completion;
            if (outcome.Status == DownloadStatus.Cancelled) return NvidiaCompanionResult.Cancelled;
            if (!outcome.Succeeded) return NvidiaCompanionResult.Failed;
            token.ThrowIfCancellationRequested();
            long unpacked = Directory.Exists(folder) ? Directory.GetFiles(folder).Sum(p => new FileInfo(p).Length) : 0;
            for (var i = 0; i < archives.Count; i++)
            {
                using var zip = ZipFile.OpenRead(downloads[i].DestinationPath);
                foreach (var path in archives[i].Entries)
                {
                    var entries = zip.Entries.Where(e => e.FullName == path).ToArray();
                    if (entries.Length != 1 || entries[0].Length <= 0) return NvidiaCompanionResult.Failed;
                    unpacked = checked(unpacked + entries[0].Length);
                }
            }
            var available = freeSpace?.Invoke(folder) ?? new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
            if (available < Math.Ceiling(unpacked * 1.1)) return NvidiaCompanionResult.Failed;
            Directory.CreateDirectory(staging);
            if (Directory.Exists(folder))
                foreach (var path in Directory.GetFiles(folder)) File.Copy(path, Path.Combine(staging, Path.GetFileName(path)));
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < archives.Count; i++)
            {
                using var zip = ZipFile.OpenRead(downloads[i].DestinationPath);
                foreach (var path in archives[i].Entries)
                {
                    var matches = zip.Entries.Where(e => string.Equals(e.FullName, path, StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1 || matches[0].Length <= 0) return NvidiaCompanionResult.Failed;
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name)) return NvidiaCompanionResult.Failed;
                    await using (var input = matches[0].Open())
                    await using (var output = File.Create(Path.Combine(staging, name))) await input.CopyToAsync(output, token);
                    await using var file = File.OpenRead(Path.Combine(staging, name));
                    hashes.Add(name, Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token)));
                }
            }
            await File.WriteAllTextAsync(Path.Combine(staging, Receipt), JsonSerializer.Serialize(new InstallReceipt(archives.ToDictionary(a => a.Id, a => a.Sha256, StringComparer.Ordinal), hashes)), token);
            token.ThrowIfCancellationRequested();
            // A loaded Windows pack refuses the first rename: leave it untouched and report failure. The offer requires restart.
            if (Directory.Exists(folder)) { Directory.Move(folder, aside); moved = true; }
            Directory.Move(staging, folder);
            moved = false;
            DeleteQuietly(aside);
            foreach (var download in downloads)
            {
                try { File.Delete(download.DestinationPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            progress(1);
            return NvidiaCompanionResult.Installed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return NvidiaCompanionResult.Cancelled; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return NvidiaCompanionResult.Failed; }
        finally
        {
            if (moved && !Directory.Exists(folder))
            {
                try { Directory.Move(aside, folder); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Retain aside: an intact pack recoverable after the handle closes. */ }
            }
            DeleteQuietly(staging);
        }
    }

    private static void DeleteQuietly(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
