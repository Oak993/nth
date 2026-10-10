// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). An ADDITIVE contract member: the one Settings › Ignis
// word the design draws (phoenix-designs.html `tr.now td:first-child::after{content:"in use"}`) that IgnisWords (U0) lacks.
namespace Marrow.Core.Port.Ignis;

public static class IgnisSettingsWords
{
    /// <summary>The chip on the Rebuild points row of the model the chat in front uses.</summary>
    public const string InUse = "in use";

    // ---- the idle-limit settings (ED-NEW-ignis-guards; Settings › Ignis and the Kindle popover, one wording) ----
    public const string RowCoolAfter = "Cool idle chats after";
    public const string RowCoolAfterDetail = "A chat left this long stops being kept warm. Your next message warms it again.";
    public const string RowLearnCooling = "Learn when to cool";
    public const string RowLearnCoolingDetail = "Cools a chat sooner, once it’s idle past the time you usually come back.";
    public const string Never = "Never";

    /// <summary>"12 hours", "1 hour", "Never".</summary>
    public static string CoolAfter(int? hours) => hours is { } h ? h.ToString(System.Globalization.CultureInfo.InvariantCulture) + (h == 1 ? " hour" : " hours") : Never;
}
