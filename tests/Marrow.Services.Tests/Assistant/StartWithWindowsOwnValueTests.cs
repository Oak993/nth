using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: final security gate N11 (ED-NEW-secfinal-24). Dextra's "Start with Windows (hidden)" shared the HKCU Run value name "nth"
// with the release's "Open at sign-in", so each overwrote or deleted the other, and the release switch showed On for Dextra's entry. Dextra's
// entry is now the value "nth Dextra"; this switch reads, writes and deletes only that value. A fake Run key stands for the registry.
[Trait("Decision", "ED-NEW-secfinal-24")]
public sealed class StartWithWindowsOwnValueTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";
    private const string SignIn = "\"" + Exe + "\" --at-sign-in";

    [Fact]
    public void The_default_value_name_is_nth_Dextra_and_never_the_sign_in_switchs()
    {
        Assert.Equal("nth Dextra", StartWithWindows.DefaultValueName);
        Assert.NotEqual("nth", StartWithWindows.DefaultValueName);
    }

    [Fact]
    public void Turning_it_on_writes_its_own_value_and_leaves_the_sign_in_value_alone()
    {
        var key = new FakeRunKey();
        key.Values["nth"] = SignIn;

        Assert.Null(new StartWithWindows(key).Turn(true, Exe));

        Assert.Equal(SignIn, key.Values["nth"]);
        Assert.Equal("\"" + Exe + "\" --hidden", key.Values["nth Dextra"]);
        Assert.Equal(2, key.Values.Count);
    }

    [Fact]
    public void Turning_it_off_deletes_its_own_value_and_leaves_the_sign_in_value_alone()
    {
        var key = new FakeRunKey();
        key.Values["nth"] = SignIn;
        var start = new StartWithWindows(key);
        start.Turn(true, Exe);

        Assert.Null(start.Turn(false, Exe));

        Assert.Equal(SignIn, key.Values["nth"]);
        Assert.False(key.Values.ContainsKey("nth Dextra"));
    }

    [Fact]
    public void The_sign_in_value_does_not_make_this_switch_read_on_even_with_the_same_command()
    {
        var key = new FakeRunKey();
        key.Values["nth"] = "\"" + Exe + "\" --hidden";

        Assert.False(new StartWithWindows(key).IsOn(Exe));
    }

    [Fact]
    public void An_entry_of_its_own_for_another_install_reads_as_off()
    {
        var key = new FakeRunKey();
        key.Values["nth Dextra"] = "\"D:\\old\\nth.exe\" --hidden";

        Assert.False(new StartWithWindows(key).IsOn(Exe));
    }
}
