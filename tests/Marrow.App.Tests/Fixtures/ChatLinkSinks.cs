using Avalonia.Controls;
using Marrow.App.Port.Web;
using Marrow.App.Runtime;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// W10 (ED-NEW-web-w10-2): replaces the four places a link from a chat can go (the first-time question, the default browser, the local-file opener and Visus)
/// with recorders, and puts the real ones back when disposed. For tests that drive <see cref="ChatLinks"/> and want to see where a link went.
/// </summary>
public sealed class ChatLinkSinks : IDisposable
{
    private readonly Action<ChatLinkChoice, Control?> show = ChatLinks.ShowChoice;
    private readonly Action<SwiftURL> external = ChatLinks.OpenExternal;
    private readonly Action<SwiftURL> local = ChatLinks.OpenLocal;
    private readonly Action<ThreadRuntime?, SwiftURL> visus = ChatLinks.OpenVisus;

    public ChatLinkSinks()
    {
        ChatLinks.ShowChoice = (choice, anchor) => Questions.Add((choice, anchor));
        ChatLinks.OpenExternal = url => Opened.Add("external:" + url.AbsoluteString);
        ChatLinks.OpenLocal = url => Opened.Add("local:" + url.AbsoluteString);
        ChatLinks.OpenVisus = (runtime, url) => { Opened.Add("visus:" + url.AbsoluteString); VisusRuntimes.Add(runtime); };
    }

    /// <summary>Where each link went, in order: "visus:", "external:" or "local:" and the address.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>The chat each Visus open was for.</summary>
    public List<ThreadRuntime?> VisusRuntimes { get; } = [];

    /// <summary>The first-time questions asked, with the control each was asked over.</summary>
    public List<(ChatLinkChoice Choice, Control? Anchor)> Questions { get; } = [];

    public void Dispose()
    {
        ChatLinks.ShowChoice = show;
        ChatLinks.OpenExternal = external;
        ChatLinks.OpenLocal = local;
        ChatLinks.OpenVisus = visus;
    }
}
