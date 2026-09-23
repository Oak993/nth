// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for the Claude Code CLI used by the
// Services.Providers.ClaudeSession tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI.
// Compiled with the system's .NET Framework csc.exe (tests/Marrow.Services.Tests/Fixtures/ClaudeStandIns.cs), so
// it is a real .exe: ClaudeSession spawns its configured "executable" with CreateProcessW directly (R-PROC-1), so a
// .cmd or .ps1 script cannot stand in for it.
//
// Controlled by CLAUDE_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt        written by the stand-in itself: every argument of call n (0-based), NUL-separated.
//   help.txt            what --help prints on stdout, when the run's sole argument is "--help" (missing: nothing).
//   response.json       one JSON line control_response "response" object template, sent for every
//                        control_request line read from stdin (part 2 onward may exercise several: the
//                        initialize handshake, then set_permission_mode/set_model/interrupt/stop_task), with
//                        the literal token __REQUEST_ID__ replaced by that request's own request_id (missing:
//                        no reply is ever sent). The run keeps reading stdin until it closes or the test
//                        terminates the process, so a session started against this stand-in stays open for
//                        Send/Interrupt/Compact/ResolveApproval/AnswerQuestion/Stop/StopAgent calls.
//   captured-<n>-<m>.txt written by the stand-in itself: the raw text of the m-th non-empty stdin line
//                        (0-based) of call n, so a test can inspect any wire line Claude actually received,
//                        not just the first.
//   exit-status.txt     the exit code once stdin closes (missing: 0).
//   exit-after-reply.txt  when present, the run stops reading stdin (and so exits) right after sending its
//                          first reply, reproducing part 1's original single-reply-then-exit shape for a test
//                          that needs the child to close its own stdout quickly (e.g. a probe whose stream
//                          ends without ever matching).
// Without CLAUDE_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.IO;
using System.Text;

internal static class ClaudeStandIn
{
    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("CLAUDE_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir)) return 0;
        Directory.CreateDirectory(dir);

        int call = 0;
        while (File.Exists(Path.Combine(dir, "argv-" + call + ".txt"))) call++;
        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        string pendingArgv = Path.Combine(dir, "argv-" + call + ".txt.tmp");
        File.WriteAllBytes(pendingArgv, argv.ToArray());
        File.Move(pendingArgv, Path.Combine(dir, "argv-" + call + ".txt"));

        if (args.Length == 1 && args[0] == "--help")
        {
            string helpPath = Path.Combine(dir, "help.txt");
            if (File.Exists(helpPath))
            {
                Stream stdout = Console.OpenStandardOutput();
                byte[] help = File.ReadAllBytes(helpPath);
                stdout.Write(help, 0, help.Length);
                stdout.Flush();
            }
            return 0;
        }

        string responsePath = Path.Combine(dir, "response.json");
        string responseTemplate = File.Exists(responsePath) ? File.ReadAllText(responsePath, Encoding.UTF8) : null;
        int message = 0;
        while (true)
        {
            string line = Console.In.ReadLine();
            if (line == null) break;
            if (line.Length == 0) continue;
            File.WriteAllText(Path.Combine(dir, "captured-" + call + "-" + message + ".txt"), line, new UTF8Encoding(false));
            message++;
            if (responseTemplate == null) continue;
            // Only a control_request (never an outbound control_response or user message the session itself
            // sent us) gets a reply.
            if (line.IndexOf("\"type\":\"control_request\"", StringComparison.Ordinal) < 0) continue;
            string requestID = ExtractRequestID(line);
            if (requestID == null) continue;
            string reply = responseTemplate.Replace("__REQUEST_ID__", requestID);
            Console.Out.Write(reply);
            Console.Out.Write('\n');
            Console.Out.Flush();
            if (File.Exists(Path.Combine(dir, "exit-after-reply.txt"))) break;
        }

        string statusPath = Path.Combine(dir, "exit-status.txt");
        if (!File.Exists(statusPath)) return 0;
        return int.Parse(File.ReadAllText(statusPath).Trim());
    }

    /// <summary>Pulls the value of the top-level "request_id" JSON string field out of one wire line, by text search:
    /// the stand-in speaks only the fixed shape ClaudeSession.Control and Handshake write, never general JSON.</summary>
    private static string ExtractRequestID(string line)
    {
        const string marker = "\"request_id\":\"";
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = line.IndexOf('"', start);
        if (end < 0) return null;
        return line.Substring(start, end - start);
    }
}
