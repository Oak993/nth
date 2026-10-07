// PORT-ONLY: MA-3b-4, openCfg :2380-2430 and AccountPanel :2699, locked effort card reused.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Popover;
internal sealed class AccountDefaultsPanel : Decorator
{
    private readonly AccountStore store;
    private readonly Guid id;
    private readonly IReadOnlyList<ModelOption> models;
    private readonly ModelPulse pulse = new(_ => { });
    private readonly CrossfadeSlot card = new();
    private readonly CzCrossfadeText modelText = new("") { FontSize = 12.5, Secondary = true };
    private string? shownModel;
    internal AccountDefaultsPanel(AccountStore store, Guid id, string name, IReadOnlyList<ModelOption> models)
    {
        this.store = store; this.id = id; this.models = models; Width = 330;
        var picker = MenuPopover.Value("Model", "account-default-model", modelText,
            () => models.Select(m => new MenuOption(m.Id, m.ChipName, false, m.Id == Current.Model)).ToArray(), SelectModel);
        var modelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; modelRow.Children.Add(LibraryUi.Text("Model", 12.5)); Grid.SetColumn(picker, 1); modelRow.Children.Add(picker);
        Child = new Border { Padding = new Thickness(12), Child = new StackPanel { Spacing = 8, Children =
        {
            LibraryUi.Text(name, 13, weight: SwiftFont.Weight.Semibold),
            LibraryUi.Text("This account’s defaults · used whenever a thread moves to it", 11.5, secondary: true, wrap: true),
            modelRow, card,
            LibraryUi.Text("Changing the model or effort in the composer applies to this thread until the next switch.", 11, secondary: true, wrap: true),
        } } };
        AttachedToVisualTree += (_, _) => { store.Changed += Changed; Apply(); };
        DetachedFromVisualTree += (_, _) => store.Changed -= Changed;
        this.Track(() => { _ = pulse.Revision; return Current.Model; }, _ => Apply()); Apply();
    }
    private AccountDefaults Current => store.Get(id).Defaults;
    internal EffortSliderCard? Card => card.Live as EffortSliderCard;
    private void Changed() { pulse.Bump(); Apply(); }
    internal void SelectModel(string value)
    {
        if (models.FirstOrDefault(m => m.Id == value) is not { } option) return;
        store.SetDefaults(id, Current with { Model = option.Id, Effort = option.DefaultEffort, FastMode = Current.FastMode && option.SupportsFast });
    }
    private void Apply()
    {
        var option = models.FirstOrDefault(m => m.Id == Current.Model) ?? models.FirstOrDefault();
        modelText.SetText(option?.ChipName ?? Current.Model ?? "");
        if (option is null || shownModel == option.Id) return; shownModel = option.Id;
        var effortCard = new EffortSliderCard(option.ChipName, store.Get(id).Service, option.Efforts, option.DefaultEffort, option.SupportsFast,
            new Binding<string?>(() => { _ = pulse.Revision; return Current.Effort; }, v => store.SetDefaults(id, Current with { Effort = v })),
            new Binding<bool>(() => { _ = pulse.Revision; return Current.FastMode; }, v => store.SetDefaults(id, Current with { FastMode = v })),
            () => store.SetDefaults(id, Current with { Effort = option.DefaultEffort, FastMode = false }));
        effortCard.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End) || option.Efforts.Count == 0) return;
            var index = option.Efforts.ToList().IndexOf(Current.Effort ?? option.DefaultEffort ?? "medium");
            var target = e.Key switch { Key.Home => 0, Key.End => option.Efforts.Count - 1, Key.Left => index - 1, _ => index + 1 };
            store.SetDefaults(id, Current with { Effort = option.Efforts[Math.Clamp(target, 0, option.Efforts.Count - 1)] }); e.Handled = true;
        };
        card.Show(effortCard, FrameClock.IsBound ? Animation.EaseOut(duration: .25) : null);
    }
}
