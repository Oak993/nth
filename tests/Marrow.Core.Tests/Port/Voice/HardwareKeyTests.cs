using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0092; voice-SPEC V-10.5): the key of a stored hardware result, and when a stored result is stale.
public sealed class HardwareKeyTests
{
    private static HardwareKeyInputs Inputs() => new("NVIDIA GeForce RTX 4080", "32.0.15.6094", "AMD Ryzen 7 9800X3D 8-Core Processor", 31, "1.7.0+21", "1.9.1");

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.5: a hash (SHA-256, 64 lower-case hex digits), the same for the same inputs
    public void The_key_is_a_stable_sha256_hex_string()
    {
        var key = HardwareKey.Compute(Inputs());
        Assert.Equal(64, key.Length);
        Assert.All(key, c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));
        Assert.Equal(key, HardwareKey.Compute(Inputs()));
        Assert.Equal(key, HardwareKey.Compute(Inputs() with { }));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.5: adapter description, driver version, CPU name, RAM GB, app version and build, Whisper.net runtime version
    public void Each_input_changes_the_key()
    {
        var key = HardwareKey.Compute(Inputs());
        var changed = new[]
        {
            Inputs() with { AdapterDescription = "NVIDIA GeForce RTX 4070" },
            Inputs() with { DriverVersion = "32.0.15.6095" },
            Inputs() with { CpuName = "AMD Ryzen 7 9800X3D" },
            Inputs() with { RamGB = 32 },
            Inputs() with { App = "1.7.0+22" },
            Inputs() with { RuntimeVersion = "1.9.2" },
        };
        foreach (var c in changed) Assert.NotEqual(key, HardwareKey.Compute(c));
        Assert.Equal(changed.Length, changed.Select(HardwareKey.Compute).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.5: values that run together must not collide
    public void Moving_text_from_one_input_to_the_next_does_not_collide()
    {
        var a = Inputs() with { AdapterDescription = "ab", DriverVersion = "c" };
        var b = Inputs() with { AdapterDescription = "a", DriverVersion = "bc" };
        Assert.NotEqual(HardwareKey.Compute(a), HardwareKey.Compute(b));
        var c = Inputs() with { CpuName = "x\ny", RamGB = 31 };
        var d = Inputs() with { CpuName = "x", AdapterDescription = "y" };
        Assert.NotEqual(HardwareKey.Compute(c), HardwareKey.Compute(d));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.5 / V-16.4: the key can be built from a probe's profile
    public void The_key_can_be_built_from_a_hardware_profile()
    {
        var profile = new HardwareProfile("AMD Ryzen 7 9800X3D 8-Core Processor", 8, 31, "NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16L << 30, 14L << 30, "32.0.15.6094", GpuBackend.Vulkan);
        Assert.Equal(HardwareKey.Compute(Inputs()), HardwareKey.Compute(profile, "1.7.0+21", "1.9.1"));
        Assert.NotEqual(HardwareKey.Compute(Inputs()), HardwareKey.Compute(profile with { MemoryGB = 16 }, "1.7.0+21", "1.9.1"));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.5: a mismatch at start marks the result stale; nothing stored means the check runs
    public void A_stored_result_is_stale_when_its_key_differs_or_nothing_is_stored()
    {
        var key = HardwareKey.Compute(Inputs());
        var stored = new VoiceHardwareRecord { Key = key };
        Assert.False(HardwareKey.IsStale(stored, key));
        Assert.True(HardwareKey.IsStale(stored, HardwareKey.Compute(Inputs() with { DriverVersion = "other" })));
        Assert.True(HardwareKey.IsStale(null, key));
        Assert.True(HardwareKey.IsStale(new VoiceHardwareRecord(), key));
        Assert.True(HardwareKey.IsStale(stored with { Key = key.ToUpperInvariant() }, key));
    }
}
