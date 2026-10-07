// PORT-ONLY: locked v22 makeInk, :220-284. Stam projection/advection, confinement and negative buoyancy.
// Float32 storage and double expressions match the executable page. ED-NEW-models-palette-18.
namespace Marrow.Core.Port.ModelsPalette;
public sealed class PaletteInk
{
    public int NX {get;} public int NY {get;}
    public float[] Density {get;} public float[] U {get;} public float[] V {get;}
    private readonly float[] u0,v0,d0,p,div,curl;private double time;
    public bool Releasing {get;set;}=true;
    public PaletteInk(int nx,int ny)
    {
        if(nx<4||ny<3)throw new ArgumentOutOfRangeException(nameof(nx));NX=nx;NY=ny;var size=(nx+2)*(ny+2);
        Density=new float[size];U=new float[size];V=new float[size];u0=new float[size];v0=new float[size];d0=new float[size];p=new float[size];div=new float[size];curl=new float[size];
    }
    public int Index(int i,int j)=>i+(NX+2)*j;
    private void Boundary(int b,float[] x)
    {
        for(var i=1;i<=NX;i++){x[Index(i,0)]=b==2?-x[Index(i,1)]:x[Index(i,1)];x[Index(i,NY+1)]=b==2?-x[Index(i,NY)]:x[Index(i,NY)];}
        for(var j=1;j<=NY;j++){x[Index(0,j)]=b==1?-x[Index(1,j)]:x[Index(1,j)];x[Index(NX+1,j)]=b==1?-x[Index(NX,j)]:x[Index(NX,j)];}
    }
    private double Sample(float[] a,double x,double y)
    {
        x=Math.Clamp(x,.5,NX+.5);y=Math.Clamp(y,.5,NY+.5);var i=(int)x;var j=(int)y;var s=x-i;var t=y-j;
        return (1-s)*((1-t)*a[Index(i,j)]+t*a[Index(i,j+1)])+s*((1-t)*a[Index(i+1,j)]+t*a[Index(i+1,j+1)]);
    }
    public (double U,double V,double D) Velocity(double x,double y)=>(Sample(U,x,y),Sample(V,x,y),Sample(Density,x,y));
    private void Advect(int b,float[] into,float[] from,float[] uu,float[] vv,double dt)
    {for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++)into[Index(i,j)]=(float)Sample(from,i-dt*uu[Index(i,j)],j-dt*vv[Index(i,j)]);Boundary(b,into);}
    private void Project()
    {
        for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++){div[Index(i,j)]=(float)(-.5*((double)U[Index(i+1,j)]-U[Index(i-1,j)]+V[Index(i,j+1)]-V[Index(i,j-1)]));p[Index(i,j)]=0;}
        Boundary(0,div);Boundary(0,p);
        for(var k=0;k<24;k++){for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++)p[Index(i,j)]=(float)(((double)div[Index(i,j)]+p[Index(i-1,j)]+p[Index(i+1,j)]+p[Index(i,j-1)]+p[Index(i,j+1)])/4);Boundary(0,p);}
        for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++){U[Index(i,j)]=(float)(U[Index(i,j)]-.5*((double)p[Index(i+1,j)]-p[Index(i-1,j)]));V[Index(i,j)]=(float)(V[Index(i,j)]-.5*((double)p[Index(i,j+1)]-p[Index(i,j-1)]));}
        Boundary(1,U);Boundary(2,V);
    }
    public void Step(double dt)
    {
        time+=dt;var sc=NX/360d;
        if(Releasing)
        {
            var puff=.35+.65*Math.Pow(Math.Max(0,Math.Sin(time*2.3)),2);var jc=(NY+1)/2d+Math.Sin(time*1.7)*NY*.18+Math.Sin(time*4.1)*NY*.06;var deep=Math.Max(3,(int)Math.Floor(NX/90d+.5));
            for(var j=1;j<=NY;j++)
            {
                var w=Math.Exp(-Math.Pow((j-jc)/(NY*.2),2));
                for(var i=NX-deep;i<=NX;i++){var k=Index(i,j);U[k]=(float)(U[k]+(-130*sc*puff*w-U[k])*.35);V[k]=(float)(V[k]+Math.Sin(time*6+j)*.7*w);Density[k]=(float)(Density[k]+5*w*puff*dt*4*(14d/NY));}
            }
        }
        for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++){var k=Index(i,j);U[k]=(float)(U[k]-6*sc*dt*2);V[k]=(float)(V[k]+5*NY/14d*dt*Math.Min(2,Density[k]));}
        for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++)curl[Index(i,j)]=(float)(.5*((double)V[Index(i+1,j)]-V[Index(i-1,j)]-U[Index(i,j+1)]+U[Index(i,j-1)]));
        for(var j=2;j<NY;j++)for(var i=2;i<NX;i++)
        {
            var nx=(Math.Abs((double)curl[Index(i+1,j)])-Math.Abs(curl[Index(i-1,j)]))*.5;var ny=(Math.Abs((double)curl[Index(i,j+1)])-Math.Abs(curl[Index(i,j-1)]))*.5;var len=Math.Sqrt(nx*nx+ny*ny)+1e-5;nx/=len;ny/=len;
            var w=curl[Index(i,j)];var k=Index(i,j);U[k]=(float)(U[k]+15*dt*ny*w);V[k]=(float)(V[k]-15*dt*nx*w);
        }
        Boundary(1,U);Boundary(2,V);Project();Array.Copy(U,u0,U.Length);Array.Copy(V,v0,V.Length);Advect(1,U,u0,u0,v0,dt);Advect(2,V,v0,u0,v0,dt);Project();
        Array.Copy(Density,d0,Density.Length);var a=.000015*NX*NY*dt;
        for(var k=0;k<4;k++){for(var j=1;j<=NY;j++)for(var i=1;i<=NX;i++)Density[Index(i,j)]=(float)((d0[Index(i,j)]+a*((double)Density[Index(i-1,j)]+Density[Index(i+1,j)]+Density[Index(i,j-1)]+Density[Index(i,j+1)]))/(1+4*a));Boundary(0,Density);}
        Array.Copy(Density,d0,Density.Length);Advect(0,Density,d0,U,V,dt);
    }
}
