using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// OD-0088 (ED-250): the Appearance page no longer hosts the source's Theme capsule (SettingsView.swift:424-426, :1155-1212);
/// its Look card's Theme row replaces it. The capsule stays the source's twin, so the tests of its own behaviour host it
/// beside the page, bound to the same setting the row was, and read the page's Theme row as before.
/// </summary>
public static class ThemePickerHost
{
    public static Control Page()
    {
        var host = new StackPanel();
        AppSettings Settings() => AppEnvironment.GetAppModel(host).Settings;
        host.Children.Add(new ThemePickerButton(new Binding<AppTheme>(() => Settings().Theme, value => Settings().Theme = value)) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
        host.Children.Add(new AppearanceSettingsPage());
        return host;
    }

    /// <summary>The Look card's Theme detail for a theme under the default accent (SPEC 2.3, 9.7).</summary>
    public static string Detail(AppTheme theme) => theme switch
    {
        AppTheme.System => "System · Windows accent",
        AppTheme.Light => "Light · Windows accent",
        AppTheme.Dark => "Dark · Windows accent",
        _ => theme.Detail(), // a named theme: "<Dark|Light> · <its accent word>", as AppTheme.swift:76-105
    };
}
