// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for `codex app-server` used by the
// Services.Providers.CodexSession tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI.
// Compiled with the system's .NET Framework csc.exe (tests/Marrow.Services.Tests/Fixtures/CodexStandIns.cs), so
// it is a real .exe: CodexSession.Connect spawns its configured "executable" with CreateProcessW directly and a
// fixed argv of exactly one token, "app-server" (R-PROC-1), so a .cmd or .ps1 script cannot stand in for it and
// the stand-in itself never inspects its own argv.
//
// Controlled by CODEX_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt             written by the stand-in itself: every argument of launch n (0-based), NUL-separated.
//   response-<method>.json   the JSON value to send back as "result" for every request whose method, with '/'
//                             replaced by '_', matches <method> (missing: no reply is sent for that request).
//   response-<method>-<k>.json  overrides the above for the k-th (0-based) request of that method, so a test
//                             can page thread/turns/list with a different reply each time; falls back to the
//                             un-numbered file when the numbered one is missing.
//   error-<method>[-<k>].json   the same lookup, but sent as {"error":{"code":-32000,"message":<content>}}
//                             instead of a result; checked before the plain response file.
//   captured-<n>-<m>.txt     written by the stand-in itself: the raw text of the m-th non-empty stdin line
//                             (0-based) of launch n, so a test can inspect any wire line Codex actually
//                             received (bare JSON, one per line, no Content-Length header, no "jsonrpc" member).
//   emit-<method>[-<k>].txt  raw wire lines (one per line) written right after that method's reply (ED-NEW-ma-engine-7).
//   exit-status.txt          the exit code once stdin closes (missing: 0).
// The run also exits on its own after 45 real seconds with no new stdin line, well past this file's own 20 and
// 30 second production watchdogs, so a test proving one of those does not leave the process (and its reader
// thread) running for the rest of the test suite.
// Without CODEX_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class CodexStandIn
{
    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("CODEX_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            // PORT-ONLY (R-TEST-8): a registry-built environment cannot carry CODEX_STANDIN_DIR, so a copy of this
            // stand-in also finds its control folder from control-dir.txt next to its own executable.
            string hint = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "control-dir.txt");
            if (File.Exists(hint)) dir = File.ReadAllText(hint, Encoding.UTF8).Trim();
        }
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
        // PORT-ONLY (SK-11b, DL-11): the launch's whole environment, written before argv-<n>.txt appears so a test that saw the argv can read it.
        StringBuilder environmentDump = new StringBuilder();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) environmentDump.Append(entry.Key).Append('=').Append(entry.Value).Append('\n');
        File.WriteAllText(Path.Combine(dir, "env-" + call + ".txt"), environmentDump.ToString(), new UTF8Encoding(false));
        string pendingArgv = Path.Combine(dir, "argv-" + call + ".txt.tmp");
        File.WriteAllBytes(pendingArgv, argv.ToArray());
        File.Move(pendingArgv, Path.Combine(dir, "argv-" + call + ".txt"));

        int message = 0;
        Dictionary<string, int> methodCounts = new Dictionary<string, int>();

        // Read stdin on its own thread so the main loop can give up after an idle period instead of blocking
        // forever on ReadLine once the test that started this process has moved on.
        Queue<string> pending = new Queue<string>();
        object gate = new object();
        bool closed = false;
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

        while (true)
        {
            if (!signal.Wait(TimeSpan.FromSeconds(45))) break; // idle timeout
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
            // Atomic publish (matches tests/fixtures/antigravity-cli/AntigravityStandIn.cs and the claude-cli stand-in):
            // write under a .tmp name, then rename, so a reader polling with File.Exists on the final name never
            // observes the name before this process has closed the file. File.Move is atomic on NTFS.
            string capturedPath = Path.Combine(dir, "captured-" + call + "-" + message + ".txt");
            string pendingCaptured = capturedPath + ".tmp";
            File.WriteAllText(pendingCaptured, line, new UTF8Encoding(false));
            File.Move(pendingCaptured, capturedPath);
            message++;

            string method = ExtractString(line, "\"method\":\"");
            string id = ExtractRawID(line);
            if (method == null || id == null) continue; // a notification, or a reply we sent nothing for

            string safeMethod = method.Replace('/', '_');
            int occurrence;
            methodCounts.TryGetValue(safeMethod, out occurrence);
            methodCounts[safeMethod] = occurrence + 1;

            string errorPath = FirstExisting(dir, "error-" + safeMethod + "-" + occurrence + ".json", "error-" + safeMethod + ".json");
            string resultPath = errorPath == null ? FirstExisting(dir, "response-" + safeMethod + "-" + occurrence + ".json", "response-" + safeMethod + ".json") : null;
            if (errorPath == null && resultPath == null) continue; // no scripted reply: stay silent

            string reply;
            if (errorPath != null)
            {
                string text = File.ReadAllText(errorPath, Encoding.UTF8);
                reply = "{\"id\":" + id + ",\"error\":{\"code\":-32000,\"message\":\"" + JsonEscape(text) + "\"}}";
            }
            else
            {
                string body = File.ReadAllText(resultPath, Encoding.UTF8);
                reply = "{\"id\":" + id + ",\"result\":" + body + "}";
            }
            Console.Out.Write(reply);
            Console.Out.Write('\n');
            // PORT-ONLY (R-TEST-8, ED-NEW-ma-engine-7): emit-<method>[-<k>].txt holds raw wire lines (notifications) written right after that method's reply, so a test can script activity on a waiting thread.
            string emitPath = FirstExisting(dir, "emit-" + safeMethod + "-" + occurrence + ".txt", "emit-" + safeMethod + ".txt");
            if (emitPath != null)
            {
                foreach (string emitted in File.ReadAllLines(emitPath, Encoding.UTF8))
                {
                    if (emitted.Length == 0) continue;
                    Console.Out.Write(emitted);
                    Console.Out.Write('\n');
                }
            }
            Console.Out.Flush();
        }

        string statusPath = Path.Combine(dir, "exit-status.txt");
        if (!File.Exists(statusPath)) return 0;
        return int.Parse(File.ReadAllText(statusPath).Trim());
    }

    private static string FirstExisting(string dir, string a, string b)
    {
        string pathA = Path.Combine(dir, a);
        if (File.Exists(pathA)) return pathA;
        string pathB = Path.Combine(dir, b);
        if (File.Exists(pathB)) return pathB;
        return null;
    }

    /// <summary>Pulls a top-level `"<marker>value"` JSON string field out of one wire line by text search: the
    /// stand-in speaks only the fixed shapes JSONRPCConnection.Write emits, never general JSON.</summary>
    private static string ExtractString(string line, string marker)
    {
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = line.IndexOf('"', start);
        if (end < 0) return null;
        return line.Substring(start, end - start);
    }

    /// <summary>Pulls the bare numeric "id" field (JSONRPCConnection always sends an integer id, never a
    /// string) out of one wire line by text search.</summary>
    private static string ExtractRawID(string line)
    {
        const string marker = "\"id\":";
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-')) end++;
        if (end == start) return null;
        return line.Substring(start, end - start);
    }

    private static string JsonEscape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
    }
}
