// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2, 6, 13.2; unit AS-11; ED-NEW-dextra-as11-9). No Swift twin.
// What the app gives the digest beside the things the controller and the scheduler already hold: where the activity log's days end, what the delegate answered
// and what each Astur has to say (those two belong to units not built yet, so they default to none), which sign-ins expired, the owner's chats as habits, the
// projects with when he last used them, the cheap model that rewords the Suggestions lane (none until one is bound: the lines nth wrote stay), and the notes' folding.
// Every delegate runs on the app's main thread unless it says otherwise; each is optional.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class DigestSetup
{
    /// <summary>The id of Dextra itself, who owns the Morning digest duty. Null: the first assistant the store lists.</summary>
    public Func<string?>? MainAssistantId { get; init; }

    /// <summary>The final HMAC of a day of the activity log (AuditVerifier.DayFinal), or null when the day has no line. Runs off the main thread (it reads the day's file).</summary>
    public Func<DateOnly, string?>? DayFinal { get; init; }

    /// <summary>Questions the delegate answered for the owner since the last digest (AS-22). None until it exists.</summary>
    public Func<IReadOnlyList<DigestAnswered>>? Answered { get; init; }

    /// <summary>Each Astur's paragraph (AS-16). None until Asturs exist.</summary>
    public Func<IReadOnlyList<DigestAstur>>? Asturs { get; init; }

    /// <summary>The names of accounts the owner has on whose sign-in expired.</summary>
    public Func<IReadOnlyList<string>>? ExpiredLogins { get; init; }

    /// <summary>The owner's chats read as habits for the spotter (hidden chats left out, local and private ones marked local).</summary>
    public Func<IReadOnlyList<ChatHabit>>? Habits { get; init; }

    /// <summary>The projects with when the owner last used each; where an unused allowance is put to work. Null: the controller's projects, none used lately.</summary>
    public Func<IReadOnlyList<SpotProject>>? Projects { get; init; }

    /// <summary>The cheap model that rewords the Suggestions lane. Null: the lines nth wrote stay. It must keep to 5.4: only a model on this PC reads a local-only batch.</summary>
    public ISuggestionModel? Rewriter { get; init; }

    /// <summary>The longest the rewriting model is given before the digest goes without its wording.</summary>
    public TimeSpan ModelTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Folds the notes once a day (8.2: the digest calls it each morning). Runs off the main thread.</summary>
    public Action? FoldNotes { get; init; }

    /// <summary>The PC's time zone; null: the system's.</summary>
    public Func<TimeZoneInfo>? Zone { get; init; }

    /// <summary>The name the owner knows an assistant by.</summary>
    public Func<string, string>? AssistantName { get; init; }

    /// <summary>The provider and model a mission the owner accepted from the lane runs on when the router has none to pick.</summary>
    public Marrow.Core.Models.ProviderKind Provider { get; init; } = Marrow.Core.Models.ProviderKind.Claude;

    public string? MissionModel { get; init; }
}
