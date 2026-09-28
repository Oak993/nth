namespace Marrow.App.Tests.Views.Chat;

// Owned fixture lifetime: close while the test's actor is bound and drain Avalonia jobs.
internal sealed class TimelineRowsUiHost(Avalonia.Controls.Window window) : IDisposable
{
    public void Dispose()
    {
        window.Content = null;
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
