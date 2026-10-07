// PORT-ONLY: Q-PICKER A's mounted menu dots read the same snapshot; OD-0063/0076, ED-628.
using Avalonia.Styling;
namespace Marrow.App.Port.ModelsPalette;
internal static class PalettePickerColours
{
    internal static void Attach(OpenRouterPicker picker,ModelShades facade)
    {
        void Apply(){foreach(var row in picker.Rows)if(facade.Resolve(row.Model.Id,null,picker.ActualThemeVariant==ThemeVariant.Light) is {} shade)row.SetColour(shade.Stops[1]);picker.UpdateMakerDots();}
        picker.AttachedToVisualTree+=(_,_)=>{facade.Changed+=Apply;Apply();};
        picker.DetachedFromVisualTree+=(_,_)=>facade.Changed-=Apply;
        picker.ActualThemeVariantChanged+=(_,_)=>Apply();
    }
}
