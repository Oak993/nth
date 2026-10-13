using Marrow.Services.Assistant;
using Microsoft.Win32;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-10, "Start with Windows (hidden)" (the lead runs it alone; every unit filter excludes
// Native). The switch writes ONE value to the REAL per-user Run key, under a value name of this run's own (never the owner's "nth" entry), which
// holds the program's path in quotes and --hidden; turning it off removes the value; the owner's other start-up entries are untouched. What
// this cannot prove is the sign-in itself: the owner signs out and in once, and the tray icon is there with no window.
[Trait("Decision", "ED-NEW-dextra-as10-14")]
[Trait("Feature", "AS-10")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class StartWithWindowsNativeTests
{
    private const string Exe = @"C:\nth-native-test\current\nth.exe";

    private static string? Raw(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey.Path);
        return key?.GetValue(name) as string;
    }

    private static string[] Names()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey.Path);
        return key?.GetValueNames().Order(StringComparer.Ordinal).ToArray() ?? [];
    }

    [Fact]
    public void Turning_it_on_writes_one_run_value_that_starts_nth_hidden_and_turning_it_off_removes_it()
    {
        var name = "nth-native-" + Guid.NewGuid().ToString("N")[..8];
        var start = new StartWithWindows(new RegistryRunKey(), name);
        var before = Names();
        try
        {
            Assert.False(start.IsOn(Exe));
            Assert.Null(start.Turn(true, Exe));
            Assert.True(start.IsOn(Exe));
            Assert.Equal("\"" + Exe + "\" --hidden", Raw(name));
            Assert.Equal([.. before, name], Names().Order(StringComparer.Ordinal).ToArray());
            Assert.Null(start.Turn(false, Exe));
            Assert.False(start.IsOn(Exe));
            Assert.Null(Raw(name));
        }
        finally
        {
            start.Turn(false, Exe);
        }

        Assert.Equal(before, Names()); // the owner's other entries are as they were
    }
}
