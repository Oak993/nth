using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// ED-NEW-lean-4 (lean pass, unit L4): search_text reads each file line by line instead of reading the whole file,
// decoding it and splitting it, and its output stays byte-identical. "Lines" stay what SwiftString.Split(text, "\n")
// made them: "\r\n" is one grapheme cluster that never equals "\n", so a CRLF pair never ends a line (the CRLF
// note in OpenAICompatibleSessionFourthTests); a file that is not valid UTF-8 anywhere is skipped whole; a BOM is
// kept as U+FEFF; the segment after the last "\n" is a line even when empty. The fixed goldens were checked on the
// unchanged whole-file code first; the random cases compare with Oracle, a frozen copy of that code.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-lean-4")]
public sealed class SearchTextGoldenTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    // The scheme:path-rootless form, as OpenAICompatibleSessionFourthTests explains.
    private static SwiftURL WorkingDirectoryURL(string root) => SwiftURL.Create("file:" + root.Replace('\\', '/'))!;

    private static OpenAICompatibleSession Session(string root)
    {
        var session = new OpenAICompatibleSession(MakeConfig(), new SessionConfiguration
        {
            Provider = ProviderKind.Deepseek,
            WorkingDirectory = WorkingDirectoryURL(root),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            ApiKey = "sk-test",
        });
        session.OnEvent = _ => { };
        return session;
    }

    private static Task<string> Search(OpenAICompatibleSession session, string pattern, string path, bool regex = false)
    {
        var arguments = new Dictionary<string, object> { ["pattern"] = pattern, ["path"] = path };
        if (regex) arguments["regex"] = true;
        return session.ExecuteToolForTests(new("call-1", "search_text", System.Text.Json.JsonSerializer.Serialize(arguments)));
    }

    // The whole-file search as it was before ED-NEW-lean-4, kept frozen: strict UTF-8 decode of the whole file
    // (null skips it), SwiftString.Split on "\n" keeping empty pieces, then the 120-line cap.
    private static string Oracle(string name, byte[] bytes, Func<string, bool> matches)
    {
        var text = SwiftText.DecodeUtf8OrNil(bytes);
        var found = new List<string>();
        if (text is not null)
        {
            var lines = SwiftString.Split(text, "\n", omittingEmptySubsequences: false);
            for (var index = 0; index < lines.Count; index++)
            {
                if (matches(lines[index])) found.Add($"{name}:{index + 1}:{lines[index]}");
            }
        }
        if (found.Count == 0) return "(no matches)";
        return found.Count > 120
            ? string.Join("\n", found.Take(120)) + $"\n\U00002026({found.Count} matches, truncated)"
            : string.Join("\n", found);
    }

    [Theory]
    [InlineData("crlf.txt", "alpha needle\r\nbeta\r\nneedle gamma\r\n", "crlf.txt:1:alpha needle\r\nbeta\r\nneedle gamma\r\n")]
    [InlineData("mixed.txt", "needle one\n\nneedle three\r\nneedle four", "mixed.txt:1:needle one\nmixed.txt:3:needle three\r\nneedle four")]
    [InlineData("nofinal.txt", "first\nneedle last", "nofinal.txt:2:needle last")]
    [InlineData("final.txt", "needle first\nsecond\n", "final.txt:1:needle first")]
    [InlineData("cr.txt", "needle\rnext\r\r\nneedle\n", "cr.txt:1:needle\rnext\r\r\nneedle")]
    [InlineData("combining.txt", "a\ńneedle\nb", "combining.txt:2:́needle")]
    [InlineData("none.txt", "nothing here\r\n\r\n", "(no matches)")]
    [InlineData("empty.txt", "", "(no matches)")]
    public async Task A_fixed_search_reports_the_same_lines_as_the_whole_file_split(string name, string content, string expected)
    {
        using var folder = new ScratchFolder();
        File.WriteAllBytes(folder.Child(name), Utf8NoBom.GetBytes(content));
        var session = Session(folder.Root);

        Assert.Equal(expected, await Search(session, "needle", name));
    }

    [Theory]
    [InlineData("a\n\n\nb\n", "empty.txt:2:\nempty.txt:3:\nempty.txt:5:")]
    [InlineData("", "empty.txt:1:")]
    [InlineData("a\r\n\r\nb", "(no matches)")]
    public async Task Empty_lines_match_an_empty_line_regex_including_the_one_after_the_last_newline(string content, string expected)
    {
        using var folder = new ScratchFolder();
        File.WriteAllBytes(folder.Child("empty.txt"), Utf8NoBom.GetBytes(content));
        var session = Session(folder.Root);

        Assert.Equal(expected, await Search(session, "^$", "empty.txt", regex: true));
    }

    [Fact]
    public async Task A_byte_order_mark_is_kept_as_part_of_the_first_line()
    {
        using var folder = new ScratchFolder();
        File.WriteAllBytes(folder.Child("bom.txt"), [0xEF, 0xBB, 0xBF, .. Utf8NoBom.GetBytes("needle\nneedle")]);
        var session = Session(folder.Root);

        Assert.Equal("bom.txt:1:﻿needle\nbom.txt:2:needle", await Search(session, "needle", "bom.txt"));
    }

    [Fact]
    public async Task A_file_with_invalid_utf8_anywhere_is_skipped_whole()
    {
        using var folder = new ScratchFolder();
        var early = Utf8NoBom.GetBytes("needle early\n");
        var filler = Utf8NoBom.GetBytes(string.Concat(Enumerable.Repeat("filler line\n", 20_000)));
        File.WriteAllBytes(folder.Child("late.bin"), [.. early, .. filler, 0xFF, .. Utf8NoBom.GetBytes("\nneedle late")]);
        File.WriteAllBytes(folder.Child("cut.bin"), [.. early, .. filler, 0xF0, 0x9F, 0x98]);
        File.WriteAllBytes(folder.Child("good.txt"), Utf8NoBom.GetBytes("needle good"));
        var session = Session(folder.Root);

        Assert.Equal("(no matches)", await Search(session, "needle", "late.bin"));
        Assert.Equal("(no matches)", await Search(session, "needle", "cut.bin"));
        Assert.Equal("good.txt:1:needle good", await Search(session, "needle", ""));
    }

    [Fact]
    public async Task Lines_and_characters_that_straddle_read_buffers_match_the_whole_file_split()
    {
        using var folder = new ScratchFolder();
        var session = Session(folder.Root);
        // A CRLF pair, a lone LF, a four-byte emoji and a two-byte letter placed across every common buffer edge.
        foreach (var edge in new[] { 1_024, 4_096, 16_384, 65_536, 81_920 })
        {
            foreach (var shift in new[] { -3, -2, -1, 0, 1 })
            {
                foreach (var piece in new[] { "\r\n", "\n", "\U0001F600", "é", "\r", "\n\n" })
                {
                    var name = $"edge-{edge}-{shift + 3}-{(int)piece[0]}-{piece.Length}.txt";
                    var head = new string('x', edge + shift - 6) + "needle";
                    var content = head + piece + "needle tail\n" + new string('y', 100) + piece + "needle";
                    var bytes = Utf8NoBom.GetBytes(content);
                    File.WriteAllBytes(folder.Child(name), bytes);

                    Assert.Equal(Oracle(name, bytes, line => line.Contains("needle", StringComparison.Ordinal)), await Search(session, "needle", name));
                }
            }
        }
    }

    [Fact]
    public async Task Random_files_give_the_same_output_as_the_frozen_whole_file_search()
    {
        using var folder = new ScratchFolder();
        var session = Session(folder.Root);
        var random = new Random(4444);
        string[] alphabet = ["n", "e", "ne", "needle", "d", "l", " ", "\r", "\n", "\r\n", "\n\n", "é", "\U0001F600", "́", "﻿", "é", "x"];
        var searches = new (string Pattern, bool Regex, Func<string, bool> Matches)[]
        {
            ("ne", false, line => line.Contains("ne", StringComparison.Ordinal)),
            ("e$", true, line => Regex.IsMatch(line, "e$", RegexOptions.CultureInvariant)),
            ("^\\r?$", true, line => Regex.IsMatch(line, "^\\r?$", RegexOptions.CultureInvariant)),
        };
        for (var file = 0; file < 150; file++)
        {
            var length = file % 25 == 0 ? random.Next(60_000, 160_000) : random.Next(0, 3_000);
            var text = new StringBuilder();
            while (text.Length < length) text.Append(alphabet[random.Next(alphabet.Length)]);
            var bytes = Utf8NoBom.GetBytes(text.ToString());
            if (file % 10 == 7 && bytes.Length > 0) bytes[random.Next(bytes.Length)] = 0xFF;
            var name = $"random-{file}.txt";
            File.WriteAllBytes(folder.Child(name), bytes);
            foreach (var (pattern, regex, matches) in searches)
            {
                Assert.Equal(Oracle(name, bytes, matches), await Search(session, pattern, name, regex));
            }
        }
    }
}
