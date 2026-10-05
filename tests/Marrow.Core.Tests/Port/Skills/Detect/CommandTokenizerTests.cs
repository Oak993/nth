using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10 (DL-10 of porting/first-release/skills-ONDEMAND-PLAN.md section 7): "the tokenizer on cmd, PowerShell and bash quoting, pipelines, &&,
// py -3 -m, C:\...\soffice.exe". ONDEMAND-PLAN.md 5.3 "Command parsing": "split the command line into pipeline and &&/; segments with a quote-aware
// tokenizer (cmd, PowerShell and bash quoting); take each segment's first word; strip a path and an extension (C:\x\soffice.exe becomes soffice); skip
// leading cd, &, call, start, env assignments and py -3 -m <module> (the module counts)". Where the plan is silent the choice is in ED-296.
[Trait("Spec", "DL-10")]
public sealed class CommandTokenizerTests
{
    private static string[][] Words(string line) => CommandTokenizer.Segments(line).Select(s => s.Select(w => w.Text).ToArray()).ToArray();

    private static (string Program, string? Module)[] Targets(string line) => CommandTokenizer.Targets(line).Select(t => (t.Program, t.Module)).ToArray();

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plain_command_is_one_segment_of_words_cut_at_blanks_and_tabs()
    {
        Assert.Equal([["soffice", "--headless", "a.docx"]], Words("soffice --headless a.docx"));
        Assert.Equal([["a", "b", "c"]], Words("  a \t b\t\tc  "));
        Assert.Empty(Words(""));
        Assert.Empty(Words("   \t "));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Pipelines_and_the_sequence_operators_of_cmd_PowerShell_and_bash_split_segments()
    {
        Assert.Equal([["a"], ["b"]], Words("a | b"));
        Assert.Equal([["a"], ["b"], ["c"], ["d"]], Words("a && b || c; d"));
        Assert.Equal([["a"], ["b"]], Words("a & b"));
        Assert.Equal([["a"], ["b"]], Words("a |& b"));
        Assert.Equal([["a", "x"], ["b"]], Words("a x\nb"));
        Assert.Equal([["a"], ["b"]], Words("a\r\nb"));
        Assert.Equal([["a"], ["b"]], Words("a;b"));
        Assert.Equal([["a"], ["b"]], Words("a&&b"));
        Assert.Equal([["a"], ["b"]], Words("a|b"));
        Assert.Equal([["a"], ["b"]], Words("a;; b"));
        Assert.Equal([["a"]], Words("a;"));
        Assert.Equal([["a"]], Words("; a &&"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Redirections_that_contain_an_ampersand_do_not_split()
    {
        Assert.Equal([["cmd", "2>&1"]], Words("cmd 2>&1"));
        Assert.Equal([["a", "2>&1"], ["b"]], Words("a 2>&1 | b"));
        Assert.Equal([["a", ">&2"]], Words("a >&2"));
        Assert.Equal([["a", "&>", "out"]], Words("a &> out"));
        Assert.Equal([["a", "<&3"]], Words("a <&3"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Quotes_keep_separators_and_blanks_inside_a_word_in_the_three_shells()
    {
        Assert.Equal([["echo", "a && b"], ["wc"]], Words("echo \"a && b\" | wc"));
        Assert.Equal([["echo", "a ; b | c"]], Words("echo 'a ; b | c'"));
        Assert.Equal([["C:\\Program Files\\LibreOffice\\program\\soffice.exe", "--headless"]], Words("\"C:\\Program Files\\LibreOffice\\program\\soffice.exe\" --headless"));
        Assert.Equal([["C:\\Program Files\\LibreOffice\\program\\soffice.exe", "--convert-to", "pdf", "x.docx"]],
            Words("& 'C:\\Program Files\\LibreOffice\\program\\soffice.exe' --convert-to pdf x.docx"));
        Assert.Equal([["/c/Program Files/LibreOffice/program/soffice", "x"]], Words("'/c/Program Files/LibreOffice/program/soffice' x"));
        Assert.Equal([["echo", "it's"]], Words("echo \"it's\""));
        Assert.Equal([["echo", "say \"hi\" now"]], Words("echo \"say \\\"hi\\\" now\""));
        Assert.Equal([["echo", "prefixpost"]], Words("echo pre\"fix\"post"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_backslash_is_a_plain_character_so_a_Windows_path_survives_in_every_shell()
    {
        Assert.Equal([["C:\\x\\soffice.exe", "a"]], Words("C:\\x\\soffice.exe a"));
        Assert.Equal([["..\\bin\\pdftoppm.exe"]], Words("..\\bin\\pdftoppm.exe"));
        Assert.Equal([["\\\\server\\share\\pandoc.exe", "-v"]], Words("\\\\server\\share\\pandoc.exe -v"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_empty_quoted_word_is_kept_and_an_unterminated_quote_runs_to_the_end_without_failing()
    {
        Assert.Equal([["start", "", "soffice"]], Words("start \"\" soffice"));
        Assert.Equal([["echo", "abc def"]], Words("echo \"abc def"));
        Assert.Equal([["echo", "abc"]], Words("echo 'abc"));
        var words = CommandTokenizer.Segments("echo \"x\" y")[0];
        Assert.False(words[0].Quoted);
        Assert.True(words[1].Quoted);
        Assert.False(words[2].Quoted);
    }

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("soffice a.docx", "soffice")]
    [InlineData("SOFFICE.EXE a.docx", "soffice")]
    [InlineData("C:\\x\\soffice.exe a", "soffice")]
    [InlineData("\"C:\\Program Files\\LibreOffice\\program\\soffice.exe\" --headless", "soffice")]
    [InlineData("& 'C:\\Program Files\\LibreOffice\\program\\soffice.exe' x", "soffice")]
    [InlineData("'/c/Program Files/LibreOffice/program/soffice' x", "soffice")]
    [InlineData("./soffice x", "soffice")]
    [InlineData("..\\bin\\pdftoppm.exe -png a b", "pdftoppm")]
    [InlineData("npm.cmd install x", "npm")]
    [InlineData("tool.bat", "tool")]
    [InlineData("qpdf.com --help", "qpdf")]
    [InlineData("python3 script.py", "python3")]
    public void The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(string line, string program) =>
        Assert.Equal([(program, (string?)null)], Targets(line));

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_executable_extensions_are_stripped_so_a_file_name_is_not_mistaken_for_a_program()
    {
        Assert.Equal("report.docx", CommandTokenizer.NormalizeProgram("Report.DOCX"));
        Assert.Equal("script.py", CommandTokenizer.NormalizeProgram("C:\\x\\script.py"));
        Assert.Equal("soffice", CommandTokenizer.NormalizeProgram("C:\\x\\Soffice.EXE"));
        Assert.Equal("soffice", CommandTokenizer.NormalizeProgram("/usr/lib/libreoffice/program/soffice"));
        Assert.Equal("exe", CommandTokenizer.NormalizeProgram("exe"));
        Assert.Equal("", CommandTokenizer.NormalizeProgram(""));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_pipeline_segment_has_its_own_first_program()
    {
        Assert.Equal([("soffice", null), ("pdftoppm", null)], Targets("soffice a.docx && pdftoppm -png a.pdf out"));
        Assert.Equal([("echo", null), ("pandoc", null)], Targets("echo hi | pandoc -t plain"));
        Assert.Equal([("qpdf", null), ("gh", null), ("uv", null)], Targets("qpdf a; gh pr list || uv run x"));
        Assert.Empty(Targets(""));
        Assert.Empty(Targets("   "));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Leading_cd_ampersand_call_start_and_environment_assignments_are_skipped()
    {
        Assert.Equal([("soffice", null)], Targets("cd C:\\work && soffice a"));
        Assert.Equal([("soffice", null)], Targets("cd /d D:\\work & soffice a"));
        Assert.Equal([("soffice", null)], Targets("cd work; soffice a"));
        Assert.Equal([("soffice", null)], Targets("& soffice a"));
        Assert.Equal([("soffice", null)], Targets("call soffice a"));
        Assert.Equal([("soffice", null)], Targets("CALL soffice a"));
        Assert.Equal([("soffice", null)], Targets("start soffice a"));
        Assert.Equal([("soffice", null)], Targets("start /wait soffice a"));
        Assert.Equal([("soffice", null)], Targets("start \"\" soffice a"));
        Assert.Equal([("soffice", null)], Targets("start \"Title\" /wait soffice a"));
        Assert.Equal([("soffice", null)], Targets("FOO=bar soffice a"));
        Assert.Equal([("soffice", null)], Targets("FOO=bar BAZ=\"a b\" soffice a"));
        Assert.Equal([("soffice", null)], Targets("env FOO=bar soffice a"));
        Assert.Equal([("soffice", null)], Targets("call start /wait soffice a"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_segment_with_nothing_to_run_has_no_target_and_a_start_with_only_a_title_has_none_either()
    {
        Assert.Empty(Targets("cd C:\\work"));
        Assert.Empty(Targets("FOO=bar"));
        Assert.Empty(Targets("call"));
        Assert.Empty(Targets("start"));
        Assert.Empty(Targets("start \"title\""));
        Assert.Empty(Targets("env"));
        Assert.Empty(Targets("\"\""));
        Assert.Equal([("soffice", null)], Targets("cd a && cd b && soffice"));
    }

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("py -3 -m pip install x", "py", "pip")]
    [InlineData("py -3.12 -m venv .venv", "py", "venv")]
    [InlineData("py -m playwright install chromium", "py", "playwright")]
    [InlineData("python -m playwright install chromium", "python", "playwright")]
    [InlineData("python3 -u -m pytest -q", "python3", "pytest")]
    [InlineData("C:\\Python314\\python.exe -m pip list", "python", "pip")]
    [InlineData("PY -3 -M pip", "py", null)]
    public void A_python_launcher_with_dash_m_names_the_module_as_well(string line, string program, string? module) =>
        Assert.Equal([(program, module)], Targets(line));

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("python script.py -m x")]
    [InlineData("python -m")]
    [InlineData("python script.py")]
    [InlineData("python -c \"print(1)\" -m pip")]
    [InlineData("node -m pip")]
    public void Dash_m_counts_only_for_a_python_launcher_before_its_first_script_word(string line) =>
        Assert.Null(Targets(line).Single().Module);

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_token_set_holds_every_word_of_the_command_and_the_words_of_a_quoted_inner_command()
    {
        var tokens = CommandTokenizer.Tokens("cmd /c \"soffice --convert-to pdf a.docx\"");
        Assert.Contains("cmd", tokens);
        Assert.Contains("soffice", tokens);
        Assert.Contains("a.docx", tokens);
        Assert.Contains("pdf", tokens);
        Assert.Contains("pandoc", CommandTokenizer.Tokens("bash -lc 'pandoc a.docx -o a.md'"));
        Assert.Contains("bash", CommandTokenizer.Tokens("bash -lc 'pandoc a.docx -o a.md'"));
        Assert.Contains("soffice", CommandTokenizer.Tokens("\"C:\\Program Files\\LibreOffice\\program\\Soffice.EXE\" x"));
        Assert.DoesNotContain("Soffice.EXE", CommandTokenizer.Tokens("C:\\x\\Soffice.EXE"));
        Assert.Empty(CommandTokenizer.Tokens(""));
        Assert.DoesNotContain("qpdf", CommandTokenizer.Tokens("echo hello"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_tokenizer_never_throws_on_odd_input()
    {
        foreach (var odd in new[] { "\0", "\"", "'", "\\", "&", "|", ";", "&&&&", "||||", "a\0b", "\"\"\"", "'\"", "^", "`", "$(", "%X%", new string('"', 99), new string('a', 5000) })
        {
            _ = CommandTokenizer.Segments(odd);
            _ = CommandTokenizer.Targets(odd);
            _ = CommandTokenizer.Tokens(odd);
        }
    }
}
