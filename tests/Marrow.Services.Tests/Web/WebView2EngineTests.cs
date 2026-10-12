using System.Reflection;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.1 (ED-NEW-web-w7-1): the WebView2 engine's pure parts, read without WebView2 or a window. The real engine's
// runtime behaviour is spike (a) and (i), in WebView2EngineNativeTests (not run here).
[Trait("Decision", "ED-NEW-web-w7-1")]
[Trait("Feature", "W-7")]
public sealed class WebView2EngineTests
{
    private static readonly WebProfiles Profiles = new(WebRig.UserData, "en-US");

    [Fact]
    public void The_two_seams_into_the_SDK_exist_in_the_pinned_package()
    {
        // Both are internal to Microsoft.Web.WebView2.Core 1.0.4258.31; a package update that renames them fails here
        // before it fails in the app (the controller wrap would be lost, and with it reserved chords and Tab out).
        Assert.NotNull(WebView2Engine.ControllerConstructor);
        Assert.NotNull(WebView2Engine.RawNativeField);
        Assert.Equal(typeof(object), Assert.Single(WebView2Engine.ControllerConstructor!.GetParameters()).ParameterType);
    }

    [Fact]
    public void Every_process_failure_kind_of_the_SDK_maps_to_its_own_kind()
    {
        var map = typeof(WebView2Engine).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "Map" && m.ReturnType == typeof(WebProcessFailureKind));
        var sdk = map.GetParameters()[0].ParameterType;
        foreach (var value in Enum.GetValues(sdk))
        {
            var mapped = (WebProcessFailureKind)map.Invoke(null, [value])!;
            Assert.Equal(value.ToString(), mapped.ToString());
        }
    }

    [Fact]
    public void Every_permission_kind_of_the_SDK_maps_to_a_kind_nth_asks_about()
    {
        var map = typeof(WebView2Engine).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "Map" && m.ReturnType == typeof(WebPermissionKind));
        var sdk = map.GetParameters()[0].ParameterType;
        var mapped = Enum.GetValues(sdk).Cast<object>().ToDictionary(v => v.ToString()!, v => (WebPermissionKind)map.Invoke(null, [v])!);
        Assert.Equal(WebPermissionKind.Location, mapped["Geolocation"]);
        Assert.Equal(WebPermissionKind.FileSystemAccess, mapped["FileReadWrite"]);
        Assert.Equal(WebPermissionKind.MultipleDownloads, mapped["MultipleAutomaticDownloads"]);
        Assert.All(mapped.Where(p => p.Key != "UnknownPermission"), p => Assert.NotEqual(WebPermissionKind.Unknown, p.Value));
    }

    [Fact]
    public void Settings_follow_each_profile()
    {
        var browse = WebView2Engine.SettingsFor(Profiles.For(WebSurfaceKind.VisusTab));
        var preview = WebView2Engine.SettingsFor(Profiles.For(WebSurfaceKind.PreviewTab));
        var agent = WebView2Engine.SettingsFor(Profiles.For(WebSurfaceKind.AgentTab, @"C:\threads\one"));
        var artifact = WebView2Engine.SettingsFor(Profiles.For(WebSurfaceKind.Artifact));

        foreach (var plan in new[] { browse, preview, agent, artifact })
        {
            Assert.False(plan.AreHostObjectsAllowed);
            Assert.False(plan.AreBrowserAcceleratorKeysEnabled); // Visus handles its own keys (DESIGN 3.7)
            Assert.False(plan.AreDefaultScriptDialogsEnabled); // nth draws them (DESIGN 8.4)
            Assert.True(plan.IsReputationCheckingRequired);
            Assert.False(plan.IsNonClientRegionSupportEnabled);
        }

        Assert.True(browse.IsPasswordAutosaveEnabled && browse.IsGeneralAutofillEnabled);
        Assert.All(new[] { preview, agent, artifact }, p => Assert.False(p.IsPasswordAutosaveEnabled || p.IsGeneralAutofillEnabled));
        Assert.True(preview.AreDevToolsEnabled);
        Assert.All(new[] { browse, agent, artifact }, p => Assert.False(p.AreDevToolsEnabled));
        Assert.True(artifact.IsWebMessageEnabled);
        Assert.All(new[] { browse, preview, agent }, p => Assert.False(p.IsWebMessageEnabled));
        Assert.False(artifact.AreDefaultContextMenusEnabled || artifact.IsZoomControlEnabled || artifact.IsSwipeNavigationEnabled || artifact.IsStatusBarEnabled);
        Assert.True(browse.AreDefaultContextMenusEnabled && browse.IsZoomControlEnabled && browse.IsStatusBarEnabled);
    }

    [Fact]
    public void A_PNG_reports_its_pixel_size()
    {
        Assert.Equal((1, 1), WebView2Engine.PngSize(FakeWebEngine.OnePixelPng));
        var wide = (byte[])FakeWebEngine.OnePixelPng.Clone();
        wide[18] = 0x0F; // the width, big-endian in bytes 16 to 19, becomes 00 00 0F 00
        wide[19] = 0x00;
        Assert.Equal((0x0F00, 1), WebView2Engine.PngSize(wide));
        Assert.Equal((0, 0), WebView2Engine.PngSize([1, 2, 3]));
        Assert.Equal((0, 0), WebView2Engine.PngSize(new byte[40]));
    }

    [Theory]
    [InlineData("rgb(255, 255, 255)", 0xFFFFFFFFu)]
    [InlineData("rgb(18, 52, 86)", 0xFF123456u)]
    [InlineData("rgba(0, 0, 0, 0.5)", 0x80000000u)]
    [InlineData("rgb(10 20 30 / 50%)", 0x800A141Eu)]
    [InlineData("RGBA(1, 2, 3, 1)", 0xFF010203u)]
    public void Page_background_colours_parse_to_ARGB(string css, uint argb)
    {
        Assert.Equal(argb, WebView2Engine.ParseCssColor(css));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rgba(0, 0, 0, 0)")]
    [InlineData("transparent")]
    [InlineData("#fff")]
    [InlineData("rgb(1, 2)")]
    [InlineData("hsl(0, 0%, 0%)")]
    public void Transparent_or_unreadable_backgrounds_give_no_colour(string? css)
    {
        Assert.Null(WebView2Engine.ParseCssColor(css));
    }

    [Fact]
    public void The_minimum_runtime_is_the_planning_value_until_spike_a_confirms_it()
    {
        Assert.Equal("112.0.1722.39", WebView2Engine.MinimumVersion);
    }
}
