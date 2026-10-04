using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// DroppyCode/App/Views/Chat/ChatView.swift:1605-1631 (B3 52bc792): the commit sheet's load and its message generation. The model call is the provider stand-in (never a real
// CLI); what reaches it is read from ShellObserver (ED-207), which reports every Shell.Run call as it is entered with its argv and the bytes it writes to the process's
// standard input, which is where TextGeneration puts the prompt (TextGeneration.swift:84-85).
[Collection("Headless")]
public sealed class ChatViewCommitSheetTests(HeadlessSession headless)
{
    private static ThreadRuntime NewRuntime(AppModel model, string directory) =>
        model.Runtime(model.NewThread(model.AddProject(new Project("P", directory).Url))!.Id);

    private static (CommitSheet Sheet, AvaloniaWindow Window) Show(AppModel model, string directory, GitStatusModel? git = null)
    {
        var sheet = new CommitSheet(model, NewRuntime(model, directory), directory, git ?? new GitStatusModel(), () => { });
        var window = new AvaloniaWindow { Content = sheet, Width = 700, Height = 600 };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        return (sheet, window);
    }

    private static string PromptOf(ShellInvocation call) => Encoding.UTF8.GetString(call.Input ?? []);

    private static bool IsGit(ShellInvocation call) => Path.GetFileNameWithoutExtension(call.Executable).Equals("git", StringComparison.OrdinalIgnoreCase);

    [Fact]
    [Trait("Req", "PR-GIT-129")] // ChatView.swift:1616-1628 `TextGeneration.commitMessage(summary:patch:instructions: model.settings.commitInstructions, ...)`: the instructions set in Settings reach the model call, with the changes read once
    public void The_commit_instructions_reach_the_model_call_with_the_summary_and_patch_read_once_for_the_sheet_and_its_draft() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        File.WriteAllText(Path.Combine(directory, "changed.txt"), "payload");
        var folder = h.NewFolder("stand-in");
        var engine = ChatProviderFixture.Create(folder, "Add the changed file\n");
        var withEngine = new ConcurrentQueue<ShellInvocation>();
        CommitSheet sheet = null!; AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.SetBinaryPath(engine, ProviderKind.Claude);
            model.Settings.TextGeneration = TextGenerationChoice.Claude;
            model.Settings.CommitInstructions = "Keep it under forty characters";
            using var scope = ShellObserver.Observe(withEngine.Enqueue); // the sheet's .task starts inside this flow and carries the observer with it
            (sheet, window) = Show(model, directory);
        });
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => !sheet.IsGenerating && sheet.Message.Length > 0));
        h.Main.OnMain(() => window.Close());
        h.Main.RunUntilIdle();
        bool IsEngine(ShellInvocation candidate) => Path.GetFullPath(candidate.Executable) == Path.GetFullPath(engine);
        var call = Assert.Single(withEngine, IsEngine);
        var prompt = PromptOf(call);
        Assert.Contains("\nFollow these instructions too: Keep it under forty characters\n", prompt, StringComparison.Ordinal);
        Assert.Contains("changed.txt", prompt, StringComparison.Ordinal); // the summary it was given
        // The sheet reads the working changes once (:1606: the summary and the patch) and hands them to the draft (:1608, :1616-1621 `changes ?? await ...`): one status, one diff, however
        // many times the sheet and its draft use them.
        var gitCalls = withEngine.Where(IsGit).Select(candidate => candidate.Arguments).ToList();
        Assert.Single(gitCalls, arguments => arguments.Contains("status"));
        Assert.Single(gitCalls, arguments => arguments.Contains("diff"));
    });

    [Fact]
    [Trait("Req", "PR-GIT-129")] // ChatView.swift:1605 `.task { ... }` is cancelled when the sheet goes: the model call is ended and a late answer writes no message
    public void Dismissing_the_sheet_while_the_message_is_being_written_ends_the_model_call_and_no_message_is_written() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        File.WriteAllText(Path.Combine(directory, "changed.txt"), "payload");
        var folder = h.NewFolder("stand-in");
        var engine = ChatProviderFixture.Create(folder, "A message that must never arrive\n");
        File.WriteAllText(Path.Combine(folder, "sleep-ms.txt"), "20000"); // the model call takes far longer than the test waits
        var running = Path.Combine(folder, "running.flag");
        File.WriteAllText(Path.Combine(folder, "marker.txt"), running); // created as the stand-in starts, deleted only by its own orderly exit
        CommitSheet sheet = null!; AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.SetBinaryPath(engine, ProviderKind.Claude);
            model.Settings.TextGeneration = TextGenerationChoice.Claude;
            (sheet, window) = Show(model, directory);
        });
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.IsGenerating && File.Exists(running)));
        // The sheet goes while the model is still answering.
        h.Main.OnMain(() => window.Close());
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => !sheet.IsGenerating));
        // The stand-in is ended (it would otherwise run for twenty seconds): none of its processes is left, and it was killed rather than allowed to finish (its flag is still there).
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => !StandInRunning(folder)));
        Assert.True(File.Exists(running), "the model call ran to its orderly exit");
        h.Main.RunUntilIdle();
        Assert.Equal("", sheet.Message);
    });

    private static bool StandInRunning(string folder)
    {
        foreach (var process in Process.GetProcessesByName("chat-provider-stand-in"))
        {
            try
            {
                if (process.MainModule?.FileName is { } path && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { /* a process of another test that cannot be read is not this one */ }
            finally { process.Dispose(); }
        }
        return false;
    }

    [Fact]
    [Trait("Req", "PR-GIT-129")] // ChatView.swift:1622-1629 `if let text = await TextGeneration.commitMessage(...) { message = text }`: a generation that returns nothing leaves the message as it was
    public void A_write_for_me_that_returns_nothing_leaves_the_typed_message_as_it_was() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        File.WriteAllText(Path.Combine(directory, "changed.txt"), "payload");
        var folder = h.NewFolder("stand-in");
        var engine = ChatProviderFixture.Create(folder, ""); // exit 0 and not a word
        CommitSheet sheet = null!; AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.SetBinaryPath(engine, ProviderKind.Claude);
            model.Settings.TextGeneration = TextGenerationChoice.Claude;
            (sheet, window) = Show(model, directory);
            sheet.Message = "Typed by hand"; // :1608 a typed message is not written over on appear
        });
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.WriteForMeButton.IsEnabled && sheet.Summary.Length > 0));
        h.Main.OnMain(() => ChatGitFixtures.Click(sheet.WriteForMeButton));
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.IsGenerating));
        h.Main.Loop.Run(() => ChatGitFixtures.Until(() => !sheet.IsGenerating));
        h.Main.OnMain(() =>
        {
            Assert.Equal("Typed by hand", sheet.Message);
            Assert.True(sheet.WriteForMeButton.IsEnabled);
            window.Close();
        });
        h.Main.RunUntilIdle();
    });
}
