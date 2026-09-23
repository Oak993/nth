using Avalonia;
using Avalonia.Headless;
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
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>One headless session per test assembly, held by a collection fixture.</summary>
public sealed class HeadlessSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(AppTestApp));

    /// <summary>Runs a test body on the headless UI thread with MainActor bound to Avalonia, and waits for it.</summary>
    public void OnUiThread(Action body) => Session.Dispatch(() =>
    {
        MainActor.Bind(AvaloniaMainExecutor.Instance);
        body();
    }, CancellationToken.None).GetAwaiter().GetResult();

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("Headless")]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSession>;
