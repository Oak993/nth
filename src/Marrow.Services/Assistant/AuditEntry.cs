// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-3 (the line) and ED-NEW-dextra-as3-4 (what an input keeps).
using System.Globalization;

namespace Marrow.Services.Assistant;

/// <summary>
/// One action for the audit log (12.5): who acted (the assistant, the mission, the agent thread), what it did (the
/// tool, the action class, the target, the inputs), how it ended (the result, the cost) and what allowed it (a grant or
/// decision id). The time is not the caller's: <see cref="AuditLog"/> stamps it. <see cref="ActionClass"/> is the class's
/// name as AS-1's classifier gives it (12.1a), kept as text so the log reads the same whatever the enum becomes.
/// </summary>
public sealed record AuditEntry
{
    public required string Assistant { get; init; }

    public string? Mission { get; init; }

    /// <summary>The agent's chat thread.</summary>
    public string? Agent { get; init; }

    public required string Tool { get; init; }

    public required string ActionClass { get; init; }

    public string? Target { get; init; }

    public IReadOnlyList<AuditInput> Inputs { get; init; } = [];

    public string? Result { get; init; }

    public decimal? CostUsd { get; init; }

    public long? Tokens { get; init; }

    /// <summary>The grant or decision id that allowed the action (12.2: every audit entry that used a grant names it).</summary>
    public string? AllowedBy { get; init; }
}

/// <summary>What kind of value an <see cref="AuditInput"/> holds.</summary>
public enum AuditInputKind
{
    /// <summary>Text, masked for secrets before it is written (<see cref="AuditRedactor"/>).</summary>
    Text,

    /// <summary>Text the agent typed: only its length is kept (12.5).</summary>
    Typed,

    /// <summary>A secret: written as <see cref="AuditRedactor.Mask"/>'s placeholder, never as itself.</summary>
    Secret,

    Number,

    Flag,
}

/// <summary>
/// One named input of an action. A typed text keeps only its length from the moment it is made, so the text itself is
/// never held by the entry (12.5: "Typed text is logged as its length only"); a secret keeps nothing.
/// </summary>
public sealed record AuditInput
{
    private AuditInput(string name, AuditInputKind kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Kind = kind;
    }

    public string Name { get; }

    public AuditInputKind Kind { get; }

    /// <summary>The text of a <see cref="AuditInputKind.Text"/> input (masked when written).</summary>
    public string? Text { get; private init; }

    /// <summary>The length in characters (text elements) of a <see cref="AuditInputKind.Typed"/> input.</summary>
    public int TypedLength { get; private init; }

    public double Number { get; private init; }

    public bool Flag { get; private init; }

    public static AuditInput Plain(string name, string? text) => new(name, AuditInputKind.Text) { Text = text ?? "" };

    public static AuditInput Typed(string name, string? typedText) =>
        new(name, AuditInputKind.Typed) { TypedLength = new StringInfo(typedText ?? "").LengthInTextElements };

    public static AuditInput Secret(string name) => new(name, AuditInputKind.Secret);

    public static AuditInput Numeric(string name, double value) => new(name, AuditInputKind.Number) { Number = value };

    public static AuditInput Boolean(string name, bool value) => new(name, AuditInputKind.Flag) { Flag = value };
}
