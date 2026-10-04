using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: S3, a failed command, missing PROGRAM (ONDEMAND-PLAN.md 5.1 and 5.3): "Missing program (any locale): cmd exit code 9009; PowerShell
// CommandNotFoundException (an identifier, not translated); bash exit code 127 with 'command not found'; the Store alias (exit 9009 for python/python3).
// The program must also be a token of the command." Lookup: "the token goes through the catalogue's provides index to one item. No hit, no offer."
// The events are the provider shapes of DetectFixtures (hand-written to the plan's descriptions, see there).
public sealed class MissingProgramTests
{
    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_same_missing_soffice_is_found_in_every_provider_shape_in_English_and_German()
    {
        var shapes = DetectFixtures.MissingSoffice().ToList();
        Assert.True(shapes.Count >= 11);
        foreach (var (shape, ev) in shapes)
        {
            var detection = DetectFixtures.Run(DetectFixtures.Ctx(), ev);
            Assert.True(DetectFixtures.Offered(detection).SequenceEqual(["T3"]), $"{shape}: {string.Join(",", detection.Findings.Select(f => f.ItemId + ":" + f.Verdict))}");
            var finding = detection.Findings.Single();
            Assert.Equal(DetectedSignal.FailedCommand, finding.Signal);
            Assert.Equal("soffice", finding.Token);
            Assert.Equal(["T3"], detection.Card!.Requested);
        }
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Exit_code_9009_is_the_cmd_signal_in_any_language_and_needs_the_program_named_in_the_output()
    {
        Assert.Equal(["T4"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pdftoppm -png a.pdf out", 9009, "'pdftoppm' n'est pas reconnu en tant que commande interne", false))));
        Assert.Equal(["T4"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pdftoppm -png a.pdf out", 9009, "„pdftoppm“ nie rozpoznano", false))));
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pdftoppm -png a.pdf out", 9009, "", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pdftoppm -png a.pdf out", 9009, "something else failed", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pdftoppm -png a.pdf out", 1, "'pdftoppm' is not recognized as an internal or external command", false)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void CommandNotFoundException_is_the_PowerShell_signal_whatever_the_exit_code_and_the_language()
    {
        const string english = "The term 'qpdf' is not recognized as the name of a cmdlet.\n+ FullyQualifiedErrorId : CommandNotFoundException";
        const string german = "Die Benennung \"qpdf\" wurde nicht als Name eines Cmdlet erkannt.\n+ FullyQualifiedErrorId : CommandNotFoundException";
        foreach (var text in new[] { english, german })
        {
            foreach (var exit in new int?[] { null, 1, 0x10 })
                Assert.Equal(["T5"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("qpdf --check a.pdf", exit, text, exit is null))));
        }
        // The same words on a command that succeeded are no failure.
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("qpdf --check a.pdf", 0, english, false)).Findings);
        // Without the identifier the words alone are not enough when the exit code is not 9009 or 127.
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("qpdf --check a.pdf", 1, "The term 'qpdf' is not recognized", false)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Exit_127_needs_the_not_found_phrase_and_without_an_exit_code_the_error_flag_and_the_phrase_do()
    {
        Assert.Equal(["T6"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", 127, "bash: pandoc: command not found", false))));
        Assert.Equal(["T6"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", 127, "bash: pandoc: Befehl nicht gefunden", false))));
        Assert.Equal(["T6"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", null, "bash: pandoc: command not found"))));
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", 127, "pandoc: something happened", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", 1, "bash: pandoc: command not found", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", 0, "bash: pandoc: command not found", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("pandoc a.docx -o a.md", null, "bash: pandoc: command not found", false)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_Store_alias_for_python_and_python3_is_a_missing_Python_through_exit_9009()
    {
        const string english = "Python was not found; run without arguments to install from the Microsoft Store, or disable this shortcut from Settings > Manage App Execution Aliases.";
        const string german = "Python wurde nicht gefunden; Sie können Python ohne Argumente ausführen, um es aus dem Microsoft Store zu installieren.";
        foreach (var command in new[] { "python script.py", "python3 script.py", "python.exe script.py", "python -3 script.py" })
            foreach (var text in new[] { english, german })
            {
                var detection = DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed(command, 9009, text, false));
                Assert.Equal(["T1"], DetectFixtures.Offered(detection));
            }
        // The alias message names Python, not the command; a command that holds neither python nor python3 gets nothing from it.
        Assert.DoesNotContain(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("node script.js", 9009, english, false)).Findings, f => f.ItemId == "T1");
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("python script.py", 1, english, false)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_program_named_in_the_output_must_be_a_token_of_the_command_that_ran()
    {
        // A script that calls soffice inside: the command does not hold the token, so no offer (the plan's rule, deliberately conservative).
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("bash run.sh", 127, "run.sh: line 3: soffice: command not found", false)).Findings);
        // A wrapper that holds the program in its quoted command line does.
        Assert.Equal(["T3"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("cmd /c \"soffice --headless a.docx\"", 9009, "'soffice' is not recognized as an internal or external command", false))));
        Assert.Equal(["T3"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("bash -lc 'soffice --headless a.docx'", 127, "bash: line 1: soffice: command not found", false))));
        // The word in the output must stand alone: a longer name that merely contains it is another word.
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("soffice-wrapper a", 127, "bash: soffice-wrapper: command not found", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("soffice a", 127, "bash: mysoffice2: command not found", false)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Two_missing_programs_in_one_command_are_both_found_in_the_order_of_the_command_line()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("soffice a.docx; pdftoppm -png a.pdf out", 1,
            "soffice : The term 'soffice' is not recognized.\n+ FullyQualifiedErrorId : CommandNotFoundException\npdftoppm : The term 'pdftoppm' is not recognized.\n+ FullyQualifiedErrorId : CommandNotFoundException"));
        Assert.Equal(["T3", "T4"], DetectFixtures.Offered(detection));
        Assert.Equal(["T3", "T4"], detection.Card!.Requested);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unknown_program_gets_no_hit_and_a_catalogue_name_in_prose_around_it_is_not_enough()
    {
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("frobnicate --now", 9009, "'frobnicate' is not recognized as an internal or external command", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("rm -rf x", 127, "bash: rm: command not found", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("frobnicate", 127, "bash: frobnicate: command not found (try installing soffice)", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("", 9009, "'soffice' is not recognized", false)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_finished_command_that_succeeded_never_signals_whatever_its_output_says()
    {
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("grep -r \"soffice: command not found\" log.txt", 0, "build.log: soffice: command not found", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("grep -r \"soffice: command not found\" log.txt", 1, "", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("echo soffice 9009", 0, "soffice 9009", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("soffice a", null, "soffice: command not found", false)).Findings);
    }
}
