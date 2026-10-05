// PORT-ONLY: first-release feature unit SK-15 (DL-17, ONDEMAND-PLAN.md 3.8 and section 6). "Reset skills and tools" in the page's foot: the first press asks (what it removes and what it keeps, then Reset and a cross), the second runs it,
// the same two steps as the trash of a row (Q-V-13 B). The two states swap by crossfade and the explanation folds open (OD-0076).
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class ResetArea : Decorator
{
    private readonly LibraryModel model;
    private readonly CrossfadeSlot slot = new();
    private readonly CzFoldRow why;
    private bool? asked;

    public ResetArea(LibraryModel model)
    {
        this.model = model;
        var explanation = LibraryUi.Text(LibraryWords.ResetWhy, 12, secondary: true, wrap: true);
        why = new CzFoldRow(explanation, false);
        slot.HorizontalAlignment = HorizontalAlignment.Left;
        Child = new StackPanel { Spacing = 8, Children = { slot, why } };
        Apply(model.Controller.ResetAsked);
    }

    public bool? Asked => asked;

    public bool WhyOpen => why.Open;

    public Control? Live => slot.Live;

    public void Apply(bool isAsked)
    {
        why.Open = isAsked;
        if (asked == isAsked) return;
        var first = asked is null;
        asked = isAsked;
        var controller = model.Controller;
        Control next;
        if (isAsked)
        {
            next = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    LibraryButtons.Make(LibraryWords.ResetConfirm, () => _ = controller.ConfirmReset(), ButtonLook.Danger, "reset-confirm"),
                    LibraryButtons.Glyph("xmark", LibraryWords.Cancel, "reset-cancel", controller.CancelReset),
                },
            };
        }
        else next = LibraryButtons.Make(LibraryWords.ResetAction, controller.AskReset, ButtonLook.Plain, "reset");
        slot.Show(next, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
    }
}
