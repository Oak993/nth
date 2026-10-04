// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): download, verify and unpack the CUDA runtime pack (Q-V-9 C).
using System.IO.Compression;
using System.Security.Cryptography;
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

    public CudaPackInstaller(string dataRoot, Downloader downloader, Func<CudaRuntimePack, Uri>? urlFor = null)
    {
        this.dataRoot = dataRoot;
        this.downloader = downloader;
        this.urlFor = urlFor ?? (p => new Uri(p.Url));
    }

    /// <summary>The download in flight (for its progress), or null.</summary>
    public DownloadJob? Job { get; private set; }

    public string FolderFor(CudaRuntimePack pack) => Path.Combine([dataRoot, .. pack.LoaderFolder.Split('/')]);

    public bool IsInstalled(CudaRuntimePack pack)
    {
        var folder = FolderFor(pack);
        return pack.Files.All(f => new FileInfo(Path.Combine(folder, f.Name)) is { Exists: true } info && info.Length == f.Bytes);
    }

    public bool Remove(CudaRuntimePack pack)
    {
        var folder = FolderFor(pack);
        if (!Directory.Exists(folder)) return false;
        Directory.Delete(folder, recursive: true);
        return true;
    }

    public async Task<CudaInstallResult> Install(CudaRuntimePack pack, CancellationToken cancellationToken = default)
    {
        if (pack.Sha256 is null) return new CudaInstallResult(CudaInstallStatus.Unpinned, null, null); // nothing to verify the download with: fetch nothing
        var folder = FolderFor(pack);
        if (IsInstalled(pack)) return new CudaInstallResult(CudaInstallStatus.Installed, folder, null);

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
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.Move(temp, folder);
            return new CudaInstallResult(CudaInstallStatus.Installed, folder, null);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            if (File.Exists(archive)) File.Delete(archive); // 250 MB that is not needed again
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
