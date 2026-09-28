using System.Diagnostics;

namespace Marrow.App.Tests.Fixtures;

// Observe the real .NET HTTP transport, including URLSession's private shared HttpClient. No replacement checker.
public sealed class SettingsCoreHttpRequests : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly List<IDisposable> subscriptions = [];
    public List<object> Requests { get; } = [];
    public SettingsCoreHttpRequests() => subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
    public void OnNext(DiagnosticListener listener) { if (listener.Name == "HttpHandlerDiagnosticListener") subscriptions.Add(listener.Subscribe(this)); }
    public void OnNext(KeyValuePair<string, object?> e) { if (e.Key == "System.Net.Http.HttpRequestOut.Start" && e.Value is not null) Requests.Add(e.Value); }
    public void OnError(Exception error) => throw error;
    public void OnCompleted() { }
    public void Dispose() { foreach (var subscription in subscriptions) subscription.Dispose(); }
}
