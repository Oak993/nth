// PORT-ONLY: locked v22 :58-62 loading overlay, same tube clock and CSS ease-in-out.
using Marrow.UI.Port;
using SkiaSharp;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PaletteLoadingPaint : IDisposable
{
    private readonly SKPaint pulse=new(){IsAntialias=true};private readonly SKPaint shim=new();
    private readonly SKPoint[] points=new SKPoint[2];private readonly uint[] colours=[0x00FFFFFF,0x66FFFFFF,0x00FFFFFF];
    private readonly float[] stops=[0,.5f,1];private static readonly Animation Ease=Animation.TimingCurve(.42,0,.58,1,1);
    internal void Draw(SKCanvas canvas,SKRect rect,double time,bool still)
    {
        var t=still?0:time;var q=Ease.Progress((t/1.1)%1);var x=rect.Width*(float)(-.4+1.5*q);var width=rect.Width*.35f;
        points[0]=new(x,0);points[1]=new(x+width,0);
        var shader=SkiaShaders.LinearGradient(points,colours,stops);
        // Loading is transient; the retained paint owns only this frame's native shader.
        try{SkiaShaders.SetShader(shim,shader);canvas.DrawRect(new SKRect(x,0,x+width,rect.Height),shim);}
        finally{SkiaShaders.SetShader(shim,0);SkiaShaders.Release(shader);}
        for(var i=0;i<3;i++)
        {
            var local=(t-i*.18)/1.1;var p=local<0?0:local%1;var h=Ease.Progress(p<.5?p*2:(1-p)*2);var radius=(float)(5*(.6+.4*h));
            pulse.Color=new(255,255,255,(byte)Math.Round(255*.8*(.35+.65*h)));canvas.DrawCircle(rect.Width*(.30f+.18f*i)+5,20,radius,pulse);
        }
    }
    public void Dispose(){shim.Dispose();pulse.Dispose();}
}
