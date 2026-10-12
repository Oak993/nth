// PORT-ONLY: the Tactus app icon source on fakes (ED-NEW-pcctl-46): an icon per key, and every key it was asked for.
using Avalonia.Media;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Fixtures;

public sealed class PcFakeAppIcons : IPcAppIconSource
{
    private readonly Dictionary<string, IImage> icons = new(StringComparer.Ordinal);

    public List<string> Asked { get; } = [];

    public PcFakeAppIcons With(string key, IImage icon)
    {
        icons[key] = icon;
        return this;
    }

    public IImage? Icon(string key)
    {
        Asked.Add(key);
        return icons.GetValueOrDefault(key);
    }
}
