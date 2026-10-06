// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for an ACP agent (`opencode acp`) used by the
// Services.Providers.ProviderRegistry tests of the OpenCode catalog probe (R-TEST-8, R-TEST-10). It reaches no network
// and runs no real AI CLI. Compiled with the system's .NET Framework csc.exe (C# 5), the same pattern as the other
// stand-ins in tests/fixtures, so it is a real .exe that ACPSession spawns with the fixed argument vector ["acp"].
//
// It reads its control files from the folder of its own executable (never from an environment variable):
//   call-<n>.args     written by the stand-in: every argument of launch n (0-based), NUL-separated.
//   cwd-<n>.txt       written by the stand-in: the working directory launch n was started in.
//   models-<n>.json   the JSON object sent as the RESULT of session/new for launch n; models.json when there is no
//                     file for that launch; {"sessionId":"s1"} (no models) when there is neither.
//   error-<n>.txt     when present for launch n (or error.txt), session/new is answered with a JSON-RPC error whose
//                     message is the file's text, instead of a result.
//   delay-ms.txt      milliseconds to wait before answering session/new (0 when absent).
// initialize is always answered with an empty agent description. Any other request is answered with an empty result.
// The process exits when its stdin closes, and on its own after 60 real seconds, so a test that never stops a probe
// cannot leave it running.
using System;
using System.IO;
using System.Text;
using System.Threading;

internal static class AcpStandIn
{
    private static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;

        int call = 0;
        string identity = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string lockName;
        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
            lockName = "Local\\MarrowAcpStandIn-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
        using (Mutex recording = new Mutex(false, lockName))
        {
            recording.WaitOne();
            try
            {
                while (File.Exists(Path.Combine(dir, "call-" + call + ".args"))) call++;
                MemoryStream argv = new MemoryStream();
                foreach (string argument in args)
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
                    argv.Write(bytes, 0, bytes.Length);
                    argv.WriteByte(0);
                }
                File.WriteAllText(Path.Combine(dir, "cwd-" + call + ".txt"), Directory.GetCurrentDirectory(), new UTF8Encoding(false));
                File.WriteAllBytes(Path.Combine(dir, "call-" + call + ".args.tmp"), argv.ToArray());
                File.Move(Path.Combine(dir, "call-" + call + ".args.tmp"), Path.Combine(dir, "call-" + call + ".args"));
            }
            finally { recording.ReleaseMutex(); }
        }

        Thread watchdog = new Thread(delegate ()
        {
            Thread.Sleep(60000);
            Environment.Exit(0);
        });
        watchdog.IsBackground = true;
        watchdog.Start();

        Stream stdout = Console.OpenStandardOutput();
        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            File.AppendAllText(Path.Combine(dir, "requests-" + call + ".jsonl"), line + "\n", new UTF8Encoding(false));
            string method = TopLevel(line, "method");
            string id = TopLevel(line, "id");
            if (method == null || id == null) continue; // a notification, or a reply to nothing we asked
            string reply;
            if (method == "initialize")
            {
                string initFile = Path.Combine(dir, "initialize.json");
                string init = File.Exists(initFile) ? File.ReadAllText(initFile, Encoding.UTF8) : "{\"protocolVersion\":1,\"agentCapabilities\":{}}";
                reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + init + "}";
            }
            else if (method == "session/new" || method == "session/load")
            {
                string delayPath = Path.Combine(dir, "delay-ms.txt");
                int delay = 0;
                if (File.Exists(delayPath)) int.TryParse(File.ReadAllText(delayPath).Trim(), out delay);
                if (delay > 0) Thread.Sleep(delay);
                string errorPath = First(dir, "error-" + call + ".txt", "error.txt");
                string modelsPath = First(dir, "models-" + call + ".json", "models.json");
                if (errorPath != null)
                    reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32000,\"message\":\"" + Escape(File.ReadAllText(errorPath, Encoding.UTF8)) + "\"}}";
                else if (modelsPath != null)
                    reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + File.ReadAllText(modelsPath, Encoding.UTF8) + "}";
                else
                    reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"sessionId\":\"s1\"}}";
            }
            else
            {
                reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{}}";
            }
            byte[] output = new UTF8Encoding(false).GetBytes(reply + "\n");
            stdout.Write(output, 0, output.Length);
            stdout.Flush();
        }
        return 0;
    }

    private static string First(string dir, string a, string b)
    {
        string pathA = Path.Combine(dir, a);
        if (File.Exists(pathA)) return pathA;
        string pathB = Path.Combine(dir, b);
        if (File.Exists(pathB)) return pathB;
        return null;
    }

    private static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
    }

    /// <summary>The raw text of a depth-1 member of one JSON object line: a number as written, a string without its
    /// quotes. The stand-in speaks only the fixed shapes JSONRPCConnection writes, so this is a scanner, not a parser.</summary>
    private static string TopLevel(string json, string key)
    {
        int depth = 0;
        int i = 0;
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '{' || c == '[') { depth++; i++; continue; }
            if (c == '}' || c == ']') { depth--; i++; continue; }
            if (c != '"') { i++; continue; }
            int start = i + 1;
            int end = start;
            while (end < json.Length && json[end] != '"')
            {
                if (json[end] == '\\') end++;
                end++;
            }
            string text = json.Substring(start, end - start);
            i = end + 1;
            if (depth != 1 || i >= json.Length || json[i] != ':' || text != key) continue;
            i++;
            if (i < json.Length && json[i] == '"')
            {
                int valueEnd = i + 1;
                while (valueEnd < json.Length && json[valueEnd] != '"')
                {
                    if (json[valueEnd] == '\\') valueEnd++;
                    valueEnd++;
                }
                return json.Substring(i + 1, valueEnd - i - 1);
            }
            int numberEnd = i;
            while (numberEnd < json.Length && json[numberEnd] != ',' && json[numberEnd] != '}') numberEnd++;
            return json.Substring(i, numberEnd - i).Trim();
        }
        return null;
    }
}
