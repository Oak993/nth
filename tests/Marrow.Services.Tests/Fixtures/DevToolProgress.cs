namespace Marrow.Services.Tests.Fixtures;

public sealed class DevToolProgress : IProgress<double?>
{
    public List<double?> Values { get; } = [];
    public void Report(double? value) => Values.Add(value);
}
