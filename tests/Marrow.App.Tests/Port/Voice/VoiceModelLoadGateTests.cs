using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Port.Voice;

[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoiceModelLoadGateTests
{
    [Theory]
    [InlineData(ModelVerification.Corrupt)]
    [InlineData(ModelVerification.Missing)]
    [Trait("Feature", "VT-15")]
    public async Task A_model_that_does_not_verify_is_never_opened(ModelVerification verification)
    {
        var source = new VoicePrepModelSource { Verification = verification };
        var engine = ScriptedEngine.Always(Hypothesis.Empty);
        var gated = new VerifiedSpeechEngine(engine, source);
        await Assert.ThrowsAsync<InvalidDataException>(() => gated.LoadAsync(new SpeechModel("base.en", "untrusted-path"), new EngineLoadOptions(), CancellationToken.None));
        Assert.Equal(["base.en"], source.Verifies);
        Assert.Empty(engine.Loads);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    public async Task A_verified_model_is_loaded_from_the_shared_stores_path()
    {
        var source = new VoicePrepModelSource();
        var engine = ScriptedEngine.Always(Hypothesis.Empty);
        var gated = new VerifiedSpeechEngine(engine, source);
        await gated.LoadAsync(new SpeechModel("base.en", "untrusted-path"), new EngineLoadOptions(), CancellationToken.None);
        var load = Assert.Single(engine.Loads);
        Assert.Equal(source.PathFor(ModelCatalog.Find("base.en")!), load.Model.Path);
    }
}
