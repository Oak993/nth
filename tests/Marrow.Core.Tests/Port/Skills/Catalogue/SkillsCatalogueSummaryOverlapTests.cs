using Marrow.Core.Port.Skills.Catalogue;
using Xunit.Abstractions;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: Marrow's summaries are its own words. ONDEMAND-PLAN.md section 7 DL-1: "Marrow summaries share no 12-word run with the vendor's own
// description (the G3 tool of W0-1)"; 2.3: "Marrow's own title and one-line summary (written by Marrow, never copied from a SKILL.md)".
// The W0-1 clean-room kit with its G3 tool is not on this branch, so the twelve-word check is the small pure function below (ENGINEERING
// DECISION ED-NEW-f-sk-1): both texts are case-folded and cut into words at every character that is not a letter or a digit, and the longest
// run of words the two texts have in common, in the same order, is measured. Twelve or more is a copy.
// The vendors' own descriptions are not in this repository (ONDEMAND-PLAN.md 1: no vendor text in Marrow's repository), so the check against them
// runs over whatever descriptions are supplied; SK-2's pin run reads them from the pinned SKILL.md files and feeds them to this same function.
public sealed class SkillsCatalogueSummaryOverlapTests(ITestOutputHelper output)
{
    private const int CopyRun = 12;

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var start = -1;
        var folded = text.ToLowerInvariant();
        for (var i = 0; i <= folded.Length; i++)
        {
            var inWord = i < folded.Length && char.IsLetterOrDigit(folded[i]);
            if (inWord && start < 0) start = i;
            if (!inWord && start >= 0)
            {
                words.Add(folded[start..i]);
                start = -1;
            }
        }
        return words;
    }

    private static int LongestSharedWordRun(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        var best = 0;
        var previous = new int[y.Count + 1];
        for (var i = 1; i <= x.Count; i++)
        {
            var current = new int[y.Count + 1];
            for (var j = 1; j <= y.Count; j++)
            {
                if (x[i - 1] != y[j - 1]) continue;
                current[j] = previous[j - 1] + 1;
                if (current[j] > best) best = current[j];
            }
            previous = current;
        }
        return best;
    }

    private static bool SharesCopyRun(string a, string b) => LongestSharedWordRun(a, b) >= CopyRun;

    private static int BruteForce(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        var best = 0;
        for (var i = 0; i < x.Count; i++)
            for (var j = 0; j < y.Count; j++)
            {
                var n = 0;
                while (i + n < x.Count && j + n < y.Count && x[i + n] == y[j + n]) n++;
                if (n > best) best = n;
            }
        return best;
    }

    private const string Twelve = "one two three four five six seven eight nine ten eleven twelve";

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_function_cuts_words_at_anything_that_is_not_a_letter_or_a_digit_and_folds_case()
    {
        Assert.Equal(["a", "b", "c1", "d", "e", "f"], Words("A,B  c1;D\te\r\nF"));
        Assert.Equal(["don", "t", "stop"], Words("Don't STOP"));
        Assert.Equal(["a", "b"], Words("a--b"));
        Assert.Empty(Words(""));
        Assert.Empty(Words("  ,.;: "));
        Assert.Equal(["ünï", "çode", "日本語"], Words("ÜNÏ çode, 日本語!"));
        Assert.Equal(["x", "2"], Words("x_2"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Twelve_words_in_a_row_are_a_copy_and_eleven_are_not()
    {
        Assert.True(SharesCopyRun(Twelve, Twelve));
        Assert.False(SharesCopyRun("one two three four five six seven eight nine ten eleven", Twelve));
        Assert.Equal(11, LongestSharedWordRun("one two three four five six seven eight nine ten eleven", Twelve));
        Assert.True(SharesCopyRun("Intro words. " + Twelve + " Then more.", "Other start " + Twelve.ToUpperInvariant() + "!"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Case_punctuation_spacing_and_line_breaks_do_not_hide_a_copy()
    {
        Assert.True(SharesCopyRun(Twelve, "ONE, two; three... four-five (six)\nseven\teight  nine ten eleven twelve"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void One_changed_or_inserted_word_in_the_middle_breaks_the_run_so_each_half_is_measured_alone()
    {
        Assert.Equal(6, LongestSharedWordRun(Twelve, "one two three four five six SEVENTH eight nine ten eleven twelve"));
        Assert.False(SharesCopyRun(Twelve, "one two three four five six extra seven eight nine ten eleven twelve"));
        Assert.False(SharesCopyRun(Twelve, "twelve eleven ten nine eight seven six five four three two one"));
        Assert.Equal(1, LongestSharedWordRun(Twelve, "twelve eleven ten nine eight seven six five four three two one"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_run_may_sit_anywhere_in_either_text_and_repeats_do_not_confuse_it()
    {
        var a = "alpha beta " + Twelve + " gamma";
        var b = "delta " + Twelve + " epsilon zeta eta";
        Assert.Equal(12, LongestSharedWordRun(a, b));
        Assert.Equal(3, LongestSharedWordRun("a a a b", "a a a a a a"));
        Assert.Equal(0, LongestSharedWordRun("", Twelve));
        Assert.Equal(0, LongestSharedWordRun(Twelve, ""));
        Assert.Equal(0, LongestSharedWordRun("???", "!!!"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_function_agrees_with_a_brute_force_count_on_seeded_random_texts()
    {
        var random = new Random(20261004);
        var vocabulary = new[] { "a", "b", "c", "d", "e" };
        for (var round = 0; round < 300; round++)
        {
            string Text() => string.Join(" ", Enumerable.Range(0, random.Next(0, 30)).Select(_ => vocabulary[random.Next(vocabulary.Length)]));
            var x = Text();
            var y = Text();
            Assert.Equal(BruteForce(x, y), LongestSharedWordRun(x, y));
        }
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void No_summary_shares_a_twelve_word_run_with_any_other_summary_or_with_a_title_or_a_licence_note()
    {
        foreach (var item in CatalogueFixtures.Real.Items)
        {
            Assert.True(Words(item.Summary).Count >= 3, item.Id);
            foreach (var other in CatalogueFixtures.Real.Items.Where(o => o.Id != item.Id))
                Assert.False(SharesCopyRun(item.Summary, other.Summary), $"{item.Id} and {other.Id} share a twelve-word run");
            Assert.False(SharesCopyRun(item.Summary, item.Title), item.Id);
            if (item.LicenceNotes is not null) Assert.False(SharesCopyRun(item.Summary, item.LicenceNotes), item.Id);
        }
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Summaries_are_checked_against_every_vendor_description_supplied_and_a_copied_one_is_caught()
    {
        // Offline there are no vendor descriptions to supply (ONDEMAND-PLAN.md 1: none is stored in the repository), so the real run compares nothing.
        IReadOnlyDictionary<string, string> supplied = new Dictionary<string, string>();
        var compared = 0;
        foreach (var item in CatalogueFixtures.Real.Items.Where(i => supplied.ContainsKey(i.Id)))
        {
            Assert.False(SharesCopyRun(item.Summary, supplied[item.Id]), item.Id);
            compared++;
        }
        output.WriteLine($"{compared} of {CatalogueFixtures.Real.Items.Count} summaries were compared with a vendor description; SK-2's pin run supplies the rest.");
        // The same comparison over a copied summary fails, so the check is not vacuous when descriptions are supplied.
        var sample = "A fixture summary with more than twelve words so that a pasted copy of it is long enough to be caught here";
        Assert.True(SharesCopyRun(sample, "Vendor text begins. " + sample + " And ends."));
        Assert.False(SharesCopyRun(sample, "A different sentence about nothing in common at all"));
    }
}
