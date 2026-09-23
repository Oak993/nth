using Avalonia;
using Avalonia.Headless;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Headless Avalonia entry point, for HeadlessUnitTestSession.StartNew (R-TEST-7).</summary>
public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>One headless session per test assembly, held by a collection fixture (xunit 2, no Avalonia.Headless.XUnit).</summary>
public sealed class HeadlessSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestApp));

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
