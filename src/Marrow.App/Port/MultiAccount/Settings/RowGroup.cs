// PORT-ONLY: T11 step 2. A card of grouped rows driven by a list of SettingRow: each row folds open when its parent makes it matter and shut when not (the page's dependent rows), dividers follow the rows that show (CzRowStack).
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class RowGroup : Decorator
{
    private readonly CzRowStack stack = new();
    private readonly Dictionary<string, (SettingRowView View, CzRowSlot Slot)> rows = [];

    internal RowGroup(IReadOnlyList<SettingRow> initial, Func<string, SettingRow> read, ModelPulse pulse, Action<string, string> choose, Action<string, bool> toggle, Action<string> press)
    {
        foreach (var row in initial)
        {
            var key = row.Key;
            var view = new SettingRowView(row, () => read(key), pulse, v => choose(key, v), on => toggle(key, on), () => press(key));
            rows[key] = (view, stack.Add(view, open: row.Visible));
        }
        Child = new ChromeCardHost(stack);
    }

    internal IReadOnlyDictionary<string, (SettingRowView View, CzRowSlot Slot)> Rows => rows;

    internal void Apply(IReadOnlyList<SettingRow> next)
    {
        foreach (var row in next)
        {
            if (!rows.TryGetValue(row.Key, out var entry)) continue;
            entry.View.Apply(row);
            entry.Slot.Open = row.Visible;
        }
    }
}
