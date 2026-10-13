// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "History", 15.3; ED-NEW-dextra-as20-9). No Swift twin.
// purchases.json beside reserves.json: the switch, the currency, the limits and the history (PurchaseBook). Never card data:
// the card lives in Credential Manager (CardVault). Written the store's temp-then-rename way.
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    public const string PurchasesFile = "purchases.json";

    /// <summary>
    /// The purchases document, or null when there is none; <paramref name="unreadable"/> says a file is there that does not
    /// parse (the book then keeps purchases off and never writes over it).
    /// </summary>
    public JsonObject? Purchases(out bool unreadable)
    {
        var document = ReadDocument(Path.Combine(Root, PurchasesFile), out var present) as JsonObject;
        unreadable = present && document is null;
        return document;
    }

    public void SavePurchases(JsonObject document) => SaveDocument(Path.Combine(Root, PurchasesFile), document);
}
