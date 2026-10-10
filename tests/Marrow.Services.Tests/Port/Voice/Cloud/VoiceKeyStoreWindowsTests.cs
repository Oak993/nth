using System.Text;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, WINDOWS-ONLY, voice-SPEC V-17.2 and V-T47: the key in the real Windows Credential Manager through the app's SecretStore. The target is renamed to a unique test-only
// service (RenamedTargetCredentialBackend), so the user's own "Marrow Code" credentials are never touched; the test deletes what it made.
public sealed class VoiceKeyStoreWindowsTests
{
    [WindowsOnlyFact("writes, reads and deletes a generic credential in Windows Credential Manager under a throwaway service")]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-9")]
    public void V_T47_the_real_Credential_Manager_holds_the_key_as_one_generic_credential_and_gives_it_back()
    {
        var testService = "Marrow VT-16 test " + Guid.NewGuid().ToString("D");
        var legacyTestService = "Marrow VT-16 legacy test " + Guid.NewGuid().ToString("D");
        // OD-0135: a read with no key under "Nth" also looks once under the earlier name "Marrow Code"; that lookup goes to its own throwaway service.
        var backend = new RenamedTargetCredentialBackend((VoiceKeyStore.Service, testService), (VoiceKeyStore.LegacyService, legacyTestService));
        var store = new SecretStore(backend);
        var target = backend.Map(SecretStore.TargetName(VoiceKeyStore.Service, VoiceKeyStore.Account));
        var key = "sk-test-vt16-" + Guid.NewGuid().ToString("N");
        try
        {
            using (SecretStore.Shared.Override(store))
            {
                var keys = new VoiceKeyStore();                       // no store given: the app's shared one, which is the override here
                Assert.Null(keys.Read());
                Assert.True(keys.Save("  " + key + "  "));
                Assert.Equal(key, keys.Read());

                var real = new CredentialManagerBackend();
                Assert.Equal(0, real.Read(target, out var credential));
                Assert.Equal(VoiceKeyStore.Account, credential!.UserName);
                Assert.Equal(1, credential.Type);                     // CRED_TYPE_GENERIC
                Assert.Equal(2, credential.Persist);                  // CRED_PERSIST_LOCAL_MACHINE
                Assert.Equal(key, Encoding.UTF8.GetString(credential.Blob));

                Assert.True(keys.Save(key + "-second"));              // a new key replaces the old
                Assert.Equal(key + "-second", keys.Read());
                Assert.True(keys.Remove());
                Assert.Null(keys.Read());
                Assert.Equal(1168, real.Read(target, out _));         // ERROR_NOT_FOUND: nothing left behind
            }
            Assert.All(backend.Touched, t => Assert.True(t.StartsWith(testService + ":", StringComparison.Ordinal) || t.StartsWith(legacyTestService + ":", StringComparison.Ordinal), t));
            Assert.Contains(backend.Touched, t => t.StartsWith(legacyTestService + ":", StringComparison.Ordinal)); // the earlier name was looked up, through the swap
            Assert.Equal(1168, new CredentialManagerBackend().Read(backend.Map(SecretStore.TargetName(VoiceKeyStore.LegacyService, VoiceKeyStore.Account)), out _)); // and never written
        }
        finally
        {
            new CredentialManagerBackend().Delete(target);
            new CredentialManagerBackend().Delete(target + SecretStore.CarriedMarkerSuffix);
            new CredentialManagerBackend().Delete(backend.Map(SecretStore.TargetName(VoiceKeyStore.LegacyService, VoiceKeyStore.Account)));
        }
    }
}
