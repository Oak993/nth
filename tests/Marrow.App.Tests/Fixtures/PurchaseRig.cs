using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (Dextra AS-20): the purchase desk over fakes only. Windows Hello is a script, the card lives in an in-memory
/// Credential Manager sealed by a fake seal, purchases.json is a JSON object in memory, the clock is manual and every checkout
/// page is a FakeCheckoutPage on a local host. The card is a published test number; nothing here touches Windows.
/// </summary>
public sealed class PurchaseRig : ICheckoutPages, IDisposable
{
    public const string Shop = "http://shop.localhost:8080";
    public const string Provider = "http://pay.test:8443";
    public const string Third = "http://collect.test:9000";

    /// <summary>A published test card number (it passes its check digit); its last 4 are 5556.</summary>
    public const string Number = "4000056655665556";
    public const string Expiry = "1230";
    public const string Code = "9183";

    public static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private JsonObject? saved;

    public PurchaseRig(bool canPay = true, IReadOnlyList<AssistantProfile>? profiles = null, Action<JsonObject>? save = null)
    {
        Profiles = profiles ?? [new AssistantProfile("dextra", "Dextra"), new AssistantProfile("store-2", "Store") { ParentId = "dextra" }];
        Clock = new ManualTimeProvider(Start);
        Queue = new DecisionQueue(Clock);
        Book = new PurchaseBook(() => saved, document => { save?.Invoke(document); saved = document; });
        Desk = new PurchaseDesk(new PurchaseDeskOptions
        {
            Setup = new PurchaseSetup
            {
                Book = Book,
                Hello = Hello,
                Vault = guard => new CardVault(new SecretStore(Backend), Sealer, guard, "nth.dextra.card.rig"),
                Pages = canPay ? this : null,
                AllowLoopbackHttp = true,
                Zone = TimeZoneInfo.Utc,
            },
            Queue = Queue,
            Audit = entry =>
            {
                if (FailAudit?.Invoke(entry) == true) throw new IOException("the log is full");
                lock (Audit) Audit.Add(entry);
            },
            Deliver = (thread, text) => { lock (Delivered) Delivered.Add((thread, text)); },
            Context = chat => new PolicyContext(AssistantChain.Of(chat.AssistantId, Profiles), Clock.GetUtcNow()),
            Profiles = () => Profiles,
            AgentName = _ => "Checkout agent",
            Time = Clock,
            Fault = fault => { lock (Faults) Faults.Add(fault); },
        });
    }

    public IReadOnlyList<AssistantProfile> Profiles { get; set; }

    public ManualTimeProvider Clock { get; }

    public DecisionQueue Queue { get; }

    public FakeHelloGate Hello { get; } = new();

    public FakeCardSealer Sealer { get; } = new();

    public InMemoryCredentialBackend Backend { get; } = new();

    public PurchaseBook Book { get; }

    public PurchaseDesk Desk { get; }

    public JsonObject? Saved => saved;

    public List<AuditEntry> Audit { get; } = [];

    public List<(Guid Thread, string Text)> Delivered { get; } = [];

    public List<string> Faults { get; } = [];

    public Func<AuditEntry, bool>? FailAudit { get; set; }

    /// <summary>The page each chat holds open, by origin.</summary>
    public Dictionary<(Guid Thread, string Origin), FakeCheckoutPage> Pages { get; } = [];

    public Guid Thread { get; } = Guid.NewGuid();

    public Guid Mission { get; } = Guid.NewGuid();

    public ICheckoutPage? Find(Guid thread, string origin) => Pages.GetValueOrDefault((thread, origin));

    public AssistantChat Chat(string assistant = "dextra", Guid? thread = null) =>
        new(thread ?? Thread, assistant, Mission, AssistantChatRole.Mission, ProviderKind.Claude, "model-a");

    /// <summary>A shop page whose card fields are the shop's own, as the agent left it at the payment step.</summary>
    public FakeCheckoutPage SameOrigin(decimal total = 46.80m, Guid? thread = null)
    {
        var page = new FakeCheckoutPage(FakeCheckoutPage.Page1(Shop, total,
            ("number", "cc-number", null, Shop, true), ("exp", "cc-exp", null, Shop, true), ("csc", "cc-csc", null, Shop, true)));
        Pages[(thread ?? Thread, Shop)] = page;
        return page;
    }

    /// <summary>A checkout whose card field is a payment provider's frame (its test-mode checkout on a .test host).</summary>
    public FakeCheckoutPage ProviderFrame(decimal total = 46.80m)
    {
        var page = new FakeCheckoutPage(FakeCheckoutPage.Page1(Shop, total,
            ("number", "cc-number", Provider, Provider, true), ("exp", "cc-exp", Provider, Provider, true), ("csc", "cc-csc", Provider, Provider, true)));
        Pages[(Thread, Shop)] = page;
        return page;
    }

    /// <summary>A checkout whose form posts the card to a third origin.</summary>
    public FakeCheckoutPage PostsElsewhere(decimal total = 46.80m)
    {
        var page = new FakeCheckoutPage(FakeCheckoutPage.Page1(Shop, total,
            ("number", "cc-number", null, Third, true), ("exp", "cc-exp", null, Third, true), ("csc", "cc-csc", null, Third, true)));
        Pages[(Thread, Shop)] = page;
        return page;
    }

    /// <summary>prepare_purchase's arguments for the shop's checkout: "Tamper, 58 mm" at the total, no shipping or tax.</summary>
    public static string Arguments(decimal total = 46.80m, string kind = "purchase", string item = "Tamper, 58 mm", string currency = "USD") =>
        $$"""{"shop":"Shop Example","checkout_url":"{{Shop}}/checkout","items":["{{item}}: {{total.ToString(System.Globalization.CultureInfo.InvariantCulture)}}"],"total":{{total.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"currency":"{{currency}}","ship_to":"Home","kind":"{{kind}}"}""";

    public AssistantToolCall Call(string arguments, AssistantChat? chat = null) =>
        new(chat ?? Chat(), AssistantToolCatalog.Find("prepare_purchase")!, JSONValue.Parse(arguments)!, new Classification(ActionClass.Agents, "puts a purchase card in front of the owner"),
            new PolicyDecision(Verdict.Allow, PolicyRule.AlwaysConfirmed, "Allowed"), AssistantRoute.Mcp);

    /// <summary>The owner adds the test card (Windows Hello says yes unless the script says otherwise).</summary>
    public async Task<CardVaultResult> AddCard(string number = Number, string expiry = Expiry, string code = Code)
    {
        using var entry = new CardEntry();
        foreach (var c in number) entry.Type(CardPart.Number, c);
        foreach (var c in expiry) entry.Type(CardPart.Expiry, c);
        foreach (var c in code) entry.Type(CardPart.Code, c);
        return await Desk.SaveCard(entry);
    }

    /// <summary>A card saved and purchases turned on, each after Windows Hello.</summary>
    public async Task<PurchaseRig> On()
    {
        await AddCard();
        if (await Desk.TurnOn() is { } refused) throw new InvalidOperationException(refused);
        return this;
    }

    /// <summary>A purchase paid earlier, kept in the history (what the limits count).</summary>
    public void Spent(decimal amount, DateTimeOffset at, string assistant = "dextra")
    {
        var request = new PurchaseRequest("Shop Example", "https://shop.example", "https://shop.example/checkout",
            new ValueList<PurchaseItem>([new("Earlier", amount)]), 0m, 0m, amount, "USD", "Home");
        Book.Settle(Book.Begin(request, null, assistant, at).Id, PurchaseRowState.Paid);
    }

    /// <summary>Every text the purchase path produced: cards, notes, log lines, words to models, the file, the faults.</summary>
    public IEnumerable<string> EveryText()
    {
        foreach (var item in Queue.Items)
        {
            yield return item.Title;
            yield return item.Why;
            yield return item.Rule;
            if (item.Note is { } note) yield return note;
            foreach (var input in item.Inputs) yield return input.Name + ": " + input.Value;
            foreach (var option in item.Options) yield return option;
            yield return item.ToString();
        }
        lock (Audit) foreach (var entry in Audit) yield return System.Text.Json.JsonSerializer.Serialize(entry);
        lock (Delivered) foreach (var (_, text) in Delivered) yield return text;
        lock (Faults) foreach (var fault in Faults) yield return fault;
        if (saved is not null) yield return saved.ToJsonString();
        yield return Desk.State.ToString();
        if (Desk.State.Status is { } status) yield return status;
    }

    public void Dispose() => Desk.Dispose();
}
