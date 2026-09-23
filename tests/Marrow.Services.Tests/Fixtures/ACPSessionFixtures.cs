namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture for ACPSessionTests (part 1). Shared helpers for driving powershell.exe as a real
// Windows system binary stand-in (R-TEST-10), never a real AI CLI.
public static class ACPSessionFixtures
{
    public static string PowerShell => Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>The argv for powershell.exe running one script, base64-encoded so quoting never matters.</summary>
    public static IReadOnlyList<string> PowerShellEncoded(string script) =>
        ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))];
}
