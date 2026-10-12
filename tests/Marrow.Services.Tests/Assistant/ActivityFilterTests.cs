using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Activity view's filter (DESIGN.md 12.5: "Filter by assistant, mission, class or date"; 3.3: a mission's
// lineage is "the Activity log filtered to this mission"). The data is ActivityDataset's six actions; the expected line
// numbers are written out here. A row passes when it passes every field set.
[Trait("Decision", "ED-NEW-dextra-as21-3")]
[Trait("Feature", "AS-21")]
public sealed class ActivityFilterTests : IDisposable
{
    private static readonly DateOnly Eleventh = new(2026, 10, 11);
    private static readonly DateOnly Twelfth = new(2026, 10, 12);
    private readonly ActivityLogRig data = ActivityDataset.Make();

    public void Dispose() => data.Dispose();

    private List<long> Pass(ActivityFilter filter) =>
        ActivityLogReader.Load(data.Source).Rows.Where(filter.Matches).Select(r => r.Sequence).ToList();

    [Fact]
    public void An_empty_filter_passes_every_row()
    {
        Assert.Equal([1L, 2, 3, 4, 5, 6], Pass(new ActivityFilter()));
        Assert.Equal([1L, 2, 3, 4, 5, 6], Pass(new ActivityFilter { Text = "   " }));
        Assert.True(new ActivityFilter { Text = " \t " }.IsEmpty);
    }

    [Fact]
    public void Each_field_passes_the_rows_that_carry_exactly_that_value()
    {
        var table = new (ActivityFilter Filter, long[] Expected)[]
        {
            (new() { Assistant = "dextra" }, [1, 2, 5, 6]),
            (new() { Assistant = "astur-shop" }, [3, 4]),
            (new() { Assistant = "Dextra" }, []), // ids compare exactly
            (new() { Assistant = "dext" }, []),
            (new() { Mission = "m-1" }, [1, 2]),
            (new() { Mission = "m-2" }, [3, 4]),
            (new() { Mission = ActivityFilter.None }, [5]), // the actions that belong to no mission
            (new() { Mission = "m-9" }, []),
            (new() { Agent = "t-1" }, [1, 6]),
            (new() { Agent = "t-3" }, [3, 4]),
            (new() { Agent = ActivityFilter.None }, [5]),
            (new() { Class = "Run" }, [1, 6]),
            (new() { Class = "Outward" }, [3, 4]),
            (new() { Class = "Read web" }, [2]),
            (new() { Class = "run" }, []),
            (new() { Grant = "grant:g-1" }, [1, 6]),
            (new() { Grant = "decision:d-7" }, [3, 4]),
            (new() { Grant = ActivityFilter.None }, [2, 5]), // actions no grant covered
        };
        foreach (var (filter, expected) in table)
            Assert.True(expected.SequenceEqual(Pass(filter)), $"{filter.Describe()} passed [{string.Join(",", Pass(filter))}], expected [{string.Join(",", expected)}]");
    }

    [Fact]
    public void Days_are_inclusive_local_days_taken_from_the_day_files()
    {
        var table = new (ActivityFilter Filter, long[] Expected)[]
        {
            (new() { From = Twelfth }, [3, 4, 5, 6]),
            (new() { To = Eleventh }, [1, 2]),
            (new() { From = Eleventh, To = Eleventh }, [1, 2]),
            (new() { From = Twelfth, To = Twelfth }, [3, 4, 5, 6]),
            (new() { From = Eleventh, To = Twelfth }, [1, 2, 3, 4, 5, 6]),
            (new() { From = Twelfth, To = Eleventh }, []), // backwards: nothing, not everything
            (new() { From = new DateOnly(2026, 10, 13) }, []),
            (new() { To = new DateOnly(2026, 10, 10) }, []),
        };
        foreach (var (filter, expected) in table)
            Assert.True(expected.SequenceEqual(Pass(filter)), $"{filter.Describe()} passed [{string.Join(",", Pass(filter))}], expected [{string.Join(",", expected)}]");
    }

    [Fact]
    public void Text_needs_every_word_somewhere_in_the_row_in_any_case()
    {
        var table = new (string Text, long[] Expected)[]
        {
            ("shop", [1, 3, 4, 6]), // the target E:/shop and the assistant astur-shop
            ("SHOP", [1, 3, 4, 6]),
            ("dotnet build", [1]), // an input's text
            ("dotnet sent", []), // each word must be there
            ("sent", [3]), // the result
            ("held pat", []),
            ("typed 148", [3]), // an input in words
            ("token", [3]), // an input's name
            ("[secret]", [3]),
            ("decision:d-7", [3, 4]), // the grant
            ("mail", [3, 4]), // the tool
            ("outward", [3, 4]), // the class, in any case
            ("duty", [5]),
            ("https://example.test", [2]),
            ("nothing like this", []),
        };
        foreach (var (text, expected) in table)
        {
            var passed = Pass(new ActivityFilter { Text = text });
            Assert.True(expected.SequenceEqual(passed), $"\"{text}\" passed [{string.Join(",", passed)}], expected [{string.Join(",", expected)}]");
        }
    }

    [Fact]
    public void Fields_combine_and_a_row_must_pass_them_all()
    {
        Assert.Equal([1L, 6], Pass(new ActivityFilter { Assistant = "dextra", Class = "Run" }));
        Assert.Equal([6L], Pass(new ActivityFilter { Assistant = "dextra", Mission = "m-3", Text = "tests" }));
        Assert.Empty(Pass(new ActivityFilter { Mission = "m-1", From = Twelfth }));
        Assert.Equal([1L], Pass(new ActivityFilter { Mission = "m-1", Grant = "grant:g-1", To = Eleventh }));
        Assert.Equal(Pass(new ActivityFilter { Mission = "m-2" }), Pass(ActivityFilter.ForMission("m-2")));
    }

    [Fact]
    public void A_line_that_cannot_be_read_passes_only_the_filters_that_need_no_field_of_it()
    {
        data.Close();
        File.AppendAllText(data.DayFile("2026-10-12"), "garbage nobody sealed\n");
        var rows = ActivityLogReader.Load(data.Source).Rows;
        var garbage = rows[^1];
        Assert.Equal(ActivityRowKind.Unreadable, garbage.Kind);

        Assert.True(new ActivityFilter().Matches(garbage));
        Assert.True(new ActivityFilter { From = Twelfth }.Matches(garbage)); // it sits in the 12th's file
        Assert.False(new ActivityFilter { To = Eleventh }.Matches(garbage));
        Assert.True(new ActivityFilter { Text = "garbage" }.Matches(garbage));
        Assert.False(new ActivityFilter { Assistant = "dextra" }.Matches(garbage));
        Assert.False(new ActivityFilter { Mission = "m-1" }.Matches(garbage));
        Assert.False(new ActivityFilter { Class = "Run" }.Matches(garbage));
        Assert.True(new ActivityFilter { Mission = ActivityFilter.None }.Matches(garbage)); // it belongs to no mission
    }

    [Fact]
    public void A_filter_is_described_in_words_and_counts_what_it_sets()
    {
        Assert.Equal("", new ActivityFilter().Describe());
        Assert.Equal("assistant dextra, mission m-1, class\u0020Run, grant grant:g-1, 2026-10-01 to 2026-10-12, text \"hi there\"",
            new ActivityFilter { Assistant = "dextra", Mission = "m-1", Class = "Run", Grant = "grant:g-1", From = new DateOnly(2026, 10, 1), To = Twelfth, Text = "  hi there " }.Describe());
        Assert.Equal("no mission, no agent, no grant", new ActivityFilter { Mission = "", Agent = "", Grant = "" }.Describe());
        Assert.Equal("2026-10-12", new ActivityFilter { From = Twelfth, To = Twelfth }.Describe());
        Assert.Equal("from 2026-10-12", new ActivityFilter { From = Twelfth }.Describe());
        Assert.Equal("up to 2026-10-11", new ActivityFilter { To = Eleventh }.Describe());
        Assert.Equal(0, new ActivityFilter().ActiveCount);
        Assert.Equal(3, new ActivityFilter { Assistant = "dextra", From = Eleventh, To = Twelfth, Text = "x" }.ActiveCount); // the two days count once
        Assert.False(new ActivityFilter { Agent = "" }.IsEmpty);
        Assert.Throws<ArgumentNullException>(() => new ActivityFilter().Matches(null!));
    }
}
