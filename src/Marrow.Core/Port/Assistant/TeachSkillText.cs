// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 5; unit AS-19; ED-NEW-dextra-as19-7). No Swift twin.
// The SKILL.md a draft becomes (Marrow.Services/Port/Skills/Custom stores it as an own skill; ProviderSkills then hands it to every
// provider). It carries the steps with every step that asks the owner marked in bold, the inputs, the decisions with their
// reasons, the obstacles and the approval boundary, in the owner's words. The marks are text for the agent that reads the skill;
// the guard itself stays in the policy engine, which asks about the real action whatever the skill says.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Port.Assistant;

public static class TeachSkillText
{
    /// <summary>The line above the boundary, and the one the agent is told about steps that ask.</summary>
    public const string BoundaryRule = "Every step below marked “asks you” needs the owner's go-ahead each time it runs: raise a decision card and wait for the answer. Never do such a step on your own, and never treat an earlier yes as covering it.";

    public const string YourselfRule = "A step marked “you do it yourself” is one nth never does for the owner (a password, a private window). Tell the owner to do it and wait until he says it is done.";

    /// <summary>The whole SKILL.md for a draft that can be saved (<see cref="TeachDraft.Problem"/> is null).</summary>
    public static string Skill(TeachDraft draft, DateTimeOffset taught)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var text = new StringBuilder();
        text.Append("---\nname: ").Append(draft.Name).Append('\n');
        text.Append("description: ").Append(JsonSerializer.Serialize(draft.Description)).Append("\n---\n\n");
        text.Append("# ").Append(Title(draft.Name)).Append("\n\n");
        text.Append("Taught by watching the owner work on ").Append(taught.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(". ");
        text.Append(draft.Steps.Count.ToString(CultureInfo.InvariantCulture)).Append(draft.Steps.Count == 1 ? " step. " : " steps. ").Append(BoundaryRule).Append("\n\n");

        if (draft.Parameters.Count > 0)
        {
            text.Append("## Inputs\n\n");
            foreach (var p in draft.Parameters)
                text.Append("- `{").Append(p.Name).Append("}`: the owner showed “").Append(p.Example).Append('”').Append(p.Where.Length > 0 ? ", used at " + p.Where : "").Append(". Ask for it when it is not given.\n");
            text.Append('\n');
        }

        text.Append("## Steps\n\n");
        foreach (var step in draft.Steps)
        {
            text.Append(step.Number.ToString(CultureInfo.InvariantCulture)).Append(". ");
            if (step.Guard is { } guard)
                text.Append(guard.OwnerDoesIt ? "**You do it yourself.** " : $"**Asks you ({guard.Word}).** ");
            else if (step.Flagged)
                text.Append("**Pauses for you.** ");
            text.Append(step.Text).Append('\n');
            if (step.Guard is { } why) text.Append("   - Why it asks: ").Append(why.Reason).Append('\n');
            if (!string.IsNullOrWhiteSpace(step.Said)) text.Append("   - The owner said: “").Append(step.Said).Append("”\n");
        }
        text.Append('\n');

        if (draft.Decisions.Count > 0)
        {
            text.Append("## Decisions\n\n");
            foreach (var d in draft.Decisions)
                text.Append("- ").Append(d.Text).Append(d.Reason.Length > 0 ? " (" + d.Reason + ")" : "").Append('\n');
            text.Append('\n');
        }

        if (draft.Obstacles.Count > 0)
        {
            text.Append("## Watch out for\n\n");
            foreach (var o in draft.Obstacles)
                text.Append("- ").Append(o.Text).Append(o.Fix.Length > 0 ? ". What worked: " + o.Fix : "").Append('\n');
            text.Append('\n');
        }

        text.Append("## Approval boundary\n\n");
        var boundary = draft.Boundary;
        if (boundary.Count == 0)
        {
            text.Append("No step asks the owner. The usual rules still apply to anything you do: the policy engine asks before sending, paying, deleting or installing, whatever this skill says.\n");
        }
        else
        {
            text.Append(BoundaryRule).Append("\n\n");
            foreach (var step in boundary)
                text.Append("- Step ").Append(step.Number.ToString(CultureInfo.InvariantCulture)).Append(": ")
                    .Append(step.Guard is { } g ? g.OwnerDoesIt ? "you do it yourself" : g.Word : "the owner asked to pause here").Append('\n');
            if (boundary.Any(s => s.Guard?.OwnerDoesIt == true)) text.Append('\n').Append(YourselfRule).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>"send-weekly-report" as "Send weekly report".</summary>
    public static string Title(string name)
    {
        var words = name.Replace('-', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
