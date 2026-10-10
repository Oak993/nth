using System.Text;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProjectGuidance.swift (B2 1fbf81e), :1-119.
public sealed class ProjectGuidanceTests
{
    // ---- PR-PROVREG-223: exactly AGENTS.md then CLAUDE.md at the root, never a parent, never a scan ---------

    [Fact]
    [Trait("Req", "PR-PROVREG-223")] // Services/Providers/ProjectGuidance.swift:38-47
    public void SystemPromptBlock_is_null_when_neither_file_is_present()
    {
        using var scratch = new ProjectGuidanceScratch();
        var guidance = new ProjectGuidance(scratch.FullPath);
        Assert.Null(guidance.SystemPromptBlock());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-223")] // Services/Providers/ProjectGuidance.swift:17-18, :80
    public void SystemPromptBlock_injects_AGENTS_md_before_CLAUDE_md()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("CLAUDE.md", "claude rules");
        scratch.Write("AGENTS.md", "agents rules");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.NotNull(block);
        var agentsIndex = block!.IndexOf("--- AGENTS.md ---", StringComparison.Ordinal);
        var claudeIndex = block.IndexOf("--- CLAUDE.md ---", StringComparison.Ordinal);
        Assert.True(agentsIndex >= 0 && claudeIndex >= 0 && agentsIndex < claudeIndex);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-223")] // Services/Providers/ProjectGuidance.swift:5-9, :65-75
    public void A_guidance_file_in_a_parent_directory_is_never_read()
    {
        using var scratch = new ProjectGuidanceScratch();
        var parentAgents = Path.Combine(Directory.GetParent(scratch.FullPath)!.FullName, "AGENTS.md");
        var wroteParent = false;
        try
        {
            if (!File.Exists(parentAgents))
            {
                File.WriteAllText(parentAgents, "parent rules");
                wroteParent = true;
            }

            var guidance = new ProjectGuidance(scratch.FullPath);
            Assert.Null(guidance.SystemPromptBlock());
        }
        finally
        {
            if (wroteParent) File.Delete(parentAgents);
        }
    }

    // ---- PR-PROVREG-224: per-file limit 16000, total budget 24000, budget consumed in injection order -------

    [Fact]
    [Trait("Req", "PR-PROVREG-224")] // Services/Providers/ProjectGuidance.swift:19-22, :89
    public void A_file_of_exactly_16000_bytes_is_included_whole()
    {
        using var scratch = new ProjectGuidanceScratch();
        var text = new string('a', 16_000);
        scratch.Write("AGENTS.md", text);
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Contains("--- AGENTS.md ---\n" + text, block, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-224")] // Services/Providers/ProjectGuidance.swift:19-22, :89
    public void A_file_of_16001_bytes_is_named_rather_than_included()
    {
        using var scratch = new ProjectGuidanceScratch();
        var text = new string('a', 16_001);
        scratch.Write("AGENTS.md", text);
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Contains("AGENTS.md is too large to include here (16001 bytes).", block, StringComparison.Ordinal);
        Assert.DoesNotContain("--- AGENTS.md ---", block, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-224")] // Services/Providers/ProjectGuidance.swift:79-91
    public void A_large_AGENTS_md_can_exhaust_the_budget_and_push_CLAUDE_md_to_the_named_path()
    {
        using var scratch = new ProjectGuidanceScratch();
        // AGENTS.md: 15000 bytes, under the 16000 per-file limit, consumes 15000 of the 24000 total budget.
        scratch.Write("AGENTS.md", new string('a', 15_000));
        // CLAUDE.md: 10000 bytes, under the per-file limit on its own, but over the 9000 bytes left in the budget.
        scratch.Write("CLAUDE.md", new string('c', 10_000));
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Contains("--- AGENTS.md ---", block, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md is too large to include here (10000 bytes).", block, StringComparison.Ordinal);
        Assert.DoesNotContain("--- CLAUDE.md ---", block, StringComparison.Ordinal);
    }

    // ---- PR-PROVREG-225: re-rendered only when a file's size or modification date changed ---------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-225")] // Services/Providers/ProjectGuidance.swift:40-47
    public void An_unchanged_file_between_calls_returns_the_same_cached_block()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("AGENTS.md", "agents rules");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var first = guidance.SystemPromptBlock();
        var second = guidance.SystemPromptBlock();

        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-225")] // Services/Providers/ProjectGuidance.swift:40-47, :49-60
    public void A_change_in_file_size_forces_a_re_render()
    {
        using var scratch = new ProjectGuidanceScratch();
        var path = scratch.Write("AGENTS.md", "short");
        var guidance = new ProjectGuidance(scratch.FullPath);
        var first = guidance.SystemPromptBlock();

        File.WriteAllText(path, "a much longer set of rules than before");
        var second = guidance.SystemPromptBlock();

        Assert.NotEqual(first, second);
        Assert.Contains("a much longer set of rules than before", second, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-225")] // Services/Providers/ProjectGuidance.swift:40-47
    public void An_edit_that_changes_neither_size_nor_modification_date_is_not_detected()
    {
        using var scratch = new ProjectGuidanceScratch();
        var path = scratch.Write("AGENTS.md", "12345");
        var guidance = new ProjectGuidance(scratch.FullPath);
        var first = guidance.SystemPromptBlock();
        var stampBefore = File.GetLastWriteTimeUtc(path);

        // Same byte count as "12345", so the size does not change; the modification time is pinned back to its
        // original value, so neither half of the cache key changes even though the content did.
        File.WriteAllText(path, "67890");
        File.SetLastWriteTimeUtc(path, stampBefore);

        var second = guidance.SystemPromptBlock();

        Assert.Equal(first, second);
        Assert.Contains("12345", second, StringComparison.Ordinal); // stale: the cached block from before the edit is reused
    }

    // ---- PR-PROVREG-226: symlink escaping the root, directories and non-regular files are rejected -----------

    [Fact]
    [Trait("Req", "PR-PROVREG-226")] // Services/Providers/ProjectGuidance.swift:62-75
    public void A_directory_named_like_a_guidance_file_is_ignored()
    {
        using var scratch = new ProjectGuidanceScratch();
        Directory.CreateDirectory(scratch.PathOf("AGENTS.md"));
        var guidance = new ProjectGuidance(scratch.FullPath);

        Assert.Null(guidance.SystemPromptBlock());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-226")] // Services/Providers/ProjectGuidance.swift:65-69
    public void A_symlink_whose_target_leaves_the_working_directory_contributes_nothing()
    {
        using var scratch = new ProjectGuidanceScratch();
        using var outside = new ProjectGuidanceScratch();
        var outsideFile = outside.Write("real-agents.md", "outside rules");

        var linkPath = scratch.PathOf("AGENTS.md");
        try
        {
            File.CreateSymbolicLink(linkPath, outsideFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ordinary Windows accounts can create a junction. Drive the exact production resolver
            // with a regular file beneath it, so the containment guard is reached before the file-type guard.
            var junction = scratch.PathOf("linked");
            ProjectGuidanceScratch.CreateJunction(junction, outside.FullPath);
            try
            {
                Assert.True(File.Exists(Path.Combine(junction, "real-agents.md")));
                Assert.True((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0);
                var resolver = typeof(ProjectGuidance).GetMethod("ResolvedFile", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                Assert.Null(resolver.Invoke(null, [Path.Combine("linked", "real-agents.md"), scratch.FullPath]));
            }
            finally { Directory.Delete(junction, recursive: false); }
            return; // The containment assertion above really ran; no silent privilege skip.
        }

        var guidance = new ProjectGuidance(scratch.FullPath);
        var block = guidance.SystemPromptBlock();

        Assert.Null(block);
    }

    // ---- PR-PROVREG-227: an oversized or out-of-budget file reports its real filesystem byte size -------------

    [Fact]
    [Trait("Req", "PR-PROVREG-227")] // Services/Providers/ProjectGuidance.swift:86-91
    public void The_reported_size_is_the_real_file_size_not_the_truncated_read_length()
    {
        using var scratch = new ProjectGuidanceScratch();
        // 50000 bytes: far larger than the 16001 bytes boundedData would ever read, so the size in the message
        // must come from the filesystem attribute, not from counting bytes actually read.
        scratch.Write("AGENTS.md", new string('a', 50_000));
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Equal(
            "Project guidelines from " + scratch.FullPath + ". They supplement the rules above; where they conflict, the nth rules take priority.\n" +
            "AGENTS.md is too large to include here (50000 bytes). Read AGENTS.md with the read_file tool; it may contain rules the project expects you to follow.",
            block);
    }

    // ---- PR-PROVREG-228: an unopenable file gets the shorter could-not-load sentence --------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-228")] // Services/Providers/ProjectGuidance.swift:82-84, :107-109
    public void A_file_held_open_exclusively_by_another_handle_could_not_load()
    {
        using var scratch = new ProjectGuidanceScratch();
        var path = scratch.Write("AGENTS.md", "agents rules");
        using var exclusiveHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var guidance = new ProjectGuidance(scratch.FullPath);
        var block = guidance.SystemPromptBlock();

        Assert.Equal(
            "Project guidelines from " + scratch.FullPath + ". They supplement the rules above; where they conflict, the nth rules take priority.\n" +
            "Could not load AGENTS.md. Read it with the read_file tool before changing the project.",
            block);
    }

    // ---- PR-PROVREG-229: dashed header per file, precedence sentence names the directory, sections joined -----

    [Fact]
    [Trait("Req", "PR-PROVREG-229")] // Services/Providers/ProjectGuidance.swift:96-103
    public void The_block_wraps_each_file_in_a_dashed_header_and_joins_with_a_blank_line()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("AGENTS.md", "agents text");
        scratch.Write("CLAUDE.md", "claude text");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        var expected =
            "Project guidelines from " + scratch.FullPath + ". They supplement the rules above; where they conflict, the nth rules take priority.\n" +
            "--- AGENTS.md ---\nagents text\n\n--- CLAUDE.md ---\nclaude text";
        Assert.Equal(expected, block);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-229")] // Services/Providers/ProjectGuidance.swift:100 // DEVIATION OD-0012: "Marrow" replaces "Droppy Code"
    public void The_precedence_sentence_names_Marrow_not_Droppy_Code()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("AGENTS.md", "agents text");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Contains("where they conflict, the nth rules take priority.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Droppy Code", block, StringComparison.Ordinal);
    }

    // ---- PR-PROVREG-230: chunked, bounded reading detects an oversized file without reading it whole ----------

    [Fact]
    [Trait("Req", "PR-PROVREG-230")] // Services/Providers/ProjectGuidance.swift:105-118
    public void Reading_stops_exactly_one_byte_past_the_per_file_limit_to_tell_over_from_at_the_limit()
    {
        using var scratch = new ProjectGuidanceScratch();
        // 16000 is included whole; 16001 is named instead. The pair proves the plus-one read boundary.
        scratch.Write("AGENTS.md", new string('a', 16_000));
        scratch.Write("CLAUDE.md", new string('c', 16_001));
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Contains("--- AGENTS.md ---", block, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md is too large to include here (16001 bytes).", block, StringComparison.Ordinal);
    }

    // ---- PR-PROVREG-231: a blank (after trim) or non-UTF-8 file is skipped silently, budget untouched ----------

    [Fact]
    [Trait("Req", "PR-PROVREG-231")] // Services/Providers/ProjectGuidance.swift:93-95
    public void A_whitespace_only_file_contributes_nothing_and_no_message()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("AGENTS.md", "   \n\t  \n  ");
        var guidance = new ProjectGuidance(scratch.FullPath);

        Assert.Null(guidance.SystemPromptBlock());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-231")] // Services/Providers/ProjectGuidance.swift:93-95
    public void An_invalid_UTF8_file_is_skipped_silently()
    {
        using var scratch = new ProjectGuidanceScratch();
        // Lone continuation byte: not valid UTF-8 on its own.
        scratch.Write("AGENTS.md", new byte[] { 0x80, 0x81, 0x82 });
        var guidance = new ProjectGuidance(scratch.FullPath);

        Assert.Null(guidance.SystemPromptBlock());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-231")] // Services/Providers/ProjectGuidance.swift:93-95
    public void A_skipped_blank_file_does_not_consume_the_budget_for_the_next_file()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("AGENTS.md", "   "); // blank after trim: skipped, budget untouched
        scratch.Write("CLAUDE.md", "real rules");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        Assert.Contains("--- CLAUDE.md ---\nreal rules", block, StringComparison.Ordinal);
        Assert.DoesNotContain("AGENTS.md", block, StringComparison.Ordinal);
    }

    // ---- PR-PROVREG-232: value-type cache lives with the instance; two instances never share it ---------------

    [Fact]
    [Trait("Req", "PR-PROVREG-232")] // Services/Providers/ProjectGuidance.swift:29-36, :40
    public void Two_instances_over_the_same_directory_each_keep_their_own_cache()
    {
        using var scratch = new ProjectGuidanceScratch();
        var path = scratch.Write("AGENTS.md", "first rules");
        var first = new ProjectGuidance(scratch.FullPath);
        var second = new ProjectGuidance(scratch.FullPath);

        var firstBlock1 = first.SystemPromptBlock();
        File.WriteAllText(path, "second rules, a different length");
        var secondBlock1 = second.SystemPromptBlock();

        Assert.Contains("first rules", firstBlock1, StringComparison.Ordinal);
        Assert.Contains("second rules, a different length", secondBlock1, StringComparison.Ordinal);
    }

    // ---- PR-PROVAPI-148: end-to-end assembly of the whole block ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-148")] // Services/Providers/ProjectGuidance.swift:77-103
    public void The_full_block_combines_the_header_and_both_files_in_order()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("AGENTS.md", "Follow the style guide.");
        scratch.Write("CLAUDE.md", "Run tests before committing.");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        var expected =
            "Project guidelines from " + scratch.FullPath + ". They supplement the rules above; where they conflict, the nth rules take priority.\n" +
            "--- AGENTS.md ---\nFollow the style guide.\n\n--- CLAUDE.md ---\nRun tests before committing.";
        Assert.Equal(expected, block);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-148")] // Services/Providers/ProjectGuidance.swift:77-78, :98
    public void Only_one_file_present_still_produces_a_block_naming_only_that_file()
    {
        using var scratch = new ProjectGuidanceScratch();
        scratch.Write("CLAUDE.md", "solo rules");
        var guidance = new ProjectGuidance(scratch.FullPath);

        var block = guidance.SystemPromptBlock();

        var expected =
            "Project guidelines from " + scratch.FullPath + ". They supplement the rules above; where they conflict, the nth rules take priority.\n" +
            "--- CLAUDE.md ---\nsolo rules";
        Assert.Equal(expected, block);
    }
}
