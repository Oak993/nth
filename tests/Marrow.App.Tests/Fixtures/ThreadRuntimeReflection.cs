using System.Reflection;
using Marrow.App.Runtime;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Reads and writes ThreadRuntime's private state where nothing public observes it (ED-119: Marrow.App has no
/// InternalsVisibleTo).</summary>
public static class ThreadRuntimeReflection
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static T? Get<T>(ThreadRuntime runtime, string field) =>
        (T?)(typeof(ThreadRuntime).GetField(field, Instance) ?? throw new MissingFieldException(nameof(ThreadRuntime), field)).GetValue(runtime);

    public static void Set(ThreadRuntime runtime, string field, object? value) =>
        (typeof(ThreadRuntime).GetField(field, Instance) ?? throw new MissingFieldException(nameof(ThreadRuntime), field)).SetValue(runtime, value);

    public static T? Call<T>(ThreadRuntime runtime, string method, params object?[] arguments) =>
        (T?)(typeof(ThreadRuntime).GetMethod(method, Instance) ?? throw new MissingMethodException(nameof(ThreadRuntime), method)).Invoke(runtime, arguments);
}
