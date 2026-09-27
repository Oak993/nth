using System.Reflection;
using System.Text;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: offline coordinator fixture. Every repository and forge reply is under the harness data root.
public sealed class HydraMergeReviewFixture : IDisposable
{
    public AppModelHarness H { get; } = new();
    public AppModelGitRepo Repo { get; }
    public string Bare { get; }
    public Project Project { get; }
    public ChatThread Lead { get; }
    public TurnRecord Turn { get; }
    public AppModel Model { get; }
    public ThreadRuntime Runtime { get; }
    public ForgeScript Forge { get; }
    public const string Url = "https://forge.example/owner/repo/pulls/7";

    public HydraMergeReviewFixture(bool origin = true, string touched = "a.txt", string title = "Team job", string? branch = null)
    {
        Repo = AppModelGitRepo.WithCommit(H.NewFolder("hydra-review"));
        Bare = H.NewFolder("origin.git");
        Repo.Git("init", "--bare", "-b", "main", Bare);
        if (origin)
        {
            Repo.Git("remote", "add", "origin", Bare);
            Repo.Git("push", "-u", "origin", "main");
        }
        if (branch == "detached") Repo.Git("checkout", "--detach");
        else if (branch is not null) Repo.Git("checkout", "-b", branch);
        Project = AppModelSamples.Project("Project", Repo.Root);
        Lead = AppModelSamples.Thread(Project) with { Title = title, HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        Turn = new TurnRecord { Id = Guid.NewGuid(), Index = 1, Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now, TouchedPaths = new ValueList<string>([touched]) };
        H.WriteLibrary([Project], [Lead]);
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(Lead.Id) { Turns = new ValueList<TurnRecord>([Turn]) }, Storage.ThreadURL(Lead.Id));
        Model = H.MakeModel();
        H.PrepareBootstrap(Model);
        Runtime = H.Main.OnMain(() =>
        {
            Model.Settings.HydraEnabled = true;
            Model.Settings.HydraAutoMerge = true;
            return Model.Runtime(Lead.Id);
        });
        H.Main.Loop.Run(() => Runtime.EnsureLoaded(), AppModelHarness.Bound);
        Forge = new ForgeScript(Repo.Root);
        Forge.Reply(0, stdout: Url);
    }

    public void Edit(string text = "team edit\n") => File.WriteAllText(Path.Combine(Repo.Root, "a.txt"), text, new UTF8Encoding(false));
    public void Run(Action<ShellInvocation>? observer = null) => H.Main.Loop.Run(async () =>
    {
        using var scope = ShellObserver.Observe(observer ?? (_ => { }));
        await Model.AutoMergeHydraWork(Lead.Id);
    }, AppModelHarness.Bound);
    public string[] Notes => H.Main.OnMain(() => Runtime.Entries.Select(e => e.Item.TimelineItemContent)
        .OfType<TimelineItem.Content.User>().Where(u => u.Value.HydraHeads is { Count: 0 }).Select(u => u.Value.Text).ToArray());
    public static string[] GitArgs(ShellInvocation call) => call.Arguments.Skip(4).ToArray();
    public static bool IsGit(ShellInvocation call, string verb) => call.Arguments.Contains(verb) && Path.GetFileNameWithoutExtension(call.Executable).Equals("git", StringComparison.OrdinalIgnoreCase);
    public static Task<bool> Sync(Git git, string head, IReadOnlyList<string> own, IReadOnlyDictionary<string, byte[]>? merged = null) =>
        (Task<bool>)typeof(AppModel).GetMethod("SyncDefaultBranch", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [git, head, "main", own, merged])!;
    public void Dispose() => H.Dispose();
}
