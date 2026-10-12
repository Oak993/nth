using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 12.1a; ED-NEW-dextracore-2): the shell splitter. Each grammar's separators, quotes and
// substitutions; nested code is listed before the part that holds it; anything it cannot read is a problem.
public sealed class CommandParserTests
{
    private static string[][] Words(string text, ShellKind shell) =>
        CommandParser.Parse(text, shell).Parts.Select(p => p.Words.Select(w => w.Text).ToArray()).ToArray();

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void Separators_split_parts_in_each_grammar()
    {
        Assert.Equal(new[] { new[] { "git", "status" }, new[] { "git", "push" } }, Words("git status && git push", ShellKind.Posix));
        Assert.Equal(new[] { new[] { "a" }, new[] { "b" }, new[] { "c" } }, Words("a | b; c", ShellKind.PowerShell));
        Assert.Equal(new[] { new[] { "a" }, new[] { "b" } }, Words("a & b", ShellKind.Cmd));
        Assert.Equal(new[] { new[] { "a" }, new[] { "b" } }, Words("a || b", ShellKind.Cmd));
        Assert.Equal(new[] { new[] { "a" }, new[] { "b" } }, Words("a\nb", ShellKind.Posix));
        // cmd has no ; separator.
        Assert.Single(Words("a; b", ShellKind.Cmd));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void Quotes_are_removed_and_keep_their_spaces()
    {
        Assert.Equal(new[] { new[] { "git", "commit", "-m", "fix the build" } }, Words("git commit -m \"fix the build\"", ShellKind.Posix));
        Assert.Equal(new[] { new[] { "echo", "it's" } }, Words("echo 'it''s'", ShellKind.PowerShell));
        Assert.Equal(new[] { new[] { "echo", "a b" } }, Words("echo \"a b\"", ShellKind.Cmd));
        var word = CommandParser.Parse("echo 'x'", ShellKind.Posix).Parts[0].Words[1];
        Assert.True(word.Quoted);
        Assert.False(word.Dynamic);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void Substitutions_are_parsed_first_and_mark_their_word_dynamic()
    {
        var parsed = CommandParser.Parse("echo $(git push)", ShellKind.Posix);
        Assert.Equal(2, parsed.Parts.Count);
        Assert.Equal(new[] { "git", "push" }, parsed.Parts[0].Words.Select(w => w.Text));
        Assert.True(parsed.Parts[0].Nested);
        Assert.True(parsed.Parts[1].Words[1].Dynamic);
        var backtick = CommandParser.Parse("echo `rm x`", ShellKind.Posix);
        Assert.Equal("rm", backtick.Parts[0].Words[0].Text);
        var powershell = CommandParser.Parse("Write-Output (Remove-Item x)", ShellKind.PowerShell);
        Assert.Contains(powershell.Parts, p => p.Words.Count > 0 && p.Words[0].Text == "Remove-Item");
        Assert.True(CommandParser.Parse("echo $HOME", ShellKind.Posix).Parts[0].Words[1].Dynamic);
        Assert.False(CommandParser.Parse("echo '$HOME'", ShellKind.Posix).Parts[0].Words[1].Dynamic);
        Assert.True(CommandParser.Parse("echo %USERPROFILE%", ShellKind.Cmd).Parts[0].Words[1].Dynamic);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void Redirects_are_kept_and_null_devices_dropped()
    {
        var part = CommandParser.Parse("echo hi > out.txt 2>&1", ShellKind.Posix).Parts[0];
        Assert.Equal("out.txt", Assert.Single(part.Redirects).Target);
        Assert.Empty(CommandParser.Parse("dir > nul", ShellKind.Cmd).Parts[0].Redirects);
        Assert.Empty(CommandParser.Parse("ls > $null", ShellKind.PowerShell).Parts[0].Redirects);
        Assert.True(CommandParser.Parse("sort < in.txt", ShellKind.Posix).Parts[0].Redirects[0].Input);
        Assert.True(CommandParser.Parse("echo x >> log.txt", ShellKind.Posix).Parts[0].Redirects[0].Append);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void A_here_doc_body_is_the_parts_input_and_its_substitutions_are_read()
    {
        var parsed = CommandParser.Parse("bash <<EOF\ngit push\nEOF", ShellKind.Posix);
        Assert.False(parsed.HasProblems);
        Assert.Equal("git push\n", parsed.Parts.Single(p => p.Words.Count > 0 && p.Words[0].Text == "bash").Stdin);
        var expanded = CommandParser.Parse("cat <<EOF\n$(rm -rf x)\nEOF", ShellKind.Posix);
        Assert.Contains(expanded.Parts, p => p.Words.Count > 0 && p.Words[0].Text == "rm");
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    [InlineData("echo \"open", ShellKind.Posix)]
    [InlineData("echo 'open", ShellKind.PowerShell)]
    [InlineData("echo $(open", ShellKind.Posix)]
    [InlineData("echo )", ShellKind.Posix)]
    [InlineData("echo >", ShellKind.Posix)]
    [InlineData("cat <<EOF", ShellKind.Posix)]
    public void What_it_cannot_read_is_a_problem(string text, ShellKind shell) =>
        Assert.True(CommandParser.Parse(text, shell).HasProblems, text);

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void Depth_and_length_are_bounded_and_Auto_is_not_a_grammar()
    {
        var deep = string.Concat(Enumerable.Repeat("$(", 20)) + "x" + string.Concat(Enumerable.Repeat(")", 20));
        Assert.True(CommandParser.Parse("echo " + deep, ShellKind.Posix).HasProblems);
        Assert.True(CommandParser.Parse(new string('a', CommandParser.MaxLength + 1), ShellKind.Posix).HasProblems);
        Assert.Throws<ArgumentException>(() => CommandParser.Parse("x", ShellKind.Auto));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void PowerShell_stop_parsing_keeps_the_rest_as_words()
    {
        var parts = CommandParser.Parse("icacls x --% /grant Everyone:F & del y", ShellKind.PowerShell).Parts;
        Assert.Single(parts);
        Assert.Contains("/grant", parts[0].Words.Select(w => w.Text));
    }
}
