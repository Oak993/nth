using System.Text;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-20 (the lead runs it alone, with focus, on a temp root): the card is
// written to, read from and deleted from the REAL Windows Credential Manager under a service name of this run's own, sealed
// with the real DPAPI in user scope plus nth's entropy; another entropy cannot open it; and neither the number nor the expiry
// appears in the clear in Credential Manager or in nth's own files. Windows Hello is the fake here (HelloGateNativeTests in
// Marrow.App.Tests asks the real one). The card is the published test number. Never run by unit agents.
[Trait("Decision", "ED-NEW-dextra-as20-7")]
[Trait("Feature", "AS-20")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class CardVaultNativeTests : IDisposable
{
    private const string Number = "4242424242424242";
    private readonly AssistantTempRoot temp = new();
    private readonly string service = "nth.dextra.card.native-" + Guid.NewGuid().ToString("N")[..8];
    private readonly CredentialManagerBackend backend = new();
    private readonly CardVault vault;

    public CardVaultNativeTests() => vault = new CardVault(new SecretStore(backend), DpapiCardSealer.Instance, new HelloGuard(new FakeHelloGate()), service);

    public void Dispose()
    {
        backend.Delete(SecretStore.TargetName(service, "card"));
        backend.Delete(SecretStore.TargetName(service, "last4"));
        temp.Dispose();
    }

    [Fact]
    public async Task The_card_is_kept_sealed_with_nths_entropy_in_credential_manager_and_deleted_for_real()
    {
        using (var entry = new CardEntry())
        {
            foreach (var c in Number) entry.Type(CardPart.Number, c);
            foreach (var c in "1230") entry.Type(CardPart.Expiry, c);
            foreach (var c in "123") entry.Type(CardPart.Code, c);
            Assert.Equal(CardVaultOutcome.Done, (await vault.Save(entry, DateTimeOffset.UtcNow)).Outcome);
        }
        Assert.Equal(0, backend.Read(SecretStore.TargetName(service, "card"), out var held));
        var blob = held!.Blob;
        Assert.DoesNotContain(Number, Encoding.Latin1.GetString(blob), StringComparison.Ordinal);
        Assert.Null(DpapiCardSealer.OpenWith(blob, new byte[32]));
        Assert.Null(DpapiCardSealer.OpenWith(blob, []));
        var opened = DpapiCardSealer.Instance.Open(blob);
        Assert.NotNull(opened);
        Assert.Contains(Number, Encoding.ASCII.GetString(opened), StringComparison.Ordinal);
        Array.Clear(opened);

        var page = new FakeCheckoutPage(FakeCheckoutPage.Page1("https://shop.example", 46.80m, ("number", "cc-number", null, "https://shop.example", true)));
        Assert.Equal(CardFillOutcome.Filled, (await vault.Fill(page, "https://shop.example", 46.80m, "pay")).Outcome);
        Assert.Equal(Number, page.Filled["number"]);

        var forms = new[] { Encoding.ASCII.GetBytes(Number), Encoding.Unicode.GetBytes(Number) };
        var credentialFolders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Credentials"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Credentials"),
            temp.Folder,
        };
        foreach (var path in credentialFolders.Where(Directory.Exists).SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories)))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var form in forms) Assert.True(bytes.AsSpan().IndexOf(form) < 0, $"{path} holds the card in the clear");
        }

        Assert.Equal(CardVaultOutcome.Done, (await vault.Remove()).Outcome);
        Assert.Equal(1168, backend.Read(SecretStore.TargetName(service, "card"), out _));
        Assert.Equal(1168, backend.Read(SecretStore.TargetName(service, "last4"), out _));
    }
}
