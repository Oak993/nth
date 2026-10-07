namespace Marrow.Services.Tests.Port.Skills.Custom;

internal sealed class CustomSourceProgress : IProgress<double>
{
    public void Report(double value) { }
}
