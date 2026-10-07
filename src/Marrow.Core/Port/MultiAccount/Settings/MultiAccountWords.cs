// PORT-ONLY: T11 step 2 (OD-0114..OD-0117). The words of Settings > Models (accounts, "When an account runs out", Handshake) and Settings > Pills, exactly as the owner-approved page
// porting/first-release/design-src/multi-account/multi-account.html writes them. The three strings of the disabled Pills page (OD-0117) are the only ones the page does not have: ENGINEERING DECISION
// (ED-449), written in the page's voice from the owner's description.
namespace Marrow.Core.Port.MultiAccount.Settings;

public static class MultiAccountWords
{
    // ---- Models: Accounts
    public const string AccountsHeading = "Accounts";
    public const string AccountsFoot = "Marrow uses your accounts from the top down. Drag to change the order. Each account’s ⋯ menu has its own options.";
    public const string AddAnother = "Add another account";
    public const string UsageDetails = "Usage details";
    public const string SignOut = "Sign out";
    public const string SignOutOfThis = "Sign out of this account";
    public const string Rename = "Rename";
    public const string MoveUp = "Move up";
    public const string MoveToTop = "Move to top";
    public const string MoveDown = "Move down";
    public const string HandshakeAt = "Handshake at…";
    public const string LikeAll = "like all";
    public const string CustomName = "custom name";
    public const string OffNotUsed = "Off · not used";
    public const string NoLimit = "No limit · Stays on this PC";
    public const string NoFiveHour = "No 5-hour limit";
    public const string UsesDefault = "uses default";
    public static string AccountNameLabel => "Account name, up to 24 characters";
    public static string Count(int used) => used + "/24";
    public static string One(string? plan) => plan is { Length: > 0 } ? "Signed in · " + plan + " plan" : "Signed in";
    public static string Several(int count) => count + " accounts · the same order as the composer’s popover";
    public static string PlanText(string? plan) => plan is { Length: > 0 } ? plan + " plan" : "Account";
    public static string OutUntil(string reset) => "out until " + reset;
    public static string WeekText(int percent, string reset) => "Week " + percent + "% · resets " + reset;

    // ---- Models: OpenRouter keys and the Local block (page strings where the page has them; the rest lead-proposed, ED-449)
    public const string KeysAndOrder = "Keys and order";
    public static string Keys(int count, double? credits) => count + (count == 1 ? " key" : " keys") + (credits is { } left ? " \u00b7 $" + left.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " of credits left" : "");
    public const string LocalTitle = "Local";
    public const string LocalNone = "No local server found yet";
    public static string LocalCount(int count) => count + (count == 1 ? " local model" : " local models");
    public const string AddLocal = "Add a local server";
    public const string LocalAddress = "Local server address";
    public const string LocalPlaceholder = "http://localhost:1234 \u00b7 or another PC, like http://192.168.1.20:11434";
    public const string LocalConnect = "Connect";
    public const string LocalLooking = "Looking for a server\u2026";
    public const string LocalNote = "Local models run on your PC: nothing leaves it, and there are no limits. They are slower than the big cloud models, and less able at long tasks.";
    public static string LocalFoundOn(string where, int models, Marrow.Core.Port.Local.LocalServerKind kind = Marrow.Core.Port.Local.LocalServerKind.Unknown) =>
        "Found " + (kind switch { Marrow.Core.Port.Local.LocalServerKind.Ollama => "Ollama", Marrow.Core.Port.Local.LocalServerKind.LMStudio => "LM Studio", _ => "a server" })
        + " on " + where + " \u00b7 " + models + (models == 1 ? " model" : " models") + " added";
    public static string LocalNotFound(string where) => "No server found at " + where + ". Check the address and that the server is running.";
    public const string LocalBadAddress = "Use an address like http://localhost:1234";

    // ---- Models: "Show an example" (the page's sheet, in its words)
    public const string HandedFullIntro = "The full conversation, word for word.";
    public static string HandedSummaryIntro(string to) => "Marrow\u2019s summary, made without AI. Nothing is deleted: the full conversation is saved as a handoff file, and the summary tells " + to + " where it is.";
    public const string HandedAiNote = "AI summary \u00b7 written by the outgoing account during the Handshake: the goal, why things were decided, what is next. Marrow\u2019s summary and the full conversation file come with it.";
    public const string HandedFileLine = "the full conversation, for any detail";
    public static string HandedHandshakeLine(string to) => "Handshake \u00b7 a briefing from the last account, read back by " + to + " and checked by Marrow";
    public const string HandedClose = "Close";
    public const string HandedNext = "your next account";

    // ---- Models: When an account runs out
    public const string RunHeading = "When an account runs out";
    public const string RunFoot = "Your conversation goes with you: when Marrow moves you to another account, that account’s service can see it. Marrow never moves you to another service unless you say so, or a pill does.";
    public const string RunTitle = "When an account runs out";
    public const string RunNone = "Ask me the first time";
    public const string MidTitle = "If a reply is still being written";
    public const string BackTitle = "When an earlier account is back";
    public const string OtherTitle = "When every account of a service is out";
    public const string BringTitle = "What the next account gets";
    public const string HandedTitle = "What was handed over";
    public const string HandedButton = "Show an example";
    public const string HandedDescription = "Each time Marrow moves you, the chat shows what the next account got.";
    public const string Recommended = "Recommended";

    public const string RunGo = "Keep me going";
    public const string RunAsk = "Ask me each time";
    public const string RunWait = "Just wait";
    public const string MidFinish = "Finish it";
    public const string MidStop = "Stop and wait for me";
    public const string BackGo = "Go back to it";
    public const string BackStay = "Stay where I am";
    public const string OtherAsk = "Ask me";
    public const string OtherSwitch = "Use my next service";
    public const string OtherWait = "Wait";
    public const string BringCompact = "Marrow’s summary";
    public const string BringFull = "The whole conversation";
    public const string BringAi = "A summary by AI";

    public const string RunNoneDescription = "You haven’t chosen yet. Marrow will ask you the first time it happens.";
    public const string RunGoDescription = "Marrow moves you to your next account, and the work carries on.";
    public const string RunAskDescription = "Marrow stops and asks you what to do.";
    public const string RunWaitDescription = "Marrow waits until the account is back.";
    public const string MidFinishDescription = "Your next account finishes the reply. A step that was half done may run again.";
    public const string MidStopDescription = "The reply stops where it is. Your next message goes to the next account.";
    public const string BackGoDescription = "When an account you were using earlier is back, Marrow returns to it.";
    public const string BackStayDescription = "Marrow stays on the account it moved to, until that one runs out too.";
    public const string OtherAskDescription = "Marrow asks before it uses another service, such as Codex instead of Claude.";
    public const string OtherSwitchDescription = "Marrow goes on with your next service. That service can see the conversation.";
    public const string OtherWaitDescription = "Marrow waits until one of these accounts is back.";
    public const string BringCompactDescription = "Marrow writes it right away on your PC. Your messages and every reply stay word for word, and the full conversation goes with it.";
    public const string BringFullDescription = "Everything, word for word. It uses more of the next account’s limit.";
    public const string BringAiDescription = "The account that is running out writes a short summary during the Handshake.";

    // ---- Models: Handshake
    public const string HandshakeHeading = "Handshake";
    public const string HandshakeFoot = "Each account can have its own moment: open its ⋯ menu and choose Handshake at.";
    public const string UseHandshake = "Use the Handshake";
    public const string UseHandshakeOn = "Shortly before an account runs out, it briefs the next one, so the work carries on without a gap.";
    public const string UseHandshakeOff = "Off. The next account starts from Marrow’s summary alone.";
    public const string WhenToStart = "When to start";
    public const string WhenSmart = "Automatically";
    public const string WhenSmartDescription = "Marrow picks the right moment for each plan.";
    public static string WhenAt(int percent) => "At " + percent + "%";
    public static string WhenAtDescription(int percent) => "When an account has used " + percent + "% of its limit.";
    public static string LikeAllAccounts(string defaultLabel) => "Like all accounts (" + defaultLabel + ")";
    public static string Near(int percent) => "near " + percent + "% on this plan";
    public static string DefaultLabel(string text) => text;

    // ---- Pills
    public const string PillsTitle = "Pills";
    public const string PillsHeading = "Your pills";
    public const string PillsFoot = "A pill is your own order of accounts and services, used one after another. Near each limit, a Handshake moves the thread to the next step, without asking. When the last step runs out, the pill starts again from the first step that is available.";
    public const string NewPill = "New pill…";
    public const string UseForNew = "Use for new threads";
    public const string UseNone = "None";
    public const string UseNoneDescription = "New threads use your accounts in their order.";
    public const string UsePillDescription = "New threads start on this pill. You can change it for a thread in the composer.";
    public const string Edit = "Edit";
    public const string Duplicate = "Duplicate";
    public const string Delete = "Delete";
    public const string Cancel = "Cancel";
    public const string UntitledPill = "Untitled pill";
    public const string DefaultPillName = "New pill";
    public const string PillNameLabel = "Pill name";
    public static string ConfirmDelete(string name) => "Delete “" + name + "”? Threads on it go back to your accounts in order.";
    public static string PillSteps(int steps, int services) => steps + " steps · " + services + (services > 1 ? " services" : " service");

    // ---- Pills when fewer than two accounts or services are connected (OD-0117; ENGINEERING DECISION wording)
    public const string DisabledWhat = "A pill is a chain of accounts and models that Marrow moves through when one runs out.";
    public const string DisabledNeed = "Pills turn on once a second account or service is connected.";
    public const string DisabledButton = "Models";

    // ---- the pill editor
    public const string EditorTitle = "Pill editor";
    public const string EditorSub = "Steps run in order. Near each limit, a Handshake moves the thread to the next step.";
    public const string PillNameInput = "Pill name, up to 24 characters";
    public const string HandshakePoint = "Handshake point";
    public const string SameForEvery = "Same for every step";
    public const string EachOnItsOwn = "Set each step on its own";
    public const string SameDescription = "One slider moves the Handshake for every step of this pill.";
    public const string EachDescription = "Each step keeps its own Handshake point, set on its row.";
    public const string EveryStep = "Every step";
    public const string AddStep = "Add a step";
    public const string NewService = "New service";
    public const string SkippedOff = "Skipped · off";
    public const string SameAsAccount = "Same as the account · ";
    public static string StepTitle(string account) => "Step: " + account;
    public const string StepSub = "Saved with the pill";
    public const string PillNoLimit = "No limit";
    public const string NeverRunsOut = "Never runs out";
    public const string LoopLine = "When the last step runs out, the pill starts again from the first step that is available.";
    public const string EarlierTitle = "When an earlier account is back";
    public const string EarlierSettings = "Like in Settings";
    public const string EarlierGo = "Go back to it";
    public const string EarlierStay = "Stay where I am";
    public const string EarlierSettingsDescription = "This pill does what Settings › Models says.";
    public const string EarlierGoDescription = "The pill returns to the earlier step as soon as it has room.";
    public const string EarlierStayDescription = "The pill stays on its current step until that one runs out.";
    public static string StepCount(int steps, int services) => steps + " steps · " + services + (services > 1 ? " services" : " service");
    public static string StepStatement(int number, string name, string model) => "Step " + number + ": " + name + ", " + model + ".";
    public static string HandshakeText(string text) => "Handshake " + text;
}
