// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for the GitHub Copilot CLI in headless
// server mode (`copilot --headless --stdio`), used by the Services.Providers.CopilotSession tests (R-TEST-8,
// R-TEST-10). It reaches no network and runs no real AI CLI. Compiled with the system's .NET Framework csc.exe
// (tests/Marrow.Services.Tests/Fixtures/CopilotStandIns.cs), so it is a real .exe: CopilotSession spawns its
// configured "executable" with CreateProcessW directly (R-PROC-1), so a .cmd or .ps1 script cannot stand in for it.
// Written in C# 5 (the language level of the Framework's csc.exe).
//
// It speaks JSON-RPC with Content-Length framing on stdin and stdout (CopilotSession.swift:484-491), and is
// controlled by COPILOT_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt            written by the stand-in itself: every argument of call n (0-based), NUL-separated.
//   captured-<n>-<m>.json   written by the stand-in itself: the raw body of the m-th frame (0-based) call n read
//                           from stdin, written before any reply to it is sent.
//   reply-<method>-<k>.json the raw JSON "result" for the k-th (1-based) request of that method in one call;
//   reply-<method>.json     otherwise this one for every request of that method; with neither, the result is {}.
//   error-<method>.json     when present (and no reply file applies), the raw JSON "error" object sent instead.
//   silent-<method>         when present, a request of that method is never answered.
//   after-<method>-<k>.jsonl / after-<method>.jsonl
//                           frames (one raw JSON message per non-empty line) sent right after answering the k-th
//                           (or any) request of that method, in order: notifications or server requests.
//   stderr.txt              written to stderr once at start.
//   exit-after-<method>     when present, the run exits right after answering (and sending the after-frames of) a
//                           request of that method.
// The run exits when stdin closes, or on its own after 20 real seconds with no new frame, so a test that starts
// a session and never stops it does not leave the process running for the rest of the suite.
// Without COPILOT_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class CopilotStandIn
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("COPILOT_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir)) return 0;
        Directory.CreateDirectory(dir);

        int call = 0;
        while (File.Exists(Path.Combine(dir, "argv-" + call + ".txt"))) call++;
        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = Utf8.GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        WriteAtomically(Path.Combine(dir, "argv-" + call + ".txt"), argv.ToArray());

        string stderrPath = Path.Combine(dir, "stderr.txt");
        if (File.Exists(stderrPath))
        {
            Stream stderr = Console.OpenStandardError();
            byte[] text = File.ReadAllBytes(stderrPath);
            stderr.Write(text, 0, text.Length);
            stderr.Flush();
        }

        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        Queue<byte[]> pending = new Queue<byte[]>();
        object gate = new object();
        bool closed = false;
        SemaphoreSlim signal = new SemaphoreSlim(0);
        Thread reader = new Thread(delegate ()
        {
            while (true)
            {
                byte[] frame = ReadFrame(input);
                lock (gate)
                {
                    if (frame == null) closed = true;
                    else pending.Enqueue(frame);
                }
                signal.Release();
                if (frame == null) break;
            }
        });
        reader.IsBackground = true;
        reader.Start();

        Dictionary<string, int> seen = new Dictionary<string, int>();
        int message = 0;
        while (true)
        {
            if (!signal.Wait(TimeSpan.FromSeconds(20))) break;
            byte[] body = null;
            bool isClosed;
            lock (gate)
            {
                isClosed = closed && pending.Count == 0;
                if (pending.Count > 0) body = pending.Dequeue();
            }
            if (body == null)
            {
                if (isClosed) break;
                continue;
            }
            WriteAtomically(Path.Combine(dir, "captured-" + call + "-" + message + ".json"), body);
            message++;

            string text = Utf8.GetString(body);
            string method = TopLevel(text, "method");
            string id = TopLevel(text, "id");
            if (method == null || id == null) continue;
            method = Unquote(method);
            int count;
            seen.TryGetValue(method, out count);
            count++;
            seen[method] = count;

            if (File.Exists(Path.Combine(dir, "silent-" + method))) continue;
            string reply = ReadIfExists(Path.Combine(dir, "reply-" + method + "-" + count + ".json"))
                ?? ReadIfExists(Path.Combine(dir, "reply-" + method + ".json"));
            string error = reply == null ? ReadIfExists(Path.Combine(dir, "error-" + method + ".json")) : null;
            if (error != null)
                WriteFrame(output, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":" + error.Trim() + "}");
            else
                WriteFrame(output, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + (reply == null ? "{}" : reply.Trim()) + "}");

            string after = ReadIfExists(Path.Combine(dir, "after-" + method + "-" + count + ".jsonl"))
                ?? ReadIfExists(Path.Combine(dir, "after-" + method + ".jsonl"));
            if (after != null)
            {
                foreach (string line in after.Split('\n'))
                {
                    string frame = line.Trim();
                    if (frame.Length > 0) WriteFrame(output, frame);
                }
            }
            if (File.Exists(Path.Combine(dir, "exit-after-" + method))) break;
        }
        return 0;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path);
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path) ? Utf8.GetString(File.ReadAllBytes(path)) : null;
    }

    private static void WriteFrame(Stream output, string json)
    {
        byte[] body = Utf8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
        output.Write(header, 0, header.Length);
        output.Write(body, 0, body.Length);
        output.Flush();
    }

    /// <summary>One Content-Length framed body from the stream, or null at its end.</summary>
    private static byte[] ReadFrame(Stream input)
    {
        int length = -1;
        while (true)
        {
            string line = ReadHeaderLine(input);
            if (line == null) return null;
            if (line.Length == 0)
            {
                if (length >= 0) break;
                continue;
            }
            int colon = line.IndexOf(':');
            if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line.Substring(colon + 1).Trim());
        }
        byte[] body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = input.Read(body, read, length - read);
            if (n <= 0) return null;
            read += n;
        }
        return body;
    }

    /// <summary>One header line without its CRLF (or LF), or null at the end of the stream.</summary>
    private static string ReadHeaderLine(Stream input)
    {
        MemoryStream line = new MemoryStream();
        while (true)
        {
            int b = input.ReadByte();
            if (b < 0) return null;
            if (b == '\n') break;
            line.WriteByte((byte)b);
        }
        string text = Encoding.ASCII.GetString(line.ToArray());
        return text.EndsWith("\r", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
    }

    /// <summary>The raw JSON text of a top-level member of the object in <paramref name="json"/>, or null.</summary>
    private static string TopLevel(string json, string key)
    {
        int i = SkipSpace(json, 0);
        if (i >= json.Length || json[i] != '{') return null;
        i++;
        while (true)
        {
            i = SkipSpace(json, i);
            if (i >= json.Length || json[i] == '}') return null;
            int keyEnd = SkipValue(json, i);
            string name = Unquote(json.Substring(i, keyEnd - i));
            i = SkipSpace(json, keyEnd);
            i++; // ':'
            i = SkipSpace(json, i);
            int valueEnd = SkipValue(json, i);
            if (name == key) return json.Substring(i, valueEnd - i);
            i = SkipSpace(json, valueEnd);
            if (i < json.Length && json[i] == ',') i++;
        }
    }

    private static int SkipSpace(string json, int i)
    {
        while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n')) i++;
        return i;
    }

    /// <summary>The index just past the JSON value starting at <paramref name="i"/>.</summary>
    private static int SkipValue(string json, int i)
    {
        if (json[i] == '"')
        {
            i++;
            while (json[i] != '"')
            {
                if (json[i] == '\\') i++;
                i++;
            }
            return i + 1;
        }
        if (json[i] == '{' || json[i] == '[')
        {
            int depth = 0;
            while (true)
            {
                char c = json[i];
                if (c == '"') { i = SkipValue(json, i); continue; }
                if (c == '{' || c == '[') depth++;
                if (c == '}' || c == ']') depth--;
                i++;
                if (depth == 0) return i;
            }
        }
        while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && json[i] != ' ' && json[i] != '\r' && json[i] != '\n' && json[i] != '\t') i++;
        return i;
    }

    private static string Unquote(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"') return raw.Substring(1, raw.Length - 2).Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
        return raw;
    }
}
