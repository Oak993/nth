using Avalonia.Logging;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: a log sink that keeps the errors it is told.</summary>
public sealed class RecordingLogSink : ILogSink
{
    public List<string> Errors { get; } = [];
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Error;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Errors.Add(area + ": " + messageTemplate);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Errors.Add(area + ": " + messageTemplate + " " + string.Join(" ", propertyValues.Select(v => v?.GetType().Name)));
}
