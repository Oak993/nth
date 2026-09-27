using Avalonia.Media;
using Marrow.UI.Common;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: all workspace operations are recorded; none reaches the operating system.
internal sealed class ChatWorkspaceFixture : IDisposable
{
    private readonly IDisposable scope;
    public Dictionary<string, string> Paths { get; } = new();
    public List<string> IconReads { get; } = [];
    public List<string> Reveals { get; } = [];
    public List<(string Executable, string[] Arguments, string? Directory, bool Console)> Launches { get; } = [];
    public IImage? Icon { get; set; }
    public const string Explorer = @"C:\fixture\explorer.exe";

    public ChatWorkspaceFixture()
    {
        scope = Workspace.Platform.Override(new Workspace.Backend
        {
            AppPath = (_, _) => null,
            Which = name => Paths.GetValueOrDefault(name),
            Exists = path => Paths.Values.Contains(path),
            ReadIcon = path => { IconReads.Add(path); return Icon; },
            Launch = (exe, args, directory, console) => Launches.Add((exe, args.ToArray(), directory, console)),
            Reveal = Reveals.Add,
            Home = @"C:\fixture home",
            Explorer = Explorer,
        });
    }

    public void Dispose() => scope.Dispose();
}
