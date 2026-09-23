namespace Marrow.Services.Tests.Fixtures;

// Support for MCPProviderConfigTests. Helper types live in fixture files, never inside a test class (R-TEST-3).
// The folder is real and lives under MARROW_PORT_DATA_DIR (R-TEST-9, R-FS-1): no file-system abstraction.

/// <summary>A fresh real folder under MARROW_PORT_DATA_DIR standing in for a home directory, removed afterwards.</summary>
public sealed class MCPProviderConfigScratchHome : IDisposable
{
    public MCPProviderConfigScratchHome()
    {
        // R-TEST-9: the variable is set before anything runs. No fallback folder is invented when it is missing.
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; R-TEST-9 requires it before any test runs");
        FullPath = Path.Combine(root, "MCPProviderConfigTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    /// <summary>Writes .codex/config.toml under this home folder with the given text.</summary>
    public void WriteCodexConfig(string text)
    {
        var dir = Path.Combine(FullPath, ".codex");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.toml"), text);
    }

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
