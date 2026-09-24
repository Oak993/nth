using System.Globalization;
using System.Text;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Test-only orchestration for TextGenerationTests: preparing a run of <see cref="TextGenerationStub"/> and
/// reading back what it captured. Helper types and methods live here, never inside a test class (R-TEST-3).</summary>
public sealed class StubRun
{
    public required string ArgvFile { get; init; }
    public required string CwdFile { get; init; }
    public required string StdinFile { get; init; }
    public required Dictionary<string, string> Environment { get; init; }
}

public static class TextGenerationHarness
{
    public static string TestFolder()
    {
        var root = Marrow.Testing.TestDataDirectory.Read()
            ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Join(root, "text-generation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Arranges the stub's environment for one call. <paramref name="mode"/> is "stdout" (Claude) or
    /// "outputfile" (Codex); <paramref name="reply"/> is what it answers with; <paramref name="writeOutput"/> false
    /// simulates Codex never writing the file at all.</summary>
    public static StubRun PrepareStub(string mode, byte[] reply, int exit = 0, bool writeOutput = true)
    {
        var folder = TestFolder();
        var argvFile = Path.Join(folder, "argv.txt");
        var cwdFile = Path.Join(folder, "cwd.txt");
        var stdinFile = Path.Join(folder, "stdin.bin");
        var replyFile = Path.Join(folder, "reply.bin");
        File.WriteAllBytes(replyFile, reply);
        var environment = TextGenerationStub.HostEnvironment();
        environment["MARROW_STUB_ARGV_FILE"] = argvFile;
        environment["MARROW_STUB_CWD_FILE"] = cwdFile;
        environment["MARROW_STUB_STDIN_FILE"] = stdinFile;
        environment["MARROW_STUB_REPLY_FILE"] = replyFile;
        environment["MARROW_STUB_MODE"] = mode;
        environment["MARROW_STUB_EXIT"] = exit.ToString(CultureInfo.InvariantCulture);
        environment["MARROW_STUB_WRITE_OUTPUT"] = writeOutput ? "true" : "false";
        return new StubRun { ArgvFile = argvFile, CwdFile = cwdFile, StdinFile = stdinFile, Environment = environment };
    }

    public static TextGeneration.Engine.Claude ClaudeEngine(StubRun run) =>
        new(TextGenerationStub.Path, new ValueMap<string, string>(run.Environment, StringComparer.OrdinalIgnoreCase));

    public static TextGeneration.Engine.Codex CodexEngine(StubRun run, string model = "gpt-5-codex") =>
        new(TextGenerationStub.Path, new ValueMap<string, string>(run.Environment, StringComparer.OrdinalIgnoreCase), model);

    public static string[] Argv(StubRun run) => File.Exists(run.ArgvFile) ? File.ReadAllLines(run.ArgvFile) : [];

    public static string Stdin(StubRun run) => File.Exists(run.StdinFile) ? Encoding.UTF8.GetString(File.ReadAllBytes(run.StdinFile)) : "";

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public static string ExpectedTemporaryDirectory =>
        Marrow.Testing.TestDataDirectory.Read() ?? Path.GetTempPath();
}
