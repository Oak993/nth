using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class ManualAccountPinTests
{
    [Fact]
    public void A_popover_pin_uses_the_chosen_key_on_the_next_request_and_retains_the_live_conversation()
    {
        using var rig = new ManualAccountPinRig();
        rig.Send("Preserve the prior user instruction.");
        Assert.True(rig.Pin(rig.Target));
        Assert.Single(rig.Http.Received);
        rig.Send("Continue on my chosen account.");
        Assert.Equal("Bearer offline-source", rig.Http.Received[0].Header("Authorization"));
        Assert.Equal("Bearer offline-next", rig.Http.Received[1].Header("Authorization"));
        using var body = JsonDocument.Parse(rig.Http.Received[1].Body);
        Assert.Equal("target-model", body.RootElement.GetProperty("model").GetString());
        var context = body.RootElement.GetProperty("messages").EnumerateArray().Select(value => value.GetProperty("content").ToString()).ToArray();
        Assert.Contains(context, text => text.Contains("Preserve the prior user instruction.", StringComparison.Ordinal));
        Assert.Contains(context, text => text.Contains("Prior answer stays.", StringComparison.Ordinal));
        Assert.Same(rig.Api, LiveApiKeyLaunch.Ensure(rig.Rig));
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountID);
        Assert.Equal(rig.Source, rig.Rig.H.Main.OnMain(() => rig.Rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id));
        Assert.False(rig.Rig.H.Main.OnMain(() => rig.Rig.App.Settings.AccountHandshakeEnabled));
        rig.WaitForWrites();
        Assert.Equal(rig.Target, AppModelHarness.LoadedLibrary().Threads.Single(thread => thread.Id == rig.Rig.InitialThread.Id).ProviderAccountPinID);
        Assert.True(rig.Pin(null));
        Assert.Null(rig.Rig.Thread.ProviderAccountPinID);
        Assert.False(rig.Rig.H.Main.OnMain(() => rig.Rig.App.Settings.AccountHandshakeEnabled));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("cap")]
    [InlineData("missing-key")]
    [InlineData("foreign-service")]
    [InlineData("missing-account")]
    public void A_popover_pin_cannot_select_an_unavailable_or_foreign_account(string unavailable)
    {
        using var rig = new ManualAccountPinRig();
        rig.Send("Stay on the original account.");
        var target = rig.Target;
        rig.Rig.H.Main.OnMain(() =>
        {
            switch (unavailable)
            {
                case "off": rig.Rig.App.Settings.Accounts.SetEnabled(target, false); break;
                case "cap": rig.Rig.App.Settings.AccountKeys.SetCap(target, 0); break;
                case "missing-key": Assert.True(Marrow.Services.Providers.APIKeychain.SetKey("", rig.Rig.App.Settings.Accounts.Get(target))); break;
                case "foreign-service": target = rig.Rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0].Id; rig.Rig.App.Settings.MetaAPIKeyInput = "offline-foreign"; break;
                case "missing-account": target = Guid.NewGuid(); break;
            }
        });
        Assert.False(rig.Pin(target));
        Assert.Null(rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountID);
        Assert.Single(rig.Http.Received);
    }
}
