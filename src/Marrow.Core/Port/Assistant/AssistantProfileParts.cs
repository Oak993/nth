// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1, 9.2; unit AS-16; ED-NEW-dextra-as16-2). No Swift twin.
// The smaller parts of an assistant's profile (9.1): a standing duty it was created with, its daily budget slice, the model it
// prefers, and a connection it wants. Plain records; the rules about them live in AsturDraft and AsturTeamRules.
namespace Marrow.Core.Port.Assistant;

/// <summary>
/// A standing duty a new Astur proposes at creation (9.2: "watch the numbers daily, keep a backlog, prepare the next item"). The owner
/// ticks the ones he wants on the profile card; AS-10's scheduler turns the kept ones into real duties. Until then it is a line on
/// the profile, and nothing runs.
/// </summary>
public sealed record DutyDraft(string Text, bool Keep = true);

/// <summary>
/// An Astur's budget slice (9.3: "Each Astur's budget is a slice it cannot exceed"): the most of any account's window it may use in a
/// day, in percent, and optionally the most money it may spend on an API key in a day. It becomes a standing per-assistant day cap
/// (13.2), which the cost guard enforces at the turn boundary.
/// </summary>
public sealed record AsturBudget(double DailyPercent, decimal? DailyDollars = null);

/// <summary>The provider, model and account an Astur's missions prefer (9.1 "default models"); the router tries them first and falls back.</summary>
public sealed record AsturModel(string ProviderId, string? Model = null, Guid? Account = null);

/// <summary>What kind of thing a connection is, for the icon on the card; never colour alone (the card also says the words).</summary>
public enum WantKind { Site, Folder, Login, Connector, Limit }

/// <summary>
/// A connection the new Astur wants, as the profile card lists it ("YouTube Studio · read · you sign in once"). It is a proposal: the
/// owner connects it and signs in himself; the profile's Connections (the MCP servers its missions may use) stay empty until he does.
/// </summary>
public sealed record AsturWant(WantKind Kind, string Text, bool NeedsLogin = false);
