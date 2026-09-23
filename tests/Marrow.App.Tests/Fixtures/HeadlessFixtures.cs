using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
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
        body();
    }, CancellationToken.None).GetAwaiter().GetResult();

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
