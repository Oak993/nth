using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: "no offer from prose alone". ONDEMAND-PLAN.md 5.1: "(ED) Natural-language guessing ('LibreOffice is not installed' in the model's prose) is not
// a signal: it is unreliable, and S3 already sees the failure that led to the sentence." The detector reads four kinds of event and nothing else; an event it
// does not know is ignored, and words in the output of a command that did not fail the way a missing program fails are only words.
public sealed class NoProseSignalTests
{
    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_event_the_detector_does_not_know_is_ignored_whatever_it_says()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(),
            new ProseEvent("LibreOffice is not installed, so I cannot convert the file. soffice: command not found. Please install pdftoppm."));
        Assert.Empty(detection.Findings);
        Assert.Null(detection.Card);
        Assert.Empty(detection.AttachTo);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Prose_between_real_events_does_not_change_what_the_real_events_give()
    {
        var plain = DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("pandoc a.docx -t plain"));
        var withProse = DetectFixtures.Run(DetectFixtures.Ctx(), new ProseEvent("I need soffice"), new CommandHeld("pandoc a.docx -t plain"), new ProseEvent("and qpdf too"));
        Assert.Equal(plain.Findings, withProse.Findings);
        Assert.Equal(plain.Card!.Items, withProse.Card!.Items);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_command_that_only_talks_about_a_missing_tool_is_not_a_missing_tool()
    {
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("echo \"soffice is not installed\"")).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("echo soffice: command not found", 0, "soffice: command not found", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("cat notes.txt", 1, "LibreOffice is not installed on this PC", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("soffice a", 1, "LibreOffice konnte die Datei nicht öffnen. Das Programm ist nicht installiert.", false)).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("soffice a", 1, "soffice is missing", true)).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_or_tool_request_with_empty_or_odd_text_gives_nothing_and_never_throws()
    {
        foreach (var text in new[] { "", " ", "\0", new string('x', 10_000), "soffice\nqpdf", "'\"" })
        {
            _ = DetectFixtures.Run(DetectFixtures.Ctx(), new SkillUsed(text));
            _ = DetectFixtures.Run(DetectFixtures.Ctx(), new ToolRequested(text, text));
            _ = DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld(text));
            _ = DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed(text, 9009, text, false));
            _ = DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed(text, null, text));
        }
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), new ToolRequested("", "x")).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_detector_has_no_seam_to_the_machine_it_decides_from_the_facts_it_is_given()
    {
        // Pure by construction (ONDEMAND-PLAN.md DL-10 "pure, Core"): no member takes a path, a process, a clock or a delegate to reach the outside.
        var types = new[] { typeof(MissingToolDetector), typeof(CommandTokenizer), typeof(DetectionContext) };
        foreach (var type in types)
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
                foreach (var parameter in method.GetParameters())
                    Assert.False(typeof(Delegate).IsAssignableFrom(parameter.ParameterType), $"{type.Name}.{method.Name}({parameter.Name})");
        var context = typeof(DetectionContext).GetProperties().Select(p => p.PropertyType).ToList();
        Assert.DoesNotContain(context, t => typeof(Delegate).IsAssignableFrom(t));
        Assert.DoesNotContain(typeof(MissingToolDetector).Assembly.GetReferencedAssemblies(), a => a.Name is "System.Diagnostics.Process" or "System.Net.Http");
    }
}
