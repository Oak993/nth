namespace Marrow.App.Tests.Fixtures;

// Support for DownloadsPopoverTests / DownloadsPopoverSecondTests (App/Views/Composer/DownloadsPopover.swift).
// Folders are real and live under MARROW_PORT_DATA_DIR (R-TEST-9): no file-system abstraction, never the real
// Downloads folder or user profile.

/// <summary>A fresh real folder under MARROW_PORT_DATA_DIR, standing in for the Downloads directory
/// RecentDownloads.Directory reads (DownloadsPopover.swift:25-27); removed afterwards.</summary>
public sealed class ScratchDownloads : IDisposable
{
    public ScratchDownloads()
    {
        // R-TEST-9: the variable is set before anything runs. No fallback folder is invented when it is missing.
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; R-TEST-9 requires it before any test runs");
        Path = System.IO.Path.Combine(root, "DownloadsPopoverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Creates a real file of <paramref name="bytes"/> bytes, last written at <paramref name="modified"/>.</summary>
    public string File(string name, DateTime modified, int bytes = 0)
    {
        var path = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        System.IO.File.SetLastWriteTimeUtc(path, modified);
        return path;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
