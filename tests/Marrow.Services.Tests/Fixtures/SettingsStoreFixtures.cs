namespace Marrow.Services.Tests.Fixtures;

// Support for SettingsStoreTests. Helper types live in fixture files, never inside a test class (R-TEST-3). Folders
// are real and live under MARROW_PORT_DATA_DIR (R-TEST-9, R-FS-1): no file-system abstraction.

/// <summary>A fresh real folder under MARROW_PORT_DATA_DIR for one test, removed afterwards.</summary>
public sealed class SettingsScratchFolder : IDisposable
{
    public SettingsScratchFolder()
    {
        // R-TEST-9: the variable is set before anything runs. No fallback folder is invented when it is missing.
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; R-TEST-9 requires it before any test runs");
        FullPath = Path.Combine(root, "SettingsStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    /// <summary>The path of <paramref name="relative"/> inside this folder (not created).</summary>
    public string PathOf(string relative) => Path.Combine(FullPath, relative);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(FullPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
            // A folder a test still holds is left for the throwaway MARROW_PORT_DATA_DIR to take away.
        }
    }
}
