using System.Reflection;
using System.Text;
using Marrow.App.Runtime;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Local;
using Marrow.Core.Support;
using Marrow.Services.Port.Local;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class ManualContextPinRig : IDisposable
{
    private readonly List<IDisposable> scopes = [];
    private readonly LocalDiscovery local = new([]);
    public ManualContextPinRig(ProviderKind provider, bool pinned = false)
    {
        Http = new();
        Http.Answer = _ => InProcessHttpHandler.Reply(200,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Config.cs: preserve the latest instruction.\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
        scopes.Add(URLSession.Transport.Override(Http));
        scopes.Add(MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null)));
        scopes.Add(ClaudeProviderRuntime.Version.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("2.1.284 (Claude Code)"), []))));
        scopes.Add(ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, [], []))));
        scopes.Add(ClaudeProviderRuntime.Process.Override((_, args, _, _) => Process = new(args, "")));
        var server = new LocalServer("https://offline-manual.invalid:1234");
        var first = new LocalModel(server, "source", "Source", new(true, 65536, true));
        var second = new LocalModel(server, "target", "Target", new(true, 65536, true));
        // Offline reported metadata, never a socket/probe/model call.
        typeof(LocalDiscovery).GetProperty(nameof(LocalDiscovery.Entries))!.SetValue(local, new LocalEntry[] { new(server, [first, second]) });
        Rig = new(provider, configure: thread => thread with { Model = provider == ProviderKind.Local ? first.SelectionId : "source-model", IsPinned = true }, settings: app =>
        {
            if (provider == ProviderKind.Local)
            {
                app.Providers.InitializeLocal(local);
                LocalAccountCatalog.Synchronize(app.Settings.Accounts, local.Entries);
                Source = app.Settings.Accounts.ForService(provider).Single(row => row.Defaults.Model == first.SelectionId).Id;
                Target = app.Settings.Accounts.ForService(provider).Single(row => row.Defaults.Model == second.SelectionId).Id;
            }
            else
            {
                Source = app.Settings.Accounts.ForService(provider)[0].Id;
                app.Settings.Accounts.MarkSignedIn(Source);
                var target = app.Settings.Accounts.Add(provider);
                Target = target.Id;
                app.Settings.Accounts.MarkSignedIn(Target);
                app.Settings.Accounts.SetDefaults(Target, new() { Model = "target-model", Effort = "high", FastMode = true });
            }
        });
        Rig.H.Main.OnMain(() => Rig.App.UpdateThread(Rig.InitialThread.Id, thread => thread with
            { ProviderAccountID = Source, ProviderAccountPinID = pinned ? Source : null }));
        Rig.Install(Rig.Session);
        if (provider == ProviderKind.Local) Rig.H.Main.OnMain(() =>
        {
            var signature = ThreadRuntimeReflection.Get<object>(Rig.Runtime, "sessionSignature")!;
            signature.GetType().GetProperty("LocalServerId")!.SetValue(signature, server.Id);
            signature.GetType().GetProperty("LocalCatalogRevision")!.SetValue(signature, local.Revision);
        });
        var build = AccountSessionFactory.Launcher.Current;
        scopes.Add(AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            if (configuration.Provider == ProviderKind.Claude)
                configuration = configuration with { Executable = SwiftURL.Create("/offline/manual-claude-" + Guid.NewGuid().ToString("N"))! };
            var created = build(registry, configuration, id, fresh);
            Incoming = created.Session;
            return created;
        }));
    }
    public ThreadRuntimeTurnRig Rig { get; }
    public InProcessHttpHandler Http { get; }
    internal RuntimeClaudePrimingProcess? Process { get; private set; }
    public ProviderSession? Incoming { get; private set; }
    public Guid Source { get; private set; }
    public Guid Target { get; private set; }
    public string Describe => "incoming=" + Incoming?.GetType().Name + "; running=" + Incoming?.IsRunning + "; http=" + Http.Received.Count
        + "; controls=" + string.Join(",", Process?.Sent.Select(value => value["request"]?["subtype"]?.JSONValueString ?? value["type"]?.JSONValueString) ?? []);
    public bool HasReceived => Http.Received.Count > 0 || Process?.Sent.Any(value => value["type"]?.JSONValueString == "user") == true;
    public Task<bool> Pin(Bring? bring = Bring.MarrowSummary, CancellationToken ct = default)
    {
        var method = typeof(AppModel).GetMethod("TryPinThreadAccountAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return (Task<bool>)method.Invoke(Rig.App, [Rig.InitialThread.Id, Target, bring, ct])!;
    }
    public void Dispose()
    {
        Rig.H.Main.OnMain(() => { Incoming?.Stop(); Rig.Runtime.CancelPersistence(); });
        Rig.H.Main.Loop.Run(() => Rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Rig.Dispose();
        foreach (var scope in scopes.AsEnumerable().Reverse()) scope.Dispose();
        local.Dispose(); Http.Dispose();
    }
}
