using Avalonia.Controls;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests;

// OD-0088, OD-0075 (ED-250): the accent is a preference, stored under "accent" (SPEC 4.1), the Windows accent by default;
// it is independent of the theme and mirrored into AccentChoice, so ThemeManager.Spec and Chrome.accent carry it.
[Collection("Headless")]
public sealed class AppSettingsAccentTests(HeadlessSession headless)
{
    private static void WithSettings(Action<AppSettingsScope, AppSettings> body)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() => body(scope, new AppSettings()));
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_accent_defaults_to_windows_writes_nothing_until_a_pick_and_is_kept_across_a_relaunch() => WithSettings((scope, settings) =>
    {
        Assert.Equal("windows", settings.Accent);
        Assert.Equal("windows", AccentChoice.Current);
        Assert.Null(scope.Defaults.Object("accent"));
        Assert.Empty(scope.Defaults.Writes);

        settings.Accent = "orchid";
        Assert.Equal("orchid", scope.Defaults.Object("accent"));
        Assert.Equal("orchid", AccentChoice.Current);
        scope.Relaunch();
        AccentChoice.Current = "windows";
        var relaunched = new AppSettings();
        Assert.Equal("orchid", relaunched.Accent);
        Assert.Equal("orchid", AccentChoice.Current);
    });

    [Theory]
    [InlineData("notAColour")]
    [InlineData("win")]
    [InlineData("")]
    [Trait("Decision", "ED-250")]
    public void A_stored_unknown_accent_reads_as_the_windows_accent(string stored)
    {
        using var scope = AppSettingsScope.Create();
        scope.Defaults.Seed("accent", stored);
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal("windows", settings.Accent);
            Assert.Equal("windows", AccentChoice.Current);
            settings.Accent = "nonsense";
            Assert.Equal("windows", settings.Accent);
            Assert.Equal("windows", scope.Defaults.Object("accent"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_theme_change_keeps_the_pick_and_the_default_means_the_named_themes_own_accent() => WithSettings((scope, settings) =>
    {
        settings.Theme = AppTheme.Dark;
        Assert.Null(ThemeManager.Spec.Accent); // as the source: Dark keeps the system accent
        settings.Accent = "orchid";
        settings.Theme = AppTheme.Dracula;
        Assert.Equal("orchid", settings.Accent);
        Assert.Equal("orchid", scope.Defaults.Object("accent"));
        Assert.Equal(Avalonia.Media.Color.Parse("#E298C6"), Chrome.Accent.Resolve(ThemeVariant.Dark)); // Orchid over Dracula
        settings.Accent = "windows";
        Assert.Equal(Avalonia.Media.Color.Parse("#BD93F9"), Chrome.Accent.Resolve(ThemeVariant.Dark)); // Dracula's own
        Assert.Equal(Avalonia.Media.Color.Parse("#BD93F9"), ThemeManager.Spec.Accent!.Resolve(ThemeVariant.Dark));
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_window_tint_is_the_pick_and_the_default_keeps_the_themes_raw_optional() => headless.OnUiThread(() =>
    {
        // WindowManager.swift:181-193 ThemeTint: content.tint(theme.spec.accent), nil for System, Light and Dark.
        Assert.Null(Popovers.GetTint(new ThemeTint(AppTheme.System, "windows").Body(new Border())));
        Assert.Null(Popovers.GetTint(new ThemeTint(AppTheme.Light).Body(new Border())));
        Assert.Equal(Avalonia.Media.Color.Parse("#BD93F9"), Popovers.GetTint(new ThemeTint(AppTheme.Dracula, "windows").Body(new Border()))!.Resolve(ThemeVariant.Dark));
        Assert.Same(AccentPalette.Pick("teal"), Popovers.GetTint(new ThemeTint(AppTheme.System, "teal").Body(new Border())));
        Assert.Same(AccentPalette.Pick("teal"), Popovers.GetTint(new ThemeTint(AppTheme.Dracula, "teal").Body(new Border())));
    });
}
