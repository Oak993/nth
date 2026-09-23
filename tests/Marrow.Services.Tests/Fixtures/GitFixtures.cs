using System.Globalization;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// Helpers for the Git tests (Services.Git.Git). Helper types live here, never inside a test class (R-TEST-3). The only
// process they start is the system git (R-TEST-10), found as the app finds it (LoginEnvironment.Git), in real folders
// under MARROW_PORT_DATA_DIR (R-TEST-9). They run git WITHOUT the app's invariant prefix, so what they read back is
// git's own answer, independent of the code under test.

/// <summary>A throwaway git checkout with a local identity, no signing and no line-ending conversion.</summary>
public sealed class GitRepository : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private readonly ScratchFolder folder = new();

    private GitRepository(string root) => Root = root;

    /// <summary>The checkout's top folder, backslashed, no trailing separator.</summary>
    public string Root { get; private set; } = "";

    /// <summary><see cref="Root"/> joined with <paramref name="parts"/> by backslashes.</summary>
    public string Child(params string[] parts) => Root + "\\" + string.Join('\\', parts);

    /// <summary>A folder that is no checkout at all (under %TEMP%, which no repository holds).</summary>
    public static ScratchFolder NotARepository() => new();

    /// <summary>An initialised checkout on branch main, with no commit.</summary>
    public static GitRepository Empty()
    {
        var repository = new GitRepository("");
        repository.Root = repository.folder.Child("repo");
        Directory.CreateDirectory(repository.Root);
        repository.Git("init", "-q", "-b", "main");
        repository.Git("config", "user.name", "Port Test");
        repository.Git("config", "user.email", "port-test@localhost");
        repository.Git("config", "commit.gpgsign", "false");
        repository.Git("config", "core.autocrlf", "false");
        return repository;
    }

    /// <summary>A checkout on main holding a.txt ("a\n") in one commit.</summary>
    public static GitRepository WithCommit()
    {
        var repository = Empty();
        repository.Write("a.txt", "a\n");
        repository.CommitAll("first", 1_700_000_000);
        return repository;
    }

    /// <summary>Writes <paramref name="text"/> as UTF-8 to the repository-relative path (forward slashes allowed).</summary>
    public void Write(string relative, string text)
    {
        var path = Root + "\\" + relative.Replace('/', '\\');
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>add -A and a commit dated <paramref name="unixSeconds"/>; returns the commit id.</summary>
    public string CommitAll(string message, long unixSeconds)
    {
        Git("add", "-A");
        var date = unixSeconds.ToString(CultureInfo.InvariantCulture) + " +0000";
        GitIn(Root, new Dictionary<string, string> { ["GIT_COMMITTER_DATE"] = date, ["GIT_AUTHOR_DATE"] = date },
            "commit", "-q", "-m", message);
        return Git("rev-parse", "HEAD").TrimEnd('\n');
    }

    /// <summary>Runs git in <see cref="Root"/> and returns its stdout; a failure fails the test.</summary>
    public string Git(params string[] arguments) => GitIn(Root, null, arguments);

    /// <summary>Runs git in <paramref name="directory"/>; a failure fails the test.</summary>
    public static string GitIn(string directory, IReadOnlyDictionary<string, string>? extra, params string[] arguments)
    {
        var result = Raw(directory, extra, arguments);
        if (!result.Succeeded)
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed: " + result.FailureMessage);
        return result.Output;
    }

    /// <summary>Runs git in <paramref name="directory"/> with no prefix and returns the whole result.</summary>
    public static ShellResult Raw(string directory, IReadOnlyDictionary<string, string>? extra, params string[] arguments)
    {
        var environment = LoginEnvironment.Current.Mutating(values =>
        {
            if (extra is null) return;
            foreach (var (key, value) in extra) values[key] = value;
        });
        var run = Shell.Run(LoginEnvironment.Git, arguments, @in: directory, environment: environment);
        if (!run.Wait(Bound)) throw new TimeoutException("git " + string.Join(' ', arguments) + " did not finish.");
        return run.Result;
    }

    public void Dispose() => folder.Dispose();
}

/// <summary>Waiting helpers for the process-bound Git tests.</summary>
public static class GitWaits
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    public static bool Eventually(Func<bool> condition) => SpinWait.SpinUntil(condition, Bound);
}
