// PORT-ONLY: post-Strike intent seam; ED-NEW-models-palette-14. No new effect or clock.
using Marrow.App.Port.ModelsPalette;
namespace Marrow.App.Views.Composer;
public sealed partial class EffortSlider
{
    private PaletteTrackTarget? palette;
    private TrackEffect? paletteEffect;
    public void SetPalette(PaletteTrackTarget? target)
    {if(target is null){paletteEffect?.SetPalette(null);palette=null;paletteEffect=null;return;}palette=target;paletteEffect=null;BindPaletteEffect(currentEffect);}
    private void BindPaletteEffect(TrackEffect? effect)
    {
        if(palette is null||effect is null||ReferenceEquals(effect,paletteEffect))return;
        effect.SetPalette(palette);paletteEffect=effect;
    }
}
