using System.Text;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-3 (the lead runs it alone, with focus, on a temp root): the audit
// key and the head anchor are written to, read from and deleted from the REAL Windows Credential Manager, under a
// service name of this run's own, and the key never appears on disk in the clear. Never run by unit agents (every unit
// filter excludes Native).
[Trait("Decision", "ED-NEW-dextra-as3-3")]
[Trait("Feature", "AS-3")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class AuditKeyNativeTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly string service = "nth.dextra.audit.native-" + Guid.NewGuid().ToString("N")[..8];
    private readonly AuditKeyStore keys;
    private readonly CredentialManagerBackend backend = new();

    public AuditKeyNativeTests() => keys = new AuditKeyStore(new SecretStore(backend), service);

    public void Dispose()
    {
        keys.DeleteAll();
        temp.Dispose();
    }

    [Fact]
    public void The_audit_key_and_anchor_live_in_credential_manager_and_never_on_disk()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        using (var log = AuditLog.Open(temp.AuditDirectory, keys, clock))
            log.Append(new AuditEntry { Assistant = "dextra", Tool = "shell", ActionClass = "Run", Result = "native" });
        var key = keys.ReadKey();
        Assert.NotNull(key);
        Assert.Equal(AuditKeyStore.KeyBytes, key.Length);
        Assert.Equal(0, backend.Read(SecretStore.TargetName(service, "hmac-key"), out var held));
        Assert.Equal(key, held!.Blob);
        Assert.Equal(1, keys.ReadAnchor()!.Sequence);
        Assert.Equal(AuditVerificationStatus.Verified, AuditVerifier.Verify(temp.AuditDirectory, keys).Status);
        var forms = new[] { key, Encoding.UTF8.GetBytes(Convert.ToHexStringLower(key)), Encoding.UTF8.GetBytes(Convert.ToBase64String(key)) };
        foreach (var path in Directory.EnumerateFiles(temp.Folder, "*", SearchOption.AllDirectories))
            foreach (var form in forms) Assert.Equal(-1, File.ReadAllBytes(path).AsSpan().IndexOf(form));
        keys.DeleteAll();
        Assert.Null(keys.ReadKey());
        Assert.Null(keys.ReadAnchor());
        Assert.Equal(1168, backend.Read(SecretStore.TargetName(service, "hmac-key"), out _));
    }
}
