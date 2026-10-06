using System.Text;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (OD-0118): the name and one-line description of a skill another tool wrote, read without ever throwing.
public sealed class ForeignFrontmatterTests
{
    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plain_front_matter_gives_its_name_description_and_body()
    {
        var parsed = ForeignFrontmatter.Parse("---\nname: pdf-tools\ndescription: Work with PDF files.\n---\nHello\n");
        Assert.Equal(("pdf-tools", "Work with PDF files.", true), (parsed.Name, parsed.Description, parsed.HasFrontmatter));
        Assert.Equal("Hello\n", parsed.Body);
    }

    [Theory]
    [InlineData("---\r\nname: a\r\ndescription: B c\r\n---\r\nbody", "a", "B c")]
    [InlineData("﻿---\nname: \"a\"\ndescription: 'It''s B'\n---\n", "a", "It's B")]
    [InlineData("---\nname: a\ndescription: >\n  folded over\n  two lines\n---\n", "a", "folded over two lines")]
    [InlineData("---\nname: a\ndescription: |-\n  literal\n  lines\n---\n", "a", "literal lines")]
    [InlineData("---\nname: a # a comment\ndescription: B # not part\n---\n", "a", "B")]
    [InlineData("---\nname: a\nallowed-tools: [Read, Grep]\nmetadata:\n  version: 1\ndescription: With other keys\n---\n", "a", "With other keys")]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_usual_shapes_other_tools_write_are_understood(string text, string name, string description)
    {
        var parsed = ForeignFrontmatter.Parse(text);
        Assert.Equal((name, description), (parsed.Name, parsed.Description));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no front matter at all\njust text")]
    [InlineData("---\nname: a\ndescription: never closed\nbody")]
    [InlineData("---\n: broken\n\u0000\u0001 \n---\n")]
    [InlineData("---\nname: [unclosed\ndescription: &anchor x\n---\n")]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Broken_text_never_throws_and_gives_nothing_it_cannot_stand_behind(string text)
    {
        var parsed = ForeignFrontmatter.Parse(text);
        Assert.Null(parsed.Description);
        if (!parsed.HasFrontmatter) Assert.Null(parsed.Name);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_description_is_one_line_with_no_control_characters()
    {
        var parsed = ForeignFrontmatter.Parse("---\nname: a\ndescription: >\n  first\n\n  second\u0007 bell\n---\n");
        Assert.DoesNotContain(parsed.Description!, char.IsControl);
        Assert.Equal("first second bell", parsed.Description);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void WithName_replaces_the_name_and_leaves_everything_else_byte_for_byte()
    {
        var text = "---\nname: old\nallowed-tools: [Read]\ndescription: Keep me\n---\nBody é\n";
        var changed = ForeignFrontmatter.WithName(text, "new-name");
        Assert.Equal("---\nname: new-name\nallowed-tools: [Read]\ndescription: Keep me\n---\nBody é\n", changed);
        Assert.Equal("new-name", ForeignFrontmatter.Parse(changed).Name);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void WithName_adds_a_missing_name_and_keeps_crlf_text_crlf()
    {
        var added = ForeignFrontmatter.WithName("---\r\ndescription: D\r\n---\r\nB\r\n", "x");
        Assert.Equal("---\r\nname: x\r\ndescription: D\r\n---\r\nB\r\n", added);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Compose_makes_a_skill_the_strict_reader_accepts()
    {
        var text = ForeignFrontmatter.Compose("rule-one", "Use for tests", "Rule body\nline two");
        var strict = Marrow.Services.Port.Skills.Generations.SkillFrontmatter.Disclosure(text);
        Assert.Equal(("rule-one", "Use for tests"), (strict.Name, strict.Description));
        Assert.EndsWith("Rule body\nline two\n", text, StringComparison.Ordinal);
        Assert.Equal(text, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)));
    }
}
