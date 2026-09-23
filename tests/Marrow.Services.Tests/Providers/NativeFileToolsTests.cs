using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/NativeFileTools.swift (B2 1fbf81e), :1-161.
public sealed class NativeFileToolsTests
{
    // ---- PR-PROVAPI-176: resolveURL confinement ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-176")] // NativeFileTools.swift:19-31
    public void ResolveURL_resolves_a_relative_path_inside_the_project()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var resolved = tools.ResolveURL("sub\\file.txt");
        Assert.Equal(folder.Child("sub", "file.txt"), resolved, ignoreCase: true);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-176")] // NativeFileTools.swift:21
    public void ResolveURL_rejects_an_empty_path_with_the_exact_message()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var error = Assert.Throws<ProviderError>(() => tools.ResolveURL("   "));
        Assert.Equal("A file path is required.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-176")] // NativeFileTools.swift:27-28
    public void ResolveURL_rejects_a_path_that_climbs_out_of_the_project_with_the_exact_message()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var error = Assert.Throws<ProviderError>(() => tools.ResolveURL("..\\outside.txt"));
        Assert.Equal($"That path is outside the project. Stay inside {folder.Root}.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-176")] // NativeFileTools.swift:23-30
    public void ResolveURL_accepts_an_absolute_path_that_lands_inside_the_project()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var absolute = folder.Child("nested", "file.txt");
        var resolved = tools.ResolveURL(absolute);
        Assert.Equal(absolute, resolved, ignoreCase: true);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-176")] // NativeFileTools.swift:27-28
    public void ResolveURL_rejects_an_absolute_path_outside_the_project()
    {
        using var outsideRoot = new ScratchFolder();
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var outside = outsideRoot.Child("secret.txt");
        var error = Assert.Throws<ProviderError>(() => tools.ResolveURL(outside));
        Assert.Equal($"That path is outside the project. Stay inside {folder.Root}.", error.ErrorDescription);
    }

    // ---- PR-PROVAPI-177: displayPath ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-177")] // NativeFileTools.swift:34-35
    public void DisplayPath_of_an_empty_path_is_the_working_directory()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        Assert.Equal(folder.Root, tools.DisplayPath(""));
        Assert.Equal(folder.Root, tools.DisplayPath("   "));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-177")] // NativeFileTools.swift:36
    public void DisplayPath_under_the_project_is_shown_relative_to_it()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var full = folder.Child("src", "app.ts");
        Assert.Equal("src\\app.ts", tools.DisplayPath(full));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-177")] // NativeFileTools.swift:37
    public void DisplayPath_outside_the_project_is_shown_in_full()
    {
        using var outsideRoot = new ScratchFolder();
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var outside = outsideRoot.Child("other.txt");
        Assert.Equal(outside, tools.DisplayPath(outside));
    }

    // ---- PR-PROVAPI-178: read_file missing / too large ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-178")] // NativeFileTools.swift:43-44
    public async Task ReadFile_of_a_missing_file_fails_with_the_exact_message()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.ReadFile("src\\app.ts"));
        Assert.Equal("No such file: src\\app.ts.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-178")] // NativeFileTools.swift:56-58
    public async Task ReadFile_over_the_byte_limit_fails_with_the_real_byte_count()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("big.txt");
        var bytes = new byte[1_000_001];
        Array.Fill(bytes, (byte)'a');
        await File.WriteAllBytesAsync(path, bytes);
        var tools = new NativeFileTools(folder.Root);
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.ReadFile("big.txt"));
        Assert.Equal("That file is too large to read (1000001 bytes).", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-178")] // NativeFileTools.swift:49-54
    public async Task ReadFile_at_exactly_the_byte_limit_succeeds()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("exact.txt");
        var bytes = new byte[1_000_000];
        Array.Fill(bytes, (byte)'a');
        await File.WriteAllBytesAsync(path, bytes);
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("exact.txt");
        // Over the 60000-character truncation threshold, so the truncated form is expected here.
        Assert.StartsWith(new string('a', 60_000), text);
        Assert.EndsWith($"…(truncated, {1_000_000} chars total; read the rest with start_line and line_count)", text);
    }

    // ---- PR-PROVAPI-179: UTF-8 then Latin-1 decode ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-179")] // NativeFileTools.swift:60
    public async Task ReadFile_falls_back_to_Latin1_for_bytes_that_are_not_valid_UTF8()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("legacy.txt");
        // 0xFF is not a valid UTF-8 lead byte, but is a valid ISO Latin 1 scalar (U+00FF).
        await File.WriteAllBytesAsync(path, [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0xFF]);
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("legacy.txt");
        Assert.Equal("Helloÿ", text);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-179")] // NativeFileTools.swift:60-62
    public async Task ReadFile_decodes_valid_UTF8_directly()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("utf8.txt");
        await File.WriteAllTextAsync(path, "café", System.Text.Encoding.UTF8);
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("utf8.txt");
        Assert.Equal("café", text);
    }

    // ---- PR-PROVAPI-180: start_line / line_count slicing ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-180")] // NativeFileTools.swift:63-70
    public async Task ReadFile_with_a_slice_returns_a_numbered_header_and_the_requested_lines()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("lines.txt");
        var lines = Enumerable.Range(1, 900).Select(i => $"line{i}");
        await File.WriteAllTextAsync(path, string.Join("\n", lines));
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("lines.txt", startLine: 41, lineCount: 200);
        Assert.StartsWith("(lines 41-240 of 900)\n", text);
        Assert.Contains("line41", text);
        Assert.Contains("line240", text);
        Assert.DoesNotContain("line241", text);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-180")] // NativeFileTools.swift:66
    public async Task ReadFile_with_a_start_line_past_the_end_reports_the_exact_line_count()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("lines.txt");
        await File.WriteAllTextAsync(path, string.Join("\n", Enumerable.Range(1, 900).Select(i => $"line{i}")));
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("lines.txt", startLine: 1200);
        Assert.Equal("(the file has 900 lines; start_line 1200 is past the end)", text);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-180")] // NativeFileTools.swift:65,67
    public async Task ReadFile_clamps_a_start_line_below_one_and_a_line_count_below_one()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("lines.txt");
        await File.WriteAllTextAsync(path, string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}")));
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("lines.txt", startLine: -5, lineCount: -1);
        Assert.Equal("(lines 1-1 of 10)\nline1", text);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-180")] // NativeFileTools.swift:67
    public async Task ReadFile_with_start_line_but_no_line_count_defaults_to_200()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("lines.txt");
        await File.WriteAllTextAsync(path, string.Join("\n", Enumerable.Range(1, 300).Select(i => $"line{i}")));
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("lines.txt", startLine: 1);
        Assert.StartsWith("(lines 1-200 of 300)\n", text);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-180")] // NativeFileTools.swift:64
    public async Task ReadFile_slicing_keeps_carriage_returns_from_a_CRLF_file()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("crlf.txt");
        await File.WriteAllTextAsync(path, "one\r\ntwo\r\nthree");
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("crlf.txt", startLine: 1, lineCount: 1);
        Assert.Equal("(lines 1-1 of 3)\none\r", text);
    }

    // ---- PR-PROVAPI-181: 60000-character truncation ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-181")] // NativeFileTools.swift:72-76
    public async Task ReadFile_of_a_whole_file_over_60000_characters_is_truncated()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("long.txt");
        var content = new string('x', 91_234);
        await File.WriteAllTextAsync(path, content);
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("long.txt");
        Assert.Equal(new string('x', 60_000) + $"\n…(truncated, 91234 chars total; read the rest with start_line and line_count)", text);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-181")] // NativeFileTools.swift:73
    public async Task ReadFile_at_exactly_60000_characters_is_returned_whole()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("edge.txt");
        var content = new string('y', 60_000);
        await File.WriteAllTextAsync(path, content);
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ReadFile("edge.txt");
        Assert.Equal(content, text);
    }

    // ---- PR-PROVAPI-182: writes and edits serialized and atomic ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-182")] // NativeFileTools.swift:86-88
    public async Task WriteFile_creates_intermediate_directories_and_writes_the_content()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        await tools.WriteFile("nested\\deep\\note.txt", "hello world");
        var written = await File.ReadAllTextAsync(folder.Child("nested", "deep", "note.txt"));
        Assert.Equal("hello world", written);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-182")] // NativeFileTools.swift:84
    public async Task WriteFile_rejects_content_that_cannot_be_UTF8_encoded()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        // A lone UTF-16 high surrogate is not valid Unicode text.
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.WriteFile("bad.txt", "\uD800"));
        Assert.Equal("Could not encode that content.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-182")] // NativeFileTools.swift:9-13, :79-90
    public async Task WriteFile_calls_do_not_corrupt_each_other_when_run_concurrently()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var writes = Enumerable.Range(0, 20)
            .Select(i => tools.WriteFile($"file{i}.txt", new string((char)('a' + i % 26), 5_000)));
        await Task.WhenAll(writes);
        for (var i = 0; i < 20; i++)
        {
            var text = await File.ReadAllTextAsync(folder.Child($"file{i}.txt"));
            Assert.Equal(new string((char)('a' + i % 26), 5_000), text);
        }
    }

    // ---- PR-PROVAPI-183: edit_file occurrence counting ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-183")] // NativeFileTools.swift:96
    public async Task EditFile_rejects_an_empty_old_string()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("f.txt");
        await File.WriteAllTextAsync(path, "content");
        var tools = new NativeFileTools(folder.Root);
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.EditFile("f.txt", "", "x"));
        Assert.Equal("old_string must not be empty. Read the file first, then edit an exact block.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-183")] // NativeFileTools.swift:113
    public async Task EditFile_reports_the_exact_message_when_the_text_is_not_found()
    {
        using var folder = new ScratchFolder();
        await File.WriteAllTextAsync(folder.Child("f.txt"), "one two three");
        var tools = new NativeFileTools(folder.Root);
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.EditFile("f.txt", "missing", "x"));
        Assert.Equal("That exact text was not found in f.txt. Read the file and copy it exactly.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-183")] // NativeFileTools.swift:114
    public async Task EditFile_reports_the_exact_count_when_the_text_appears_more_than_once()
    {
        using var folder = new ScratchFolder();
        await File.WriteAllTextAsync(folder.Child("f.txt"), "aaaa"); // "aa" occurs twice, non-overlapping
        var tools = new NativeFileTools(folder.Root);
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.EditFile("f.txt", "aa", "b"));
        Assert.Equal("That text appears 2 times in f.txt. Include more context so it matches once.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-183")] // NativeFileTools.swift:116-118
    public async Task EditFile_replaces_the_single_match_and_writes_it_atomically()
    {
        using var folder = new ScratchFolder();
        var path = folder.Child("f.txt");
        await File.WriteAllTextAsync(path, "before MARK after");
        var tools = new NativeFileTools(folder.Root);
        await tools.EditFile("f.txt", "MARK", "REPLACED");
        Assert.Equal("before REPLACED after", await File.ReadAllTextAsync(path));
    }

    // ---- PR-PROVAPI-184: list_files, not a directory / empty ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-184")] // NativeFileTools.swift:127-130
    public async Task ListFiles_of_a_missing_directory_fails_with_the_exact_message()
    {
        using var folder = new ScratchFolder();
        var tools = new NativeFileTools(folder.Root);
        var error = await Assert.ThrowsAsync<ProviderError>(() => tools.ListFiles("src\\nope", recursive: false));
        Assert.Equal("No such directory: src\\nope.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-184")] // NativeFileTools.swift:158
    public async Task ListFiles_of_an_empty_directory_returns_the_exact_marker()
    {
        using var folder = new ScratchFolder();
        Directory.CreateDirectory(folder.Child("empty"));
        var tools = new NativeFileTools(folder.Root);
        Assert.Equal("(empty)", await tools.ListFiles("empty", recursive: false));
        Assert.Equal("(empty)", await tools.ListFiles("empty", recursive: true));
    }

    // ---- PR-PROVAPI-185: recursive listing ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-185")] // NativeFileTools.swift:133-143
    public async Task ListFiles_recursive_skips_named_and_hidden_entries_and_lists_only_files()
    {
        using var folder = new ScratchFolder();
        Directory.CreateDirectory(folder.Child("node_modules"));
        await File.WriteAllTextAsync(folder.Child("node_modules", "pkg.js"), "x");
        Directory.CreateDirectory(folder.Child("src"));
        await File.WriteAllTextAsync(folder.Child("src", "app.ts"), "x");
        await File.WriteAllTextAsync(folder.Child(".hidden"), "x");
        var tools = new NativeFileTools(folder.Root);
        var text = await tools.ListFiles("", recursive: true);
        var results = text.Split('\n');
        Assert.Contains("src\\app.ts", results);
        Assert.DoesNotContain(results, r => r.Contains("node_modules"));
        Assert.DoesNotContain(results, r => r.Contains(".hidden"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-185")] // NativeFileTools.swift:142-143
    public async Task ListFiles_recursive_stops_after_300_entries_with_the_exact_marker()
    {
        using var folder = new ScratchFolder();
        for (var i = 0; i < 305; i++) await File.WriteAllTextAsync(folder.Child($"f{i:D3}.txt"), "x");
        var tools = new NativeFileTools(folder.Root);
        var results = (await tools.ListFiles("", recursive: true)).Split('\n');
        Assert.Equal(301, results.Length);
        Assert.Equal("…(truncated)", results[^1]);
    }

    // ---- PR-PROVAPI-186: non-recursive listing ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-186")] // NativeFileTools.swift:147,153
    public async Task ListFiles_non_recursive_sorts_and_marks_directories_with_a_trailing_slash()
    {
        using var folder = new ScratchFolder();
        Directory.CreateDirectory(folder.Child("zdir"));
        Directory.CreateDirectory(folder.Child("adir"));
        await File.WriteAllTextAsync(folder.Child("bfile.txt"), "x");
        var tools = new NativeFileTools(folder.Root);
        var results = (await tools.ListFiles("", recursive: false)).Split('\n');
        Assert.Equal(["adir/", "bfile.txt", "zdir/"], results);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-186")] // NativeFileTools.swift:149
    public async Task ListFiles_non_recursive_skips_named_directories_and_DS_Store_but_keeps_other_dot_files()
    {
        using var folder = new ScratchFolder();
        Directory.CreateDirectory(folder.Child("node_modules"));
        await File.WriteAllTextAsync(folder.Child(".DS_Store"), "x");
        await File.WriteAllTextAsync(folder.Child(".gitignore"), "x");
        var tools = new NativeFileTools(folder.Root);
        var results = (await tools.ListFiles("", recursive: false)).Split('\n');
        Assert.DoesNotContain("node_modules/", results);
        Assert.DoesNotContain(".DS_Store", results);
        Assert.Contains(".gitignore", results);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-186")] // NativeFileTools.swift:154-155
    public async Task ListFiles_non_recursive_stops_after_300_entries_with_the_exact_marker()
    {
        using var folder = new ScratchFolder();
        for (var i = 0; i < 305; i++) await File.WriteAllTextAsync(folder.Child($"f{i:D3}.txt"), "x");
        var tools = new NativeFileTools(folder.Root);
        var results = (await tools.ListFiles("", recursive: false)).Split('\n');
        Assert.Equal(301, results.Length);
        Assert.Equal("…(truncated)", results[^1]);
    }
}
