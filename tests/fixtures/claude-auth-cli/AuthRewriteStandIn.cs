// PORT-ONLY test stand-in, no Swift twin: a scripted stand-in for the Claude Code CLI's `auth status` refresh
// call, used only by the Services.Providers.PlanLimits tests (R-TEST-8, R-TEST-10). It reaches no network and
// runs no real AI CLI. Compiled with the system's .NET Framework csc.exe, exactly the ClaudeStandIns.cs pattern.
//
// Records every call's argv to AUTH_STANDIN_DIR\argv-<n>.txt (NUL-separated, 0-based), the same shape
// ClaudeStandIn.cs uses. When AUTH_REWRITE_SOURCE and AUTH_REWRITE_TARGET are both set, it then copies the
// source file onto the target file (overwrite), reproducing the Claude CLI's own credential-file rewrite on a
// refresh; when either is unset, it writes nothing further. Always exits 0.
using System;
using System.IO;
using System.Text;

internal static class AuthRewriteStandIn
{
    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("AUTH_STANDIN_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
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
            File.WriteAllBytes(Path.Combine(dir, "argv-" + call + ".txt"), argv.ToArray());
        }

        string source = Environment.GetEnvironmentVariable("AUTH_REWRITE_SOURCE");
        string target = Environment.GetEnvironmentVariable("AUTH_REWRITE_TARGET");
        if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target) && File.Exists(source))
        {
            File.Copy(source, target, overwrite: true);
        }
        return 0;
    }
}
