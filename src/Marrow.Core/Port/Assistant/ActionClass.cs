// PORT-ONLY: Dextra, nth's always-on assistant (DESIGN.md rev 5, 12.1; unit AS-1; ED-NEW-dextracore-1). No Swift twin.
// The action classes and the owner's 2026-10-08 choices as the defaults. Pure: no I/O, no clock, no model.
namespace Marrow.Core.Port.Assistant;

/// <summary>What an action does, as the policy engine sees it (12.1). Unknown is anything the classifier cannot place.</summary>
public enum ActionClass
{
    /// <summary>Start, stop or redirect sub-agents within budget.</summary>
    Agents,
    /// <summary>Change files inside the mission worktree or the access folders (git keeps it reversible).</summary>
    Work,
    /// <summary>The project's own build and test commands, from its allowlist (12.1a).</summary>
    Run,
    /// <summary>Read-only research: no logins, forms or purchases.</summary>
    ReadWeb,
    /// <summary>Read files in the access folders and the assistant's own folder.</summary>
    ReadLocal,
    /// <summary>Sign in to a site or connector.</summary>
    Login,
    /// <summary>Files outside the access folders; app settings.</summary>
    WriteElsewhere,
    /// <summary>Drafts, calendar holds, sheet edits through a connector.</summary>
    ConnectorWrite,
    /// <summary>Send, post, email, share, reply.</summary>
    Outward,
    /// <summary>Book, accept, agree, or "I'm on my way".</summary>
    Commitment,
    /// <summary>Delete outside git, overwrite unversioned files, uninstall.</summary>
    Destructive,
    /// <summary>Install software or extensions.</summary>
    Install,
    /// <summary>Purchase, subscribe, transfer (12.7).</summary>
    Money,
    /// <summary>Address, phone, IDs leaving the PC.</summary>
    PersonalDataOut,
    /// <summary>Password managers, security settings, UAC, nth's own grants, credential fields.</summary>
    HardDeny,
    /// <summary>A command or tool the classifier cannot place: asks, whatever the grants (12.1a).</summary>
    Unknown,
}

/// <summary>The owner's default for a class, from the most permissive to the strictest.</summary>
public enum ClassDefault
{
    /// <summary>Done alone.</summary>
    Alone,
    /// <summary>Asks; a structured grant can cover it.</summary>
    AsksGrantPossible,
    /// <summary>Asks every time; no grant can cover it.</summary>
    AsksEveryTime,
    /// <summary>Always confirmed (the irreversible classes); no grant can cover it.</summary>
    AlwaysConfirmed,
    /// <summary>Never done.</summary>
    Never,
}

/// <summary>What a kind of money movement is. Only a purchase can ever be paid by nth (12.7).</summary>
public enum MoneyKind { None, Purchase, Subscription, Transfer, ToPerson }

/// <summary>The class table of 12.1 in code.</summary>
public static class ActionClasses
{
    /// <summary>Every class, in the order of the 12.1 table.</summary>
    public static IReadOnlyList<ActionClass> All { get; } = Enum.GetValues<ActionClass>();

    /// <summary>The owner's 2026-10-08 choice for each class (12.1).</summary>
    public static ClassDefault DefaultOf(ActionClass actionClass) => actionClass switch
    {
        ActionClass.Agents or ActionClass.Work or ActionClass.Run or ActionClass.ReadWeb or ActionClass.ReadLocal => ClassDefault.Alone,
        ActionClass.Login or ActionClass.WriteElsewhere or ActionClass.ConnectorWrite => ClassDefault.AsksGrantPossible,
        ActionClass.Outward or ActionClass.Commitment or ActionClass.Destructive or ActionClass.Install or ActionClass.Money
            or ActionClass.PersonalDataOut => ClassDefault.AlwaysConfirmed,
        ActionClass.HardDeny => ClassDefault.Never,
        ActionClass.Unknown => ClassDefault.AsksEveryTime,
        _ => throw new ArgumentOutOfRangeException(nameof(actionClass)),
    };

    /// <summary>Only the classes marked "grant possible" can be covered by a grant (12.2).</summary>
    public static bool IsGrantable(ActionClass actionClass) => DefaultOf(actionClass) == ClassDefault.AsksGrantPossible;

    /// <summary>The irreversible classes: always confirmed, and no grant, allowlist or rule can cover them.</summary>
    public static bool IsIrreversible(ActionClass actionClass) => DefaultOf(actionClass) == ClassDefault.AlwaysConfirmed;

    /// <summary>The stricter of two defaults.</summary>
    public static ClassDefault Stricter(ClassDefault a, ClassDefault b) => (ClassDefault)Math.Max((int)a, (int)b);

    /// <summary>
    /// How strict a class is, for showing the one class of a command made of several parts: hard deny first, then
    /// the irreversible classes, then Unknown, then the grant classes, then what runs alone. The engine itself decides
    /// each part on its own and keeps the strictest verdict; this order only names the result.
    /// </summary>
    public static int Strictness(ActionClass actionClass) => actionClass switch
    {
        ActionClass.HardDeny => 100,
        ActionClass.Money => 90,
        ActionClass.Outward => 86,
        ActionClass.PersonalDataOut => 85,
        ActionClass.Commitment => 84,
        ActionClass.Destructive => 83,
        ActionClass.Install => 82,
        ActionClass.Unknown => 70,
        ActionClass.Login => 60,
        ActionClass.WriteElsewhere => 56,
        ActionClass.ConnectorWrite => 55,
        ActionClass.Run => 40,
        ActionClass.ReadWeb => 30,
        ActionClass.Agents => 20,
        ActionClass.Work => 15,
        ActionClass.ReadLocal => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(actionClass)),
    };

    /// <summary>The rule line a decision card shows ("which rule sent it to the owner", 4.2).</summary>
    public static string RuleText(ActionClass actionClass) => actionClass switch
    {
        ActionClass.Outward => "Always confirmed: sending",
        ActionClass.Commitment => "Always confirmed: booking or agreeing",
        ActionClass.Destructive => "Always confirmed: deleting",
        ActionClass.Install => "Always confirmed: installing",
        ActionClass.Money => "Always confirmed: paying, then Windows Hello",
        ActionClass.PersonalDataOut => "Always confirmed: personal data leaving the PC",
        ActionClass.HardDeny => "Never allowed",
        ActionClass.Unknown => "Asks: not recognised",
        ActionClass.Login => "Asks: signing in",
        ActionClass.WriteElsewhere => "Asks: writing outside access folders",
        ActionClass.ConnectorWrite => "Asks: connector writes",
        _ => "Does on its own",
    };
}
