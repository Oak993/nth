using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-17.2 and V-T47: the OpenAI key lives in Credential Manager through SecretStore, service "Marrow Code", account
// "voice-openai"; it is never logged. The store runs on an in-memory Credential Manager here (ED-093's backend seam); the real one is
// never touched.
[Trait("Spec", "V-T47")]
public sealed class VoiceKeyStoreTests
{
    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T47_the_key_is_one_generic_credential_under_the_service_and_account()
    {
        var (keys, backend) = CloudFixtures.KeyStore();
        Assert.Equal("Marrow Code", VoiceKeyStore.Service);
        Assert.Equal("voice-openai", VoiceKeyStore.Account);
        Assert.Equal([SecretStore.TargetName("Marrow Code", "voice-openai")], backend.TargetNames);
        Assert.True(backend.Read(backend.TargetNames[0], out var credential) == 0 && credential is not null);
        Assert.Equal("voice-openai", credential!.UserName);
        Assert.Equal(CloudFixtures.Key, System.Text.Encoding.UTF8.GetString(credential.Blob));
        Assert.Equal(CloudFixtures.Key, keys.Read());
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T47_a_new_key_replaces_the_old_and_removing_it_leaves_nothing()
    {
        var (keys, backend) = CloudFixtures.KeyStore();
        Assert.True(keys.Save("  sk-second  "));                  // SetReplacing trims, as the Providers key does
        Assert.Equal("sk-second", keys.Read());
        Assert.Single(backend.TargetNames);
        Assert.True(keys.Remove());
        Assert.Null(keys.Read());
        Assert.Empty(backend.TargetNames);
        Assert.True(keys.Save("   "));                           // blank is no key
        Assert.Null(keys.Read());
        Assert.Empty(backend.TargetNames);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T47_a_refused_store_gives_no_key_and_no_throw()
    {
        var backend = new RefusingCredentialBackend(new InMemoryCredentialBackend()) { RefuseReads = true };
        var keys = new VoiceKeyStore(new SecretStore(backend));
        Assert.Null(keys.Read());
        backend.RefuseReads = false;
        backend.RefuseWriteTargets.Add(SecretStore.TargetName("Marrow Code", "voice-openai"));
        Assert.False(keys.Save(CloudFixtures.Key));
        Assert.Null(keys.Read());
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T47_nothing_it_does_writes_the_key_to_the_console_or_the_trace()
    {
        var output = new StringWriter();
        var trace = new System.Diagnostics.TextWriterTraceListener(output);
        var was = (Console.Out, Console.Error);
        System.Diagnostics.Trace.Listeners.Add(trace);
        Console.SetOut(output); Console.SetError(output);
        try
        {
            var (keys, _) = CloudFixtures.KeyStore();
            _ = keys.Read();
            keys.Save(CloudFixtures.Key + "x");
            _ = keys.Remove();
            var refusing = new VoiceKeyStore(new SecretStore(new RefusingCredentialBackend(new InMemoryCredentialBackend()) { RefuseReads = true }));
            refusing.Save(CloudFixtures.Key);
            _ = refusing.Read();
        }
        finally
        {
            Console.SetOut(was.Out); Console.SetError(was.Error);
            System.Diagnostics.Trace.Listeners.Remove(trace);
        }
        trace.Flush();
        Assert.DoesNotContain("sk-test", output.ToString(), StringComparison.Ordinal);
    }
}
