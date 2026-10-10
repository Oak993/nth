// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-guards). The two idle-limit settings ("Cool idle chats after", "Learn when to cool")
// as Settings › Ignis and the Kindle popover both draw them (owner 2026-10-10: "both places, one source of truth"): the same
// menu choices and the same switch, each reading and writing IgnisSettings, so a change in one view shows in the other.
using System.Globalization;
using Avalonia.Automation;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Ignis.Settings;

internal static class IgnisCoolingControls
{
    /// <summary>The menu id of "Never".</summary>
    public const string NeverId = "never";

    /// <summary>The menu id of a limit: its hours, or <see cref="NeverId"/>.</summary>
    public static string Id(int? hours) => hours is { } h ? h.ToString(CultureInfo.InvariantCulture) : NeverId;

    /// <summary>The limit a menu id names; false for an id that is not one.</summary>
    public static bool TryHours(string id, out int? hours)
    {
        hours = null;
        if (id == NeverId) return true;
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h < 1) return false;
        hours = h;
        return true;
    }

    /// <summary>1, 2, 4, 6, 8, 12, 24 hours and Never; a value from a settings file that is not a choice is listed in its place.</summary>
    public static IReadOnlyList<(string Id, string Text)> Options(int? current)
    {
        var hours = current is { } h && !IgnisSettings.CoolAfterChoices.Contains(h) ? IgnisSettings.CoolAfterChoices.Append(h).Order().ToList() : IgnisSettings.CoolAfterChoices;
        return [.. hours.Select(h => (Id(h), IgnisSettingsWords.CoolAfter(h))), (NeverId, IgnisSettingsWords.Never)];
    }

    /// <summary>
    /// The "Cool idle chats after" menu. <paramref name="read"/> is the view's observed copy of the preferences, so the shown
    /// value follows a change made anywhere; a pick writes IgnisSettings.
    /// </summary>
    public static CzMenuButton Menu(IgnisSettings settings, Func<IgnisPreferences> read)
    {
        var current = read().CoolAfterHours;
        var menu = new CzMenuButton(IgnisSettingsWords.RowCoolAfter, Options(current), Id(current));
        AutomationProperties.SetName(menu, IgnisSettingsWords.RowCoolAfter);
        menu.SelectionChanged += id =>
        {
            if (TryHours(id, out var hours) && settings.Value.CoolAfterHours != hours) settings.Update(p => p with { CoolAfterHours = hours });
        };
        menu.Track(() => read().CoolAfterHours, hours => { if (menu.SelectedId != Id(hours)) menu.SetOptions(Options(hours), Id(hours)); });
        return menu;
    }

    /// <summary>The "Learn when to cool" switch, observed as the menu is.</summary>
    public static SettingsSwitch Switch(IgnisSettings settings, Func<IgnisPreferences> read)
    {
        var toggle = new SettingsSwitch(new Binding<bool>(() => read().LearnCooling, on =>
        {
            if (settings.Value.LearnCooling != on) settings.Update(p => p with { LearnCooling = on });
        }));
        AutomationProperties.SetName(toggle, IgnisSettingsWords.RowLearnCooling);
        return toggle;
    }
}
