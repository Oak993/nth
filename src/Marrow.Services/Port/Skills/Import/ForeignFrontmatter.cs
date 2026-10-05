// PORT-ONLY: first-release feature unit SK-18 (OD-0118). A tolerant reader for the front matter of skills other tools wrote. SkillFrontmatter (SK-9) fails closed on anything it does not know, which is right for
// what Marrow hands out; here a skill from another app is read to be LISTED, so a file that cannot be understood is reported and left out, never an exception.
namespace Marrow.Services.Port.Skills.Import;

public static class ForeignFrontmatter
{
    public sealed record Parsed(string? Name, string? Description, string Body, bool HasFrontmatter);

    /// <summary>Reads <c>name</c> and <c>description</c> from a leading <c>---</c> block. Never throws.</summary>
    public static Parsed Parse(string text) => throw new NotImplementedException();

    /// <summary>The same text with the front matter's <c>name</c> set to <paramref name="name"/> (added when missing); the body is untouched.</summary>
    public static string WithName(string text, string name) => throw new NotImplementedException();

    /// <summary>The text of a Marrow-made SKILL.md.</summary>
    public static string Compose(string name, string description, string body) => throw new NotImplementedException();
}
