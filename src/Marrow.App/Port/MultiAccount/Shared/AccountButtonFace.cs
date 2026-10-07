// PORT-ONLY: locked multi-account button faces; keep PlainControls' source-faithful plain template untouched.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Shared;
internal static class AccountButtonFace
{
    internal static void Apply(Button button, double radius, bool focusRing = false)
    {
        button.Template = new FuncControlTemplate<Button>((owner, scope) =>
        {
            var content = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                [!ContentPresenter.PaddingProperty] = owner[!Button.PaddingProperty],
                [!ContentPresenter.HorizontalContentAlignmentProperty] = owner[!Button.HorizontalContentAlignmentProperty],
                [!ContentPresenter.VerticalContentAlignmentProperty] = owner[!Button.VerticalContentAlignmentProperty],
            }.RegisterInNameScope(scope);
            var face = new Border
            {
                [!Border.BackgroundProperty] = owner[!Button.BackgroundProperty],
                [!Border.BorderBrushProperty] = owner[!Button.BorderBrushProperty],
                [!Border.BorderThicknessProperty] = owner[!Button.BorderThicknessProperty],
                Child = content,
            }.RoundedRectangle(radius, RoundedCornerStyle.Continuous);
            if (!focusRing) return face;
            var ring = new Border { BorderThickness = new Thickness(2), BorderBrush = MultiAccountPalette.Accent.Brush(owner), IsHitTestVisible = false, Opacity = 0 }
                .RoundedRectangle(radius, RoundedCornerStyle.Continuous);
            var fade = new AnimatedScalar(owner, 0, value => ring.Opacity = value);
            owner.GotFocus += (_, e) => fade.Set(e.NavigationMethod == NavigationMethod.Pointer ? 0 : 1, FrameClock.IsBound ? Animation.EaseOut(duration: .1) : null);
            owner.LostFocus += (_, _) => fade.Set(0, FrameClock.IsBound ? Animation.EaseOut(duration: .1) : null);
            return new Panel { Children = { face, ring } };
        });
    }
}
