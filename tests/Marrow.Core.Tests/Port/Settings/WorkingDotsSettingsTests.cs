using System.Text;
using Marrow.Core.Port.Settings;

namespace Marrow.Core.Tests.Port.Settings;

// SA-1 (customization-SPEC CZ-13.3, CZ-10.3; OD-0085, OD-0099 Look picks): the workingDots setting and its JSON.
public sealed class WorkingDotsSettingsTests
{
    private const string DefaultJson = "{\"mode\":\"accent\",\"custom\":\"blue\"}";

    private static WorkingDotsSettings Read(string json) => WorkingDotsSettings.FromJson(Encoding.UTF8.GetBytes(json));

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T50")]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-13.3: defaults write {"mode":"accent","custom":"blue"}
    public void The_defaults_write_the_exact_bytes_of_CZ_13_3()
    {
        var d = WorkingDotsSettings.Default;
        Assert.Equal((WorkingDotsMode.Accent, "blue"), (d.Mode, d.Custom));
        Assert.Equal("workingDots", WorkingDotsSettings.StoreKey);
        Assert.Equal(Encoding.UTF8.GetBytes(DefaultJson), d.ToJson());
        Assert.Equal(d, Read(DefaultJson));
    }

    [Theory]
    [InlineData(WorkingDotsMode.Accent, "accent")]
    [InlineData(WorkingDotsMode.Model, "model")]
    [InlineData(WorkingDotsMode.Custom, "custom")]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T50")]
    [Trait("Decision", "ED-251")] // CZ-13.3: mode is accent | model | custom
    public void Each_mode_writes_its_wire_name_and_reads_back(WorkingDotsMode mode, string wire)
    {
        var settings = WorkingDotsSettings.Default with { Mode = mode };
        var json = Encoding.UTF8.GetString(settings.ToJson());
        Assert.Equal("{\"mode\":\"" + wire + "\",\"custom\":\"blue\"}", json);
        Assert.Equal(settings, Read(json));
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T50")]
    [Trait("Decision", "ED-251")] // CZ-13.3: custom is an AccentPalette id, windows for the Windows accent
    public void Every_accent_palette_id_round_trips_as_the_custom_colour()
    {
        Assert.Equal(22, WorkingDotsSettings.KnownCustomIds.Count);
        Assert.Equal("windows", WorkingDotsSettings.KnownCustomIds[0]);
        foreach (var id in WorkingDotsSettings.KnownCustomIds)
        {
            var settings = new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = id };
            Assert.Equal(settings, Read(Encoding.UTF8.GetString(settings.ToJson())));
        }
    }

    [Theory]
    [InlineData("{}", WorkingDotsMode.Accent, "blue")]                                  // missing keys keep the defaults
    [InlineData("{\"mode\":\"model\"}", WorkingDotsMode.Model, "blue")]                 // missing custom
    [InlineData("{\"custom\":\"teal\"}", WorkingDotsMode.Accent, "teal")]               // missing mode
    [InlineData("{\"mode\":\"rainbow\",\"custom\":\"teal\"}", WorkingDotsMode.Accent, "teal")]   // unknown mode
    [InlineData("{\"mode\":\"custom\",\"custom\":\"chartreuse\"}", WorkingDotsMode.Custom, "blue")] // unknown id
    [InlineData("{\"mode\":\"custom\",\"custom\":\"Teal\"}", WorkingDotsMode.Custom, "blue")]       // ids are exact
    [InlineData("{\"mode\":3,\"custom\":false}", WorkingDotsMode.Accent, "blue")]       // wrong JSON types
    [InlineData("{\"mode\":null,\"custom\":null}", WorkingDotsMode.Accent, "blue")]
    [InlineData("[]", WorkingDotsMode.Accent, "blue")]                                  // not an object
    [InlineData("\"accent\"", WorkingDotsMode.Accent, "blue")]
    [InlineData("{oops", WorkingDotsMode.Accent, "blue")]                               // garbage
    [InlineData("", WorkingDotsMode.Accent, "blue")]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T50")]
    [Trait("Decision", "ED-251")] // CZ-13.3: missing or unknown values keep their defaults, unreadable data is the default
    public void A_lenient_read_keeps_the_default_for_what_it_cannot_use(string json, WorkingDotsMode mode, string custom)
    {
        var read = Read(json);
        Assert.Equal((mode, custom), (read.Mode, read.Custom));
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T50")]
    [Trait("Decision", "ED-251")] // the ED-251 tests' rule: stored bytes that are not valid UTF-8 are the default, not an exception
    public void Bytes_that_are_not_valid_text_are_the_default()
    {
        byte[] bad = [.. "{\"mode\":\""u8, 0xFF, .. "\"}"u8];
        Assert.Equal(WorkingDotsSettings.Default, WorkingDotsSettings.FromJson(bad));
        Assert.Equal(WorkingDotsSettings.Default, Read("{\"custom\":\"\\ud800\"}"));
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T50")]
    [Trait("Decision", "ED-251")] // a hand-built value holding a mode or id outside the lists still writes readable, default-valued JSON
    public void A_record_outside_the_lists_writes_the_defaults_for_those_fields()
    {
        var odd = new WorkingDotsSettings { Mode = (WorkingDotsMode)9, Custom = "nope" };
        Assert.Equal(DefaultJson, Encoding.UTF8.GetString(odd.ToJson()));
    }
}
