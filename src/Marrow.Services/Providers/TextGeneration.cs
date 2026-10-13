using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/TextGeneration.swift (B2 1fbf81e), :1-102.

/// <summary>One-shot generation of thread titles and commit messages through a provider CLI with no tools. :3-4</summary>
public static class TextGeneration
{
    // :5-8
    public abstract record Engine
    {
        private Engine() { }

        // :6
        public sealed record Claude(string Executable, ValueMap<string, string> Environment) : Engine
        {
            // PORT-ONLY: DESIGN.md 4.3, ED-NEW-dextra-open-1. Titles keep the source's haiku default.
            public string Model { get; init; } = "haiku";
        }

        // :7
        public sealed record Codex(string Executable, ValueMap<string, string> Environment, string Model) : Engine;
    }

    // The characters CharacterSet(charactersIn: "\"'`*#. ") trims from a cleaned title (:27, :34).
    private static readonly char[] TitleTrimChars = ['"', '\'', '`', '*', '#', '.', ' '];

    // The UTF-16 units Character.isNewline reads as the first scalar of a newline Character (LF, VT, FF, CR, NEL,
    // LINE SEPARATOR, PARAGRAPH SEPARATOR): the same set StdioFramer.swift:72's split(whereSeparator: \.isNewline)
    // reads, ported the same way (StdioFramer.cs :72-75; CR LF is one Character either way, so splitting on these
    // UTF-16 units and dropping empty pieces gives the same lines Swift's split would).
    private static readonly char[] NewlineUnits =
    [
        '\n', '\r', '\U0000000B', '\U0000000C',
        '\U00000085', // U+0085 NEXT LINE (NEL)
        '\U00002028', // U+2028 LINE SEPARATOR
        '\U00002029', // U+2029 PARAGRAPH SEPARATOR
    ];

    // :47-49
    private static readonly string[] Openers =
    [
        "i ", "i'", "sorry", "sure", "here", "please", "could you", "can you",
        "what ", "it seems", "it looks", "unfortunately", "there is no",
        "not enough", "need more", "maybe ", "the request", "this request",
    ];

    // :51
    private static readonly string[] Phrases =
        ["more context", "meaningful title", "as an ai", "cannot", "can't", "unable to"];

    // :45
    private static readonly string[] ForbiddenEndings = [":", ";", ",", "!", "…"]; // U+2026 HORIZONTAL ELLIPSIS

    // :10-35
    public static async Task<string?> ThreadTitle(string message, Engine engine)
    {
        // :11-22
        var prompt =
            "Write a title for this coding task so the user can recognize it weeks later.\n" +
            "Rules: 3 to 7 words, under 40 characters, sentence case, no quotes, no trailing punctuation.\n" +
            "Name the subject and the outcome. Do not copy the request word for word.\n" +
            "The request may be a word or two, or come with attachments; even then, reply with\n" +
            "your single best title and nothing else. Never ask a question, never explain,\n" +
            "never mention the request or the word title.\n" +
            "Reply with the title only.\n" +
            "\n" +
            "Request:\n" +
            SwiftString.Prefix(message, 4_000);
        // :23-25
        if (await Run(prompt, engine, TemporaryDirectory) is not { } text) return null;
        // :26-27
        var cleaned = TextCleanup.SingleLine(text, 60).Trim(TitleTrimChars);
        // :28
        if (Usable(cleaned) is { } title) return title;
        // :29-33 A model that answers with a sentence usually puts the title on its first non-empty line;
        // that line is worth keeping on its own.
        var firstLine = text
            .Split(NewlineUnits, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => FoundationText.Trim(line)) // :32 .trimmingCharacters(in: .whitespaces)
            .FirstOrDefault(line => line.Length > 0); // :33
        // :34
        return firstLine is null ? null : Usable(firstLine.Trim(TitleTrimChars));
    }

    /// <summary>
    /// :37-39 A reply that reads as a title, or nil when the model answered with a sentence or a question
    /// instead - "I need more context to create a meaningful title. What spec..." is one such reply that
    /// reached a chat's name.
    /// </summary>
    private static string? Usable(string candidate)
    {
        // :41
        var title = candidate.Trimmed();
        // :42
        if (title.Length == 0 || SwiftString.Count(title) > 64) return null;
        // :43 title.split(whereSeparator: \.isWhitespace).count <= 9, counted the same way Shell.cs's Tokens
        // counts runs of non-whitespace Characters (Shell.swift:186).
        if (WordCount(title) > 9) return null;
        // :44
        if (title.Contains('\n', StringComparison.Ordinal) || title.Contains('?', StringComparison.Ordinal)) return null;
        // :45
        var characters = SwiftString.Characters(title).ToArray();
        var last = characters[^1];
        if (ForbiddenEndings.Any(ending => SwiftString.Equal(last, ending))) return null;
        // :46
        var lower = SwiftString.Lowercased(title);
        // :47-50
        if (Openers.Any(opener => SwiftString.HasPrefix(lower, opener))) return null;
        // :51-52
        if (Phrases.Any(phrase => lower.Contains(phrase, StringComparison.Ordinal))) return null;
        // :53
        return title;
    }

    // :43 `title.split(whereSeparator: \.isWhitespace).count`
    private static int WordCount(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftCharacter.IsWhitespace(character)) inWord = false;
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }
        return count;
    }

    // :56-74
    public static async Task<string?> CommitMessage(string summary, string patch, string instructions, Engine engine, string directory)
    {
        // :57
        var extra = instructions.Length == 0 ? "" : "\nFollow these instructions too: " + instructions + "\n";
        // :58-68
        var prompt =
            "Write a git commit message for the changes below.\n" +
            "Rules: an imperative subject line of at most 72 characters with no trailing period. " +
            "Optionally add a blank line and a few short bullet points. Reply with the message only, without code fences.\n" +
            extra +
            "\n" +
            "Changed files:\n" +
            SwiftString.Prefix(summary, 6_000) +
            "\n" +
            "\n" +
            "Patch:\n" +
            SwiftString.Prefix(patch, 40_000);
        // :69
        if (await Run(prompt, engine, directory) is not { } text) return null;
        // :70-72
        var message = text.Replace("```", "", StringComparison.Ordinal).Trimmed();
        // :73
        return message.Length == 0 ? null : message;
    }

    /// <summary>
    /// PORT-ONLY (Dextra AS-22, 4.3, ED-NEW-dextra-as22-10): the engine's one-shot reply to a prompt, with no tools and no session, in the user temporary
    /// directory. The delegate responder reads a question through it. Null when the engine failed or said nothing.
    /// </summary>
    public static Task<string?> Complete(string prompt, Engine engine) => Run(prompt, engine, TemporaryDirectory);

    // PORT-ONLY: ED-NEW-dextra-open-2. Side calls consume the CLI's completed-turn usage; ordinary titles keep their exact argv.
    public static Task<string?> Complete(string prompt, Engine engine, Action<TokenSpend> spend) => Run(prompt, engine, TemporaryDirectory, spend);

    internal static TokenSpend? CodexSpend(string stdout, string model)
    {
        TokenSpend? result = null;
        foreach (var line in stdout.Split('\n').Take(4096))
        {
            if (line.Length == 0 || line.Length > 64_000) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "turn.completed" || !root.TryGetProperty("usage", out var usage)) continue;
                long Read(string name) => usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? Math.Max(0, number) : 0;
                var input = Read("input_tokens"); var output = Read("output_tokens"); var cached = Math.Min(input, Read("cached_input_tokens"));
                // One ephemeral exec has one completed turn; repeated terminal summaries do not double debit it.
                result = new TokenSpend(ProviderKind.Codex, checked(input + output), input, output, cached, model);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or OverflowException) { continue; }
        }
        return result;
    }

    // :76-100
    private static async Task<string?> Run(string prompt, Engine engine, string directory, Action<TokenSpend>? spend = null)
    {
        switch (engine)
        {
            case Engine.Claude claude:
            {
                // :79-83
                var arguments = new[]
                {
                    "-p", "--output-format", "text", "--model", claude.Model, "--tools", "",
                    "--disable-slash-commands", "--strict-mcp-config", "--permission-mode", "dontAsk",
                    "--no-session-persistence",
                };
                // :84-85
                async Task<ShellResult?> RunClaude() => await Shell.Run(claude.Executable, arguments, directory,
                    claude.Environment, Encoding.UTF8.GetBytes(prompt), 120);
                if (await SwiftTry.OptionalAsync(RunClaude) is not { Succeeded: true } result)
                    return null;
                // :86
                return result.TrimmedOutput.NilIfEmpty();
            }
            case Engine.Codex codex:
            {
                // :88 DEVIATION OD-0012 F1: the file is "marrow-<UUID>.txt", not upstream's "droppy-code-<UUID>.txt";
                // its path is only ever handed to the Codex CLI as --output-last-message and removed after the run,
                // never one of the two kept machine contracts (droppy-code.json, DROPPY_CODE_PROJECT_ROOT).
                var output = SwiftPath.AppendingPathComponent(TemporaryDirectory, "marrow-" + SwiftUUID.UuidString(Guid.NewGuid()) + ".txt");
                try
                {
                    // :90-93
                    var arguments = new List<string>
                    {
                        "exec", "--ephemeral", "--skip-git-repo-check", "-s", "read-only", "--model", codex.Model,
                        "--config", "model_reasoning_effort=\"low\"", "--output-last-message", output, "-",
                    };
                    if (spend is not null) arguments.Insert(1, "--json");
                    // :94-95
                    async Task<ShellResult?> RunCodex() => await Shell.Run(codex.Executable, arguments, directory,
                        codex.Environment, Encoding.UTF8.GetBytes(prompt), 180);
                    var result = await SwiftTry.OptionalAsync(RunCodex);
                    if (result is not null && spend is not null && CodexSpend(result.Output, codex.Model) is { } usage) spend(usage);
                    if (result is not { Succeeded: true }) return null;
                    // :96-98
                    return SwiftTry.Optional(() => SwiftText.DecodeUtf8OrNil(File.ReadAllBytes(output)))
                        ?.Trimmed()
                        .NilIfEmpty();
                }
                finally
                {
                    // :89
                    SwiftTry.Ignore(() => File.Delete(output));
                }
            }
            default:
                throw new UnreachableException();
        }
    }

    // FileManager.default.temporaryDirectory (:23, :88): the Windows user temporary directory (PR-PROVAPI-187
    // windows_path), the same mapping WorkingTreeWatch.cs:100 gives it.
    private static string TemporaryDirectory =>
        Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? System.IO.Path.GetTempPath();
}
