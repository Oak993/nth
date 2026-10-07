using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// handleLocalCommand (App/Runtime/ThreadRuntime.swift:1158-1171): "/compact" is the app's own command only for the
// providers whose supportsCompact is true; on every other provider it falls through and is sent as an ordinary slash command.
public sealed class ThreadRuntimeLocalCommandTests
{
    private static readonly ProviderKind[] Compacting =
    [
        ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Copilot, ProviderKind.Deepseek,
        ProviderKind.Meta, ProviderKind.Zai, ProviderKind.Pi,
        ProviderKind.OpenRouter, // ED-234: the seven of Provider.swift:149-154 and OpenRouter, a native API provider with a /compact of its own
        ProviderKind.Local, // ED-410
    ];

    public static TheoryData<ProviderKind> AllProviders()
    {
        var data = new TheoryData<ProviderKind>();
        foreach (var provider in Enum.GetValues<ProviderKind>()) data.Add(provider);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    [Trait("Req", "PR-RUNTIME-188")] // :1164-1167 case "/compact" where provider.supportsCompact == true; Provider.swift:146-154
    public void Compact_is_handled_locally_for_exactly_the_seven_providers_that_support_it(ProviderKind provider)
    {
        using var h = new ThreadRuntimePart12Harness();
        var intercepted = h.Main.OnMain(() =>
        {
            h.App.UpdateThread(h.Thread.Id, thread => thread with { Provider = provider });
            return ThreadRuntimeReflection.Call<bool>(h.Runtime, "HandleLocalCommand", "/compact");
        });

        h.Main.OnMain(() =>
        {
            var recent = h.App.Providers.RecentCommands.TryGetValue(SwiftEnum<ProviderKind>.RawValue(provider), out var list)
                ? list.ToArray()
                : [];
            var notices = h.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.Notice>().Select(notice => notice.Value).ToArray();
            if (Array.IndexOf(Compacting, provider) >= 0)
            {
                Assert.True(intercepted);
                Assert.Equal("compact", Assert.Single(recent)); // recordCommand("compact", for: provider)
                // compact() ran: with no session to compact it says so (an info notice).
                var notice = Assert.Single(notices);
                Assert.Equal(Notice.Level.Info, notice.NoticeLevel);
                Assert.Equal("Nothing to compact yet.", notice.Message);
            }
            else
            {
                Assert.False(intercepted); // the default arm: sent to the provider as an ordinary slash command
                Assert.Empty(recent);
                Assert.Empty(notices);
            }
        });
    }
}
