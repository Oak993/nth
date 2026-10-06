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
//
// post-update-stdout.txt and post-update-exit.txt, if present, are copied over stdout.txt and exit.txt
// (deterministically, before this call exits) whenever this call's argv is exactly ["update"], so a test
// can prove ProviderRegistry.UpdateCLIIfDue's before/after version comparison (PR-PROVREG-256) with a
// stand-in that "installs" a new version, starts answering, or stops answering: the next call (an
// installedCLIVersion/cliVersion "--version" probe) then reports the new text or exit code. Never a real AI
// CLI (R-TEST-10): this file's own "update" is the only thing that ever runs.
//
// update-exit.txt, if present, is this process's OWN exit code for a call whose argv is exactly ["update"]
// only - read instead of exit.txt for that one call, so a test can make the "update" call itself fail
// (nonzero exit) while the --version calls before and after it keep reporting exit.txt's own code (review
// fix, PR-PROVREG-254/255: proves the date write still happens after a failing update, ProviderRegistry.cs
// :514-516 runs the write unconditionally on the shell result, ignored via SwiftTry.OptionalAsync).
using System;
using System.IO;
using System.Text;
using System.Threading;

internal static class ProviderProbeStandIn
{
    private static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;

        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        // Every mode can overlap: a limits read and a catalog load are separate processes.
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string lockName;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            lockName = "Local\\MarrowProviderProbe-" + BitConverter.ToString(hash.ComputeHash(identity)).Replace("-", "");
        int call;
        using (var recording = new Mutex(false, lockName))
        {
            try { recording.WaitOne(); }
            catch (AbandonedMutexException) { /* Ownership is acquired even though the previous publisher died. */ }
            try
            {
                call = 0;
                while (File.Exists(Path.Combine(dir, "call-" + call + ".args"))) call++;
                string recorded = Path.Combine(dir, "call-" + call + ".args");
                File.WriteAllBytes(recorded + ".tmp", argv.ToArray());
                File.Move(recorded + ".tmp", recorded);
            }
            finally { recording.ReleaseMutex(); }
        }

        string markerPath = Path.Combine(dir, "marker.txt");
        string marker = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : "";
        if (marker.Length != 0) File.WriteAllText(marker, "");

        string sleepPath = Path.Combine(dir, "sleep-ms.txt");
        int sleepMs = 0;
        if (File.Exists(sleepPath)) int.TryParse(File.ReadAllText(sleepPath).Trim(), out sleepMs);
        if (sleepMs > 0) Thread.Sleep(sleepMs);

        try
        {
            if (args.Length == 1 && args[0] == "app-server" && File.Exists(Path.Combine(dir, "codex-rpc.txt")))
                return CodexRPC();
            // Optional test-only gate for ComposerView.swift:205-215's delayed command listing. No fixed sleep
            // decides delivery: the test releases listings after asserting its focused panel is already open.
            // Claude's independent --help warmup is not a listing and must never wait on this gate.
            if (args.Length > 0 && args[0] == "-p" && File.Exists(Path.Combine(dir, "wait-for-release.txt")))
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                string release = Path.Combine(dir, "release-listings.txt");
                while (!File.Exists(release))
                {
                    if (DateTime.UtcNow >= deadline) return 1;
                    Thread.Sleep(10);
                }
            }
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
            if (args.Length == 1 && args[0] == "update")
            {
                string updateExitPath = Path.Combine(dir, "update-exit.txt");
                if (File.Exists(updateExitPath)) int.TryParse(File.ReadAllText(updateExitPath).Trim(), out code);
            }
            return code;
        }
        finally
        {
            if (args.Length == 1 && args[0] == "update")
            {
                string postUpdateStdout = Path.Combine(dir, "post-update-stdout.txt");
                if (File.Exists(postUpdateStdout)) File.Copy(postUpdateStdout, Path.Combine(dir, "stdout.txt"), true);
                string postUpdateExit = Path.Combine(dir, "post-update-exit.txt");
                if (File.Exists(postUpdateExit)) File.Copy(postUpdateExit, Path.Combine(dir, "exit.txt"), true);
            }
            if (marker.Length != 0 && File.Exists(marker)) File.Delete(marker);
        }
    }

    // The sign-in follow-ups need a functioning offline app-server, rather than one that exits
    // mid-initialize. Only the fixed JSONRPCConnection request shapes are accepted here.
    private static int CodexRPC()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        output.AutoFlush = true;
        string line;
        while ((line = input.ReadLine()) != null)
        {
            const string marker = "\"id\":";
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) continue;
            start += marker.Length;
            int end = start;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-')) end++;
            if (end == start) continue;
            string result;
            if (line.Contains("\"method\":\"initialize\"")) result = "{}";
            else if (line.Contains("\"method\":\"model/list\""))
                result = "{\"data\":[{\"id\":\"offline-model\",\"displayName\":\"Offline model\"}],\"nextCursor\":null}";
            else if (line.Contains("\"method\":\"account/rateLimits/read\""))
                result = "{\"rateLimits\":{\"planType\":\"plus\"}}";
            else return 2;
            output.WriteLine("{\"id\":" + line.Substring(start, end - start) + ",\"result\":" + result + "}");
        }
        return 0;
    }
}
