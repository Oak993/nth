using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-2.5: the engine's seams fail closed. A backend nobody bound touches no native library, model file or network.
public sealed class WhisperNetEngineSeamTests
{
    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_ambient_backend_default_throws_when_touched()
    {
        var error = Assert.Throws<SeamNotBoundException>(() =>
        {
            _ = EngineSeams.Backend.Current.LoadAsync(new BackendLoad("/m.bin", true, [RuntimeKind.Cpu], null), CancellationToken.None);
        });
        Assert.Equal(nameof(IWhisperBackend), error.Seam);
        Assert.IsType<FailClosedWhisperBackend>(EngineSeams.Backend.Current);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_ambient_VAD_native_default_throws_when_touched()
    {
        var error = Assert.Throws<SeamNotBoundException>(() => EngineSeams.VadNative.Current.Detect(new float[512]));
        Assert.Equal(nameof(IVadNative), error.Seam);
        Assert.IsType<FailClosedVadNative>(EngineSeams.VadNative.Current);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task An_engine_over_the_default_backend_fails_closed_on_its_first_load()
    {
        using var engine = new WhisperNetEngine(EngineSeams.Backend.Current, () => new RuntimeFacts([RuntimeKind.Cpu], false, null), () => 4);
        await Assert.ThrowsAsync<SeamNotBoundException>(() => engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        Assert.False(engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_test_override_binds_a_scripted_backend_for_its_own_flow_only()
    {
        var scripted = new ScriptedWhisperBackend();
        using (EngineSeams.Backend.Override(scripted)) Assert.Same(scripted, EngineSeams.Backend.Current);
        Assert.IsType<FailClosedWhisperBackend>(EngineSeams.Backend.Current);
    }
}
