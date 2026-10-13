// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Reader and actor split"; unit AS-7; ED-NEW-dextra-as7-2). No Swift twin.
// What an agent of a mission may be asked to do at all. Research steps run in readers: they have read tools only, cannot
// act, and hand back structured summaries. Actors work from those summaries, and because the summaries are tainted,
// every side-effecting step an actor takes after reading one asks. The reviewer is a reader of the mission's own work.
// Pure: the engine applies the role before any grant or default.
namespace Marrow.Core.Port.Assistant;

/// <summary>An agent's role in its mission (12.4).</summary>
public enum AgentRole
{
    /// <summary>A lead, a head or a helper: acts within the policy.</summary>
    Actor,
    /// <summary>A research step: reads only, and reports a summary up.</summary>
    Reader,
    /// <summary>The mission's reviewer (3.4): reads only.</summary>
    Reviewer,
}

public static class AgentRoles
{
    /// <summary>The nth-dextra tools a reader may call: it reports, asks the owner, and reads nth's own state.</summary>
    public static IReadOnlySet<string> ReaderTools { get; } = new HashSet<string>(StringComparer.Ordinal) { "report", "ask_owner", "recall", "usage_status" };

    public const string ReadsOnly = "A reader only reads: it cannot act";

    /// <summary>Whether an agent of this role may be asked about one action part at all.</summary>
    public static bool Permits(AgentRole role, Classification leaf)
    {
        if (role == AgentRole.Actor) return true;
        if (leaf.Class is ActionClass.ReadLocal or ActionClass.ReadWeb) return true;
        // An nth-dextra tool is placed with its own name as target and field (ActionClassifier.ClassifyAssistantTool).
        return leaf.Class == ActionClass.Agents && leaf.Guard == AssistantToolGuard.None && leaf.Target is { } name
            && string.Equals(leaf.Field, name, StringComparison.Ordinal) && ReaderTools.Contains(name);
    }

    /// <summary>The role's word for a feed line or a card ("reader").</summary>
    public static string Word(AgentRole role) => role switch
    {
        AgentRole.Reader => "reader",
        AgentRole.Reviewer => "reviewer",
        _ => "actor",
    };
}
