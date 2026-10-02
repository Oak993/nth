using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// One real MessageEditor on one real thread runtime, over a throwaway git checkout the rig makes under the harness's
/// scratch folder (R-TEST-9, R-TEST-10): the turn changed <c>fileCount</c> files between two commits, the checkpoint refs
/// name them, and the file the editor's revert preview reads is the one on disk. Nothing real is reached: every CLI
/// provider gets a binary path that does not exist, API keys in the environment fail the test, and git runs only in the
/// rig's own folder. Built and disposed inside <c>HeadlessSession.OnUiThread</c>.
/// </summary>
public sealed class MessageEditorRig : IDisposable
{
    private readonly AppModelHarness harness = new(withRuntime: false);
    private readonly List<Avalonia.Controls.Window> windows = [];

    public MessageEditorRig(EditorPreviewKind kind, int fileCount = 1, string text = "edit me", bool pumpOnShow = true)
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (provider.ApiKeyEnvVar is { } variable && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(variable)))
                throw new InvalidOperationException("The environment holds " + variable + ": run these tests without it (R-TEST-10).");
        Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();

        Repo = AppModelGitRepo.WithCommit(harness.NewFolder("work"));
        Files = Enumerable.Range(0, fileCount).Select(index => "file" + index + ".txt").ToArray();
        foreach (var file in Files) Write(file, "before\n");
        Repo.Git("add", "-A");
        Repo.Git("commit", "-q", "-m", "turn start");
        var start = Repo.Git("rev-parse", "HEAD").Trim();
        foreach (var file in Files) Write(file, "after\n");
        Repo.Git("add", "-A");
        Repo.Git("commit", "-q", "-m", "turn end");
        var end = Repo.Git("rev-parse", "HEAD").Trim();
        if (kind == EditorPreviewKind.FirstKept) Write(Files[0], "changed again\n");
        if (kind == EditorPreviewKind.AllKept)
            foreach (var file in Files) Write(file, "changed again\n");

        var threadId = Guid.NewGuid();
        StartRef = Git.CheckpointRef(threadId, 0, "start");
        var endRef = Git.CheckpointRef(threadId, 0, "end");
        var hasCheckpoint = kind != EditorPreviewKind.Unavailable;
        if (hasCheckpoint)
        {
            Repo.Git("update-ref", StartRef, start);
            Repo.Git("update-ref", endRef, end);
        }

        TurnId = Guid.NewGuid();
        var item = new TimelineItem(TurnId, new TimelineItem.Content.User(new UserMessage { Text = "original message" }));
        var turn = new TurnRecord
        {
            Id = TurnId,
            Index = 0,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            UserItemID = item.Id,
            TouchedPaths = new ValueList<string>(kind == EditorPreviewKind.NoFiles ? [] : Files),
            BaseCheckpoint = hasCheckpoint ? StartRef : null,
            EndCheckpoint = hasCheckpoint ? endRef : null,
        };
        var project = AppModelSamples.Project("P", Repo.Root);
        var thread = AppModelSamples.Thread(project) with { Id = threadId, Provider = ProviderKind.Cursor };
        harness.WriteLibrary([project], [thread]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(threadId) { Turns = new ValueList<TurnRecord>([turn]), Items = new ValueList<TimelineItem>([item]) },
            Storage.ThreadURL(threadId));

        Model = new AppModel();
        var missing = Path.Combine(harness.Scratch, "no-such-cli", "missing.exe");
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) Model.Settings.SetBinaryPath(missing, provider);
        Runtime = Model.Runtime(threadId);
        var loaded = Runtime.EnsureLoaded();
        WaitUntil(() => loaded.IsCompleted, "the thread document to load");
        loaded.GetAwaiter().GetResult();

        Edit = new PromptEdit(TurnId, text, ValueList<Attachment>.Empty);
        Runtime.MessageEdit = Edit;
        Editor = new MessageEditor(Edit, Runtime);
        Show(pumpOnShow);
    }

    public AppModelGitRepo Repo { get; }

    /// <summary>The repository-relative names of the files the turn changed.</summary>
    public string[] Files { get; }

    public string StartRef { get; }

    public Guid TurnId { get; }

    public AppModel Model { get; }

    public ThreadRuntime Runtime { get; }

    public PromptEdit Edit { get; }

    public MessageEditor Editor { get; }

    public Avalonia.Controls.Window Window => windows[0];

    /// <summary>The restore toggle's switch (:102-113): the one control whose automation peer toggles.</summary>
    public Avalonia.Automation.Provider.IToggleProvider Switch() =>
        (Avalonia.Automation.Provider.IToggleProvider)Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(
            Assert.Single(All<Control>(), control => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control) is Avalonia.Automation.Provider.IToggleProvider));

    /// <summary>Takes the turn's start checkpoint away from the repository, after the preview came in. A send that asks for the
    /// files back then fails at :1855 (<c>rev-parse --verify</c>), and one that does not ask is not touched by it: which of the
    /// two a send did is what the editor shows afterwards.</summary>
    public void DeleteStartCheckpoint() => Repo.Git("update-ref", "-d", StartRef);

    /// <summary>The message the thread's first user entry holds now.</summary>
    public string? FirstUserText() => Runtime.Entries
        .Select(entry => entry.Item.TimelineItemContent)
        .OfType<TimelineItem.Content.User>().FirstOrDefault()?.Value.Text;

    /// <summary>The file as it is on disk now.</summary>
    public string Read(string file) => File.ReadAllText(Path.Combine(Repo.Root, file));

    private void Write(string file, string content) =>
        File.WriteAllText(Path.Combine(Repo.Root, file), content, new System.Text.UTF8Encoding(false));

    /// <summary>Opens the window. Without <paramref name="pump"/> the dispatcher is not run, so a preview that is still
    /// being fetched cannot have been delivered: the editor is in its checking state, exactly.</summary>
    public void Show(bool pump = true, double width = 480, double height = 700)
    {
        Editor.HorizontalAlignment = HorizontalAlignment.Left;
        Editor.VerticalAlignment = VerticalAlignment.Top;
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = Editor };
        windows.Add(window);
        window.Show();
        if (pump) Relayout();
        else window.UpdateLayout();
    }

    public static void Drain() => Dispatcher.UIThread.RunJobs();

    /// <summary>Lays the editor out again after a state change.</summary>
    public void Relayout()
    {
        Drain();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Drain();
    }

    /// <summary>Pumps the dispatcher (and lets real I/O finish) until <paramref name="condition"/> holds.</summary>
    public static void WaitUntil(Func<bool> condition, string what, double seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException("timed out waiting for " + what);
            Drain();
            Thread.Sleep(2);
        }
    }

    /// <summary>Waits until the editor shows the answer of the revert preview in place of the checking line.</summary>
    public void WaitForPreview()
    {
        WaitUntil(() => !Texts().Contains("Checking for file changes…"), "the revert preview to resolve");
        Relayout();
    }

    /// <summary>Every text on the editor now.</summary>
    public string[] Texts() => All<TextBlock>().Select(block => block.Text ?? "").ToArray();

    public T[] All<T>() where T : class => LogicalQuery.SelfAndDescendants<T>(Editor).ToArray();

    /// <summary>The button whose label is <paramref name="title"/>.</summary>
    public Button ButtonNamed(string title) => Assert.Single(All<Button>(), button =>
        Equals(button.Content, title) || LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(block => block.Text == title));

    /// <summary>A press of <paramref name="button"/>, as a click raises it.</summary>
    public void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain();
    }

    /// <summary>Whether the whole editor takes input: Swift's <c>.disabled(isSending)</c> on the PromptEditor (:63).</summary>
    public bool TakesInput => Editor.Child!.IsEnabled;

    /// <summary>Waits for a send that is in flight to end, one way or the other: the edit closed, or the editor enabled again.</summary>
    public void WaitForSendToEnd() =>
        WaitUntil(() => Runtime.MessageEdit is null || TakesInput, "the send to end");

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        Drain();
        harness.Dispose();
    }
}
