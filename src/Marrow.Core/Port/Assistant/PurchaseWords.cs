// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7; the canvas's section 9 "Purchases", work/design/assistant/index.html
// lines 948-993 and 3174-3285; ED-NEW-dextra-as20-6). No Swift twin.
// Every word purchases show, in one place: the card is only ever "•••• 4242" (its last 4 digits; no expiry, brand or name)
// and what a model reads names it only as the placeholder {card}. Words that go to a model never carry a digit of the card.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public static class PurchaseWords
{
    /// <summary>What Dextra, every Astur and every Hydra head see in place of the card (12.7, Muse's placeholder pattern).</summary>
    public const string Placeholder = "{card}";

    // Settings › Dextra › Purchases
    public const string Section = "Purchases";
    public const string Switch = "Let Dextra buy things";
    public const string NoCard = "No card";
    public const string NoCardYet = "No card yet";
    public const string OnlyLastFour = "Only the last 4 digits are ever shown";
    public const string Kept = "Kept by Windows for your account. No reveal; to change it, add it again.";
    public const string AddCard = "Add card";
    public const string Replace = "Replace";
    public const string Remove = "Remove";
    public const string PerPurchase = "Per purchase";
    public const string PerDay = "Per day";
    public const string PerMonth = "Per month";
    public const string LimitsNote = "In the card's currency. A new limit counts only after Windows Hello.";
    public const string AsturNote = "Per " + AssistantNames.Astur + ", per month. At $0 an " + AssistantNames.Astur + " may find and fill a cart, then hands the payment to you.";
    public const string Help = "Use a virtual card from your bank or card app, with its own limit set at the bank. Single-use or merchant-locked is best if your bank offers it. No extra service is needed.";
    public const string History = "History";
    public const string NoHistory = "No purchases yet";
    public const string NoHistoryDetail = "Each one lands here: shop, amount, date and mission. Never card data.";
    public const string NeverHint = "Never allowed: subscriptions, transfers and money to people. Three failed Windows Hello checks turn purchases off until you turn them on again.";
    public const string HandOverLead = "Dextra finds, compares and fills the cart, then hands the payment to you with its summary card. You pay yourself.";
    public const string HandOverWhy = "Paying for you needs nth's own browser or PC control to fill the shop's card field, and this build has neither yet.";
    public const string NotRunning = "Purchases are set up while Dextra is on.";
    public const string NewerFile = "Purchases were last set by a newer nth. They stay off here, and nothing is changed.";
    public const string UnreadableFile = "nth cannot read its purchases file. Purchases stay off, and the file is left as it is.";

    // the add-card sheet
    public const string AddTitle = "Add a card";
    public const string NumberLabel = "Card number";
    public const string ExpiryLabel = "Expiry";
    public const string CodeLabel = "Security code";
    public const string NumberName = "Card number, hidden as you type";
    public const string ExpiryName = "Expiry, hidden";
    public const string CodeName = "Security code, hidden";
    public const string VaultHidden = "Every digit is hidden as you type, and checked as you type: a mistyped number or a past expiry is caught without showing it.";
    public const string VaultWhere = "Saved in Windows Credential Manager, encrypted for your Windows account with nth's own extra key. Never in nth's settings, logs, transcripts, crash dumps, exports, sync or the clipboard.";
    public const string VaultAfter = "Afterwards nth shows only the last 4 digits: no expiry, no brand, no name. There is no reveal; to change the card, add it again.";
    public const string Save = "Save with Windows Hello";
    public const string Checked = "Checked: the number's check digit passes and the expiry is in the future.";
    public const string BadNumber = "This number does not check out. Type it again.";
    public const string PastExpiry = "This expiry is in the past.";
    public const string BadCode = "The security code is 3 or 4 digits.";

    // Windows Hello
    public const string HelloDetail = "Windows asks for your face, fingerprint or PIN, or your Windows password if Hello is not set up.";
    public const string LockedOut = "Three failed Windows Hello checks. Purchases are off until you turn them on again.";
    public const string HelloBusy = "Another Windows Hello check is open. Finish it first.";
    public const string HelloUnavailable = "Windows Hello could not ask. Nothing changed.";

    // the confirmation card
    public const string Rule = "Always confirmed: paying, then Windows Hello";
    public const string Bound = "Bound to this shop, these items and this total. Any change brings a new card. It expires in 10 minutes.";
    public const string DontBuy = "Don't buy";
    public const string IllPay = "I'll pay";
    public const string Expired = "This card expired. Ask Dextra for a fresh request.";
    public const string Changed = "Its shop, items or total changed. A new card asks you again.";
    public const string LimitsChanged = "What is left of your limits changed. A new card asks you again.";

    // the hand-over card and what the desk says while it pays
    public const string HandOverRule = "Handed to you: you pay yourself";
    public const string HandedOff = "Purchases are off, so the payment is yours to make.";
    public const string HandedNoCard = "There is no card to pay with, so the payment is yours to make.";
    public const string TurnedOff = "Purchases were turned off, so this payment is yours to make.";
    public const string PageGone = "The checkout is not open any more. Ask Dextra for a fresh request.";
    public const string NotWritten = "nth could not write the purchase down before paying, so it did not pay. The card was taken off the page.";
    public const string NotTaken = "The shop did not take the payment. The card was taken off the page.";
    public const string CheckWithShop = "nth could not tell whether the payment went through. Check with the shop before paying again.";
    public const string CardDamaged = "The card in Credential Manager cannot be opened. Remove it and add it again.";
    public const string FieldRefused = "The shop's card field did not take the card. Nothing was paid.";
    public const string PageUnread = "nth could not read the checkout. Nothing was paid.";
    public const string Underway = "This payment is under way. Cancel it in Windows Hello.";
    public const string PayOnCard = "Pay from the card: it asks Windows Hello first.";
    public const string Stopped = "Stopped before anything was filled.";
    public const string NoSuchCard = "This card is closed. Ask Dextra for a fresh request.";
    public const string LogNotWritten = "The activity log could not be written, so nth did not pay. The card was taken off the page.";
    public const string CardChanged = "The card changed. A new card asks you again.";
    public const string UnknownAssistant = "nth does not know the assistant that asked, so the payment is yours to make.";
    public const string NothingFilled = "Nothing was filled.";
    public const string NothingChanged = "Nothing changed.";

    // the settings section's lines
    public const string NeedsCard = "Add a card first.";
    public const string NeedsFill = "This build cannot fill a shop's card field, so payments stay handed to you.";
    public const string TurnedOn = "Purchases are on.";
    public const string NowOff = "Purchases are off.";
    public const string OffNotSaved = "Purchases are off. nth could not save that, so turn them off again after nth restarts.";
    public const string NotSaved = "nth could not save the change. Nothing changed.";
    public const string LimitSaved = "The new limit counts from now.";
    public const string LimitTyped = "A limit is an amount, for example 50.00.";
    public const string CardSaved = "Saved. Only the last 4 digits are shown.";
    public const string CardRemoved = "The card was removed from Credential Manager.";
    public const string NoCardToRemove = "There is no card to remove.";
    public const string CardNotSaved = "Windows did not save the card. Nothing changed.";
    public const string Incomplete = "Type the whole number, the expiry and the security code.";
    public const string Cancel = "Cancel";
    public const string AsturLimits = "Limits per " + AssistantNames.Astur;
    public const string NoAsturs = "No " + AssistantNames.Asturs + " yet. Each one you make starts at $0: it hands its payments to you.";

    /// <summary>"Windows Hello: pay $46.80 to shop.example", shown while Windows asks.</summary>
    public static string Asking(string what) => "Windows Hello: " + what;

    /// <summary>A checkout in another currency than the card's limits: handed over, never converted.</summary>
    public static string OtherCurrency(string currency, string limits) =>
        $"This checkout is in {currency} and your limits are in {limits}, so the payment is yours to make.";

    /// <summary>The over line on a hand-over card: "Over your $50.00 limit per purchase, so the payment is yours to make."</summary>
    public static string OverHandOver(string over) => over + ", so the payment is yours to make.";

    /// <summary>The activity log's and the history's line: "Paid $46.80 at shop.example after Windows Hello".</summary>
    public static string Paid(PurchaseRequest request) =>
        $"Paid {PurchaseMoney.Format(request.Total, request.Currency)} at {CheckoutOrigins.Host(request.Origin)} after Windows Hello";

    public static string ToAgentNotTaken(PurchaseRequest request) =>
        $"The shop at {CheckoutOrigins.Host(request.Origin)} did not take the payment. nth took {Placeholder} off the page; nothing was paid that nth knows of.";

    public static string ToAgentStopped(PurchaseRequest request, string why) =>
        $"The purchase at {CheckoutOrigins.Host(request.Origin)} did not go ahead: {why} Nothing was filled.";

    /// <summary>"•••• 4242": the card as nth shows it, everywhere.</summary>
    public static string LastFour(string? lastFour) => lastFour is { Length: 4 } l ? "•••• " + l : NoCard;

    /// <summary>The switch's line under its title, by state.</summary>
    public static string SwitchDetail(bool hasCard, bool on) =>
        !hasCard ? "Add a card first. Turning it on asks Windows Hello."
        : on ? "On. Every purchase still shows you its card and asks Windows Hello."
        : "Off. Turning it on asks Windows Hello.";

    public static string LimitName(PurchaseLimitKind kind, string? astur = null) => kind switch
    {
        PurchaseLimitKind.PerPurchase => "Limit per purchase",
        PurchaseLimitKind.PerDay => "Limit per day",
        PurchaseLimitKind.PerMonth => "Limit per month",
        _ => (astur ?? AssistantNames.Astur) + ": limit per month",
    };

    /// <summary>"Over your $50.00 limit per purchase" (the canvas's over line).</summary>
    public static string Over(PurchaseLimitKind kind, PurchaseLimits limits, string currency, string? astur, string? asturName) => kind switch
    {
        PurchaseLimitKind.PerPurchase => $"Over your {PurchaseMoney.Format(limits.PerPurchase, currency)} limit per purchase",
        PurchaseLimitKind.PerDay => $"Over your {PurchaseMoney.Format(limits.PerDay, currency)} limit per day",
        PurchaseLimitKind.PerMonth => $"Over your {PurchaseMoney.Format(limits.PerMonth, currency)} limit per month",
        _ => astur is not null && limits.AsturLimit(astur) == 0m
            ? $"{asturName ?? AssistantNames.Astur} has a {PurchaseMoney.Format(0m, currency)} limit"
            : $"Over {asturName ?? AssistantNames.Astur}'s {PurchaseMoney.Format(limits.AsturLimit(astur ?? ""), currency)} limit per month",
    };

    /// <summary>"$103.20 of $150.00 today · $253.20 of $300.00 this month" (the card's Left after).</summary>
    public static string Left(PurchaseLimitCheck check, PurchaseLimits limits, string currency) =>
        $"{PurchaseMoney.Format(check.LeftToday, currency)} of {PurchaseMoney.Format(limits.PerDay, currency)} today · "
        + $"{PurchaseMoney.Format(check.LeftThisMonth, currency)} of {PurchaseMoney.Format(limits.PerMonth, currency)} this month";

    /// <summary>"Not verified (1 of 3). Nothing was filled." and its kin.</summary>
    public static string NotVerified(int failures, string nothing) =>
        string.Create(CultureInfo.InvariantCulture, $"Not verified ({failures} of {HelloGuard.LockAfter}). {nothing}");

    /// <summary>The card's title: "Buy from shop.example?".</summary>
    public static string Title(PurchaseRequest request) => "Buy from " + CheckoutOrigins.Host(request.Origin) + "?";

    /// <summary>The Pay button: "Pay $46.80".</summary>
    public static string Pay(PurchaseRequest request) => "Pay " + PurchaseMoney.Format(request.Total, request.Currency);

    /// <summary>The Windows Hello prompt's reason for a payment: "pay $46.80 to shop.example".</summary>
    public static string PayReason(PurchaseRequest request) => $"pay {PurchaseMoney.Format(request.Total, request.Currency)} to {CheckoutOrigins.Host(request.Origin)}";

    // ---- words a model reads: never a digit of the card, only the placeholder ----

    public static string ToAgentWaiting(Guid card) =>
        $"The owner has the purchase card ({card:D}). You never see the card: wherever it goes, it is {Placeholder}, and nth fills the real number itself after the owner pays. Leave the checkout at the payment step and wait for the answer.";

    public static string ToAgentHandOver(Guid card, string why) =>
        $"Handed to the owner ({card:D}): he pays himself. {why} Leave the checkout at the payment step with everything else filled, and wait for his answer.";

    public static string ToAgentPaid(PurchaseRequest request) =>
        $"Paid {PurchaseMoney.Format(request.Total, request.Currency)} at {CheckoutOrigins.Host(request.Origin)} after Windows Hello. nth filled {Placeholder} into the shop's own card field.";

    public static string ToAgentNotBought(PurchaseRequest request) =>
        $"The owner chose not to buy at {CheckoutOrigins.Host(request.Origin)}. Nothing was filled; keep the cart.";

    public static string ToAgentOwnerPays(PurchaseRequest request) =>
        $"The owner is paying at {CheckoutOrigins.Host(request.Origin)} himself. Do not touch the checkout.";

    public static string ToAgentExpired(PurchaseRequest request) =>
        $"The purchase card for {CheckoutOrigins.Host(request.Origin)} expired after 10 minutes. Prepare it again if it is still wanted.";

    public static string ToAgentUnknown(PurchaseRequest request) =>
        $"nth filled {Placeholder} at {CheckoutOrigins.Host(request.Origin)}, but could not tell whether the payment went through. Do not pay again; the owner checks it.";
}
