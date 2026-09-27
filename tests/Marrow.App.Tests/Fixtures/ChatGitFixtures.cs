using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: disposable local Git repositories and private Swift-helper invocation for ChatView review.
internal static class ChatGitFixtures
{
    public static string Init(string directory)
    {
        Directory.CreateDirectory(directory);
        Run(directory, "init", "-q", "-b", "main");
        Run(directory, "config", "user.name", "test");
        Run(directory, "config", "user.email", "test@test");
        Run(directory, "config", "commit.gpgsign", "false");
        Run(directory, "commit", "--allow-empty", "-m", "Initial");
        return directory;
    }

    public static string Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "Scratch Git timed out");
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    public static T Invoke<T>(object owner, string name, params object?[] arguments) =>
        (T)owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, arguments)!;

    public static void InvokeStatic(Type type, string name, params object?[] arguments) =>
        type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);

    public static Button Button(Control root, string title) => LogicalQuery.SelfAndDescendants<Button>(root).Single(b => Equals(b.Content, title));
    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    public static async Task Until(Func<bool> completed)
    {
        var timer = Stopwatch.StartNew();
        while (!completed())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20), "ChatView action did not finish");
            await Task.Delay(10);
        }
    }
}
