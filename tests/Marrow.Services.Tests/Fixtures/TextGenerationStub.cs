using System.Collections;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// A compiled stand-in "engine" executable for TextGenerationTests (R-TEST-8). ProcessLauncher.cs:520-521 refuses to
/// launch a .cmd or .bat directly, so the stand-in is a real PE console application, compiled once via Windows
/// PowerShell's Add-Type (a system binary, R-TEST-10; nothing here starts an AI CLI) and reused by every test.
/// Driven entirely by environment variables, never by its own argv (which every test needs free to assert the exact
/// vector TextGeneration.cs builds), it records the argv it was given, its current directory and its standard input
/// to files, then answers as either a stdout-replying CLI (Claude) or a file-replying CLI (Codex), with a chosen
/// exit code, so a test can drive every path TextGeneration's Run(_:engine:directory:) takes.
/// </summary>
public static class TextGenerationStub
{
    private const string Source = """
        using System;
        using System.IO;
        public static class MarrowTextGenerationStub
        {
            public static int Main(string[] args)
            {
                var argvFile = Environment.GetEnvironmentVariable("MARROW_STUB_ARGV_FILE");
                if (argvFile != null) File.WriteAllLines(argvFile, args);
                var cwdFile = Environment.GetEnvironmentVariable("MARROW_STUB_CWD_FILE");
                if (cwdFile != null) File.WriteAllText(cwdFile, Environment.CurrentDirectory);
                var stdinFile = Environment.GetEnvironmentVariable("MARROW_STUB_STDIN_FILE");
                if (stdinFile != null)
                {
                    using (var stdin = Console.OpenStandardInput())
                    using (var buffer = new MemoryStream())
                    {
                        stdin.CopyTo(buffer);
                        File.WriteAllBytes(stdinFile, buffer.ToArray());
                    }
                }
                var replyFile = Environment.GetEnvironmentVariable("MARROW_STUB_REPLY_FILE");
                var reply = (replyFile != null && File.Exists(replyFile)) ? File.ReadAllBytes(replyFile) : Array.Empty<byte>();
                var mode = Environment.GetEnvironmentVariable("MARROW_STUB_MODE") ?? "stdout";
                if (mode == "stdout")
                {
                    using (var stdout = Console.OpenStandardOutput())
                    {
                        stdout.Write(reply, 0, reply.Length);
                        stdout.Flush();
                    }
                }
                else if (mode == "outputfile")
                {
                    var flag = Array.IndexOf(args, "--output-last-message");
                    var writeOutput = Environment.GetEnvironmentVariable("MARROW_STUB_WRITE_OUTPUT") != "false";
                    if (flag >= 0 && flag + 1 < args.Length && writeOutput)
                        File.WriteAllBytes(args[flag + 1], reply);
                }
                var exitText = Environment.GetEnvironmentVariable("MARROW_STUB_EXIT");
                return exitText != null ? int.Parse(exitText) : 0;
            }
        }
        """;

    private static readonly Lazy<string> CompiledPath = new(Compile);

    /// <summary>The compiled stand-in's path, compiling it into a folder under MARROW_PORT_DATA_DIR on first use.</summary>
    public static string Path => CompiledPath.Value;

    /// <summary>The host process's own environment as a case-insensitive dictionary, as every environment dictionary
    /// in this codebase is (ED-128) - the compiled stub needs the ordinary Windows and .NET runtime variables to
    /// start at all.</summary>
    public static Dictionary<string, string> HostEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);

    private static string Compile()
    {
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = System.IO.Path.Join(root, "text-generation-stub");
        Directory.CreateDirectory(folder);
        var outputPath = System.IO.Path.Join(folder, "marrow-textgen-stub.exe");
        var script = "$src = @'\n" + Source + "\n'@\n" +
            "Add-Type -OutputType ConsoleApplication -OutputAssembly '" + outputPath.Replace("'", "''", StringComparison.Ordinal) +
            "' -TypeDefinition $src -Language CSharp\n";
        var powerShell = System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'; " + script));
        var result = Shell.Run(powerShell, ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], null, HostEnvironment(), null, 60)
            .GetAwaiter().GetResult();
        if (!result.Succeeded || !File.Exists(outputPath))
            throw new InvalidOperationException("Could not compile the TextGeneration test stub: " + result.FailureMessage);
        return outputPath;
    }
}
