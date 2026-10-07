// PORT-ONLY: selects observable stroke/glow/ghost samples recorded by the page oracle, without duplicating its geometry maths.
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.Core.Tests.Fixtures;
internal static class HandshakeFrameSamples
{
    internal static double[] Of(HandshakeFrame frame)
    {
        var values=new List<double> {frame.Strokes.Count,frame.Glows.Count,frame.Ghost.X,frame.Ghost.Y,frame.Ghost.Alpha,frame.Ghost.Scale};
        foreach(var i in new[]{0,(frame.Strokes.Count-1)/2,frame.Strokes.Count-1}.Distinct().Where(i=>i>=0&&i<frame.Strokes.Count))
        {
            var p=frame.Strokes[i];values.AddRange([p.Alpha,p.Width,p.Points.Count]);
            foreach(var k in new[]{0,(p.Points.Count-1)/2,p.Points.Count-1}.Distinct().Where(k=>k>=0&&k<p.Points.Count))
                values.AddRange([p.Points[k].X,p.Points[k].Y,p.Points[k].Move?1:0]);
        }
        foreach(var glow in frame.Glows)values.AddRange([glow.X,glow.Y,glow.Radius,glow.R,glow.G,glow.B,glow.Alpha]);
        return values.ToArray();
    }
}
