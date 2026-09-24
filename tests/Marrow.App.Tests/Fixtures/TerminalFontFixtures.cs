namespace Marrow.App.Tests.Fixtures;

// Support for TerminalFontTests (PR-TERM-125 to PR-TERM-134). Folders are real and live under
// MARROW_PORT_DATA_DIR (R-TEST-9): no file-system abstraction, and never the real user profile.

/// <summary>A fresh real folder under MARROW_PORT_DATA_DIR for one test, standing in for the home folder
/// TerminalFont.GhosttyFont(home) reads from (ED-175 (2)); removed afterwards.</summary>
public sealed class ScratchHome : IDisposable
{
    public ScratchHome()
    {
        // R-TEST-9: the variable is set before anything runs. No fallback folder is invented when it is missing.
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; R-TEST-9 requires it before any test runs");
        Path = System.IO.Path.Combine(root, "TerminalFontTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
