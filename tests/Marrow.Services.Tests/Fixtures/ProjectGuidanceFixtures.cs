using System.Runtime.InteropServices;

namespace Marrow.Services.Tests.Fixtures;

// Support for ProjectGuidanceTests. Helper types live in fixture files, never inside a test class (R-TEST-3).
// Folders are real and live under MARROW_PORT_DATA_DIR (R-TEST-9, R-FS-1): no file-system abstraction.

/// <summary>A fresh real folder under MARROW_PORT_DATA_DIR for one test, used as a project working directory.</summary>
public sealed class ProjectGuidanceScratch : IDisposable
{
    public ProjectGuidanceScratch()
    {
        // R-TEST-9: the variable is set before anything runs. No fallback folder is invented when it is missing.
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; R-TEST-9 requires it before any test runs");
        FullPath = Path.Combine(root, "ProjectGuidanceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string PathOf(string relative) => Path.Combine(FullPath, relative);

    /// <summary>Writes a project-root file with the given bytes.</summary>
    public string Write(string name, byte[] bytes)
    {
        var path = PathOf(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Writes a project-root file with the given text as UTF-8, no BOM.</summary>
    public string Write(string name, string text) => Write(name, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Creates a directory symlink named <paramref name="linkName"/> at the root pointing at <paramref name="targetDirectory"/>.
    /// Requires Developer Mode or elevation on Windows; the caller should skip the test when creation fails.
    /// </summary>
    public bool TryCreateDirectorySymlink(string linkName, string targetDirectory)
    {
        try
        {
            Directory.CreateSymbolicLink(PathOf(linkName), targetDirectory);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Sets the last-write time of a root file so it differs measurably from its current value.</summary>
    public static void Touch(string path, DateTime utc) => File.SetLastWriteTimeUtc(path, utc);

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
