// PORT-ONLY: the literal SVG geometry from approved openrouter-picker/Main.dc.html; ED-NEW-models-palette-5.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PickerGlyph : Control
{
    private readonly string kind;
    public PickerGlyph(string kind){this.kind=kind;Width=kind=="back"?12:kind=="search"?13:14;Height=Width;VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center;}
    public override void Render(DrawingContext context)
    {
        var ink=OpenRouterPicker.Brush(.957,.965,.984,kind=="check"?1:kind=="clear"?.5:.6);
        var thickness=kind switch {"back"=>1.8,"search"=>1.7,"check"=>2,_=>1.5};
        var pen=new Pen(ink,thickness,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round);
        if(kind=="search")
        {
            using(context.PushTransform(Matrix.CreateScale(13d/14,13d/14)))
            {context.DrawEllipse(null,pen,new Point(6,6),4.2,4.2);context.DrawLine(pen,new Point(9.2,9.2),new Point(12.3,12.3));}
        }
        else if(kind=="clear")
        {
            context.DrawEllipse(ink,null,new Point(7,7),6.5,6.5);var cut=new Pen(new SolidColorBrush(Color.Parse("#262a32")),1.5,lineCap:PenLineCap.Round);
            context.DrawLine(cut,new Point(4.8,4.8),new Point(9.2,9.2));context.DrawLine(cut,new Point(9.2,4.8),new Point(4.8,9.2));
        }
        else context.DrawGeometry(null,pen,StreamGeometry.Parse(kind=="back"?"M7.5,2.5 L4,6 L7.5,9.5":"M2.8,7.4 L5.6,10.2 L11.2,4"));
    }
}
