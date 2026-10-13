using Marrow.Core.Models;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// DESIGN 3.7 (ED-NEW-web-w7-5, ED-NEW-web-w3b-1): W7 shipped the design's reserved list as the default of
// WebSurface.IsReservedChord; the shortcut layer (W3b) builds the same list from the user's bindings. With the default
// bindings the two must be one list, so a page keeps the same keys whether or not the shortcut layer is connected.
[Trait("Decision", "ED-NEW-web-w3b-1")]
public sealed class ReservedChordAgreementTests
{
    [Fact]
    public void The_designs_default_list_is_what_the_shortcut_layer_reserves_under_the_default_bindings()
    {
        using var runtime = new TestRuntime();
        var store = runtime.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
        runtime.OnMain(() =>
        {
            var reserved = ShortcutScopes.ReservedChords(store).ToHashSet();
            Assert.Equal(WebKeys.DefaultReserved.ToHashSet(), reserved);
            foreach (var chord in WebKeys.DefaultReserved) Assert.True(ShortcutScopes.IsReserved(store, chord), chord.Description);
        });
    }
}
