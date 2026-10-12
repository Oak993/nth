// PORT-ONLY: v22 worker/painter :285-427, :482-541; ED-629.
// A retained worker receives bounded owed steps. Render never runs the simulation.
using System.Runtime.InteropServices;
using Marrow.Core.Port.ModelsPalette;
using SkiaSharp;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PaletteInkWorker : IDisposable
{
    private readonly PaletteInk fluid=new(480,20);private readonly PaletteInkParticles particles=new(false);private readonly Thread thread;
    private readonly AutoResetEvent wake=new(false);private readonly Lock gate=new();private int owed;private bool disposed;
    private readonly SKBitmap bitmap=new(480,20,SKColorType.Rgba8888,SKAlphaType.Unpremul);
    private readonly SKPaint paint=new(){IsAntialias=true};private readonly SKPaint tracer=new(){IsAntialias=true};
    private readonly SKPaint blur=new(){ImageFilter=SKImageFilter.CreateBlur(.8f,.8f)};
    private readonly byte[] pixels=new byte[480*20*4];private readonly SKColor[] colours;
    private readonly PaletteInkParticle[] shown=new PaletteInkParticle[700];private readonly double[] alpha=new double[700];private int shownCount;
    private double time,acc,last=double.NaN,done=double.NaN;private volatile bool finished;
    public bool Finished=>finished;
    internal PaletteInkWorker(SKColor[] row)
    {colours=row;thread=new Thread(Run){IsBackground=true,Name="nth palette ink",Priority=ThreadPriority.BelowNormal};thread.Start();}
    internal void Advance(double at)
    {
        lock(gate)
        {
            if(disposed||finished)return;
            var real=double.IsNaN(last)?1/60d:Math.Min(1/30d,Math.Max(0,at-last));last=at;acc+=real*2;
            var steps=Math.Min(4,(int)Math.Floor(acc*60));acc-=steps/60d;if(acc>1/60d)acc=1/60d;
            owed=Math.Min(4,owed+steps);if(owed>0)wake.Set();
        }
    }
    private void Run()
    {
        EcoQoS(true);
        while(true)
        {
            wake.WaitOne();int steps;lock(gate){if(disposed)return;steps=owed;owed=0;}
            for(var i=0;i<steps;i++){fluid.Step(1/60d);time+=1/60d;}
            var solid=PaintDensity();particles.Advance(fluid,steps/60d,!double.IsNaN(done));
            if(double.IsNaN(done)&&solid>=480*20*.995){done=time;fluid.Releasing=false;}
            lock(gate)
            {
                if(disposed)return;Marshal.Copy(pixels,0,bitmap.GetPixels(),pixels.Length);bitmap.NotifyPixelsChanged();shownCount=particles.Count;
                for(var i=0;i<shownCount;i++){shown[i]=particles.At(i);alpha[i]=particles.Alpha(i);}
            }
            if(!double.IsNaN(done)&&time-done>1.5)finished=true;
        }
    }
    private int PaintDensity()
    {
        var solid=0;var d=fluid.Density;
        for(var j=1;j<=20;j++)for(var i=1;i<=480;i++)
        {
            var dd=Math.Max(0,d[fluid.Index(i,j)]);var a=1-Math.Exp(-1.6*dd);if(a>.97)solid++;
            var gx=(double)d[fluid.Index(Math.Min(480,i+1),j)]-d[fluid.Index(Math.Max(1,i-1),j)];var gy=(double)d[fluid.Index(i,Math.Min(20,j+1))]-d[fluid.Index(i,Math.Max(1,j-1))];
            var edge=Math.Min(1,Math.Sqrt(gx*gx+gy*gy)*.9)*(1-a*.6);var lift=.3*(1-a)+.28*edge;var deep=.12*Math.Min(1,dd/3);
            var c=colours[Math.Min(255,(int)((i-.5)/480*256))];var k=((j-1)*480+i-1)*4;
            static byte Byte(double v)=>(byte)Math.Clamp(Math.Round(v,MidpointRounding.ToEven),0,255);
            pixels[k]=Byte((c.Red+(255-c.Red)*lift)*(1-deep));pixels[k+1]=Byte((c.Green+(255-c.Green)*lift)*(1-deep));pixels[k+2]=Byte((c.Blue+(255-c.Blue)*lift)*(1-deep));pixels[k+3]=Byte(a*255);
        }
        return solid;
    }
    internal void Draw(SKCanvas canvas,SKRect rect,float opacity)
    {
        lock(gate)
        {
            if(disposed)return;blur.Color=SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(opacity,0,1)*255));canvas.SaveLayer(rect,blur);canvas.DrawBitmap(bitmap,rect,paint);
            for(var i=0;i<shownCount;i++)
            {
                if(alpha[i]<=.02*.8)continue;var p=shown[i];var c=colours[Math.Clamp((int)Math.Round(p.X/480*255),0,255)];
                tracer.Color=new((byte)((c.Red+255)/2),(byte)((c.Green+255)/2),(byte)((c.Blue+255)/2),(byte)Math.Round(alpha[i]*255));
                canvas.DrawCircle((p.X-.5f)*rect.Width/480,(p.Y-.5f)*rect.Height/20,p.Radius,tracer);
            }
            canvas.Restore();
        }
    }
    // ED-NEW-lean-18b: the same EcoQoS as the palette engine's PaletteExecutor (Marrow.Services keeps its helper internal).
    internal static bool EcoQoS(bool on)
    {
        if(!OperatingSystem.IsWindows())return false;
        var state=new PowerThrottling{Version=1,ControlMask=on?1u:0u,StateMask=on?1u:0u};
        return SetThreadInformation(GetCurrentThread(),3,ref state,(uint)Marshal.SizeOf<PowerThrottling>());
    }
    [StructLayout(LayoutKind.Sequential)]private struct PowerThrottling{public uint Version;public uint ControlMask;public uint StateMask;}
    [DllImport("kernel32.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(IntPtr thread,int informationClass,ref PowerThrottling information,uint size);
    [DllImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();
    internal void Retire()=>ThreadPool.QueueUserWorkItem(_=>Dispose());
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;wake.Set();}
        thread.Join();bitmap.Dispose();paint.Dispose();tracer.Dispose();blur.ImageFilter?.Dispose();blur.Dispose();wake.Dispose();
    }
}
