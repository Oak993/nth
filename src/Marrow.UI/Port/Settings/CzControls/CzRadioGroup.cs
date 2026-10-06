// PORT-ONLY CZ-1: shared roving-tab radio group (CZ-12.1, CZ-12.5, CZ-15).
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
namespace Marrow.UI.Port.Settings;

public class CzRadioGroup : StackPanel
{
    public IReadOnlyList<CzRadioButton> Buttons { get; }
    public string SelectedId { get; private set; }
    public event Action<string>? SelectionChanged;
    /// <summary>CZ-10.2: every enabled user pick, including the current one. Showing a stored selection raises nothing here.</summary>
    public event Action<string>? SelectionInvoked;
    private bool dimmed;
    private readonly CzNumber opacity;
    public bool Dimmed
    {
        get => dimmed;
        set { dimmed = value; IsEnabled = !value; opacity.To(value ? .5 : 1, CzMotion.Ease(.3)); UpdateSelection(); }
    }
    /// <param name="selected">The pick, one of the options; null for a group that holds none (CZ-10.1: of the Working dots' Classic and Signature lines at most one holds the pick).</param>
    public CzRadioGroup(string name, IReadOnlyList<(string Id, string Text)> options, string? selected)
    {
        if (options.Count == 0 || selected is not null && !options.Any(x => x.Id == selected)) throw new ArgumentException("The selected option must belong to a nonempty group.", nameof(selected));
        if (options.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != options.Count) throw new ArgumentException("Option IDs must be unique.", nameof(options));
        Orientation = Orientation.Horizontal; HorizontalAlignment = HorizontalAlignment.Left;
        opacity = new CzNumber(new CzMotion(this), 1, v => Opacity = v);
        AutomationProperties.SetName(this, name); SelectedId = selected ?? "";
        Buttons = options.Select(x => new CzRadioButton(this, x.Id, x.Text)).ToArray();
        foreach (var button in Buttons) Children.Add(button);
        UpdateSelection();
    }
    public virtual void Select(string id, bool focus = false)
    {
        if (Dimmed || !IsEffectivelyEnabled) return;
        var button = Buttons.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));
        var changed = SelectedId != id; SelectedId = id; UpdateSelection();
        if (focus) button.Focus(NavigationMethod.Directional);
        if (changed) SelectionChanged?.Invoke(id);
        SelectionInvoked?.Invoke(id);
    }
    /// <summary>PORT-ONLY CZ-4: shows <paramref name="id"/> as the pick because the setting it mirrors changed (not a user's pick): works while the group is folded shut or dimmed, and raises <see cref="SelectionChanged"/> so the pill and cards follow; the owner of the setting ignores that echo.</summary>
    public void Show(string id)
    {
        if (Buttons.All(x => x.Id != id)) throw new ArgumentOutOfRangeException(nameof(id));
        var changed = SelectedId != id; SelectedId = id; UpdateSelection();
        if (changed) SelectionChanged?.Invoke(id);
    }

    /// <summary>PORT-ONLY CZ-5: shows that the group holds no pick because the setting it mirrors is held by another group; works while folded shut or dimmed; raises nothing.</summary>
    public void ShowNone() { SelectedId = ""; UpdateSelection(); }

    protected virtual void UpdateSelection()
    {
        foreach (var button in Buttons)
        {
            button.Picked = button.Id == SelectedId;
            // one tab stop: the pick, or the first button of a group with none
            button.Focusable = !Dimmed && (button.Picked || SelectedId.Length == 0 && button == Buttons[0]); button.IsEnabled = !Dimmed;
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (e.Handled || Dimmed) return;
        var direction = e.Key is Key.Left or Key.Up ? -1 : e.Key is Key.Right or Key.Down ? 1 : 0;
        if (direction == 0) return;
        var index = Buttons.ToList().FindIndex(x => x.Id == SelectedId);
        Select(Buttons[index < 0 ? (direction > 0 ? 0 : Buttons.Count - 1) : (index + direction + Buttons.Count) % Buttons.Count].Id, true); e.Handled = true;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzRadioGroupPeer(this);
}
