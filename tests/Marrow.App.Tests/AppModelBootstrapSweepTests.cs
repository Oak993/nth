using System.Collections.Concurrent;
using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests;

public sealed class AppModelBootstrapSweepTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Req", "PR-LAUNCH-085")] // App/AppModel.swift:275-286
    public async Task Installed_updaters_run_Claude_then_Codex_without_holding_the_orphan_sweep(bool claudeInstalled, bool codexInstalled)
    {
        using var h = new AppModelHarness();
        var claude = MakeStandIn(h, "claude");
        var codex = MakeStandIn(h, "codex");
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider => provider switch
        {
            ProviderKind.Claude when claudeInstalled => FileUrl(claude),
            ProviderKind.Codex when codexInstalled => FileUrl(codex),
            _ => null,
        });
        var calls = new ConcurrentQueue<string>();
        using var observer = ShellObserver.Observe(call =>
        {
            if (call.Arguments.SequenceEqual(new[] { "update" })) calls.Enqueue(call.Executable);
        });
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        h.Main.OnMain(() =>
        {
            model.Settings.SetBinaryPath("", ProviderKind.Claude);
            model.Settings.SetBinaryPath("", ProviderKind.Codex);
            // This record starts AFTER catalog loading. Keep already-attempted catalogs offline; the real
            // RefreshAll still discovers our two stand-ins and the real updater still probes/runs them.
            var attempted = (HashSet<ProviderKind>)typeof(ProviderRegistry)
                .GetField("attemptedCatalogs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model.Providers)!;
            attempted.Add(ProviderKind.Claude);
            attempted.Add(ProviderKind.Codex);
        });
        Directory.CreateDirectory(Storage.AttachmentsDirectory);
        var orphan = Path.Combine(Storage.AttachmentsDirectory, SwiftUUID.UuidString(Guid.NewGuid()) + ".png");
        File.WriteAllBytes(orphan, [1]);
        File.SetLastWriteTimeUtc(orphan, (SwiftDate.Now - TimeSpan.FromHours(25)).UtcDateTime);
        var first = claudeInstalled ? claude : codex;
        try
        {
            Task bootstrap = h.Main.OnMain(() => model.Bootstrap());
            h.Main.Loop.RunUntil(() => bootstrap.IsCompleted && File.Exists(Marker(first, "update-started")), AppModelHarness.Bound);
            await bootstrap;
            Assert.False(File.Exists(orphan));
            Assert.False(File.Exists(Marker(first, "update-finished")));
            Assert.Equal(new[] { first }, calls.ToArray());
            h.Main.OnMain(() => Assert.Contains(claudeInstalled ? ProviderKind.Claude : ProviderKind.Codex, model.Providers.UpdatingCLIs));
            if (claudeInstalled && codexInstalled)
            {
                Assert.False(File.Exists(Marker(codex, "update-started")));
                File.WriteAllText(Marker(claude, "release-update"), "");
                h.Main.Loop.RunUntil(() => File.Exists(Marker(codex, "update-started")), AppModelHarness.Bound);
                Assert.True(File.Exists(Marker(claude, "update-finished")));
                Assert.Equal(new[] { claude, codex }, calls.ToArray());
            }
            File.WriteAllText(Marker(claude, "release-update"), "");
            File.WriteAllText(Marker(codex, "release-update"), "");
            h.Main.Loop.RunUntil(() => model.Providers.UpdatingCLIs.Count == 0, AppModelHarness.Bound);
            Assert.Equal(new[] { claudeInstalled ? claude : null, codexInstalled ? codex : null }.OfType<string>(), calls.ToArray());
        }
        finally
        {
            File.WriteAllText(Marker(claude, "release-update"), "");
            File.WriteAllText(Marker(codex, "release-update"), "");
            h.Main.Loop.RunUntil(() => model.Providers.UpdatingCLIs.Count == 0, AppModelHarness.Bound);
        }
    }

    private static string Marker(string executable, string name) => Path.Combine(Path.GetDirectoryName(executable)!, name);
    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/'))!;

    private static string MakeStandIn(AppModelHarness h, string provider)
    {
        var folder = h.NewFolder("bootstrap-" + provider);
        var output = Path.Combine(folder, "bootstrap-update.exe");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "appmodel-bootstrap-cli", "BootstrapUpdateStandIn.cs");
            if (File.Exists(candidate)) { source = candidate; break; }
            directory = directory.Parent;
        }
        Assert.NotNull(source);
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var compile = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        Assert.True(compile.Wait(AppModelHarness.Bound));
        Assert.True(compile.Result.Succeeded, compile.Result.FailureMessage);
        return output.Replace('\\', '/');
    }
}
