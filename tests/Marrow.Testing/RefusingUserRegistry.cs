using Marrow.Core.Support;

namespace Marrow.Testing;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8, unit U8, ED-NEW-inst-u8-1). An <see cref="IUserRegistry"/> that reads like the one it
/// wraps and throws on every write, as a locked-down profile does (access denied unless another error is given). Nothing
/// here reaches the real registry.
/// </summary>
public sealed class RefusingUserRegistry(IUserRegistry inner, Exception? error = null) : IUserRegistry
{
    private const int AccessDenied = unchecked((int)0x80070005);

    private readonly Exception refusal = error ?? new UnauthorizedAccessException("denied") { HResult = AccessDenied };

    /// <summary>Puts a value in the wrapped <see cref="FakeUserRegistry"/> before the test refuses anything.</summary>
    public RefusingUserRegistry Seeded(string subKey, string name, object value)
    {
        (inner as FakeUserRegistry ?? throw new InvalidOperationException("Only a FakeUserRegistry can be seeded.")).Seed(subKey, name, value);
        return this;
    }

    public bool KeyExists(string subKey) => inner.KeyExists(subKey);

    public void CreateKey(string subKey) => throw refusal;

    public object? GetValue(string subKey, string name) => inner.GetValue(subKey, name);

    public void SetString(string subKey, string name, string value) => throw refusal;

    public void SetDword(string subKey, string name, int value) => throw refusal;

    public void DeleteValue(string subKey, string name) => throw refusal;
}
