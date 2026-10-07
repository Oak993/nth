// PORT-ONLY: HTML 2381-2383/4462-4465; pill defaults float on the shared popover presenter.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Settings;
internal sealed class PillStepSettingsPopup : MainActorModel
{
    private bool presented;
    private bool Presented { get => Read(presented); set => Set(ref presented, value); }
    internal Popup Popup { get; }
    internal PillStepSettingsPopup(Control anchor, Func<Control> content)
    {
        Popup = PlainControls.Popover(anchor, new Binding<bool>(() => Presented, value => Presented = value), () =>
        {
            var body = content();
            body.KeyDown += (_, e) => { if (e.Key != Key.Escape) return; Presented = false; e.Handled = true; };
            return body;
        });
    }
    internal void Toggle() => Presented = !Presented;
}
