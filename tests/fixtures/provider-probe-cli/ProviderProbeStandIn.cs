// PORT-ONLY test stand-in, no Swift twin: a generic scripted stand-in for a provider CLI's `--version` and
// `auth status`/`login status`/`status`/`auth list` probes, used only by the Services.Providers.ProviderRegistry
// part 2 tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI. Compiled with the system's
// .NET Framework csc.exe, the same pattern ClaudeStandIns.cs and AuthRewriteStandIns.cs use.
//
// Reads its own executable's directory (never an environment variable, so it needs nothing from the
// production Environment(for:) computation to reach it): sleep-ms.txt holds milliseconds to sleep before doing
// anything else (0 if absent or unparseable); marker.txt, if present, names a full path this process creates
// as soon as it starts and deletes just before it exits (a test's concurrency probe polls for it); stdout.txt
// and stderr.txt (UTF-8) are written verbatim to the matching stream if present; exit.txt holds the integer
// exit code (0 if absent or unparseable). Every call also records its own argv, NUL-separated, to
// call-<n>.args (0-based, the same shape ClaudeStandIn.cs uses), so a test can assert call order and argument
// vectors across the two probes of one Refresh.
using System;
using System.IO;
using System.Text;
using System.Threading;

internal static class ProviderProbeStandIn
{
    private static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;

        int call = 0;
        while (File.Exists(Path.Combine(dir, "call-" + call + ".args"))) call++;
        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        File.WriteAllBytes(Path.Combine(dir, "call-" + call + ".args"), argv.ToArray());

        string markerPath = Path.Combine(dir, "marker.txt");
        string marker = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : "";
        if (marker.Length != 0) File.WriteAllText(marker, "");

        string sleepPath = Path.Combine(dir, "sleep-ms.txt");
        int sleepMs = 0;
        if (File.Exists(sleepPath)) int.TryParse(File.ReadAllText(sleepPath).Trim(), out sleepMs);
        if (sleepMs > 0) Thread.Sleep(sleepMs);

        try
        {
            string stdoutPath = Path.Combine(dir, "stdout.txt");
            string stderrPath = Path.Combine(dir, "stderr.txt");
            string exitPath = Path.Combine(dir, "exit.txt");

            UTF8Encoding utf8 = new UTF8Encoding(false);
            try { Console.OutputEncoding = utf8; } catch { }

            if (File.Exists(stdoutPath))
            {
                byte[] bytes = File.ReadAllBytes(stdoutPath);
                using (Stream stdout = Console.OpenStandardOutput()) stdout.Write(bytes, 0, bytes.Length);
            }
            if (File.Exists(stderrPath))
            {
                byte[] bytes = File.ReadAllBytes(stderrPath);
                using (Stream stderr = Console.OpenStandardError()) stderr.Write(bytes, 0, bytes.Length);
            }

            int code = 0;
            if (File.Exists(exitPath)) int.TryParse(File.ReadAllText(exitPath).Trim(), out code);
            return code;
        }
        finally
        {
            if (marker.Length != 0 && File.Exists(marker)) File.Delete(marker);
        }
    }
}
