using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.MultiAccount.Capsule;

// MA-9 (step 3a, OD-0115, OD-0119): the twelve "Pill in the composer" styles, their words, their widths and the one setting that picks among them.
[Trait("Feature", "MA-9")]
[Trait("Rule", "R-TEST-4")]
public sealed class CapsuleStylesTests
{
    [Fact]
    public void There_are_twelve_styles_in_the_pages_order_with_its_names()
    {
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "11o"], CapsuleStyles.Ids);
        Assert.Equal(["Merged capsule", "Name below", "Lights only", "Segmented rim", "Orbit", "Liquid fill", "Stacked marks", "Aurora rim", "Dial", "Minimal", "Glow rim", "Glow rim · original colours"],
            CapsuleStyles.Ids.Select(CapsuleStyles.Name));
    }

    [Fact]
    public void Each_style_has_the_pages_description_and_the_card_line_is_its_first_sentence()
    {
        Assert.Equal("The account capsule becomes the pill: a little wider, the current mark inside, a row of tiny step lights along its top edge.", CapsuleStyles.Description("1"));
        Assert.Equal("The capsule stays as it is. The step lights sit just above it and the pill’s name in tiny text under it.", CapsuleStyles.Description("2"));
        Assert.Equal("Only a hairline of step lights over the capsule. The name appears on hover or focus.", CapsuleStyles.Description("3"));
        Assert.Equal("The capsule’s own rim is split into one segment per step: lit is the current step, faint are used, dim are next.", CapsuleStyles.Description("4"));
        Assert.Equal("Tiny step dots drift slowly around inside the rim; the current one glows.", CapsuleStyles.Description("5"));
        Assert.Equal("The glass fills like a gauge with the current account’s use; step ticks are etched along the rim.", CapsuleStyles.Description("6"));
        Assert.Equal("The next step’s mark peeks out behind the current one, like a stack of cards.", CapsuleStyles.Description("7"));
        Assert.Equal("A faint aurora runs slowly around the rim.", CapsuleStyles.Description("8"));
        Assert.Equal("A round capsule: a hairline arc around the mark, one segment per step.", CapsuleStyles.Description("9"));
        Assert.Equal("Just the mark. A single dot breathes beside it only while a Handshake runs.", CapsuleStyles.Description("10"));
        Assert.Equal("A glowing rim around dark glass. It blooms on hover, on press and during a Handshake.", CapsuleStyles.Description("11"));
        Assert.Equal("The owner’s blue-to-pink gradient, as pasted.", CapsuleStyles.Description("11o"));
        Assert.Equal("The account capsule becomes the pill: a little wider, the current mark inside, a row of tiny step lights along its top edge.", CapsuleStyles.CardLine("1"));
        Assert.Equal("The capsule stays as it is.", CapsuleStyles.CardLine("2"));
        Assert.Equal("A round capsule: a hairline arc around the mark, one segment per step.", CapsuleStyles.CardLine("9"));
    }

    [Fact]
    public void The_pill_capsule_is_as_wide_as_the_page_makes_it_and_the_plain_account_capsule_is_not_wider_than_the_narrowest()
    {
        Assert.Equal([48, 34, 34, 36, 44, 44, 46, 40, 22, 34, 44, 44], CapsuleStyles.Ids.Select(CapsuleStyles.Width));
    }

    [Fact]
    public void Merged_capsule_is_the_default_and_the_cards_list_every_style_but_original_colours_which_is_a_variant_of_the_glow_card()
    {
        Assert.Equal("1", CapsuleStyles.Default);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11"], CapsuleStyles.Cards);
    }

    [Fact]
    public void The_setting_is_merged_capsule_until_chosen_and_keeps_a_choice()
    {
        var store = new InMemorySettingsStore();
        var setting = new CapsuleStyleSetting(store);
        Assert.Equal("1", setting.Value);
        var changed = 0;
        setting.Changed += () => changed++;
        setting.Value = "5";
        Assert.Equal("5", setting.Value);
        Assert.Equal(1, changed);
        Assert.Equal("5", new CapsuleStyleSetting(store).Value);
        setting.Value = "5";
        Assert.Equal(1, changed);
    }

    [Fact]
    public void An_unknown_style_is_refused_and_an_unreadable_stored_one_reads_as_the_default()
    {
        var store = new InMemorySettingsStore();
        var setting = new CapsuleStyleSetting(store);
        setting.Value = "7";
        setting.Value = "13";
        Assert.Equal("7", setting.Value);
        store.Set(CapsuleStyleSetting.SettingsKey, "nonsense");
        Assert.Equal("1", new CapsuleStyleSetting(store).Value);
        store.Set(CapsuleStyleSetting.SettingsKey, 4);
        Assert.Equal("1", new CapsuleStyleSetting(store).Value);
    }

    [Fact]
    public void The_glow_card_has_two_variants_and_the_glow_family_is_both_of_them()
    {
        Assert.True(CapsuleStyles.IsGlow("11"));
        Assert.True(CapsuleStyles.IsGlow("11o"));
        Assert.False(CapsuleStyles.IsGlow("10"));
        Assert.Equal(["11", "11o"], CapsuleStyles.GlowVariants.Select(v => v.Id));
        Assert.Equal(["Usage light", "Original colours"], CapsuleStyles.GlowVariants.Select(v => v.Label));
        Assert.Equal(["Plenty", "Half", "Low", "Almost out"], CapsuleStyles.UsageLegend.Select(l => l.Label));
        Assert.Equal("Usage left", CapsuleStyles.UsageLeftLabel);
    }
}
