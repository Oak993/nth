namespace Marrow.Services.Tests.Port.Skills.Pipelines;
internal sealed class PipelineProgress(List<double?> reports) : IProgress<double?>
{
    public void Report(double? value) => reports.Add(value);
}
