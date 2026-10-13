// PORT-ONLY: Dextra (DESIGN.md rev 5, 6, 15.3; unit AS-11; ED-NEW-dextra-as11-6). No Swift twin.
// The digest's one document at the store's root, written like every other (temp-then-rename).
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    public const string DigestFileName = "digest.json";

    /// <summary>
    /// The digest document as last saved, or null when there is none. <paramref name="damaged"/> is true when the file is there but does not parse
    /// as one JSON object with distinct names (a missing file and a damaged one are not the same: a damaged one is never replaced).
    /// </summary>
    public JsonObject? DigestDocument(out bool damaged)
    {
        var document = ReadDocument(Path.Combine(Root, DigestFileName), out var present) as JsonObject;
        damaged = present && document is null;
        return document;
    }

    public void SaveDigestDocument(JsonObject document) => SaveDocument(Path.Combine(Root, DigestFileName), document);
}
