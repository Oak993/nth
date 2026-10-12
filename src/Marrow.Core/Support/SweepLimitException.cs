namespace Marrow.Core.Support;

/// <summary>PORT-ONLY (ED-NEW-inst-c7-10): a stopped cleanup keeps the affected path and reports LimitReached.</summary>
internal sealed class SweepLimitException(string path) : IOException("The cleanup budget was reached: '" + path + "'.")
{
    public string KeptPath { get; } = path;
}
