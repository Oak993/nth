using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-pickerclaude-1/2 (owner 2026-10-11): "Its showing sonnet only by other providers, where is claude?". The owner's Claude chat
// (Opus 5.5) was on a provider whose Settings › Providers switch was off, and with an empty "Your models" list the picker showed that
// chat's current model alone: searching "sonnet" found Antigravity's and OpenRouter's Sonnets, never Claude's. The real list over a real
// AppModel whose CLIs cannot resolve, with the installed state's catalogue shape: Claude's lineup, Antigravity's Sonnet, OpenRouter's
// Sonnets with their batch endpoints.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pickerclaude-1")]
public sealed class ModelListClaudeLineupTests(HeadlessSession headless)
{
    private const string AdvisorName = "Sonnet 5.5 + Opus advisor";

    private static EffortListEnv Env(ProviderKind provider, string model, bool claudeOn, bool limitUsedUp = false) =>
        Make(() => Thread(provider, model), app =>
        {
            // Only the three providers of the owner's screenshot are on (the others' seeds would add their own Sonnets); Claude as given.
            foreach (var other in SwiftEnum<ProviderKind>.AllCases)
                app.Settings.SetEnabled(other is ProviderKind.Antigravity or ProviderKind.OpenRouter || other == ProviderKind.Claude && claudeOn, other);
            var registry = typeof(ProviderRegistry);
            registry.GetProperty("Statuses")!.SetValue(app.Providers, app.Providers.Statuses.Mutating(map =>
                map[ProviderKind.Claude] = new ProviderStatus { Executable = SwiftURL.Create("/offline/claude")!, Version = "2.1.296 (Claude Code)", ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }));
            if (limitUsedUp)
                registry.GetProperty("PlanLimits")!.SetValue(app.Providers, app.Providers.PlanLimits.Mutating(map => map[ProviderKind.Claude] = new PlanLimits
                {
                    PlanName = "Pro",
                    Windows = new([new PlanLimits.Window { Id = "weekly", Title = "Weekly", Percent = 100, ResetsAt = DateTimeOffset.UtcNow.AddDays(2) }]),
                }));
        },
        (ProviderKind.Claude, [Option("opus", "Opus 5.5", fast: true), Option("sonnet", "Sonnet 5.5"), Option("haiku", "Haiku 5.5"), Option("claude-opus-5", "Opus 5")]),
        (ProviderKind.Antigravity, [Option("claude-sonnet-4-6", "Claude Sonnet 4.6 (Thinking)", efforts: false)]),
        (ProviderKind.OpenRouter, [Option("anthropic/claude-sonnet-4.6", "Claude Sonnet 4.6"), Option("anthropic/claude-sonnet-4.6:batch", "Claude Sonnet 4.6 (batch)"),
            Option("anthropic/claude-sonnet-5.5", "Claude Sonnet 5.5"), Option("anthropic/claude-sonnet-5.5:batch", "Claude Sonnet 5.5 (batch)")]));

    // The Claude catalogue as the registry keeps it: the CLI's list laid over the Claude seed (MergedClaudeCatalog), with the advisor
    // entry right after Sonnet.
    private static readonly string[] ClaudeLineup = ["default", "opus", "claude-fable-5-1[1m]", "sonnet", "claude-sonnet-5-5+advisor-opus", "haiku", "claude-opus-5"];

    private static string[] Ids(EffortListEnv env, ProviderKind provider) =>
        ModelCatalog.Entries(env.Model, env.Thread).Where(entry => entry.Provider == provider).Select(entry => entry.Option.Id).ToArray();

    [Fact]
    public void A_claude_chat_lists_claudes_whole_lineup_even_with_its_provider_switched_off_or_its_limit_used_up() => headless.OnUiThread(() =>
    {
        foreach (var (claudeOn, limit) in new[] { (true, false), (false, false), (false, true), (true, true) })
        {
            using var env = Env(ProviderKind.Claude, "opus", claudeOn, limit);
            Assert.Empty(env.Model.Settings.ModelList); // the owner's "Your models" list is empty
            var entries = ModelCatalog.Entries(env.Model, env.Thread);
            Assert.Equal(ClaudeLineup, Ids(env, ProviderKind.Claude));
            // One Claude group, in the providers' own order, the current model not repeated at the top.
            var first = entries.ToList().FindIndex(entry => entry.Provider == ProviderKind.Claude);
            Assert.All(entries.Skip(first).Take(ClaudeLineup.Length), entry => Assert.Equal(ProviderKind.Claude, entry.Provider));
            var order = entries.Select(entry => entry.Provider).Distinct().ToList();
            Assert.Equal(order.OrderBy(provider => SwiftEnum<ProviderKind>.AllCases.ToList().IndexOf(provider)), order);
        }
    });

    [Fact]
    public void A_switched_off_claude_stays_out_of_another_providers_chat() => headless.OnUiThread(() =>
    {
        using (var off = Env(ProviderKind.Antigravity, "claude-sonnet-4-6", claudeOn: false)) Assert.Empty(Ids(off, ProviderKind.Claude));
        using var on = Env(ProviderKind.Antigravity, "claude-sonnet-4-6", claudeOn: true);
        Assert.Contains("sonnet", Ids(on, ProviderKind.Claude));
    });

    [Fact]
    public void Searching_sonnet_shows_claudes_sonnets_beside_the_other_providers_and_no_batch_endpoint() => headless.OnUiThread(() =>
    {
        foreach (var claudeOn in new[] { false, true })
        {
            using var env = Env(ProviderKind.Claude, "opus", claudeOn, limitUsedUp: true);
            var list = new ModelList(env.Thread, false, _ => { }, () => { }) { Width = 330 };
            var window = env.Show(list);
            for (var i = 0; i < 60; i++) { env.Step(16); window.UpdateLayout(); }
            var field = Assert.Single(list.GetVisualDescendants().OfType<TextBox>());
            field.Text = "sonnet"; Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 60; i++) { env.Step(16); window.UpdateLayout(); }
            var shown = Models(list).Where(row => row.IsVisible && row.Opacity > 0 && row.Bounds.Height > 0).Select(Name).ToArray();
            Assert.Equal(["Sonnet 5.5", AdvisorName, "Claude Sonnet 4.6 (Thinking)", "Claude Sonnet 4.6", "Claude Sonnet 5.5"], shown);
            env.Window = null; window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-pickerclaude-2")]
    public void Openrouter_batch_endpoints_stay_out_of_the_chat_picker_and_in_the_catalogue() => headless.OnUiThread(() =>
    {
        using var env = Env(ProviderKind.Claude, "opus", claudeOn: true);
        Assert.Equal(["anthropic/claude-sonnet-4.6", "anthropic/claude-sonnet-5.5"], Ids(env, ProviderKind.OpenRouter).Where(id => id.StartsWith("anthropic/", StringComparison.Ordinal)));
        Assert.Contains(env.Model.Providers.Models(ProviderKind.OpenRouter), option => option.Id == "anthropic/claude-sonnet-4.6:batch");
        // A pinned batch endpoint ("Your models") is left out the same way; only a chat already on one keeps it, as its current model.
        env.Model.Settings.AddToModelList(Pin(ProviderKind.OpenRouter, "anthropic/claude-sonnet-5.5:batch"));
        env.Model.Settings.AddToModelList(Pin(ProviderKind.OpenRouter, "anthropic/claude-sonnet-5.5"));
        Assert.Equal(["anthropic/claude-sonnet-5.5"], Ids(env, ProviderKind.OpenRouter));
        var onBatch = Thread(ProviderKind.OpenRouter, "anthropic/claude-sonnet-5.5:batch");
        Assert.Equal("anthropic/claude-sonnet-5.5:batch", ModelCatalog.Entries(env.Model, onBatch)[0].Option.Id);
        Assert.False(ModelCatalog.IsChatModel(new(ProviderKind.OpenRouter, Option("x/y:batch", "Y (batch)"))));
        Assert.True(ModelCatalog.IsChatModel(new(ProviderKind.Antigravity, Option("x/y:batch", "Y"))));
    });
}
