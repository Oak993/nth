// PORT-ONLY: MA-3b-6. Button/list/chips and OD-0120 glass confirmation over the job-0 seam; W owns the send adapter.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class AskOthersView : Decorator
{
    private readonly AskOthersModel model;
    private readonly AskOthersPresentation state = new();
    internal Button Button { get; }
    internal AskOthersChips Chips { get; } = new();
    internal Popup ListPopup { get; }
    internal Popup ConfirmPopup { get; }
    internal AskOthersList? List { get; private set; }
    internal OtherServiceConfirmation? Confirmation { get; private set; }
    internal string? LastAskedNote => model.LastAskedNote;
    internal event Action<string, Guid?>? Sent;
    internal AskOthersView(AskOthersModel model)
    {
        this.model = model;
        Button = PlainControls.Button(new AskOthersGlyph(), () => state.List = !state.List);
        Button.Width = Button.Height = 28; Button.Focusable = true; Button.Named(model.ButtonAria, "ask-others-button"); ToolTip.SetTip(Button, model.ButtonTitle);
        ListPopup = PlainControls.Popover(Button, new Binding<bool>(() => state.List, v => state.List = v), () => List = new(model, () => state.List = false));
        ConfirmPopup = PlainControls.Popover(Button, new Binding<bool>(() => state.Confirm, v => { state.Confirm = v; if (!v && model.Confirmation is not null && !model.Busy) _ = model.ConfirmAsync(false); }), () =>
            Confirmation = new(model.Confirmation?.Text ?? "", "Ask others", model.SetDontAskAgain, accepted => MainActor.Spawn(async () => { await model.ConfirmAsync(accepted); Changed(); })));
        Child = new Panel { Children = { Button, ListPopup, ConfirmPopup } };
        AttachedToVisualTree += (_, _) => { model.Changed += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => { model.Changed -= Changed; state.List = state.Confirm = false; };
        Refresh();
    }
    internal async Task<AskSendResult> SendAsync(string message, CancellationToken ct = default)
    {
        state.List = false; var result = await model.SendAsync(message, ct); Changed(); return result;
    }
    private void Changed() { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
    private Guid? notified;
    private void Refresh()
    {
        Button.IsVisible = model.Shown; Button.IsEnabled = !model.Busy;
        Chips.Apply(model.ChipsText, model.Picked.Select(id => model.Rows.Single(r => r.Account.Id == id).Account).ToArray());
        if (!model.Shown) state.List = false;
        if (model.Confirmation is not null) state.List = false;
        state.Confirm = model.Confirmation is not null;
        if (!state.Confirm) Confirmation = null;
        if (model.LastRound is { } round && round != notified && model.LastAskedNote is { } note) { notified = round; Sent?.Invoke(note, round); }
    }
}
