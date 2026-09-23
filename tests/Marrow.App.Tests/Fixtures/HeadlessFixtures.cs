using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture: the headless Avalonia session of R-TEST-7 (ED-082) for Marrow.App.Tests, the same shape as
// tests/Marrow.UI.Tests/Fixtures/HeadlessFixtures.cs (one HeadlessUnitTestSession per test assembly, held by a
// collection fixture; xunit 2.9.3, no Avalonia.Headless.XUnit). Skia drawing is on, so a test can read the pixels of
// a rendered frame (Window.CaptureRenderedFrame).

/// <summary>Headless Avalonia entry point, for HeadlessUnitTestSession.StartNew (R-TEST-7).</summary>
public sealed class AppTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AppTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithSwiftFonts(); // ED-107 (5): the one font registration point, shared with the app shell's AppBuilder and
                            // with tests/Marrow.UI.Tests/Fixtures/HeadlessFixtures.cs's TestApp; a TextBlock's layout
                            // pass needs a resolvable Inter glyph typeface even under headless drawing.
}

/// <summary>One headless session per test assembly, held by a collection fixture.</summary>
public sealed class HeadlessSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(AppTestApp));

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
