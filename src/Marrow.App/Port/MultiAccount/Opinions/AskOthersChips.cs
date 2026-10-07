// PORT-ONLY: locked page :4811, :886-888. Chips inside the compose box, state changes crossfade.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class AskOthersChips : Decorator
{
    private readonly CrossfadeSlot slot = new();
    internal string TextShown { get; private set; } = "";
    internal AskOthersChips() { Child = slot; IsVisible = false; }
    internal void Apply(string text)
    {
        if (TextShown == text) return; TextShown = text;
        slot.Show(text.Length == 0 ? new Border() : LibraryUi.Panel(LibraryUi.Text(text, 11), 10, .06, new Thickness(7, 2, 6, 2)), FrameClock.IsBound ? Animation.EaseOut(duration: CapsuleMotion.Reduce(this) ? .16 : .24) : null);
        IsVisible = true; // outgoing chips finish their fade; the empty live slot has no extent.
    }
}
