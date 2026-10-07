using System.Globalization;

namespace Marrow.App.Views.Composer;

public readonly record struct StrikeBubbleRise(double Cx, double Cy, double Outer, double Dur, double O, double S0, double S1, double S2,
    double X1, double Y1, double X2, double Y2, double X3, double Y3, double Gap, double Wa, double Wn, double Wp, bool Nuc);
