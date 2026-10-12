using Marrow.App.Tray;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-6: the tray icon as a test sees it: what was shown, in what state, with which menu, and when it was hidden.</summary>
internal sealed class FakeTrayHost : ITrayHost
{
    public List<TrayModel> Shown { get; } = [];

    public int Hides { get; private set; }

    public bool Disposed { get; private set; }

    public TrayModel? Last => Shown.Count > 0 ? Shown[^1] : null;

    /// <summary>The labels of the menu now, groups separated by "-".</summary>
    public string Menu(TrayModel? model = null) =>
        string.Join("|", (model ?? Last!).Groups.Select(g => string.Join("|", g.Items.Select(i => i.Label + (i.Enabled ? "" : " (off)")))).Select(g => g));

    /// <summary>A click on the item with this id in the menu now.</summary>
    public void Click(string id) => Last!.Groups.SelectMany(g => g.Items).Single(i => i.Id == id).Run();

    public void Show(TrayModel model) => Shown.Add(model);

    public void Hide() => Hides++;

    public void Dispose() => Disposed = true;
}
