using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Headless Avalonia entry point, for HeadlessUnitTestSession.StartNew (R-TEST-7). Same shape as
/// tests/Marrow.UI.Tests/Fixtures/HeadlessFixtures.cs.</summary>
public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithSwiftFonts(); // ED-107 (5): the one font registration point, shared with the app shell's AppBuilder;
        // matches tests/Marrow.UI.Tests/Fixtures/HeadlessFixtures.cs, missing here until App.Views.Settings.TokenActivitySection's
        // tests needed a real Inter Medium glyph typeface (Avalonia's headless session otherwise has none registered).
}

/// <summary>One headless session per test assembly, held by a collection fixture (xunit 2, no Avalonia.Headless.XUnit).</summary>
public sealed class HeadlessSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    public HeadlessSession() => OnUiThread(WarmFonts);

    /// <summary>Runs a test body on the headless UI thread with MainActor bound to Avalonia, and waits for it.</summary>
    public void OnUiThread(Action body) => Session.Dispatch(() =>
    {
        MainActor.Bind(AvaloniaMainExecutor.Instance);
        inBody = true;
        try { body(); }
        finally { inBody = false; }
    }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Whether the caller runs inside an <see cref="OnUiThread"/> body, where this test's dispatcher may be
    /// pumped (the session makes a new one for the next test).</summary>
    public static bool IsInBody => inBody;

    [ThreadStatic] private static bool inBody;

    /// <summary>
    /// From inside an <see cref="OnUiThread"/> body: runs the headless dispatcher in a nested frame until
    /// <paramref name="signal"/> completes. This is for a view whose Swift <c>.task</c> awaits real off-main work (an
    /// R-ISO-2 <c>@concurrent</c> Task.Run) and then resumes on main. That continuation needs this same dispatcher,
    /// which a test body occupies and a single RunJobs() may run before the continuation is queued. The frame exits
    /// the moment the signal completes, so nothing sleeps or polls. <paramref name="bound"/> only turns work that never
    /// finishes into a failure instead of a hung run.
    /// </summary>
    public static void RunDispatcherUntil(Task signal, TimeSpan bound)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!signal.IsCompleted)
        {
            var frame = new DispatcherFrame();
            // Exit on the UI thread, so the frame reads Continue after the job that completed the signal.
            void Exit() => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send);
            using var timeout = new CancellationTokenSource(bound);
            using var expired = timeout.Token.Register(Exit);
            _ = signal.ContinueWith(_ => Exit(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        if (!signal.IsCompleted)
            throw new TimeoutException("The awaited main-actor work did not finish within " + bound + ".");
    }

    // Forces the embedded Inter release files to resolve every weight this assembly's tests draw with (Regular, Bold,
    // SemiBold) before any test measures text, so a test whose first layout pass needs two glyph typefaces of the
    // same weight at once (two crossfading pages, each titled in Bold) never races the collection's own first load.
    private static void WarmFonts()
    {
        foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold })
        {
            var block = new TextBlock { Text = "Warm", FontFamily = new FontFamily("fonts:Inter#Inter"), FontWeight = weight };
            block.Measure(Size.Infinity);
        }
    }

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("Headless")]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSession>;

/// <summary>
/// The R-VIEW-9 popover seam, recording. Avalonia 12.1.2 headless cannot open a Popup ("Unable to create IPopupImpl
/// and no overlay layer is found for the target control", seen again on this machine for this unit), so a test
/// overrides <c>Presentation.Presenter</c> with this and reads what the view asked for.
/// </summary>
public sealed class RecordingPresenter : IPopoverPresenter
{
    public List<(Popup Popup, bool Presented)> Calls { get; } = [];

    public void SetPresented(Popup popup, bool presented) => Calls.Add((popup, presented));
}

/// <summary>A tiny Avalonia logical-tree query (popover content is not in a visual tree while headless).</summary>
public static class LogicalQuery
{
    public static IEnumerable<T> SelfAndDescendants<T>(ILogical root) where T : class =>
        LogicalExtensions.GetSelfAndLogicalDescendants(root).OfType<T>();
}
