using Avalonia.Controls;
using Marrow.App.Port.Settings;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A page that asks for the Settings chrome row and supplies one accessory (SA-1: ISettingsPageChrome).</summary>
internal sealed class ChromePageSample : Decorator, ISettingsPageChrome
{
    public bool HasChromeControls => true;

    public IReadOnlyList<Control> ChromeAccessories { get; } = [new Border { Width = 10, Height = 10 }];
}
