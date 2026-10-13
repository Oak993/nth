// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for the Pi coding harness (`pi --mode rpc`)
// used by the Services.Providers.PiCLI tests (R-TEST-8, R-TEST-10). It reaches no network and runs no real AI CLI.
// Compiled with the system's .NET Framework csc.exe (tests/Marrow.Services.Tests/Fixtures/PiStandIns.cs), so it is a
// real .exe that StdioProcess can spawn with CreateProcessW (R-PROC-1).
//
// Controlled by PI_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv.txt          written by the stand-in: every argument, NUL-separated.
//   cwd.txt           written by the stand-in: its working directory.
//   env-<KEY>.txt     written by the stand-in for each of PI_SKIP_VERSION_CHECK, DROPPY_CODE_PI_GATE,
//                     DROPPY_CODE_MCP_BRIDGE, DROPPY_CODE_MCP_CONFIG and PI_CACHE_RETENTION that is set: the value.
//   stdin-<m>.txt     written by the stand-in: the raw text of the m-th stdin line (0-based).
//   reply-<m>.jsonl   read by the stand-in: text written to stdout right after the m-th stdin line (missing: none).
//   eof.txt           written by the stand-in when its stdin closes.
//   ignore-eof        when present, a closed stdin does not end the run (only a terminate or the 60 s limit does).
// The run exits when stdin closes, or on its own after 60 real seconds, so no test leaves it running.
// Without PI_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.IO;
using System.Text;
using System.Threading;

internal static class PiStandIn
{
    private static string dir;
    private static readonly ManualResetEvent done = new ManualResetEvent(false);

    private static int Main(string[] args)
    {
        dir = Environment.GetEnvironmentVariable("PI_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir)) return 0;
        Directory.CreateDirectory(dir);

        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        Write("argv.txt", argv.ToArray());
        Write("cwd.txt", new UTF8Encoding(false).GetBytes(Environment.CurrentDirectory));
        foreach (string key in new string[] { "PI_SKIP_VERSION_CHECK", "DROPPY_CODE_PI_GATE", "DROPPY_CODE_MCP_BRIDGE", "DROPPY_CODE_MCP_CONFIG", "PI_CACHE_RETENTION" })
        {
            string value = Environment.GetEnvironmentVariable(key);
            if (value != null) Write("env-" + key + ".txt", new UTF8Encoding(false).GetBytes(value));
        }

        Thread reader = new Thread(ReadLoop);
        reader.IsBackground = true;
        reader.Start();
        done.WaitOne(60000);
        return 0;
    }

    private static void ReadLoop()
    {
        StreamReader input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        output.AutoFlush = true;
        int line = 0;
        while (true)
        {
            string text = input.ReadLine();
            if (text == null) break;
            Write("stdin-" + line + ".txt", new UTF8Encoding(false).GetBytes(text));
            string reply = Path.Combine(dir, "reply-" + line + ".jsonl");
            if (File.Exists(reply))
            {
                byte[] bytes = File.ReadAllBytes(reply);
                output.BaseStream.Write(bytes, 0, bytes.Length);
                output.BaseStream.Flush();
            }
            line++;
        }
        Write("eof.txt", new byte[] { 0x31 });
        if (File.Exists(Path.Combine(dir, "ignore-eof"))) return; // a test that needs a child only a Terminate can end: it then runs to its 60 s limit
        done.Set();
    }

    private static void Write(string name, byte[] bytes)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string lockName;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            lockName = "Local\\MarrowPiStandIn-" + BitConverter.ToString(hash.ComputeHash(identity)).Replace("-", "");
        using (var publication = new Mutex(false, lockName))
        {
            try { publication.WaitOne(); }
            catch (AbandonedMutexException) { /* Ownership is acquired even though the previous publisher died. */ }
            try
            {
                string pending = Path.Combine(dir, name + ".tmp");
                File.WriteAllBytes(pending, bytes);
                string target = Path.Combine(dir, name);
                if (File.Exists(target)) File.Delete(target);
                File.Move(pending, target);
            }
            finally { publication.ReleaseMutex(); }
        }
    }
}
