// PORT-ONLY: T46's persistent draggable model scrollbar. The app's default scroll template has no bar.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.ModelsPalette;

internal static class PickerScroll
{
    internal static void Apply(ScrollViewer viewer)
    {
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.AllowAutoHide = false;
        viewer.Fades(top: 16, bottom: 18); // PORT-ONLY (OD-0151): rows dissolve under the search pill above and fade out below the fold (ScrollEdgeFade)
        viewer.Template = new FuncControlTemplate<ScrollViewer>((v, scope) =>
        {
            var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12") };
            var presenter = new ScrollContentPresenter
            {
                Name = "PART_ContentPresenter", Background = Brushes.Transparent,
                [!ContentPresenter.ContentProperty] = v[!ContentControl.ContentProperty],
                [!ContentPresenter.PaddingProperty] = v[!TemplatedControl.PaddingProperty],
            };
            presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true });
            panel.Children.Add(presenter.RegisterInNameScope(scope));
            var bar = new ScrollBar
            {
                Name = "PART_VerticalScrollBar", Orientation = Orientation.Vertical, Width = 12,
                AllowAutoHide = false,
                Template = new FuncControlTemplate<ScrollBar>((b, names) =>
                {
                    var thumb = new Thumb
                    {
                        Name = "PART_Thumb", MinHeight = 24,
                        Template = new FuncControlTemplate<Thumb>((_, _) => new Border
                        {
                            Margin = new Thickness(3, 1),
                            Background = Chrome.SecondaryText.Opacity(.55).Brush(b),
                        }.Capsule(RoundedCornerStyle.Continuous)),
                    };
                    var track = new Track
                    {
                        Name = "PART_Track", Orientation = Orientation.Vertical,
                        IsDirectionReversed = true, Thumb = thumb.RegisterInNameScope(names),
                        [!Track.MinimumProperty] = b[!RangeBase.MinimumProperty],
                        [!Track.MaximumProperty] = b[!RangeBase.MaximumProperty],
                        [!!Track.ValueProperty] = b[!!RangeBase.ValueProperty],
                        [!Track.ViewportSizeProperty] = b[!ScrollBar.ViewportSizeProperty],
                    };
                    return track.RegisterInNameScope(names);
                }),
            };
            Grid.SetColumn(bar, 1); panel.Children.Add(bar.RegisterInNameScope(scope));
            return panel;
        });
    }
}
