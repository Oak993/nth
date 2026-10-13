namespace Marrow.UI.Tests.NthUi;

/// <summary>ED-NEW-web-w16b-1: what each piece of chart work costs, in milliseconds, on the wall clock and in processor time.</summary>
internal sealed record NthUiChartTimings(double PlaceWall, double PlaceCpu, double ChangeWall, double ChangeCpu, double PaintWall, double PaintCpu, double GlideWall, double GlideCpu);
