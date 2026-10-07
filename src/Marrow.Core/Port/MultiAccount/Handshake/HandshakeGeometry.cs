// PORT-ONLY: page geo/phaseVals/drawA :3069-3160, concept A neural filaments.
namespace Marrow.Core.Port.MultiAccount.Handshake;
public static class HandshakeGeometry
{
    public static double Smooth(double a,double b,double x) {var t=Math.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);}
    public static double Ease(double x)=>x<.5?4*x*x*x:1-Math.Pow(-2*x+2,3)/2;
    private static double Frac(double x)=>x-Math.Floor(x);
    public static HandshakePoint Arc(double width,double height,double s)
    {
        var cx=width-46;var cy=height-26;var gx=cx-72;var gy=cy-56;var length=Math.Sqrt(72*72+56*56);
        var px=(cx+gx)/2-56/length*20;var py=(cy+gy)/2+72/length*20;var u=1-s;
        return new(u*u*cx+2*u*s*px+s*s*gx,u*u*cy+2*u*s*py+s*s*gy);
    }
    public static HandshakePoint Normal(double s)
    {
        var length=Math.Sqrt(72*72+56*56);var px=-36-56/length*20;var py=-28+72/length*20;
        var x=2*(1-s)*px+2*s*(-72-px);var y=2*(1-s)*py+2*s*(-56-py);var l=Math.Sqrt(x*x+y*y);if(l==0)l=1;
        return new(-y/l,x/l);
    }
    public static HandshakeFrame Draw(double width,double height,double milliseconds,bool dark,bool reduceMotion,
        (byte R,byte G,byte B)? outgoing=null,(byte R,byte G,byte B)? incoming=null)
    {
        var t=double.IsFinite(milliseconds)?Math.Max(0,milliseconds):0;
        var c1=outgoing??(240,105,90);var c2=incoming??(58,214,168);
        var sb=Smooth(1000,1600,t);var lk=Smooth(2000,3000,t);var lkl=Math.Clamp((t-2000)/1000,0,1);
        var sy=Smooth(3000,3800,t);var sw=Math.Clamp((t-4400)/620,0,1);var post=Smooth(4900,5700,t);var swe=Ease(sw);var end=1-swe;
        var strokes=new List<HandshakeStroke>();var glows=new List<HandshakeGlow>();
        void Glow(HandshakePoint point,double radius,double r,double g,double b,double alpha) {if(alpha>.01)glows.Add(new(point.X,point.Y,radius,r,g,b,alpha));}
        var gp=Arc(width,height,end);var ghost=new HandshakeGhost(gp.X,gp.Y,sb*(1-Smooth(.75,1,sw)),1-.25*swe);
        if(reduceMotion)
        {
            var alpha=.25*lk+.5*sy;
            if(alpha>.01&&sw<1)strokes.Add(new(1-sw,1.4,Enumerable.Range(0,31).Select(k=>Arc(width,height,k/30.0) with {Move=k==0}).ToArray()));
            var g=Arc(width,height,1);return new(strokes,glows,new(g.X,g.Y,sb*(1-sw),1));
        }
        var grow=Math.Max(.14*sb,Smooth(0,.55,lkl));var braid=Smooth(.3,1,lkl);var pres=sb*(.5+.5*lk)*(1-Smooth(.85,1,sw));
        Glow(Arc(width,height,0),24,c1.R+(c2.R-c1.R)*sy,c1.G+(c2.G-c1.G)*sy,c1.B+(c2.B-c1.B)*sy,.16*(lk+sy)*(1-post));
        var half=grow*.5;
        for(var i=0;i<9;i++)
        {
            var ph=i*Math.PI*2/9;var fq=2.1+i*.37;var points=new List<HandshakePoint>();var pen=false;
            for(var k=0;k<=48;k++)
            {
                var s=k/48.0;var visible=(s<=half||s>=1-half)&&s<=end+.001;
                if(!visible){pen=false;continue;}
                var env=Math.Sin(Math.PI*s);
                var loose=(1-braid)*11*env*Math.Sin(fq*s*6.283+t*.0021*(1+i*.13)+ph)*(.6+.4*Math.Sin(t*.0013+i));
                var tight=braid*(2.4-1.4*sy)*env*Math.Sin(s*24-t*.0065+ph);var b=Arc(width,height,s);var n=Normal(s);var offset=loose+tight;
                points.Add(new(b.X+n.X*offset,b.Y+n.Y*offset,!pen));pen=true;
            }
            strokes.Add(new((dark?.42:.55)*pres*(.55+.45*Math.Sin(t/900+i)),dark?.6:.75,points));
        }
        var core=Smooth(.6,1,lkl)*(1-Smooth(.8,1,sw));
        if(core>.01)
        {
            var beat=.75+.25*Math.Sin(t/170);
            strokes.Add(new((.35+.35*sy*beat)*core,1.2+.6*sy,Enumerable.Range(0,31).Select(k=>Arc(width,height,k/30.0*end) with {Move=k==0}).ToArray()));
        }
        var pa=Smooth(.3,.6,lkl)*(1-Smooth(0,.4,sw));
        if(pa>.01)
        {
            var locked=Smooth(.55,1,lkl);var drift=(1-locked)*.37;
            for(var j=0;j<2;j++)
            {
                var so=Frac(t/720+j/2.0);var si=1-Frac(t/720+j/2.0+drift);var meet=Math.Max(0,1-Math.Abs(so-si)/.08)*locked;
                foreach(var (s,c) in new[]{(so,c1),(si,c2)})
                {
                    var point=Arc(width,height,s);Glow(point,8+meet*6,c.R,c.G,c.B,(.55+.4*meet)*pa);Glow(point,2.2,255,255,255,.85*pa);
                }
            }
        }
        return new(strokes,glows,ghost);
    }
}
