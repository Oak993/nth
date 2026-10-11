using Marrow.App.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, unit U8, ED-NEW-inst-u8-1). A <see cref="OnThisPcModel"/> on a machine of fakes
/// (FakeShellLinks, FakeUserRegistry, a ManualTimeProvider) whose worker and main-actor hand-back the test runs itself, step
/// by step: <see cref="RunWorker"/> does what the worker was asked, <see cref="RunPosts"/> hands the answers to the main actor.
/// Nothing here touches the real Desktop, Start menu or HKCU.
/// </summary>
public sealed class OnThisPcRig
{
    public const string Root = FakeShellLinks.DefaultRoot;
    public const string Current = Root + @"\current";
    public const string CurrentExe = Current + @"\nth.exe";

    public OnThisPcRig(bool available = true, Func<IUserRegistry, IUserRegistry>? wrapRegistry = null)
    {
        Shell.AddFile(CurrentExe);
        Shell.AddFile(Root + @"\Update.exe");
        Shell.Seed(FakeShellLinks.StartLink, new ShellLink(CurrentExe, "", Current, CurrentExe, 0, AppUserModelId: ShellLinkKeys.NthAppUserModelId));
        Registry.SeedKey(UninstallEntry.RunKeyPath);
        Platform = available
            ? new OnThisPcPlatform(Root, () => { ShellMade++; return Shell; }, () => { RegistryMade++; return wrapRegistry is null ? Registry : wrapRegistry(Registry); })
            : OnThisPcPlatform.Unavailable;
        Model = new OnThisPcModel(Platform, Clock, work => { Work.Add(work); return Task.CompletedTask; }, Posts.Add);
    }

    public FakeShellLinks Shell { get; } = new();

    public FakeUserRegistry Registry { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    public OnThisPcPlatform Platform { get; }

    public OnThisPcModel Model { get; }

    /// <summary>What the model asked its worker to run (one drain per burst).</summary>
    public List<Action> Work { get; } = [];

    /// <summary>What the worker handed back for the main actor.</summary>
    public List<Action> Posts { get; } = [];

    /// <summary>How many times the platform made the shell (lazily, on the worker).</summary>
    public int ShellMade { get; private set; }

    public int RegistryMade { get; private set; }

    /// <summary>Runs what the worker was asked to do, in order (the shell and registry calls happen here).</summary>
    public void RunWorker()
    {
        var work = Work.ToArray();
        Work.Clear();
        foreach (var job in work) job();
    }

    /// <summary>Hands the results back to the main actor, in order.</summary>
    public void RunPosts()
    {
        var posts = Posts.ToArray();
        Posts.Clear();
        foreach (var post in posts) post();
    }

    public void Pump()
    {
        while (Work.Count + Posts.Count > 0)
        {
            RunWorker();
            RunPosts();
        }
    }

    /// <summary>A shortcut nth made (its marker), as the install hook and Settings write it.</summary>
    public static ShellLink Made() =>
        new(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe", "", @"%LOCALAPPDATA%\Programs\nth\current", CurrentExe, 0, ShellLinkKeys.Description,
            AppUserModelId: ShellLinkKeys.NthAppUserModelId, CreatedBy: ShellLinkKeys.CreatedByValue);

    /// <summary>The user's own shortcut into the root: no marker.</summary>
    public static ShellLink Users() => new(CurrentExe, "--mine", Current, CurrentExe, 0, "nth");
}

/// <summary>A registry that refuses every write, as a locked-down profile does; reads pass through.</summary>
public sealed class RefusingUserRegistry(IUserRegistry inner) : IUserRegistry
{
    private const int AccessDenied = unchecked((int)0x80070005);

    public bool KeyExists(string subKey) => inner.KeyExists(subKey);

    public void CreateKey(string subKey) => throw new UnauthorizedAccessException("denied") { HResult = AccessDenied };

    public object? GetValue(string subKey, string name) => inner.GetValue(subKey, name);

    public void SetString(string subKey, string name, string value) => throw new UnauthorizedAccessException("denied") { HResult = AccessDenied };

    public void SetDword(string subKey, string name, int value) => throw new UnauthorizedAccessException("denied") { HResult = AccessDenied };

    public void DeleteValue(string subKey, string name) => throw new UnauthorizedAccessException("denied") { HResult = AccessDenied };
}

/// <summary>An <see cref="IUserRegistry"/> that notes the thread of every call, then does it on the inner registry.</summary>
public sealed class ThreadSpyUserRegistry(IUserRegistry inner) : IUserRegistry
{
    private readonly List<int> threads = [];

    public IReadOnlyList<int> Threads
    {
        get { lock (threads) return [.. threads]; }
    }

    private T Note<T>(T value)
    {
        lock (threads) threads.Add(Environment.CurrentManagedThreadId);
        return value;
    }

    public bool KeyExists(string subKey) => Note(inner.KeyExists(subKey));

    public void CreateKey(string subKey) { Note(0); inner.CreateKey(subKey); }

    public object? GetValue(string subKey, string name) => Note(inner.GetValue(subKey, name));

    public void SetString(string subKey, string name, string value) { Note(0); inner.SetString(subKey, name, value); }

    public void SetDword(string subKey, string name, int value) { Note(0); inner.SetDword(subKey, name, value); }

    public void DeleteValue(string subKey, string name) { Note(0); inner.DeleteValue(subKey, name); }
}

/// <summary>An <see cref="IShellLinks"/> that notes the thread of every call, then does it on the inner shell.</summary>
public sealed class ThreadSpyShellLinks(IShellLinks inner) : IShellLinks
{
    private readonly List<int> threads = [];

    public IReadOnlyList<int> Threads
    {
        get { lock (threads) return [.. threads]; }
    }

    private T Note<T>(T value)
    {
        lock (threads) threads.Add(Environment.CurrentManagedThreadId);
        return value;
    }

    public string KnownFolder(ShellKnownFolder folder) => Note(inner.KnownFolder(folder));

    public string ExpandEnvironment(string path) => Note(inner.ExpandEnvironment(path));

    public bool FileExists(string path) => Note(inner.FileExists(path));

    public DateTime? FolderWriteTimeUtc(string folder) => Note(inner.FolderWriteTimeUtc(folder));

    public IEnumerable<ShellLinkFile> EnumerateLinks(string folder, string searchPattern = "*.lnk") => Note(inner.EnumerateLinks(folder, searchPattern));

    public ShellLink ReadLink(string path) => Note(inner.ReadLink(path));

    public void WriteLink(string path, ShellLink link) { Note(0); inner.WriteLink(path, link); }

    public void UpdateLink(string path, ShellLinkChange change) { Note(0); inner.UpdateLink(path, change); }

    public void DeleteLink(string path) { Note(0); inner.DeleteLink(path); }

    public void MoveLink(string from, string to) { Note(0); inner.MoveLink(from, to); }

    public bool DeleteEmptyFolder(string folder) => Note(inner.DeleteEmptyFolder(folder));

    public bool DesktopIconsHidden() => Note(inner.DesktopIconsHidden());

    public void Notify(ShellChange change, string path) { Note(0); inner.Notify(change, path); }

    public IDisposable? TryEnterWriter(TimeSpan wait) => Note(inner.TryEnterWriter(wait));
}
