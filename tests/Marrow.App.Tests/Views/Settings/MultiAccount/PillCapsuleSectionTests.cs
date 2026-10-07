using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-9 (step 3a, OD-0115, OD-0119): Settings > Customization > "Pill in the composer": the cards, the glow card with its two variants, the "Usage left" scrubber and the legend.
[Collection("Headless")]
public sealed class PillCapsuleSectionTests(HeadlessSession headless)
{
    private void Run(Action<MultiAccountHarness, PillCapsuleSection, CapsuleStyleSetting> check, bool motion = false, bool reduceMotion = true, string? picked = null)
    {
        var rig = new AccountsRig();
        var style = new CapsuleStyleSetting(rig.Settings);
        if (picked is not null) style.Value = picked;
        PillCapsuleSection? section = null;
        MultiAccountHarness.Run(headless, rig, () => section = new PillCapsuleSection(style), h => check(h, section!, style), motion, reduceMotion);
    }

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_section_has_the_pages_heading_the_row_words_and_the_default_in_them() => Run((h, section, _) =>
    {
        var words = h.Words();
        Assert.Contains("Composer", words);
        Assert.Contains("Pill in the composer", words);
        Assert.Contains("How a thread that runs on a pill shows it. Default: Merged capsule.", words);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_style_but_the_original_colours_has_a_card_with_its_name_and_first_sentence() => Run((h, section, _) =>
    {
        var words = h.Words();
        foreach (var id in CapsuleStyles.Cards)
        {
            Assert.Contains(CapsuleStyles.Name(id), words);
            Assert.Contains(CapsuleStyles.CardLine(id), words);
        }
        Assert.Equal(CapsuleStyles.Cards, section.Cards.Select(c => c.Id));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_merged_capsule_wears_the_default_tag() => Run((h, section, _) =>
    {
        var tags = h.Words().Where(w => w == "Default").ToList();
        Assert.Single(tags);
        Assert.Equal(["1"], section.Cards.Where(c => c.HasDefaultTag).Select(c => c.Id));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_glow_card_holds_the_two_variants_the_usage_left_scrubber_and_the_legend() => Run((h, section, _) =>
    {
        var words = h.Words();
        Assert.Contains("Usage light", words);
        Assert.Contains("Original colours", words);
        Assert.Contains("Usage left", words);
        foreach (var label in new[] { "Plenty", "Half", "Low", "Almost out" }) Assert.Contains(label, words);
        Assert.Equal(["11", "11o"], section.Radios.Where(r => r.Id is "11" or "11o").Select(r => r.Id));
        Assert.Equal(12, section.Radios.Count);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Merged_capsule_is_the_pick_until_the_user_picks_and_the_radios_say_so() => Run((h, section, style) =>
    {
        Assert.Equal("1", section.Picked);
        Assert.Equal(["1"], section.Radios.Where(r => r.Picked).Select(r => r.Id));
        Assert.Equal("1", style.Value);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Choosing_a_card_sets_the_one_style_every_capsule_follows() => Run((h, section, style) =>
    {
        section.Radios.First(r => r.Id == "6").Group.Select("6");
        h.Settle();
        Assert.Equal("6", style.Value);
        Assert.Equal(["6"], section.Radios.Where(r => r.Picked).Select(r => r.Id));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Choosing_the_original_colours_variant_picks_style_eleven_o() => Run((h, section, style) =>
    {
        section.Radios.First(r => r.Id == "11o").Group.Select("11o");
        h.Settle();
        Assert.Equal("11o", style.Value);
        Assert.Equal(["11o"], section.Radios.Where(r => r.Picked).Select(r => r.Id));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_style_chosen_elsewhere_is_shown_as_the_pick() => Run((h, section, style) =>
    {
        style.Value = "9";
        h.Settle();
        Assert.Equal("9", section.Picked);
        Assert.Equal(["9"], section.Radios.Where(r => r.Picked).Select(r => r.Id));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_stored_style_is_the_pick_when_the_page_opens() => Run((h, section, _) =>
    {
        Assert.Equal("11o", section.Picked);
    }, picked: "11o");

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_click_on_a_card_picks_it() => Run((h, section, style) =>
    {
        var card = section.Radios.First(r => r.Id == "4");
        var centre = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), h.Window)!.Value;
        h.Window.MouseDown(centre, MouseButton.Left);
        h.Window.MouseUp(centre, MouseButton.Left);
        h.Settle();
        Assert.Equal("4", style.Value);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_arrow_keys_move_the_pick_through_the_cards_and_the_glow_variants() => Run((h, section, style) =>
    {
        style.Value = "10";
        h.Settle();
        section.Radios.First(r => r.Id == "10").Focus();
        h.Settle();
        h.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        h.Settle();
        Assert.Equal("11", style.Value);
        h.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        h.Settle();
        Assert.Equal("11o", style.Value);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_card_and_variant_previews_the_capsule_live_and_the_previews_take_no_pointer() => Run((h, section, _) =>
    {
        Assert.Equal(12, section.Previews.Count);
        Assert.Equal(CapsuleStyles.Ids, section.Previews.Select(p => p.Style));
        Assert.All(section.Previews, p => Assert.False(p.IsHitTestVisible));
        Assert.All(section.Previews, p => Assert.Equal(5, p.Marks.Count));
        Assert.All(section.Previews, p => Assert.Equal(StepRole.Cur, p.Marks[1].Role));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_scrubber_reads_a_hundred_percent_left_and_says_so_for_a_screen_reader() => Run((h, section, _) =>
    {
        var scrubber = section.Scrubber;
        Assert.Equal(100, scrubber.Value);
        Assert.Equal("100%", scrubber.ValueText);
        Assert.Contains("100%", h.Words());
        Assert.Equal("Usage left, for the preview", AutomationProperties.GetName(scrubber));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_scrubber_answers_the_keys_in_steps_of_one_and_ten_and_to_the_ends() => Run((h, section, _) =>
    {
        var scrubber = section.Scrubber;
        scrubber.Focus();
        h.Settle();
        h.Window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(99, scrubber.Value);
        h.Window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        Assert.Equal(89, scrubber.Value);
        h.Window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(0, scrubber.Value);
        h.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal(1, scrubber.Value);
        h.Window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
        Assert.Equal(11, scrubber.Value);
        h.Window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Equal(100, scrubber.Value);
        Assert.Equal("100% left", scrubber.SpokenText);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Dragging_the_scrubber_sets_the_percent_by_where_the_pointer_is() => Run((h, section, _) =>
    {
        var scrubber = section.Scrubber;
        h.Window.Height = 1600; // the page is taller than the test window
        h.Settle();
        var left = scrubber.TranslatePoint(new Point(0, 8), h.Window)!.Value;
        var width = scrubber.Bounds.Width;
        h.Window.MouseDown(new Point(left.X + width * 0.25, left.Y), MouseButton.Left);
        Assert.Equal(25, scrubber.Value, 0);
        h.Window.MouseMove(new Point(left.X + width * 0.6, left.Y));
        Assert.Equal(60, scrubber.Value, 0);
        h.Window.MouseUp(new Point(left.X + width * 0.6, left.Y), MouseButton.Left);
        Assert.Equal(60, scrubber.Value, 0);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_the_two_glow_previews_follow_the_scrubber_the_others_stay_where_they_are() => Run((h, section, _) =>
    {
        var before = section.Previews.Select(p => (p.LightNow, p.Style)).ToList();
        section.Scrubber.Value = 40;
        h.Settle();
        foreach (var preview in section.Previews)
        {
            var was = before.First(b => b.Style == preview.Style).LightNow;
            if (preview.Style is "11" or "11o")
                Assert.Equal(UsageLight.Color(0.4, dark: false), preview.LightNow);
            else
                Assert.Equal(was, preview.LightNow);
        }
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Left_alone_the_scrubber_runs_from_a_hundred_down_to_four_percent_rests_and_starts_again() => Run((h, section, _) =>
    {
        var scrubber = section.Scrubber;
        h.Window.Height = 1600; // the whole page in view
        h.Settle();
        Assert.Equal(100, scrubber.Value);
        h.Step(100);
        Assert.Equal(99, scrubber.Value);
        for (var i = 0; i < 400 && scrubber.Value > 4; i++) h.Step(160);
        Assert.Equal(4, scrubber.Value);
        h.Step(160); // it sees the end and rests
        h.Step(1700);
        Assert.Equal(4, scrubber.Value);
        h.Step(200);
        Assert.Equal(100, scrubber.Value);
    }, motion: true, reduceMotion: false);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_touch_holds_the_run_for_four_seconds() => Run((h, section, _) =>
    {
        var scrubber = section.Scrubber;
        h.Window.Height = 1600;
        h.Settle();
        scrubber.Focus();
        h.Window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        scrubber.Value = 50;
        h.Step(3500);
        Assert.Equal(50, scrubber.Value);
        h.Step(1000);
        Assert.True(scrubber.Value < 50);
    }, motion: true, reduceMotion: false);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Under_Reduce_Motion_the_scrubber_does_not_run_by_itself() => Run((h, section, _) =>
    {
        h.Window.Height = 1600;
        h.Settle();
        h.Step(2000);
        Assert.Equal(100, section.Scrubber.Value);
    }, motion: true, reduceMotion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Out_of_view_nothing_on_the_section_loops_and_the_scrubber_does_not_run() => Run((h, section, _) =>
    {
        // the test window is shorter than the page: the glow card and its scrubber are below the fold
        h.Step(100);
        Assert.Equal(100, section.Scrubber.Value);
        foreach (var preview in section.Previews)
        {
            var top = preview.TranslatePoint(new Point(0, 0), h.Window)!.Value.Y;
            if (preview.IsLooping) Assert.True(top < h.Window.Bounds.Height, $"style {preview.Style} loops at y {top}, out of the window");
            if (top >= h.Window.Bounds.Height) Assert.False(preview.IsLooping, $"style {preview.Style} below the fold loops");
        }
        Assert.Contains(section.Previews, p => p.Style is "5" or "6" or "8" && p.IsLooping); // those in view do move
    }, motion: true, reduceMotion: false);
}
