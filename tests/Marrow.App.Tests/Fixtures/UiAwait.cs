namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// W10 tests: waits, from inside a headless UI test body, for a task whose continuations need the UI thread (an await that resumes on it). The
/// dispatcher runs in a nested frame until the task is done, so the body never blocks the thread the task needs; the bound only turns work that
/// never finishes into a failure.
/// </summary>
internal static class UiAwait
{
    public static T Of<T>(Task<T> task, TimeSpan? bound = null)
    {
        T value = default!;
        async Task Capture() => value = await task;
        var capture = Capture();
        HeadlessSession.RunDispatcherUntil(capture, bound ?? TimeSpan.FromSeconds(15));
        Assert.True(capture.IsCompletedSuccessfully, capture.Exception?.ToString());
        return value;
    }

    public static void Of(Task task, TimeSpan? bound = null)
    {
        async Task Capture() => await task;
        var capture = Capture();
        HeadlessSession.RunDispatcherUntil(capture, bound ?? TimeSpan.FromSeconds(15));
        Assert.True(capture.IsCompletedSuccessfully, capture.Exception?.ToString());
    }
}
