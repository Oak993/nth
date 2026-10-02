namespace Marrow.App.Tests.Window.Native;

// ED-236: one screen sample of GlassSampler: when, the mean luma and the stripes' contrast.
internal readonly record struct GlassSample(double Milliseconds, double Mean, double Contrast);
