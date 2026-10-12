using Marrow.Core.Port;

// PORT-ONLY: web surfaces W3c, the one global the pane workspace keeps (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11
// "Global: ... default ratios for new threads (the last ones the user set)"; ED-NEW-web-w3c-1). No Swift counterpart. A
// thread that has no saved workspace starts from these ratios; every ratio the user sets (a gutter drag, an arrow key) is
// written here, and a reset forgets it. Sidebar width and mode and the terminal height are the settings they already were.

namespace Marrow.App;

public sealed partial class AppSettings
{
    private const string PaneRatioDefaultsKey = "paneRatioDefaults";

    private ValueMap<string, double>? paneRatioDefaults;

    /// <summary>The gutter ratios the user set last, by gutter key (<c>chat</c>, <c>side</c>, <c>cols</c>, ...).</summary>
    public ValueMap<string, double> PaneRatioDefaults
    {
        get => paneRatioDefaults ??= ReadPaneRatioDefaults();
        set
        {
            var clean = new ValueMap<string, double>(value.Where(pair => double.IsFinite(pair.Value)));
            if (clean.Equals(PaneRatioDefaults)) return;
            paneRatioDefaults = clean;
            if (clean.Count == 0) defaults.RemoveObject(PaneRatioDefaultsKey);
            else defaults.Set(PaneRatioDefaultsKey, clean.ToDictionary(pair => pair.Key, object (pair) => pair.Value));
        }
    }

    private ValueMap<string, double> ReadPaneRatioDefaults()
    {
        if (defaults.Dictionary(PaneRatioDefaultsKey) is not { } stored) return ValueMap<string, double>.Empty;
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in stored)
            if (Plist.AsDouble(value) is { } number && double.IsFinite(number)) map[key] = number;
        return new ValueMap<string, double>(map);
    }
}
