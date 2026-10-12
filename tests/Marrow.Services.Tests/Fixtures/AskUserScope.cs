using Marrow.Services.Web;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (web surfaces W25, ED-NEW-web-w25-1): ask_user on, for the calling async flow only. The directory answers
/// <see cref="Host"/> for every chat (or for none when <c>everyChat</c> is false), so the tools are offered and every card lands on the fake.
/// </summary>
public sealed class AskUserScope : IDisposable
{
    private readonly IDisposable scope;

    public AskUserScope(bool everyChat = true)
    {
        Host = new FakeAskUserHost();
        scope = AskUserDirectory.Shared.Override(new AskUserDirectory(_ => everyChat ? Host : null));
    }

    public FakeAskUserHost Host { get; }

    public void Dispose() => scope.Dispose();
}
