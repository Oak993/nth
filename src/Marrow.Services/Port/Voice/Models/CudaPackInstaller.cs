// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): download, verify and unpack the CUDA runtime pack (Q-V-9 C).
using System.IO.Compression;
using System.Security.Cryptography;
using Marrow.Core.Port;
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

/// <summary>
/// The pack is a nupkg (a zip). It is fetched by the shared downloader (size and SHA-256), checked against the pinned SHA-512, and only the files
/// the pin lists, found at their exact path in the archive and of the pinned size, are unpacked into <c>&lt;data root&gt;\runtimes\cuda12\win-x64</c>
/// (built in a sibling folder and renamed into place). Nothing else in the archive is read out. The NVIDIA libraries it needs beside it (OD-0104) are
/// not part of this class.
/// </summary>
public sealed class CudaPackInstaller
{
    private readonly string dataRoot;
    private readonly Downloader downloader;
    private readonly Func<CudaRuntimePack, Uri> urlFor;
    private readonly TimeProvider clock;

    /// <summary>The first-url host list of the pack's download (its pinned url is nuget.org's flat container): ED-NEW-f-vt-7 (9).</summary>
    public static IReadOnlyList<string> AllowedHosts { get; } = ["api.nuget.org"];

    /// <summary>
    /// The waits before the folder swap is tried again when Windows refuses it because a handle is open inside a folder (a scanner looking at the
    /// fresh DLLs, a loaded runtime): 0.25 s, 0.5 s, 1 s, 2 s, 4 s, on the clock (ED-NEW-f-vt-7 (9)).
    /// </summary>
    public static IReadOnlyList<TimeSpan> SwapRetryDelays { get; } =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <param name="clock">The clock of the swap's waits; <see cref="SwiftClock.Provider"/> when null.</param>
    public CudaPackInstaller(string dataRoot, Downloader downloader, Func<CudaRuntimePack, Uri>? urlFor = null, TimeProvider? clock = null)
    {
        this.dataRoot = dataRoot;
        this.downloader = downloader;
        this.urlFor = urlFor ?? (p => new Uri(p.Url));
        this.clock = clock ?? SwiftClock.Provider.Current;
    }

    /// <summary>The download in flight (for its progress), or null.</summary>
    public DownloadJob? Job { get; private set; }

    public string FolderFor(CudaRuntimePack pack) => Path.Combine([dataRoot, .. pack.LoaderFolder.Split('/')]);

    public bool IsInstalled(CudaRuntimePack pack)
    {
        var folder = FolderFor(pack);
        return pack.Files.All(f => new FileInfo(Path.Combine(folder, f.Name)) is { Exists: true } info && info.Length == f.Bytes);
    }

    /// <summary>
    /// Deletes the pack's folder; false when there is none, or when a handle is open inside it (a loaded runtime): then nothing is deleted, because
    /// the folder is first set aside whole, by a rename Windows refuses in that case (ED-NEW-f-vt-7 (9)).
    /// </summary>
    public bool Remove(CudaRuntimePack pack)
    {
        var folder = FolderFor(pack);
        if (!Directory.Exists(folder)) return false;
        var aside = folder + ".old-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.Move(folder, aside);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        DeleteQuietly(aside);
        return true;
    }

    public async Task<CudaInstallResult> Install(CudaRuntimePack pack, CancellationToken cancellationToken = default)
    {
        if (pack.Sha256 is null) return new CudaInstallResult(CudaInstallStatus.Unpinned, null, null); // nothing to verify the download with: fetch nothing
        var folder = FolderFor(pack);
        if (IsInstalled(pack)) return new CudaInstallResult(CudaInstallStatus.Installed, folder, null);
        DeleteLeftovers(folder);

        var archive = Path.Combine(dataRoot, "runtimes", pack.Id + "-" + pack.Version + ".nupkg.download");
        var request = new DownloadRequest(urlFor(pack), archive, pack.Bytes, pack.Sha256, "nuget.org");
        var job = downloader.Start(request);
        Job = job;
        DownloadOutcome outcome;
        using (cancellationToken.Register(job.Cancel)) outcome = await job.Completion;
        Job = null;
        if (outcome.Status == DownloadStatus.Cancelled) return new CudaInstallResult(CudaInstallStatus.Cancelled, null, null);
        if (!outcome.Succeeded)
            return new CudaInstallResult(outcome.Failure?.Kind == DownloadFailureKind.Mismatch ? CudaInstallStatus.Corrupt : CudaInstallStatus.Failed, null, outcome.Failure);

        var temp = folder + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (!await Sha512Matches(archive, pack.Sha512, cancellationToken)) return new CudaInstallResult(CudaInstallStatus.Corrupt, null, null);
            Directory.CreateDirectory(temp);
            if (!Unpack(archive, pack, temp)) return new CudaInstallResult(CudaInstallStatus.Corrupt, null, null);
            return await Swap(temp, folder)
                ? new CudaInstallResult(CudaInstallStatus.Installed, folder, null)
                : new CudaInstallResult(CudaInstallStatus.Failed, null, new DownloadFailure(DownloadFailureKind.DiskError, DownloadMessages.DiskError));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CudaInstallResult(CudaInstallStatus.Cancelled, null, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A write that failed (a full disk) or a file Windows would not open: nothing was put in place (ED-NEW-f-vt-7 (9)).
            return new CudaInstallResult(CudaInstallStatus.Failed, null, new DownloadFailure(DownloadFailureKind.DiskError, DownloadMessages.DiskError));
        }
        finally
        {
            DeleteQuietly(temp);
            try
            {
                if (File.Exists(archive)) File.Delete(archive); // 250 MB that is not needed again
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // held for a moment (a scanner): the next install overwrites it
            }
        }
    }

    /// <summary>
    /// Puts the unpacked folder in place: an existing folder is first set aside WHOLE (a rename, which Windows refuses while a handle is open inside),
    /// then the new one is renamed in, then the old one is deleted. A refusal is tried again after each of <see cref="SwapRetryDelays"/>; when it
    /// still fails, the old folder is where it was, untouched, and the answer is false.
    /// </summary>
    private async Task<bool> Swap(string temp, string folder)
    {
        string? aside = null;
        if (Directory.Exists(folder))
        {
            aside = folder + ".old-" + Guid.NewGuid().ToString("N");
            if (!await MoveWithRetries(folder, aside)) return false;
        }
        if (!await MoveWithRetries(temp, folder))
        {
            if (aside is not null) await MoveWithRetries(aside, folder); // put the old one back
            return false;
        }
        if (aside is not null) DeleteQuietly(aside);
        return true;
    }

    private async Task<bool> MoveWithRetries(string from, string to)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt >= SwapRetryDelays.Count) return false;
            }
            await Task.Delay(SwapRetryDelays[attempt], clock);
        }
    }

    /// <summary>Temp and set-aside folders an earlier install or remove could not delete (best effort).</summary>
    private static void DeleteLeftovers(string folder)
    {
        var parent = Path.GetDirectoryName(folder);
        if (parent is null || !Directory.Exists(parent)) return;
        var name = Path.GetFileName(folder);
        foreach (var leftover in Directory.EnumerateDirectories(parent, name + ".*"))
        {
            var suffix = Path.GetFileName(leftover)[name.Length..];
            if (suffix.StartsWith(".tmp-", StringComparison.Ordinal) || suffix.StartsWith(".old-", StringComparison.Ordinal)) DeleteQuietly(leftover);
        }
    }

    private static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // still held: the next install deletes it (DeleteLeftovers)
        }
    }

    private static async Task<bool> Sha512Matches(string path, string expected, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA512.HashDataAsync(stream, token);
        return string.Equals(Convert.ToHexStringLower(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Unpack(string archivePath, CudaRuntimePack pack, string target)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var file in pack.Files)
        {
            // Exact, ordinal names only: an archive entry is never turned into a path of ours, so no entry can write outside the target.
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, pack.PackFolder + "/" + file.Name, StringComparison.Ordinal));
            if (entry is null || entry.Length != file.Bytes) return false;
            var destination = Path.Combine(target, file.Name);
            using (var input = entry.Open())
            using (var output = File.Create(destination)) input.CopyTo(output);
            if (new FileInfo(destination).Length != file.Bytes) return false;
        }
        return true;
    }
}
