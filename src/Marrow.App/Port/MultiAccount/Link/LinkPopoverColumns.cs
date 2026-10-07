// PORT-ONLY: MA-3b-5. Adds A's column to retained T16 rows at composition time; no edits to T16's files, no drag/link-mode variant.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
namespace Marrow.App.Port.MultiAccount.Link;
internal sealed class LinkPopoverColumns : Decorator
{
    private readonly AccountPopover popover;
    private readonly LinkUpModel model;
    private readonly Func<Control?> target;
    private readonly Panel overlay = new() { ClipToBounds = false, IsHitTestVisible = false };
    private readonly Dictionary<Guid, Button> buttons = [];
    private readonly AskOthersPresentation state = new();
    private readonly Popup confirm;
    internal AccountPopover Popover => popover;
    private Panel? flightLayer;
    private OverlayLayer? ownerLayer;
    internal LinkFlyMark? Flight { get; private set; }
    internal OtherServiceConfirmation? Confirmation { get; private set; }
    internal LinkPopoverColumns(AccountPopover popover, LinkUpModel model, Func<Control?> target)
    {
        this.popover = popover; this.model = model; this.target = target;
        confirm = PlainControls.Popover(this, new Binding<bool>(() => state.Confirm, v => { state.Confirm = v; if (!v && model.Pending is not null) model.Confirm(false); }), () => Confirmation = new(model.Pending!.Name + " is another service: it will see this conversation.", "Link", v => model.DontAskAgain = v, Answer));
        confirm.Closed += (_, _) => Refresh();
        Child = new Panel { ClipToBounds = false, Children = { popover, overlay, confirm } };
        AttachedToVisualTree += (_, _) => { model.Changed += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => { model.Changed -= Changed; Flight?.Stop(); state.Confirm = false; };
    }
    private void Changed() => Dispatcher.UIThread.Post(Refresh);
    private void Refresh()
    {
        if (Flight is { Running: true } && model.FlyingAccountId is null) Flight.Stop();
        foreach (var row in popover.Rows)
        {
            if (!buttons.TryGetValue(row.AccountId, out var button))
            {
                var id = row.AccountId; button = row.LinkButton; row.LinkBound = true;
                button.Click += (_, e) => { Toggle(id); e.Handled = true; }; buttons.Add(id, button);
            }
            button.IsVisible = model.CanLink(row.AccountId); button.IsEnabled = !model.Busy && !confirm.IsOpen;
            button.Named(model.Aria(row.AccountId), "link-account-" + row.AccountId.ToString("N"));
            ToolTip.SetTip(button, model.Aria(row.AccountId));
        }
        state.Confirm = model.Pending is not null;
        if (!state.Confirm) Confirmation = null;
    }
    private void Toggle(Guid id) { if (model.RequestToggle(id)) Fly(id); Refresh(); }
    private void Answer(bool accepted) { if (model.Confirm(accepted) is { } id) Fly(id); Refresh(); }
    private void Fly(Guid id)
    {
        if (model.Accounts.FirstOrDefault(a => a.Id == id) is not { } account) return;
        var row = popover.Rows.Single(r => r.AccountId == id);
        var source = row.LinkMark; var end = target();
        if (end is null) { model.Complete(id); Refresh(); return; } // exact page missing-anchor fast path.
        Flight?.Stop();
        ownerLayer = OverlayLayer.GetOverlayLayer(end);
        flightLayer = ownerLayer is null ? overlay : new Panel { IsHitTestVisible = false, ClipToBounds = false };
        ownerLayer?.Children.Add(flightLayer);
        var a = At(source, new Point(source.Bounds.Width / 2 - 11, source.Bounds.Height / 2 - 11), flightLayer);
        var b = At(end, new Point(end.Bounds.Width - 16, end.Bounds.Height / 2 - 11), flightLayer);
        Flight = new(account.Mono, AccountMarkColors.Of(account), a, b, CapsuleMotion.Reduce(this), () => { model.Complete(id); ClearFlight(); Refresh(); }, () => { model.CancelFlight(id); ClearFlight(); });
        flightLayer.Children.Add(Flight); Flight.Start();
    }
    private static Point At(Control source, Point point, Control layer) => source.TranslatePoint(point, layer) ?? layer.PointToClient(source.PointToScreen(point));
    private void ClearFlight()
    {
        flightLayer?.Children.Clear();
        if (flightLayer is not null) ownerLayer?.Children.Remove(flightLayer);
        flightLayer = null; ownerLayer = null;
    }
}
