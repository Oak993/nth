using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-review-9")]
public sealed class WebReviewLiveAnswerTests
{
    [Fact]
    public async Task A_late_saved_lookup_must_not_replace_a_live_update()
    {
        var chat = Guid.NewGuid();
        var lookup = new TaskCompletionSource<LiveViewRecord?>();
        var host = new LiveAnswerHost(lookup: (_, _) => lookup.Task);
        var pending = host.Find(chat, "view");
        host.Show(chat, new LiveViewRecord("view", LiveViewKind.Ui, "fresh", null, 1, false, null));
        host.Update(chat, "view", "newer", null, null);
        lookup.SetResult(new LiveViewRecord("view", LiveViewKind.Ui, "old", null, 1, false, null));
        Assert.Equal("newer", (await pending)!.Text);
        Assert.Equal("newer", (await host.Find(chat, "view"))!.Text);
        Assert.Equal(2, (await host.Find(chat, "view"))!.Version);
    }
}
