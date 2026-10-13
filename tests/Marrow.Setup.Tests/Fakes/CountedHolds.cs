namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// The install hold as NthCheck takes it (ED-NEW-inst-fix-1): each take is counted and gives a <see cref="FakeHold"/> that counts
/// its disposals, or nothing when <see cref="Deny"/> stands for another setup holding the root.
/// </summary>
public sealed class CountedHolds
{
    public bool Deny { get; init; }

    public int Taken { get; private set; }

    public FakeHold? Last { get; private set; }

    public IDisposable? Take(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (Deny) return null;
        Taken++;
        Last = new FakeHold();
        return Last;
    }
}
