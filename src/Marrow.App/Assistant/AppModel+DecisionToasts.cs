// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 13.1 "Windows toasts only when a decision is needed", "Toast buttons", 4.2 "Keyboard"; ED-NEW-dextra-as9-1, -3, -5).
// No Swift twin. The app's side of the decision toasts: the toasts of the running Dextra (bound when the controller is, let go when it is), what a
// press on one does (answer the card quietly, or bring nth forward on the card), and the request the Assistant home takes up to show a card.
using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App;

public sealed partial class AppModel
{
    private DecisionToasts? dextraToasts;
    private Guid? pendingAssistantReview;
    private Guid? pendingAssistantMission;
    private Lazy<DecisionToastLogo>? decisionLogo;

    /// <summary>The decision toasts of the running controller, or null.</summary>
    internal DecisionToasts? AssistantToasts => dextraToasts;

    /// <summary>A test's channel in place of the Windows one (decision toasts then go nowhere near a real toast center).</summary>
    internal NotificationChannel? DecisionChannelOverride { get; set; }

    /// <summary>A test's picture files in place of the app's own folder.</summary>
    internal Func<DecisionToastLogo>? DecisionLogoFactory { get; set; }

    /// <summary>The card a toast asked to see, until the home shows it (or null).</summary>
    public Guid? PendingAssistantReview => pendingAssistantReview;

    /// <summary>A toast asked for a card while the home is already built: it shows the card now.</summary>
    public event Action<Guid>? AssistantReviewRequested;

    /// <summary>The mission a duty's toast asked to see, until the home shows it (or null).</summary>
    public Guid? PendingAssistantMission => pendingAssistantMission;

    /// <summary>A duty's toast asked for a mission while the home is already built: it shows the mission now.</summary>
    public event Action<Guid>? AssistantMissionRequested;

    /// <summary>
    /// Binds the toasts to a controller's queue. <paramref name="nameOf"/> is the asking assistant's name by id. The toast shows only while
    /// the home is not in front (<see cref="ToastNotifications.IsSurfaceOnScreen"/>) and only for a card that needs the owner.
    /// </summary>
    internal void BindDecisionToasts(AssistantController controller, Func<string, string> nameOf, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(nameOf);
        UnbindDecisionToasts();
        decisionLogo = new Lazy<DecisionToastLogo>(DecisionLogoFactory ?? DecisionToastLogo.Production);
        // The picture is written only for the real Windows toast center: a recording center (a test's) or none gets the text alone.
        var channel = DecisionChannelOverride ?? new WindowsNotificationChannel(
            logo: () => ToastNotifications.Center.Current is WindowsToastCenter ? decisionLogo.Value.Path() : null,
            requestPermission: RequestNotificationPermission,
            newsLogo: () => ToastNotifications.Center.Current is WindowsToastCenter ? decisionLogo.Value.NewsPath() : null);
        var toasts = new DecisionToasts(controller.Queue, channel, new DecisionToastOptions
        {
            NameOf = nameOf,
            HomeOnScreen = () => ToastNotifications.IsSurfaceOnScreen(this, AppSurface.AssistantHome),
            Time = time ?? SwiftClock.Provider.Current,
        });
        toasts.Faulted += message => controller.Runner.ReportFault(message);
        dextraToasts = toasts;
    }

    internal void UnbindDecisionToasts()
    {
        dextraToasts?.Dispose();
        dextraToasts = null;
        decisionLogo = null;
    }

    /// <summary>
    /// A mission chat's decisions are cards in Dextra's queue and make their toast from there; the chat's own "Waiting for your decision." toast
    /// would say it twice, and its turn ending is a status, which never pushes (13.1).
    /// </summary>
    private bool DecisionToastCovers(ChatThread thread) => Dextra is not null && thread.MissionID is not null;

    /// <summary>
    /// A press on a decision toast (AppDelegate calls it for the activation, on the main thread). An answer the toast really carried is given
    /// quietly; everything else leaves the card to be shown (<see cref="ReviewAssistantDecision"/>, by the caller, after nth is brought forward).
    /// </summary>
    internal DecisionToastOutcome DecisionToastActivated(Guid decision, string? nonce, NoticeAction action) =>
        dextraToasts?.Activate(decision, nonce, action) ?? DecisionToastOutcome.Opened;

    /// <summary>
    /// Shows a decision card: the home opens in the main area and its Decisions tab takes the card in front (a built home takes it at once; one
    /// that is made after this takes it when it is built).
    /// </summary>
    public void ReviewAssistantDecision(Guid decision)
    {
        pendingAssistantReview = decision;
        ShowAssistantHome();
        AssistantReviewRequested?.Invoke(decision);
    }

    /// <summary>The home took the card up: the request is done.</summary>
    public void TakeAssistantReview() => pendingAssistantReview = null;

    /// <summary>
    /// A duty with the report style Notify me found something (13.1 "Duty opt-in"; DutyHost calls it when a run ends). The toast follows the same rules
    /// as a decision's: gathered, spaced, and not shown while the home is in front. With Dextra off there is no toast to make.
    /// </summary>
    internal void NotifyDutyFinding(DutyFinding finding) => dextraToasts?.NotifyDuty(finding);

    /// <summary>
    /// Shows a mission (a duty toast's Open or body): the home opens in the main area on its Missions tab, with the mission's card brought into view
    /// (a built home does it at once; one that is made after this does it when it is built).
    /// </summary>
    public void ReviewAssistantMission(Guid mission)
    {
        pendingAssistantMission = mission;
        ShowAssistantHome();
        AssistantMissionRequested?.Invoke(mission);
    }

    /// <summary>The home took the mission up: the request is done.</summary>
    public void TakeAssistantMission() => pendingAssistantMission = null;
}
