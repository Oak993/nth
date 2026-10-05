// PORT-ONLY: SK-13 / DL-15. A non-open catalogue item with a Marrow-made licence file, the state the terms step reads and a recorder for what it stores.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class TermsRig
{
    public const string LicenceBody = "Marrow-made fixture terms. Not a vendor text.\n";
    public static readonly DateTimeOffset At = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    public TermsRig(string vendor = "Anthropic", TermsClass terms = TermsClass.Proprietary, string licence = LicenceBody)
    {
        Item = Make(vendor, terms, licence);
        Catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [Item] };
    }

    public CatalogueItem Item { get; }
    public SkillsCatalogue Catalogue { get; }
    public AddonsState State { get; set; } = AddonsState.Empty;
    public List<(string Id, DateTimeOffset At)> Accepted { get; } = [];
    public StubLicenceText Text { get; } = new(LicenceBody);

    public TermsStep Step() => new(Catalogue, () => State, (id, at) =>
    {
        Accepted.Add((id, at));
        State = State.AcceptTerms(ItemTerms.LicenceHash(Item)!, id, at);
    }, Text, new FixedClock(At));

    public static CatalogueItem Make(string vendor, TermsClass terms, string licence) => new()
    {
        Id = "A2", Kind = ItemKind.Skillset, Title = "Fixture documents", Summary = "Marrow-made fixture", Vendor = vendor, CountState = CountState.Plan,
        PinState = PinState.Pinned, Pin = "1234567890abcdef1234567890abcdef12345678", SourceUrl = "https://github.com/example/fixture",
        LicenceName = "Proprietary", LicenceUrl = "https://raw.githubusercontent.com/example/fixture/1234567890abcdef1234567890abcdef12345678/LICENSE.txt",
        LicenceFile = "LICENSE.txt", TermsClass = terms, ApproxSizeMb = 1, Needs = [], Provides = [], Artifacts = [],
        Files = [new CatalogueFile { Path = "LICENSE.txt", Size = Encoding.UTF8.GetByteCount(licence), Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(licence))) }],
    };
}
