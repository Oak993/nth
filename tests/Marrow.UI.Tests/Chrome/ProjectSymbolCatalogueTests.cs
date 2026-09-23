using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Source: DroppyCode/UI/Chrome/ProjectSymbolCatalogue.swift (B2 1fbf81e), :1-53.
// Every expected value is transcribed from the pinned Swift or from the records PR-SIDEBAR-164 and UI-SIDEBAR-061;
// the name table is the generated contract fixture Fixtures/ProjectSymbolCatalogueFixture.cs (from :25-51).
// Swift normalizes the line ends of a multiline string literal to LF, so `raw.split(separator: "\n")` sees one
// line per category whatever the checkout's line ends are; no name may carry a carriage return.
public sealed class ProjectSymbolCatalogueTests
{
    // UI-SIDEBAR-061 exact_text, split at " | ", in the order it lists them (the table order, :25-51).
    private static readonly string[] Titles =
    [
        "Communication", "Maps", "Objects & Tools", "Devices", "Camera & Photos", "Gaming", "Connectivity",
        "Transportation", "Automotive", "Accessibility", "Privacy & Security", "Human", "Home", "Fitness", "Nature",
        "Editing", "Text Formatting", "Media", "Keyboard", "Commerce", "Time", "Health", "Shapes", "Arrows", "Indices",
        "Math", "Other",
    ];

    // PR-SIDEBAR-164 expected_result and UI-SIDEBAR-061 notes: name count (tiles per section), in table order.
    private static readonly int[] Counts =
    [
        88, 20, 283, 140, 45, 90, 35, 15, 278, 37, 16, 87, 126, 120, 109, 34, 5, 31, 17, 141, 1, 45, 17, 186, 176, 24, 464,
    ];

    // ProjectIconPicker.swift:87-102, the Popular shortlist, in source order.
    private static readonly string[] Shortlist =
    [
        "hammer.fill", "wrench.and.screwdriver.fill", "gearshape.fill", "terminal.fill",
        "curlybraces.square.fill", "chevron.left.square.fill", "shippingbox.fill", "cube.fill",
        "tray.full.fill", "doc.fill", "square.stack.3d.up.fill", "memorychip.fill",
        "cpu.fill", "externaldrive.fill", "paintbrush.fill", "paintpalette.fill",
        "book.closed.fill", "graduationcap.fill", "lightbulb.fill", "key.fill",
        "lock.fill", "leaf.fill", "flame.fill", "drop.fill",
        "sun.max.fill", "moon.stars.fill", "cloud.fill", "bolt.fill",
        "star.fill", "heart.fill", "diamond.fill", "crown.fill",
        "trophy.fill", "medal.fill", "checkmark.seal.fill", "gift.fill",
        "gamecontroller.fill", "dpad.fill", "popcorn.fill", "film.fill",
        "mic.fill", "speaker.wave.3.fill", "camera.fill", "photo.fill",
        "globe.fill", "point.3.filled.connected.trianglepath.dotted", "hand.point.up.left.fill", "person.fill",
        "person.2.fill", "pawprint.fill", "bird.fill", "fish.fill",
        "cup.and.saucer.fill", "waterbottle.fill", "takeoutbag.and.cup.and.straw.fill", "theatermasks.fill",
    ];

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:14-20, :25-51
    [Trait("Req", "UI-SIDEBAR-061")] // UI/Chrome/ProjectSymbolCatalogue.swift:14-22, :24-52
    public void The_catalogue_holds_27_groups_titled_as_the_table_in_table_order_byte_for_byte()
    {
        var groups = ProjectSymbolCatalogue.Groups;
        Assert.Equal(27, groups.Count);
        Assert.Equal(Titles, groups.Select(g => g.Title).ToArray());
        var expected = Encoding.UTF8.GetBytes(string.Join(" | ", Titles));
        var actual = Encoding.UTF8.GetBytes(string.Join(" | ", groups.Select(g => g.Title)));
        Assert.Equal(expected, actual);
        Assert.Equal(
            "Communication | Maps | Objects & Tools | Devices | Camera & Photos | Gaming | Connectivity | Transportation | Automotive | Accessibility | Privacy & Security | Human | Home | Fitness | Nature | Editing | Text Formatting | Media | Keyboard | Commerce | Time | Health | Shapes | Arrows | Indices | Math | Other",
            string.Join(" | ", groups.Select(g => g.Title)));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:19
    public void Every_group_id_is_its_title()
    {
        foreach (var group in ProjectSymbolCatalogue.Groups)
            Assert.Equal(group.Title, group.Id);
        Assert.Equal(Titles, ProjectSymbolCatalogue.Groups.Select(g => g.Id).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:17, :25-51
    [Trait("Req", "UI-SIDEBAR-061")] // UI/Chrome/ProjectSymbolCatalogue.swift:25-51, one tile per name
    public void Each_group_holds_the_name_count_the_records_state_2630_in_all()
    {
        var groups = ProjectSymbolCatalogue.Groups;
        Assert.Equal(Counts, groups.Select(g => g.Names.Count).ToArray());
        Assert.Equal(2630, groups.Sum(g => g.Names.Count));
        Assert.Equal(2630, Counts.Sum());
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:14-20, :25-51
    public void Each_group_holds_exactly_the_table_names_in_table_order()
    {
        var groups = ProjectSymbolCatalogue.Groups;
        var table = ProjectSymbolCatalogueFixture.Table;
        Assert.Equal(table.Length, groups.Count);
        for (var i = 0; i < table.Length; i++)
        {
            Assert.Equal(table[i].Title, groups[i].Title);
            Assert.Equal(table[i].Names, groups[i].Names.ToArray());
        }
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:24-52
    public void The_groups_written_back_as_table_lines_are_the_Swift_table_byte_for_byte()
    {
        // Each group rebuilt as `title TAB names joined by single spaces`, the lines joined by LF, must hash to the
        // SHA-256 of ProjectSymbolCatalogue.swift:25-51 (pinned blob, LF). A stray CR, a dropped or reordered name,
        // or a changed title changes the hash.
        var lines = ProjectSymbolCatalogue.Groups.Select(g => g.Title + "\t" + string.Join(" ", g.Names));
        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines));
        Assert.Equal(66907, bytes.Length);
        Assert.Equal(ProjectSymbolCatalogueFixture.RawTableSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:22
    public void All_is_every_group_s_names_concatenated_in_group_order()
    {
        var all = ProjectSymbolCatalogue.All;
        Assert.Equal(2630, all.Count);
        Assert.Equal(ProjectSymbolCatalogue.Groups.SelectMany(g => g.Names).ToArray(), all.ToArray());
        Assert.Equal(ProjectSymbolCatalogueFixture.Table.SelectMany(t => t.Names).ToArray(), all.ToArray());
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:25, :51
    public void The_table_begins_and_ends_with_the_names_the_record_quotes()
    {
        var groups = ProjectSymbolCatalogue.Groups;
        Assert.Equal("arrow.down.left.video.fill", groups[0].Names[0]);
        Assert.Equal("arrow.down.message.fill", groups[0].Names[1]);
        var other = groups[26];
        Assert.Equal("Other", other.Title);
        Assert.Equal("123.rectangle.fill", other.Names[0]);
        Assert.Equal("a.book.closed.fill", other.Names[1]);
        Assert.Equal("zr.rectangle.roundedtop.fill", other.Names[other.Names.Count - 1]);
        Assert.Equal("arrow.down.left.video.fill", ProjectSymbolCatalogue.All[0]);
        Assert.Equal("zr.rectangle.roundedtop.fill", ProjectSymbolCatalogue.All[2629]);
        Assert.Equal("globe.badge.clock.fill", Assert.Single(groups[20].Names)); // :45, Time
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:25-51
    public void No_name_is_repeated_within_or_across_groups()
    {
        var all = ProjectSymbolCatalogue.All;
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        foreach (var group in ProjectSymbolCatalogue.Groups)
            Assert.Equal(group.Names.Count, group.Names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:25-51
    public void Names_within_a_group_keep_the_table_order_which_is_ascending_code_point_order()
    {
        foreach (var group in ProjectSymbolCatalogue.Groups)
        {
            // Every name is ASCII, so ordinal UTF-16 order is code-point order.
            Assert.All(group.Names, name => Assert.All(name, c => Assert.True(c < 128, $"{name} is not ASCII")));
            for (var i = 1; i < group.Names.Count; i++)
                Assert.True(string.CompareOrdinal(group.Names[i - 1], group.Names[i]) < 0,
                    $"{group.Title}: {group.Names[i - 1]} before {group.Names[i]}");
        }
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:15-17
    public void No_name_is_empty_or_holds_a_space_a_tab_a_carriage_return_or_a_line_feed()
    {
        // :15 splits a line at its first tab, :17 splits the rest at spaces omitting empty pieces, and the literal's
        // line ends are LF: a name never carries a separator or a CR (a CRLF raw literal split at LF would).
        foreach (var group in ProjectSymbolCatalogue.Groups)
        {
            Assert.NotEmpty(group.Names);
            Assert.DoesNotContain('\t', group.Title);
            Assert.DoesNotContain('\r', group.Title);
            Assert.DoesNotContain('\n', group.Title);
            foreach (var name in group.Names)
            {
                Assert.NotEqual("", name);
                Assert.True(name.IndexOfAny([' ', '\t', '\r', '\n']) < 0, $"{group.Title}: [{name}]");
            }
        }
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:3-6, :51
    [Trait("Req", "UI-SIDEBAR-061")] // UI/Chrome/ProjectSymbolCatalogue.swift:24-52
    public void Every_name_ends_in_dot_fill_except_four_in_Other()
    {
        var unfilled = ProjectSymbolCatalogue.Groups
            .SelectMany(g => g.Names.Where(n => !n.EndsWith(".fill", StringComparison.Ordinal)).Select(n => (g.Title, n)))
            .ToArray();
        Assert.Equal(
            new[] { ("Other", "apple.logo"), ("Other", "curlybraces"), ("Other", "swift"), ("Other", "terminal") },
            unfilled);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectIconPicker.swift:87-109; ProjectSymbolCatalogue.swift:25-51
    public void Fifty_five_of_the_56_Popular_shortlist_names_are_also_in_the_catalogue_each_in_one_group()
    {
        Assert.Equal(56, Shortlist.Length);
        var missing = Shortlist.Where(n => !ProjectSymbolCatalogue.All.Contains(n)).ToArray();
        Assert.Equal(new[] { "point.3.filled.connected.trianglepath.dotted" }, missing);
        foreach (var name in Shortlist.Except(missing))
            Assert.Single(ProjectSymbolCatalogue.Groups, g => g.Names.Contains(name));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-164")] // UI/Chrome/ProjectSymbolCatalogue.swift:8-12; ProjectIconPicker.swift:107-109
    [Trait("Rule", "R-VAL-2")]
    public void A_group_is_a_value_the_picker_can_build_and_it_compares_by_content()
    {
        // ProjectIconPicker builds Group(id: "popular", title: "Popular", names: favourites); Group is Hashable.
        var popular = new ProjectSymbolCatalogue.Group("popular", "Popular", new ValueList<string>(["hammer.fill", "cube.fill"]));
        var same = new ProjectSymbolCatalogue.Group("popular", "Popular", new ValueList<string>(["hammer.fill", "cube.fill"]));
        var reordered = new ProjectSymbolCatalogue.Group("popular", "Popular", new ValueList<string>(["cube.fill", "hammer.fill"]));
        Assert.Equal("popular", popular.Id);
        Assert.Equal("Popular", popular.Title);
        Assert.Equal(new[] { "hammer.fill", "cube.fill" }, popular.Names.ToArray());
        Assert.Equal(popular, same);
        Assert.Equal(popular.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(popular, reordered);
        Assert.NotEqual(popular, popular with { Id = "Popular" });
    }
}
