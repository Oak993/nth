// PORT-ONLY: locked option A's .rt small opacity .25s, CSS ease; OD-0127 / ED-618.
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PaletteDetailText : Grid
{
    private readonly CzMotion motion;
    private TextBlock? incoming;
    private CzNumber? opacity;
    public string Text=>incoming?.Text??"";
    public double IncomingOpacity=>incoming?.Opacity??1;
    public PaletteDetailText(){motion=new(this);}
    public void SetText(string text)
    {
        if(Text==text)return;
        var previous=incoming;var oldOpacity=opacity;
        foreach(var old in Children.OfType<TextBlock>().Where(b=>b!=previous).ToArray())Children.Remove(old);
        incoming=new(){Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Chrome.SecondaryText.Brush(this)};
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(incoming);Children.Add(incoming);
        var block=incoming;opacity=new(motion,previous is null||motion.Still?1:0,v=>block.Opacity=v);block.Opacity=opacity.Value;
        opacity.To(1,CzMotion.Ease(.25));oldOpacity?.To(0,CzMotion.Ease(.25));
        if(motion.Still&&previous is not null)Children.Remove(previous);
    }
}
