// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for Google's Antigravity CLI (`agy`), used by
// the Services.Providers.AntigravitySession tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI.
// Compiled with the system's .NET Framework csc.exe (tests/Marrow.Services.Tests/Fixtures/AntigravityStandIns.cs), so
// it is a real .exe: AntigravitySession spawns its configured executable with CreateProcessW directly (R-PROC-1), so a
// .cmd or .ps1 script cannot stand in for it. Written in C# 5 (the language level of the Framework's csc.exe).
//
// It is controlled by AGY_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt            written by the stand-in itself: every argument of call n (0-based), NUL-separated.
//
// One-shot calls (the first argument is not "--input-format"): the key is "models" for `agy models`, the second
// argument without its leading "/" for `agy -p /<key> ...`, and "other" otherwise.
//   <key>.stdout            written to stdout as raw bytes (nothing when absent).
//   <key>.stderr            written to stderr as raw bytes (nothing when absent).
//   <key>.exit              the exit status (0 when absent).
//   rendezvous              when present, its non-empty lines are keys: the call writes started-<key> and waits up to
//                           10 real seconds for started-<k> of every listed key, then writes rendezvous-<key>.txt
//                           holding "ok" when all appeared and "timeout" otherwise, before it prints anything.
//
// Session calls (the first argument is "--input-format", AntigravitySession.swift:76-94):
//   session.stderr          written to stderr at start.
//   init.jsonl              lines written to stdout at start, in order.
//   exit-at-start           when present, the run exits right after the start lines, with the status it holds.
//   captured-<n>-<m>.txt    written by the stand-in itself: the raw text of the m-th stdin line (0-based) of call n,
//                           written before any reply to it is sent.
//   turn-<m>.jsonl          lines written to stdout after the m-th stdin line (0-based); otherwise turn.jsonl.
//   turn-<m>.stderr         written to stderr after the m-th line's stdout lines.
//   exit-after-turn-<m>     when present, the run exits after answering the m-th line, with the status it holds.
//   ignore-eof             when present, a closed stdin does not end a session run (only a terminate or the 20 s idle limit does).
// The session run exits when stdin closes, or on its own after 20 real seconds with no new line, so a test that
// never stops its session does not leave the process running for the rest of the suite.
// Without AGY_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class AntigravityStandIn
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("AGY_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir)) return 0;
        Directory.CreateDirectory(dir);

        int call = RecordArguments(dir, args);
        if (args.Length > 0 && args[0] == "--input-format") return Session(dir, call);
        return OneShot(dir, args);
    }

    private static int RecordArguments(string dir, string[] args)
    {
        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = Utf8.GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        byte[] data = argv.ToArray();
        // Two concurrent calls may race for the same number; creating the file exclusively decides it.
        for (int call = 0; ; call++)
        {
            string path = Path.Combine(dir, "argv-" + call + ".txt");
            if (File.Exists(path)) continue;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, data);
            try
            {
                File.Move(temporary, path);
                return call;
            }
            catch (IOException)
            {
                File.Delete(temporary);
            }
        }
    }

    private static int OneShot(string dir, string[] args)
    {
        string key = "other";
        if (args.Length > 0 && args[0] == "models") key = "models";
        else if (args.Length > 1 && args[0] == "-p") key = args[1].TrimStart('/');

        string rendezvous = ReadIfExists(Path.Combine(dir, "rendezvous"));
        if (rendezvous != null)
        {
            File.WriteAllText(Path.Combine(dir, "started-" + key), "1");
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            bool all = false;
            while (DateTime.UtcNow < deadline)
            {
                all = true;
                foreach (string line in rendezvous.Split('\n'))
                {
                    string other = line.Trim();
                    if (other.Length > 0 && !File.Exists(Path.Combine(dir, "started-" + other))) all = false;
                }
                if (all) break;
                Thread.Sleep(10);
            }
            File.WriteAllText(Path.Combine(dir, "rendezvous-" + key + ".txt"), all ? "ok" : "timeout");
        }

        WriteFileTo(Console.OpenStandardOutput(), Path.Combine(dir, key + ".stdout"));
        WriteFileTo(Console.OpenStandardError(), Path.Combine(dir, key + ".stderr"));
        return Status(Path.Combine(dir, key + ".exit"));
    }

    private static int Session(string dir, int call)
    {
        Stream output = Console.OpenStandardOutput();
        WriteFileTo(Console.OpenStandardError(), Path.Combine(dir, "session.stderr"));
        WriteLines(output, ReadIfExists(Path.Combine(dir, "init.jsonl")));
        string exitAtStart = Path.Combine(dir, "exit-at-start");
        if (File.Exists(exitAtStart))
        {
            // Give the client's stderr reader time to take the tail before stdout closes.
            Thread.Sleep(300);
            return Status(exitAtStart);
        }

        Stream input = Console.OpenStandardInput();
        Queue<string> pending = new Queue<string>();
        object gate = new object();
        bool closed = false;
        SemaphoreSlim signal = new SemaphoreSlim(0);
        Thread reader = new Thread(delegate ()
        {
            MemoryStream line = new MemoryStream();
            while (true)
            {
                int value = input.ReadByte();
                if (value < 0)
                {
                    lock (gate) closed = true;
                    signal.Release();
                    break;
                }
                if (value != 0x0A)
                {
                    line.WriteByte((byte)value);
                    continue;
                }
                string text = Utf8.GetString(line.ToArray());
                line.SetLength(0);
                if (text.Trim().Length == 0) continue;
                lock (gate) pending.Enqueue(text);
                signal.Release();
            }
        });
        reader.IsBackground = true;
        reader.Start();

        int message = 0;
        while (true)
        {
            if (!signal.Wait(TimeSpan.FromSeconds(20))) break;
            string text = null;
            bool isClosed;
            lock (gate)
            {
                isClosed = closed && pending.Count == 0;
                if (pending.Count > 0) text = pending.Dequeue();
            }
            if (text == null)
            {
                if (isClosed && !File.Exists(Path.Combine(dir, "ignore-eof"))) break;
                continue;
            }
            string captured = Path.Combine(dir, "captured-" + call + "-" + message + ".txt");
            File.WriteAllBytes(captured + ".tmp", Utf8.GetBytes(text));
            File.Move(captured + ".tmp", captured);

            string reply = ReadIfExists(Path.Combine(dir, "turn-" + message + ".jsonl"))
                ?? ReadIfExists(Path.Combine(dir, "turn.jsonl"));
            WriteLines(output, reply);
            WriteFileTo(Console.OpenStandardError(), Path.Combine(dir, "turn-" + message + ".stderr"));
            string exitAfter = Path.Combine(dir, "exit-after-turn-" + message);
            message++;
            if (File.Exists(exitAfter))
            {
                Thread.Sleep(300);
                return Status(exitAfter);
            }
        }
        return 0;
    }

    private static void WriteLines(Stream output, string text)
    {
        if (text == null) return;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            byte[] bytes = Utf8.GetBytes(trimmed + "\n");
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }
    }

    private static void WriteFileTo(Stream stream, string path)
    {
        if (!File.Exists(path)) return;
        byte[] bytes = File.ReadAllBytes(path);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static int Status(string path)
    {
        string text = ReadIfExists(path);
        int status;
        return text != null && int.TryParse(text.Trim(), out status) ? status : 0;
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path) ? Utf8.GetString(File.ReadAllBytes(path)) : null;
    }
}
