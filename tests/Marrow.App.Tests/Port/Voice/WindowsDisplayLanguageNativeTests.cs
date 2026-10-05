using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// VT-13: the real reader, on this PC (Windows only): GetUserDefaultUILanguage answers a culture name. Reads one number; touches no device.
public sealed class WindowsDisplayLanguageNativeTests
{
    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-7.14: the system reader returns a culture name such as "en-US"
    public void The_system_reader_answers_a_culture_name()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = WindowsDisplayLanguage.ReadSystem();
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Matches("^[a-z]{2,3}(-[A-Za-z0-9]+)*$", name!);
    }
}
