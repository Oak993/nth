// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 13.1 "through AppModel.Notify's pipeline, extended with a non-thread payload (ToastRequest kind
// AssistantDecision, target decision id)"; 2.5 size ladder; ED-NEW-dextra-as9-4, -5). No Swift twin.
// A decision notice as the request the Windows toast center takes. Its userInfo carries the target decision, the button that was pressed and the
// nonce of this toast; the toast's own click is the card's last button (Open or Review), and each button launches nth with its own userInfo.
using Marrow.App.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>The keys of a decision toast's userInfo (the protocol launch carries them back to nth).</summary>
public static class DecisionToastKeys
{
    public const string Decision = "assistantDecision";

    public const string Action = "action";

    public const string Nonce = "nonce";
}

public static class DecisionToastRequests
{
    /// <summary>The toast's tag: one per target decision, so a toast shown again replaces its own and a withdrawal finds it.</summary>
    public static string Identifier(Guid target) => "dextra-decision-" + target.ToString("N");

    public static ToastRequest Build(DecisionNotice notice, string? logo)
    {
        ArgumentNullException.ThrowIfNull(notice);
        // the body click is the last button: Open for a reversible card, Review for every other
        var click = notice.Actions.Count > 0 ? notice.Actions[^1] : NoticeAction.Review;
        return new ToastRequest(Identifier(notice.Target), notice.Title, notice.Body, Sound: true, UserInfo(notice, click))
        {
            Kind = ToastKind.AssistantDecision,
            Decision = notice.Target,
            Buttons = [.. notice.Actions.Select(action => new ToastButton(DecisionNoticeWords.Label(action), UserInfo(notice, action)))],
            Attribution = AssistantNames.Assistant,
            Logo = logo,
        };
    }

    private static Dictionary<string, object> UserInfo(DecisionNotice notice, NoticeAction action) => new(StringComparer.Ordinal)
    {
        [DecisionToastKeys.Decision] = notice.Target.ToString("D"),
        [DecisionToastKeys.Action] = DecisionNoticeWords.Key(action),
        [DecisionToastKeys.Nonce] = notice.Nonce,
    };
}
