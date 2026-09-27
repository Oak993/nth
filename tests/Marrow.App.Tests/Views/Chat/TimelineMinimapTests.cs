using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineMinimapTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-066")] // App/Views/Chat/TimelineMinimap.swift:56-76
    public void Titles_use_eight_words_unicode_whitespace_and_the_message_fallbacks()
    {
        using var runtime = new TestRuntime();

        var actual = runtime.OnMain(() => TimelineMinimap.Entries(new[]
        {
            Block("eight", "one\U000000A0two three four five six seven eight nine"),
            Block("empty-one-image", "", Attachment("picture.png", "image/png")),
            Block("empty-many-images", "", Attachment("one.png", "image/png"), Attachment("two.png", "image/jpeg")),
            Block("empty-file", "", Attachment("notes.txt", "text/plain")),
            Block("empty-no-attachment", ""),
        }));

        Assert.Equal("one two three four five six seven eight\U00002026", actual[0].Title); // U+2026 HORIZONTAL ELLIPSIS
        Assert.Equal("1 image", actual[1].Title);
        Assert.Equal("2 images", actual[2].Title);
        Assert.Equal("notes.txt", actual[3].Title);
        Assert.Equal("Message", actual[4].Title);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-067")] // App/Views/Chat/TimelineMinimap.swift:45-50,78-82
    public void Preview_flattens_whitespace_truncates_at_160_characters_and_suppresses_a_matching_title()
    {
        using var runtime = new TestRuntime();

        var actual = runtime.OnMain(() => TimelineMinimap.Entries(new[]
        {
            Block("same", "short\U000000A0message"),
            Block("long", new string('a', 161)),
            Block("flatten", "first\n\tsecond"),
        }));

        Assert.Equal("short message", actual[0].Title);
        Assert.Equal("", actual[0].Snippet);
        Assert.Equal(new string('a', 160) + "\U00002026", actual[1].Snippet); // U+2026 HORIZONTAL ELLIPSIS
        // "first\n\tsecond" is only 2 words: its title (FirstWords) and its preview (flattened whitespace) are both
        // exactly "first second", so :46-51's "snippet.isEmpty || snippet == title ? \"\" : snippet" suppresses it -
        // this is the SAME "a snippet identical to the title is dropped" rule the "same" case above already tests,
        // just reached through a message with no more than 8 words rather than one already equal by construction.
        Assert.Equal("", actual[2].Snippet);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-068")] // App/Views/Chat/TimelineMinimap.swift:37-54
    public void An_entry_is_memoized_by_block_id()
    {
        using var runtime = new TestRuntime();
        var id = Guid.NewGuid().ToString();

        var actual = runtime.OnMain(() =>
        {
            var first = TimelineMinimap.Entries(new[] { Block(id, "original title") });
            var second = TimelineMinimap.Entries(new[] { Block(id, "replacement title") });
            return (First: first[0], Second: second[0]);
        });

        Assert.Equal("original title", actual.First.Title);
        Assert.Equal(actual.First, actual.Second);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-069")] // App/Views/Chat/TimelineMinimap.swift:84-86,276-282
    [Trait("Req", "UI-CHAT-004")] // App/Views/Chat/TimelineMinimap.swift:258-282
    public void Tick_width_uses_grapheme_weight_and_expanded_selected_and_hovered_states()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var narrow = new TimelineMinimapEntry { Id = "narrow", Title = "narrow", Snippet = "", Weight = 0.15 };
            var wide = new TimelineMinimapEntry { Id = "wide", Title = "wide", Snippet = "", Weight = 1 };
            var rail = new TimelineMinimapRail(new[] { narrow, wide }, 100, null, (_, _) => { });
            var tickWidth = typeof(TimelineMinimapRail).GetMethod("TickWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.Equal(11.8, (double)tickWidth.Invoke(rail, new object[] { narrow, false, false })!, 10);
            Assert.Equal(22, (double)tickWidth.Invoke(rail, new object[] { wide, false, false })!, 10);
            Assert.Equal(26, (double)tickWidth.Invoke(rail, new object[] { narrow, true, false })!);
            Assert.Equal(26, (double)tickWidth.Invoke(rail, new object[] { narrow, false, true })!);

            // :293-295 while the rail is expanded (a press is down), every other tick widens by 2, capped at 26.
            var expanded = new TimelineMinimapRail(new[] { narrow }, 100, null, (_, _) => { });
            var isPressing = typeof(TimelineMinimapRail).GetField("isPressing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            isPressing.SetValue(expanded, true);
            var expandedTickWidth = typeof(TimelineMinimapRail).GetMethod("TickWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(11.8 + 2, (double)expandedTickWidth.Invoke(expanded, new object[] { narrow, false, false })!, 10);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-070")] // App/Views/Chat/TimelineMinimap.swift:373-403
    public void Rail_metrics_keep_roomy_spacing_shrink_to_the_floor_and_center_the_stack()
    {
        // Roomy: ticks = 3*2 = 6, gaps = 2; 6 + 2*8 = 22 <= 100, so spacing stays roomy (8) and
        // topInset = (100 - 22) / 2 = 39; CenterY(i) = topInset + i*(tickHeight+spacing) + tickHeight/2.
        var roomy = Metrics(centeringHeight: 100, count: 3);
        Assert.Equal(8, Property<double>(roomy, "Spacing"));
        Assert.Equal(39, Property<double>(roomy, "TopInset"));
        Assert.Equal(40, Invoke<double>(roomy, "CenterY", 0L));
        Assert.Equal(60, Invoke<double>(roomy, "CenterY", 2L));

        // Tight: 6 + 2*8 = 22 <= 10 is false, so spacing = max(2, (10-6)/2) = 2;
        // topInset = (10 - (6 + 2*2)) / 2 = (10 - 10) / 2 = 0.
        var tight = Metrics(centeringHeight: 10, count: 3);
        Assert.Equal(2, Property<double>(tight, "Spacing"));
        Assert.Equal(0, Property<double>(tight, "TopInset"));

        var overflowing = Metrics(centeringHeight: 4, count: 5);
        Assert.Equal(2, Property<double>(overflowing, "Spacing"));
        Assert.Equal(-7, Property<double>(overflowing, "TopInset"));

        var empty = Metrics(centeringHeight: 0, count: 0);
        Assert.Equal(8, Property<double>(empty, "Spacing"));
        Assert.Equal(0, Property<double>(empty, "TopInset"));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-071")] // App/Views/Chat/TimelineMinimap.swift:412-435
    public void Hit_area_covers_the_tick_stack_but_stays_below_the_chrome_clearance()
    {
        // topInset = 39 (see Rail_metrics_..., same 100/3 metrics), pitch = 10: CenterY(0) = 40, CenterY(2) = 60.
        // top = max(40 - 5, chromeClearance=44) = 44 (35 < 44); bottom = min(60 + 5, 100) = 65; height = 65 - 44 = 21.
        var metrics = Metrics(centeringHeight: 100, count: 3);
        var rect = Invoke<Rect>(metrics, "HitRect", 3L, new Size(20, 100));

        Assert.Equal(new Rect(0, Chrome.ChromeTopPadding + Chrome.CapsuleHeight, 20, 21), rect);
        Assert.Equal(new Rect(0, 0, 0, 0), Invoke<Rect>(metrics, "HitRect", 0L, new Size(20, 100)));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-072")] // App/Views/Chat/TimelineMinimap.swift:348-365,405-410
    public void Pointer_y_maps_to_the_nearest_tick_and_clamps_to_both_ends()
    {
        var metrics = Metrics(centeringHeight: 100, count: 3);
        var entries = new[]
        {
            new TimelineMinimapEntry { Id = "first", Title = "first", Snippet = "", Weight = 0.15 },
            new TimelineMinimapEntry { Id = "middle", Title = "middle", Snippet = "", Weight = 0.15 },
            new TimelineMinimapEntry { Id = "last", Title = "last", Snippet = "", Weight = 0.15 },
        };

        // topInset = 39, pitch = 10 (100/3 metrics, see Rail_metrics_...): raw = (y - 39 - 1 + 5) / 10 = (y - 35) / 10.
        // y=40 -> raw 0.5 -> floor 0 -> "first"; y=46 -> raw 1.1 -> floor 1 -> "middle".
        Assert.Equal("first", Invoke<string?>(metrics, "EntryID", -100.0, entries));
        Assert.Equal("first", Invoke<string?>(metrics, "EntryID", 40.0, entries));
        Assert.Equal("middle", Invoke<string?>(metrics, "EntryID", 46.0, entries));
        Assert.Equal("last", Invoke<string?>(metrics, "EntryID", 1000.0, entries));
        Assert.Null(Invoke<string?>(metrics, "EntryID", 20.0, Array.Empty<TimelineMinimapEntry>()));
    }

    // Marrow.UI.Port.PointerStyle field: matches ArrangeGrip's own reflection style elsewhere in this test suite.
    private static Border HitAreaOf(TimelineMinimapRail rail) =>
        (Border)typeof(TimelineMinimapRail).GetField("hitArea", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(rail)!;

    [Fact]
    [Trait("Req", "PR-CHAT-072")] // App/Views/Chat/TimelineMinimap.swift:211
    [Trait("Req", "UI-CHAT-003")] // App/Views/Chat/TimelineMinimap.swift:201-240
    [Trait("Decision", "ED-205")]
    public void The_rail_hit_area_declares_the_link_pointer_style_and_keeps_it_after_the_rail_leaves_the_tree()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var entry = new TimelineMinimapEntry { Id = "one", Title = "one", Snippet = "", Weight = 0.15 };
            var rail = new TimelineMinimapRail(new[] { entry }, 100, null, (_, _) => { });
            var linkCursor = Marrow.UI.Port.PointerStyleCursors.Get(Marrow.UI.Port.PointerStyle.Link, 1);

            var hitArea = HitAreaOf(rail);
            Assert.Same(linkCursor, hitArea.Cursor);

            // B3 deletes ownsCursorPush and this port's matching LIFO CursorOwner stack (the pre-B3 push/pop
            // ownership bookkeeping): the style is declared once on the control itself, so there is nothing left
            // for a disappear to release, and nothing for the rail leaving a real visual tree to clear either.
            var window = new Avalonia.Controls.Window { Content = rail };
            window.Show();
            window.Content = null;
            Assert.Same(linkCursor, hitArea.Cursor);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-074")] // App/Views/Chat/TimelineMinimap.swift:289-342
    [Trait("Req", "UI-CHAT-005")] // App/Views/Chat/TimelineMinimap.swift:289-342
    public void Preview_card_height_uses_the_source_arithmetic_estimate()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var entry = new TimelineMinimapEntry { Id = "card", Title = "title", Snippet = "", Weight = 0.15 };
            var rail = new TimelineMinimapRail(new[] { entry }, 100, null, (_, _) => { });
            var estimate = typeof(TimelineMinimapRail).GetMethod("EstimatedCardHeight", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.Equal(37d, (double)estimate.Invoke(rail, new object[] { entry })!);

            var oneLine = entry with { Snippet = new string('x', 39) };
            var twoLines = entry with { Snippet = new string('x', 40) };
            var threeLines = entry with { Snippet = new string('x', 117) };
            Assert.Equal(55d, (double)estimate.Invoke(rail, new object[] { oneLine })!);
            Assert.Equal(70d, (double)estimate.Invoke(rail, new object[] { twoLines })!);
            Assert.Equal(85d, (double)estimate.Invoke(rail, new object[] { threeLines })!);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-075")] // App/Views/Chat/TimelineMinimap.swift:89-129,250-254
    public void Equal_column_inputs_compare_blocks_tracking_identity_and_center_height()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var tracking = new TimelineScrollTracking();
            var block = Block("column", "prompt");
            var first = new TimelineMinimapColumn(new[] { block }, tracking, 100, (_, _) => { });
            var equal = new TimelineMinimapColumn(new[] { block }, tracking, 100, (_, _) => { });
            var otherHeight = new TimelineMinimapColumn(new[] { block }, tracking, 101, (_, _) => { });
            var otherTracking = new TimelineMinimapColumn(new[] { block }, new TimelineScrollTracking(), 100, (_, _) => { });

            Assert.True(TimelineMinimapColumn.InputsEqual(first, equal));
            Assert.False(TimelineMinimapColumn.InputsEqual(first, otherHeight));
            Assert.False(TimelineMinimapColumn.InputsEqual(first, otherTracking));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-076")] // App/Views/Chat/TimelineMinimap.swift:238-239,270-273
    public void The_outline_is_named_and_each_tick_is_named_for_its_message()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var entries = new[]
            {
                new TimelineMinimapEntry { Id = "first", Title = "First prompt", Snippet = "", Weight = 0.15 },
                new TimelineMinimapEntry { Id = "second", Title = "Second prompt", Snippet = "", Weight = 0.15 },
            };
            var rail = new TimelineMinimapRail(entries, 100, null, (_, _) => { });

            Assert.Equal("Conversation outline", AutomationProperties.GetName(rail));
            var names = LogicalQuery.SelfAndDescendants<Control>(rail)
                .Select(AutomationProperties.GetName)
                .Where(name => name is not null)
                .ToArray();
            Assert.Contains("First prompt", names);
            Assert.Contains("Second prompt", names);
        });
    }

    private static DisplayBlock Block(string id, string text, params Attachment[] attachments)
    {
        var message = new UserMessage
        {
            Text = text,
            Attachments = new ValueList<Attachment>(attachments),
        };
        var timelineEntry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.User(message), id));
        return new DisplayBlock.Turn(id, Guid.NewGuid(), new ValueList<TimelineEntry>(new[] { timelineEntry }), null, false);
    }

    private static Attachment Attachment(string name, string mimeType) =>
        new() { Name = name, Path = name, MimeType = mimeType };

    private static object Metrics(double centeringHeight, long count)
    {
        var type = typeof(TimelineMinimap).Assembly.GetType("Marrow.App.Views.Chat.MinimapRailMetrics", throwOnError: true)!;
        return Activator.CreateInstance(type, new object[] { centeringHeight, count })!;
    }

    private static TValue Property<TValue>(object instance, string name) =>
        (TValue)instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance)!;

    private static TValue Invoke<TValue>(object instance, string name, params object[] arguments) =>
        (TValue)instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)!.Invoke(instance, arguments)!;
}

// B3 audit: the former animation STOP #1 is satisfied by the transaction-driven persistent ticks.
