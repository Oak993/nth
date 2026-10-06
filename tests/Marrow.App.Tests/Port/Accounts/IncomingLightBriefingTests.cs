using System.Reflection;
using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
namespace Marrow.App.Tests.Port.Accounts;
[Trait("Rule", "R-TEST-4")]
public sealed class IncomingLightBriefingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("no-light")]
    [InlineData("light-model-rejected")]
    [InlineData("revoked")]
    public void The_incoming_AI_fallback_uses_only_the_selected_accounts_reported_light_model_and_full_archive(string failure)
    {
        using var rig = new ThreadRuntimeTurnRig();
        using var version = ClaudeProviderRuntime.Version.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("2.1.284 (Claude Code)"), [])));
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, [], [])));
        RuntimeClaudePrimingProcess? process = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, _) => process = new(args, failure));
        rig.H.Main.Loop.Run(async () =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.App.Settings.Accounts.MarkSignedIn(account.Id);
            rig.App.Settings.Accounts.SetDefaults(account.Id, new() { Model = "selected-model", Effort = "high", FastMode = true });
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var launches = 0;
            using var revocation = ClaudeProviderRuntime.Process.Override((_, args, _, _) =>
            {
                process = new(args, failure);
                if (failure == "revoked") process.OnPolicyRead = () =>
                {
                    if (process.PolicyReads == 2) rig.App.Settings.Accounts.SetEnabled(account.Id, false);
                };
                return process;
            });
            var host = new ThreadAccountHost(rig.App, rig.Runtime, rig.App.Settings.Pills, summaries, (config, id) =>
            {
                launches++;
                Assert.Equal(account.Id, id);
                Assert.Equal(account.Id, config.AccountId);
                Assert.False(config.FastMode);
                Assert.Null(config.Effort);
                var prepared = config with { Executable = SwiftURL.Create("/offline/light-summary-" + Guid.NewGuid().ToString("N"))! };
                return new(prepared, new ClaudeSession(prepared));
            });
            var target = new SwitchTarget(new(account.Id.ToString("D"), account.Service.Id, "Destination"));
            var full = "ARCHIVE-Café-17:" + new string('x', 60_000) + ":LAST-DETAIL";
            var path = Path.Combine(rig.H.NewFolder("light-summary"), "conversation.json");
            await File.WriteAllTextAsync(path, full);
            var summary = new HandoffSummary("MECHANICAL-ONLY", path, ["Config.cs"]);
            var method = typeof(ThreadAccountHost).GetMethod("RequestIncomingBriefingAsync", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            var call = (Task<string?>)method.Invoke(host, [target, summary, CancellationToken.None])!;
            if (failure.Length == 0) Assert.Equal("Config.cs: preserve the latest instruction.", await call);
            else await Assert.ThrowsAnyAsync<Exception>(() => call);
            Assert.Equal(1, launches);
            Assert.NotNull(process);
            Assert.True(process.Terminated);
            var settingsIndex = Array.IndexOf(process.Arguments, "--settings");
            Assert.True(settingsIndex >= 0);
            Assert.False(JSONValue.Parse(process.Arguments[settingsIndex + 1])!["fastMode"]!.JSONValueBool);
            Assert.DoesNotContain("--effort", process.Arguments);
            var turns = process.Sent.Where(value => value["type"]?.JSONValueString == "user").ToArray();
            if (failure.Length == 0)
            {
                var text = Assert.Single(turns)["message"]!["content"]!.JSONValueArray![0]["text"]!.JSONValueString!;
                Assert.Contains(full, text, StringComparison.Ordinal);
                Assert.Single(process.Sent, value => value["request"]?["subtype"]?.JSONValueString == "set_model" && value["request"]?["model"]?.JSONValueString == "haiku");
            }
            else Assert.Empty(turns);
            Assert.Equal("selected-model", rig.App.Settings.Accounts.Get(account.Id).Defaults.Model);
            Assert.True(rig.App.Settings.Accounts.Get(account.Id).Defaults.FastMode);
            Assert.Equal(0, rig.Session.StopCalls);
            Assert.Empty(rig.Session.Inputs);
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        }, AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
