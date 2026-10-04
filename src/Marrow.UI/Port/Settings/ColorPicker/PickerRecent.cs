namespace Marrow.UI.Port.Settings.ColorPicker;

/// <summary>
/// The recent colours (variant B's S.recent): "#RRGGBB" only (the alpha is not part of an entry), the newest first, no duplicate,
/// at most eight. A colour joins on a commit (pointer up, a hex entry, a recent or palette click).
/// </summary>
public sealed class PickerRecent
{
    public const int Capacity = 8;

    private readonly List<string> items;

    /// <summary>The design's opening list: AURA_DEFAULTS' two colours.</summary>
    public PickerRecent() : this(["#8E7CFF", "#38C9B4"]) { }

    public PickerRecent(IEnumerable<string> items) => this.items = items.Take(Capacity).ToList();

    public IReadOnlyList<string> Items => items;

    /// <summary>[hex].concat(recent.filter(x != hex)).slice(0, 8)</summary>
    public void Commit(string hex)
    {
        items.RemoveAll(x => x == hex);
        items.Insert(0, hex);
        if (items.Count > Capacity) items.RemoveRange(Capacity, items.Count - Capacity);
    }
}
