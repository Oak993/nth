using Avalonia;
using Avalonia.Headless;

namespace Marrow.Setup.Tests.Fixtures;

/// <summary>
/// One headless Avalonia session for the setup's window tests (nth installer PLAN.md 10, "Screens, headless"; unit U1). It
/// runs the setup's own application class (its ScrollViewer theme) on Skia with real drawing, as the setup does, but on
/// the headless platform, which has no Mica: the window falls back to its solid tint there, as the reference renders do.
/// </summary>
public sealed class SetupHeadlessSession : IDisposable
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SetupApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(SetupHeadlessSession));

    /// <summary>Runs <paramref name="body"/> on the headless UI thread and waits for it (retried once on the session's start-up race).</summary>
    public void OnUiThread(Action body)
    {
        try { DispatchOnce(body); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("a different thread owns it", StringComparison.Ordinal))
        {
            DispatchOnce(body);
        }
    }

    private void DispatchOnce(Action body) => Session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("SetupHeadless")]
public sealed class SetupHeadlessCollection : ICollectionFixture<SetupHeadlessSession>;
