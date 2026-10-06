using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Testing;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-14 (DL-18, DL-19). A chat that has an offer flow draws its card and its caption line inline in the reply (the end of the timeline's rows, Offer A), and AppModel makes one flow per chat once the skills host is bound.
// Lead decision (ED-384 item 16): Offer A is an inline card in the reply (OD-0099, BUILD-PLAN SK-14); it replaces the earlier place above the composer.
[Collection("Headless")]
public sealed class SkillsOfferMountTests(HeadlessSession headless)
{
    private static void LoadLogin() => System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_chat_with_an_offer_flow_gets_its_card_then_its_caption_after_the_rows_and_one_without_gets_nothing() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var r = new SkillsOfferRig();
        var body = r.Turn.H.Main.OnMain(() =>
        {
            var body = new Avalonia.Controls.StackPanel { Children = { new Avalonia.Controls.Border { Height = 40 } } };
            Assert.True(SkillsOfferRows.Append(body, r.Turn.Runtime, 20));
            body.Children.Add(new Avalonia.Controls.Border { Height = 1 });
            return body;
        });
        var order = body.Children.Select(c => c.GetType()).ToArray();
        Assert.Equal([typeof(Avalonia.Controls.Border), typeof(OfferCardControl), typeof(OfferCaptionControl), typeof(Avalonia.Controls.Border)], order);
        Assert.Equal(new Avalonia.Thickness(0, 20, 0, 0), body.Children[1].Margin);
        using var without = new ThreadRuntimeTurnRig();
        var plain = new Avalonia.Controls.StackPanel();
        Assert.False(without.H.Main.OnMain(() => SkillsOfferRows.Append(plain, without.Runtime, 20)));
        Assert.Empty(plain.Children);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void With_a_bound_skills_host_AppModel_gives_each_chat_one_offer_flow_and_without_one_it_gives_none()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk14 mount " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var unbound = new ThreadRuntimeTurnRig()) Assert.Null(unbound.Runtime.SkillsOffers);
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production().WithDownloads(root));
            using var binding = SkillsSeams.Host.Override(host);
            using var bound = new ThreadRuntimeTurnRig();
            var flow = Assert.IsType<SkillsOfferFlow>(bound.Runtime.SkillsOffers);
            Assert.Same(flow, bound.H.Main.OnMain(() => bound.App.Runtime(bound.InitialThread.Id)).SkillsOffers);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
