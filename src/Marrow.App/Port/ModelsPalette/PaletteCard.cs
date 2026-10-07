// PORT-ONLY: C25 title/glow are driven by the retained tube clock, not a second animation.
// ED-NEW-models-palette-17. The card preserves its numeric-text morph and seven-second gradient drift.
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Port.ModelsPalette;
using Marrow.UI.Port;
namespace Marrow.App.Views.Composer;
public sealed partial class EffortSliderCard
{
    internal void SetPalette(PaletteTrackTarget? target)
    {
        var slider=this.GetLogicalDescendants().OfType<EffortSlider>().FirstOrDefault();
        var title=this.GetLogicalDescendants().OfType<MorphingTextBlock>().FirstOrDefault();
        if(slider is null)return;
        slider.SetPalette(target);
        if(title is not null&&target is not null)slider.SetPaletteTitle(title,target);
    }
}
