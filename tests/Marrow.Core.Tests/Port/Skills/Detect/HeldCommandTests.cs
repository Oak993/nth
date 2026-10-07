using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: S2, a held command (ONDEMAND-PLAN.md 5.1): "The first program of any pipeline segment is a catalogue provides name, not installed and not
// found", from Claude can_use_tool, Codex requestApproval, ACP request_permission and the native providers' approval for run_command. And 5.3 "py -3 -m
// <module> (the module counts)". The command has not run, so only what it names can be checked.
[Trait("Spec", "DL-10")]
public sealed class HeldCommandTests
{
    private static string[] Offered(string command, DetectionContext? context = null) =>
        DetectFixtures.Offered(DetectFixtures.Run(context ?? DetectFixtures.Ctx(), new CommandHeld(command)));

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("soffice --headless --convert-to pdf a.docx", "T3")]
    [InlineData("\"C:\\Program Files\\LibreOffice\\program\\soffice.exe\" --headless a.docx", "T3")]
    [InlineData("& 'C:\\Program Files\\LibreOffice\\program\\soffice.exe' --headless a.docx", "T3")]
    [InlineData("'/c/Program Files/LibreOffice/program/soffice' a.docx", "T3")]
    [InlineData("cd C:\\work && soffice a.docx", "T3")]
    [InlineData("pdftoppm -png a.pdf out", "T4")]
    [InlineData("pdftotext a.pdf -", "T4")]
    [InlineData("qpdf --check a.pdf", "T5")]
    [InlineData("pandoc a.docx -t plain", "T6")]
    [InlineData("bash script.sh", "T7")]
    [InlineData("sh -c 'echo hi'", "T7")]
    [InlineData("gh pr list", "T8")]
    [InlineData("uv run x.py", "T9")]
    [InlineData("node make.js", "T2")]
    [InlineData("npm install docx", "T2")]
    [InlineData("npx tsx x.ts", "T2")]
    [InlineData("python script.py", "T1")]
    [InlineData("python3 script.py", "T1")]
    [InlineData("py -3 script.py", "T1")]
    [InlineData("pip install x", "T1p")]
    public void The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(string command, string item) =>
        Assert.Equal([item], Offered(command));

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_segment_of_a_pipeline_counts_and_the_card_lists_them_in_command_order()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("soffice a.docx && pdftoppm -png a.pdf out | qpdf --check b.pdf"));
        Assert.Equal(["T3", "T4", "T5"], DetectFixtures.Offered(detection));
        Assert.All(detection.Findings, f => Assert.Equal(DetectedSignal.HeldCommand, f.Signal));
        Assert.Equal(["soffice", "pdftoppm", "qpdf"], detection.Findings.Select(f => f.Token));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_program_that_is_only_an_argument_or_sits_after_the_first_word_does_not_count()
    {
        Assert.Empty(Offered("echo soffice"));
        Assert.Empty(Offered("ls pandoc.exe"));
        Assert.Empty(Offered("git add soffice"));
        Assert.Empty(Offered("type C:\\x\\qpdf.txt"));
        Assert.Equal(["T6"], Offered("echo soffice | pandoc -t plain"));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unknown_program_or_an_empty_command_gets_no_hit()
    {
        Assert.Empty(Offered("frobnicate --now"));
        Assert.Empty(Offered("git status"));
        Assert.Empty(Offered("dir"));
        Assert.Empty(Offered(""));
        Assert.Empty(Offered("   "));
        Assert.Empty(Offered("cd C:\\work"));
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("frobnicate")).Findings);
        Assert.Null(DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("frobnicate")).Card);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_python_launcher_with_dash_m_counts_the_module_too_a_python_module_first_then_a_program()
    {
        Assert.Equal(["P4"], Offered("python -m playwright install chromium", DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1")))));
        Assert.Equal(["T1", "P4"], Offered("python -m playwright install chromium"));
        Assert.Equal(["T1", "T1p"], Offered("py -3 -m pip install x"));
        Assert.Equal(["T1p"], Offered("py -3 -m pip install x", DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1")))));
        Assert.Equal(["P1"], Offered("python -m docx", DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1")))));
        Assert.Empty(Offered("python -m venv .venv", DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1")))));
        Assert.Empty(Offered("python script.py -m playwright", DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1")))));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_same_item_named_twice_is_one_finding()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("soffice a && soffice b"), new CommandHeld("soffice c"));
        Assert.Single(detection.Findings);
        Assert.Equal(["T3"], detection.Card!.Requested);
    }
}
