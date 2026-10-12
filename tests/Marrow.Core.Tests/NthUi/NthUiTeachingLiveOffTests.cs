using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

// PORT-ONLY: web surfaces W10, Settings > Tabula > "Live answers in chat" off (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; ED-NEW-web-w10-3). With the switch off nth draws
// no live view in the chat, so the teaching must not tell a model to write one: no view-showing guidance, no fenced nth-ui example, no html widget,
// and one plain sentence that live views are off. Artifacts are still taught, an nth-ui artifact still has its language, and the core stays under the limit.
[Trait("Decision", "ED-NEW-web-w10-3")]
public sealed class NthUiTeachingLiveOffTests
{
    private static IEnumerable<NthUiTeachingOptions> EveryOptionSet()
    {
        foreach (var profile in new[] { NthUiProfile.Full, NthUiProfile.Lite })
        foreach (var live in new[] { false, true })
        foreach (var artifacts in new[] { false, true })
        foreach (var html in new[] { false, true })
        foreach (var ask in new[] { false, true })
        foreach (var reference in new[] { false, true })
        foreach (var level in Enum.GetValues<VisualAnswerLevel>())
            yield return new NthUiTeachingOptions { Profile = profile, LiveTools = live, ArtifactTools = artifacts, HtmlWidget = html, AskUser = ask, Reference = reference, Visual = level, LiveAnswers = false };
    }

    [Fact]
    public void Live_answers_default_to_on_and_the_default_teaching_is_unchanged()
    {
        Assert.True(new NthUiTeachingOptions().LiveAnswers);
        var on = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { LiveTools = true });
        Assert.DoesNotContain(NthUiTeaching.LiveOff, on, StringComparison.Ordinal);
        Assert.Contains("SHOWING A VIEW", on, StringComparison.Ordinal);
        Assert.Contains("```nth-ui", on, StringComparison.Ordinal);
    }

    [Fact]
    public void The_off_teaching_says_so_and_teaches_no_view_for_every_combination()
    {
        var count = 0;
        foreach (var options in EveryOptionSet())
        {
            count++;
            foreach (var text in new[] { NthUiTeaching.SystemBlock(options), NthUiTeaching.McpInstructions(options), NthUiTeaching.Core(options) })
            {
                Assert.Contains(NthUiTeaching.LiveOff, text, StringComparison.Ordinal);
                Assert.DoesNotContain("SHOWING A VIEW", text, StringComparison.Ordinal);
                Assert.DoesNotContain("```nth-ui", text, StringComparison.Ordinal);
                Assert.DoesNotContain("```nth-html", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Show a view with ui_show", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Call ui_show with", text, StringComparison.Ordinal);
                Assert.DoesNotContain("html_widget only when", text, StringComparison.Ordinal);
                Assert.DoesNotContain("The reader likes them", text, StringComparison.Ordinal);
                Assert.DoesNotContain("asked for fewer visual answers", text, StringComparison.Ordinal);
            }
            var core = NthUiTeaching.Core(options);
            Assert.True(core.Length <= NthUiTeaching.McpCoreLimit, $"{core.Length} characters for {options}");
            Assert.All(core, c => Assert.True(c == '\n' || (c >= ' ' && c <= '~'), "not ASCII: " + (int)c));
            Assert.Contains("Text is the default", core, StringComparison.Ordinal);
            Assert.Equal(options.ArtifactTools, core.Contains("artifact_create", StringComparison.Ordinal));
            Assert.Equal(options.AskUser, core.Contains("ask_user", StringComparison.Ordinal));
            Assert.Equal(options.Reference, core.Contains("ui_reference", StringComparison.Ordinal));
        }
        Assert.Equal(2 * 2 * 2 * 2 * 2 * 2 * 3, count);
    }

    [Fact]
    public void Artifacts_are_still_taught_and_an_nth_ui_artifact_keeps_its_language()
    {
        var withTools = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { LiveTools = false, ArtifactTools = true, LiveAnswers = false });
        Assert.Contains("artifact_create", withTools, StringComparison.Ordinal);
        Assert.Contains("SAVING AN ARTIFACT", withTools, StringComparison.Ordinal);
        Assert.Contains("THE VIEW LANGUAGE", withTools, StringComparison.Ordinal);
        var fenced = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { ArtifactTools = false, LiveAnswers = false });
        Assert.Contains("nth-artifact", fenced, StringComparison.Ordinal);
    }
}
