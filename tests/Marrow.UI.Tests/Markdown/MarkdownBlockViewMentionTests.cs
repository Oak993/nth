using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e), :386-404, :489-504 (part 2 of the
// unit). Covers MentionGlyphOffset (PR-THEME-177), MentionGlyph (PR-THEME-177) and MentionText (PR-THEME-177,
// PR-THEME-178).
[Collection("Headless")]
public sealed class MarkdownBlockViewMentionTests(HeadlessSession headless)
{
    private static double InvokeMentionGlyphOffset(double size)
    {
        var method = typeof(MarkdownBlockView).GetMethod("MentionGlyphOffset", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (double)method.Invoke(null, [size])!;
    }

    private static StyledText InvokeMentionText(MarkdownBlockView view, HydraPersona persona)
    {
        var method = typeof(MarkdownBlockView).GetMethod("MentionText", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (StyledText)method.Invoke(view, [persona])!;
    }

    // SwiftColor has no value equality (Port/SwiftColor.cs is a plain class); resolve both sides to their
    // rendered Avalonia colour instead, the same way SwiftColor.Brush already does at every real call site.
    private static Color ResolvedColor(SwiftColor color)
    {
        var probe = new TextBlock();
        return ((ISolidColorBrush)color.Brush(probe)).Color;
    }

    // ---- :386 mentionGlyphOffset ----

    [Theory]
    [InlineData(16.0, -3.2)]
    [InlineData(10.0, -2.0)]
    [InlineData(0.0, 0.0)]
    [Trait("Req", "PR-THEME-177")] // DroppyCode/UI/Markdown/MarkdownView.swift:386
    public void MentionGlyphOffset_is_negative_two_tenths_of_size(double size, double expected)
    {
        Assert.Equal(expected, InvokeMentionGlyphOffset(size), 10);
    }

    // ---- :390-404 mentionGlyph ----

    [Fact]
    [Trait("Req", "PR-THEME-177")] // DroppyCode/UI/Markdown/MarkdownView.swift:390-404
    public void MentionGlyph_returns_the_same_cached_image_for_the_same_persona_and_size()
    {
        headless.OnUiThread(() =>
        {
            var persona = HydraRoster.Persona(0);
            var first = MarkdownBlockView.MentionGlyph(persona, 16);
            var second = MarkdownBlockView.MentionGlyph(persona, 16);
            Assert.Same(first, second); // :393 if let cached = glyphCache[key] { return cached }
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-177")] // DroppyCode/UI/Markdown/MarkdownView.swift:392
    public void MentionGlyph_caches_separately_per_size()
    {
        headless.OnUiThread(() =>
        {
            var persona = HydraRoster.Persona(1);
            var small = MarkdownBlockView.MentionGlyph(persona, 12);
            var large = MarkdownBlockView.MentionGlyph(persona, 20);
            Assert.NotSame(small, large);
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-177")] // DroppyCode/UI/Markdown/MarkdownView.swift:396-399
    public void MentionGlyph_is_rendered_at_the_requested_size()
    {
        headless.OnUiThread(() =>
        {
            var persona = HydraRoster.Persona(2);
            var image = MarkdownBlockView.MentionGlyph(persona, 16);
            var bitmap = Assert.IsType<Avalonia.Media.Imaging.RenderTargetBitmap>(image);
            Assert.Equal(16, bitmap.PixelSize.Width);
            Assert.Equal(16, bitmap.PixelSize.Height);
        });
    }

    // ---- :489-504 mentionText ----

    [Fact]
    [Trait("Req", "PR-THEME-177")] // DroppyCode/UI/Markdown/MarkdownView.swift:495-500
    public void MentionText_with_a_target_carries_a_link_the_persona_colour_and_bold()
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownBlockView(new MarkdownBlock.Rule());
            var persona = HydraRoster.Persona(3);
            var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
            MarkdownView.SetHydraMentionTargets(view, new ValueMap<string, HydraMentionTarget>(
                new System.Collections.Generic.Dictionary<string, HydraMentionTarget> { [persona.Name] = target }));

            var text = InvokeMentionText(view, persona);
            Assert.Equal(3, text.Runs.Count); // glyph, " ", name (:500 "\(glyph) \(Text(name))")
            var nameRun = text.Runs[2];
            Assert.Equal(persona.Name, nameRun.Text); // :496 AttributedString(persona.name), emitted verbatim
            Assert.Equal(target.Url, nameRun.Link); // :497 name.link = target.url
            Assert.Equal(SwiftFont.Weight.Bold, nameRun.Weight); // :499 .stronglyEmphasized -> bold (PR-THEME-131)
            Assert.Equal(ResolvedColor(HydraGlyph.PersonaColor(persona)), ResolvedColor(nameRun.Color!)); // :498
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-178")] // DroppyCode/UI/Markdown/MarkdownView.swift:502-503
    public void MentionText_with_no_target_is_bold_verbatim_text_with_no_link()
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownBlockView(new MarkdownBlock.Rule());
            var persona = HydraRoster.Persona(4);
            // No SetHydraMentionTargets call: the environment default is empty (ValueMap<string, HydraMentionTarget>.Empty).

            var text = InvokeMentionText(view, persona);
            Assert.Equal(3, text.Runs.Count);
            var nameRun = text.Runs[2];
            Assert.Equal(persona.Name, nameRun.Text); // :502 Text(verbatim: persona.name)
            Assert.Null(nameRun.Link); // no link and no custom head attribute
            Assert.Equal(SwiftFont.Weight.Bold, nameRun.Weight); // :502 .fontWeight(.bold)
            Assert.Equal(ResolvedColor(HydraGlyph.PersonaColor(persona)), ResolvedColor(nameRun.Color!)); // :502 .foregroundColor(persona.color)
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-178")] // DroppyCode/UI/Markdown/MarkdownView.swift:489-504
    public void MentionText_glyph_run_is_a_template_image_tinted_with_the_persona_colour()
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownBlockView(new MarkdownBlock.Rule());
            var persona = HydraRoster.Persona(5);

            var text = InvokeMentionText(view, persona);
            var glyphRun = text.Runs[0];
            Assert.True(glyphRun.IsTemplateImage); // :400 out.isTemplate = true
            Assert.NotNull(glyphRun.Image);
            Assert.Equal(InvokeMentionGlyphOffset(16), glyphRun.BaselineOffset); // :493
            Assert.Equal(ResolvedColor(HydraGlyph.PersonaColor(persona)), ResolvedColor(glyphRun.Color!)); // :492
            Assert.Equal(" ", text.Runs[1].Text); // :500 the space between glyph and name
        });
    }
}
