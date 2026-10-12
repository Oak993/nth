using Marrow.Core.Port;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W10, links from chats (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11; ED-NEW-web-w10-2). No Swift
// counterpart. One router per chat: it sends a link to Visus, to the default browser or to the local-file opener under the reader's
// choice, asks the first time, and does nothing else. The three places a link can go are handed in, so the router has no browser, no
// file system and no window of its own; the sheet that shows the question is the app's (ChatLinkSheet) and only watches Pending.
// The router holds at most one question: a link clicked while a question is open is not queued (the open question belongs to the first
// click), and an answered or cancelled question is gone, so a second answer from a button pressed twice changes nothing.

namespace Marrow.App.Port.Web;

public sealed class ChatLinkRouter(WebSettings settings, Action<SwiftURL> visus, Action<SwiftURL> external, Action<SwiftURL> local)
{
    /// <summary>The first-time question being asked, or null. It holds the first click's address and whether Ctrl was down.</summary>
    public ChatLinkChoice? Pending { get; private set; }

    /// <summary>Raised when a question opens (the app shows its sheet) and again, with null, when it is answered or cancelled.</summary>
    public event Action<ChatLinkChoice?>? PendingChanged;

    /// <summary>
    /// A link was clicked. <paramref name="control"/>: Ctrl was down (the other browser, for this link only). <paramref name="forceExternal"/>:
    /// the caller knows the link is a sign-in or an update or store link and it always goes to the default browser.
    /// Returns where the link goes; Ask when the question was opened (or is already open) and nothing has opened yet.
    /// </summary>
    public ChatLinkTarget Open(SwiftURL url, bool control = false, bool forceExternal = false)
    {
        var target = settings.Destination(url.AbsoluteString, control, forceExternal);
        if (target != ChatLinkTarget.Ask)
        {
            Dispatch(url, target);
            return target;
        }
        if (Pending is not null) return target;
        var choice = new ChatLinkChoice(settings, url, control);
        Pending = choice;
        choice.Completed += destination =>
        {
            if (!ReferenceEquals(Pending, choice)) return;
            Pending = null;
            PendingChanged?.Invoke(null);
            if (destination is { } where) Dispatch(url, where);
        };
        PendingChanged?.Invoke(choice);
        return target;
    }

    private void Dispatch(SwiftURL url, ChatLinkTarget target)
    {
        switch (target)
        {
            case ChatLinkTarget.Visus: visus(url); break;
            case ChatLinkTarget.DefaultBrowser: external(url); break;
            case ChatLinkTarget.Local: local(url); break;
        }
    }
}

/// <summary>The first-time question: "Open links in Visus or your default browser?", with Remember on until the reader clears it.</summary>
public sealed class ChatLinkChoice(WebSettings settings, SwiftURL url, bool control)
{
    private bool finished;

    /// <summary>The words of the question.</summary>
    public const string Title = "Open links in Visus or your default browser?";

    /// <summary>The address the first click asked for; the answer opens this one.</summary>
    public SwiftURL Url { get; } = url;

    /// <summary>Whether the click that opened the question held Ctrl (the answer is then inverted for this link).</summary>
    public bool Control { get; } = control;

    /// <summary>"Remember" (checked by default): the answer becomes the setting.</summary>
    public bool Remember { get; set; } = true;

    /// <summary>Whether the question has been answered or cancelled.</summary>
    public bool IsFinished => finished;

    internal event Action<ChatLinkTarget?>? Completed;

    /// <summary>Raised once, after the router has acted on the answer: the sheet that shows the question closes on it.</summary>
    public event Action? Finished;

    /// <summary>Visus or the default browser. A second answer, or one after a cancel, does nothing.</summary>
    public void Choose(bool visus)
    {
        if (finished) return;
        finished = true;
        Completed?.Invoke(settings.CompleteChoice(visus, Remember, Control));
        Finished?.Invoke();
    }

    /// <summary>Esc, Cancel, or the window going away: nothing opens and nothing is stored. A second call does nothing.</summary>
    public void Cancel()
    {
        if (finished) return;
        finished = true;
        Completed?.Invoke(null);
        Finished?.Invoke();
    }
}
