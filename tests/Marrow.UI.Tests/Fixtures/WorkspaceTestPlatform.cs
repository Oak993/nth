using System.Reflection;
using Avalonia.Media;
using Marrow.UI.Common;

namespace Marrow.UI.Tests.Fixtures;

// A fail-closed fake of Components.swift:139-167's workspace and ED-087/ED-089's Windows boundary.
// Reflection lets the tests build and run red before the new injectable API exists.
public sealed class WorkspaceTestPlatform : IDisposable
{
    private readonly IDisposable scope;
    public Dictionary<(string, bool), string> Registry { get; } = new();
    public Dictionary<string, string> Path { get; } = new();
    public HashSet<string> Existing { get; } = new();
    public List<(string Name, bool Machine)> RegistryReads { get; } = new();
    public List<string> PathReads { get; } = new();
    public List<string> IconReads { get; } = new();
    public List<(string Executable, string[] Arguments, string? Directory, bool Console)> Launches { get; } = new();
    public IImage? Image { get; set; }
    public Action<string> Reveal { get; set; } = _ => throw new InvalidOperationException("Unexpected reveal");

    public WorkspaceTestPlatform()
    {
        var type = typeof(Workspace).GetNestedType("Backend") ?? throw new InvalidOperationException("Workspace.Backend is missing");
        var backend = Activator.CreateInstance(type)!;
        void Set(string name, object value) => type.GetProperty(name)!.SetValue(backend, value);
        Set("AppPath", (Func<string, bool, string?>)((exe, machine) => { RegistryReads.Add((exe, machine)); return Registry.GetValueOrDefault((exe, machine)); }));
        Set("Which", (Func<string, string?>)(name => { PathReads.Add(name); return Path.GetValueOrDefault(name); }));
        Set("Exists", (Func<string, bool>)(Existing.Contains));
        Set("ReadIcon", (Func<string, IImage?>)(path => { IconReads.Add(path); return Image; }));
        Set("Launch", (Action<string, IReadOnlyList<string>, string?, bool>)((exe, args, cwd, console) => Launches.Add((exe, args.ToArray(), cwd, console))));
        Set("Reveal", (Action<string>)(path => Reveal(path)));
        Set("Home", @"C:\fixture home");
        Set("Explorer", @"C:\Windows\explorer.exe");
        var ambient = typeof(Workspace).GetProperty("Platform")?.GetValue(null) ?? throw new InvalidOperationException("Workspace.Platform is missing");
        scope = (IDisposable)ambient.GetType().GetMethod("Override")!.Invoke(ambient, [backend])!;
    }

    public void Dispose() => scope.Dispose();
}
