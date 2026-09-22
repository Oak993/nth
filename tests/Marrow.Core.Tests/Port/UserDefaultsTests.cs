using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-SET-1 (IMPLEMENTING.md 3.13). Source: draft C (proto-C Plist tests), extended to the getters the source uses.
public sealed class UserDefaultsTests
{
    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    public void Absent_is_distinct_from_false_and_zero()
    {
        using var runtime = new TestRuntime();
        var defaults = UserDefaults.Standard.Current;
        Assert.Null(defaults.Object("notifyWhenFinished"));
        Assert.True(Plist.AsBool(defaults.Object("notifyWhenFinished")) ?? true); // object(forKey:) as? Bool ?? true
        Assert.False(defaults.Bool("sidebarActivityView"));                     // bool(forKey:): absent is false
        defaults.Set("notifyWhenFinished", false);
        Assert.False(Plist.AsBool(defaults.Object("notifyWhenFinished")) ?? true);
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    public void Property_list_bridging_matches_the_as_question_casts()
    {
        Assert.True(Plist.AsBool(1L));
        Assert.Null(Plist.AsBool(2L));
        Assert.Equal(1L, Plist.AsInt(true));
        Assert.Equal(3L, Plist.AsInt(3.0));
        Assert.Null(Plist.AsInt(3.5));
        Assert.Equal(1.0, Plist.AsDouble(true));
        Assert.Null(Plist.AsInt("3"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Oracle", "O-10")]
    public void String_for_key_renders_integers_and_refuses_to_guess_a_Double()
    {
        using var runtime = new TestRuntime();
        var defaults = UserDefaults.Standard.Current;
        defaults.Set("a", 42L);
        defaults.Set("b", 1.5);
        defaults.Set("c", "text");
        Assert.Equal("42", defaults.String("a"));
        Assert.Equal("text", defaults.String("c"));
        Assert.Null(defaults.String("missing"));
        Assert.Throws<SwiftTrapException>(() => defaults.String("b"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    public void The_typed_getters_return_nil_for_another_type()
    {
        using var runtime = new TestRuntime();
        var defaults = UserDefaults.Standard.Current;
        defaults.Set("list", new List<object> { "a", "b" });
        defaults.Set("mixed", new List<object> { "a", 1L });
        defaults.Set("blob", new byte[] { 1, 2 });
        Assert.Equal(["a", "b"], defaults.StringArray("list")!);
        Assert.Null(defaults.StringArray("mixed"));
        Assert.Equal(2, defaults.Array("mixed")!.Count);
        Assert.Equal([1, 2], defaults.Data("blob")!);
        Assert.Null(defaults.Data("list"));
        Assert.Null(defaults.Dictionary("list"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    public void An_unbound_store_traps_so_settings_are_never_silently_lost()
    {
        Assert.Throws<SwiftTrapException>(() => UserDefaults.Standard.Current.Object("any"));
    }
}
