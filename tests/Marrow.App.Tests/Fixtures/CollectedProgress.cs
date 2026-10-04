namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (UE-5): the progress values an update item reported, in order.
public sealed class CollectedProgress : IProgress<double?>
{
    public List<double?> Values { get; } = [];

    public void Report(double? value) => Values.Add(value);
}
