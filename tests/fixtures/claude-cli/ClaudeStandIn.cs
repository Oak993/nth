// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for the Claude Code CLI used by the
// Services.Providers.ClaudeSession tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI.
// Compiled with the system's .NET Framework csc.exe (tests/Marrow.Services.Tests/Fixtures/ClaudeStandIns.cs), so
// it is a real .exe: ClaudeSession spawns its configured "executable" with CreateProcessW directly (R-PROC-1), so a
// .cmd or .ps1 script cannot stand in for it.
//
// Controlled by CLAUDE_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt        written by the stand-in itself: every argument of call n (0-based), NUL-separated.
//   cwd-<n>.txt         written by the stand-in itself: the working directory call n was started in.
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
//   reply-limit.txt      optional maximum number of control replies; later requests are captured but
//                        left unanswered while the child stays alive (ClaudeSession.swift:301-309).
//   exit-now.txt         test-created release signal: close stdout and exit only after the test has
//                        registered its pending requests/prompts (ClaudeSession.swift:678-692).
// The run also exits on its own after 20 real seconds with no new stdin line, so a test that starts a session
// and never explicitly stops it does not leave the process (and its reader thread) running for the rest of
// the test suite: every test's own sequence of calls happens well inside that window, and the process is
// gone shortly after the test that started it moves on.
// Without CLAUDE_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class ClaudeStandIn
{
    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("CLAUDE_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            // PORT-ONLY (R-TEST-8): a registry-built environment cannot carry CLAUDE_STANDIN_DIR, so a copy of this
            // stand-in also finds its control folder from control-dir.txt next to its own executable.
            string hint = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "control-dir.txt");
            if (File.Exists(hint)) dir = File.ReadAllText(hint, Encoding.UTF8).Trim();
        }
        if (string.IsNullOrEmpty(dir)) return 0;
        Directory.CreateDirectory(dir);
        if (args.Length == 1 && args[0] == "--usage-orphan-child")
            new ManualResetEvent(false).WaitOne();

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
        // The folder this launch ran in (cwd-<n>.txt), so a test can prove where a probe was spawned.
        File.WriteAllText(Path.Combine(dir, "cwd-" + call + ".txt"), Directory.GetCurrentDirectory(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "pid-" + call + ".txt"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

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

        // A parent that exits on the test's event, leaving a child alive with inherited output handles.
        if (File.Exists(Path.Combine(dir, "orphan.txt")))
        {
            var helper = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                Arguments = "--usage-orphan-child",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            string pidPath = Path.Combine(dir, "orphan-pid.txt");
            File.WriteAllText(pidPath + ".tmp", helper.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(pidPath + ".tmp", pidPath);
            using (var exit = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("CLAUDE_STANDIN_EXIT_EVENT")))
                exit.WaitOne();
            return 0;
        }

        // PLANLIMITS-HANG: opt-in child that never answers or exits, including an unfinished stdout frame.
        string stallPath = Path.Combine(dir, "stall.txt");
        if (File.Exists(stallPath))
        {
            if (File.ReadAllText(stallPath).Trim() == "partial")
            {
                Console.Out.Write("{\"type\":\"control_response\"");
                Console.Out.Flush();
            }
            new ManualResetEvent(false).WaitOne();
        }

        string responsePath = Path.Combine(dir, "response.json");
        string responseTemplate = File.Exists(responsePath) ? File.ReadAllText(responsePath, Encoding.UTF8) : null;
        int message = 0;
        string replyLimitPath = Path.Combine(dir, "reply-limit.txt");
        int replyLimit = File.Exists(replyLimitPath) ? int.Parse(File.ReadAllText(replyLimitPath).Trim()) : int.MaxValue;
        int replies = 0;

        // Read stdin on its own thread so the main loop can give up after an idle period instead of blocking
        // forever on ReadLine once the test that started this process has moved on.
        Queue<string> pending = new Queue<string>();
        object gate = new object();
        bool closed = false;
        // One count per queued line (and one for the end of stdin), so the main loop takes exactly one wake-up per
        // line. An AutoResetEvent here lost lines: Set() on an event that is already set does nothing, so when two
        // lines arrived before the main loop waited again (ClaudeSession.Interrupt writes the tool deny and the
        // interrupt control_request back to back), the second stayed queued, unanswered, until the 20 s idle exit.
        SemaphoreSlim signal = new SemaphoreSlim(0);
        Thread reader = new Thread(delegate ()
        {
            while (true)
            {
                string read = Console.In.ReadLine();
                lock (gate)
                {
                    if (read == null) { closed = true; }
                    else { pending.Enqueue(read); }
                }
                signal.Release();
                if (read == null) break;
            }
        });
        reader.IsBackground = true;
        reader.Start();

        DateTime idleDeadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            if (File.Exists(Path.Combine(dir, "exit-now.txt"))) break;
            // Poll only the test's explicit exit signal; keep the same 20-second no-input idle deadline.
            if (!signal.Wait(TimeSpan.FromMilliseconds(20)))
            {
                if (DateTime.UtcNow >= idleDeadline) break;
                continue;
            }
            idleDeadline = DateTime.UtcNow.AddSeconds(20);
            string line = null;
            bool isClosed;
            lock (gate)
            {
                isClosed = closed && pending.Count == 0;
                if (pending.Count > 0) line = pending.Dequeue();
            }
            if (line == null)
            {
                if (isClosed) break;
                continue;
            }
            if (line.Length == 0) continue;
            // Atomic publish, exactly like argv-<n>.txt above: write under a .tmp name, then rename. A reader
            // (ClaudeScript.CapturedLine) polls with File.Exists on the final name; without this, File.Exists could
            // observe the name the instant this process creates it, while WriteAllText's handle was still open (or
            // not yet flushed/closed), so the reader's File.ReadAllText raced this writer and threw "the process
            // cannot access the file because it is being used by another process" (seen under the parallel suite,
            // where CPU contention widens the window between create and close). File.Move is atomic on NTFS: the
            // final name only ever appears once the file is completely written and closed.
            string capturedPath = Path.Combine(dir, "captured-" + call + "-" + message + ".txt");
            string pendingCaptured = capturedPath + ".tmp";
            File.WriteAllText(pendingCaptured, line, new UTF8Encoding(false));
            File.Move(pendingCaptured, capturedPath);
            message++;
            if (responseTemplate == null) continue;
            // Only a control_request (never an outbound control_response or user message the session itself
            // sent us) gets a reply.
            if (line.IndexOf("\"type\":\"control_request\"", StringComparison.Ordinal) < 0) continue;
            string requestID = ExtractRequestID(line);
            if (requestID == null) continue;
            if (replies >= replyLimit) continue;
            string reply = responseTemplate.Replace("__REQUEST_ID__", requestID);
            Console.Out.Write(reply);
            Console.Out.Write('\n');
            Console.Out.Flush();
            replies++;
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
