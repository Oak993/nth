// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for the Claude Code CLI used by the
// Services.Providers.ClaudeSession tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI.
// Compiled with the system's .NET Framework csc.exe (tests/Marrow.Services.Tests/Fixtures/ClaudeStandIns.cs), so
// it is a real .exe: ClaudeSession spawns its configured "executable" with CreateProcessW directly (R-PROC-1), so a
// .cmd or .ps1 script cannot stand in for it.
//
// Controlled by CLAUDE_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt      written by the stand-in itself: every argument of call n (0-based), NUL-separated.
//   help.txt          what --help prints on stdout, when the run's sole argument is "--help" (missing: nothing).
//   response.json     one JSON line control_response "response" object template, sent for the FIRST
//                      control_request read from stdin, with the literal token __REQUEST_ID__ replaced by
//                      that request's own request_id (missing: no reply is sent, ever). The run exits right
//                      after sending it (part 1 of this unit tests only the initialize handshake), so no
//                      test process is left running.
//   captured-<n>.txt  written by the stand-in itself: the raw text of the control_request line read by
//                      call n, so a test can inspect the request Claude actually received.
//   exit-status.txt   the exit code once stdin closes (missing: 0).
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
        while (true)
        {
            string line = Console.In.ReadLine();
            if (line == null) break;
            if (line.Length == 0) continue;
            File.WriteAllText(Path.Combine(dir, "captured-" + call + ".txt"), line, new UTF8Encoding(false));
            if (responseTemplate == null) continue;
            string requestID = ExtractRequestID(line);
            if (requestID == null) continue;
            string reply = responseTemplate.Replace("__REQUEST_ID__", requestID);
            Console.Out.Write(reply);
            Console.Out.Write('\n');
            Console.Out.Flush();
            break;
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
