using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

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

/// <summary>A test-only SwiftUI animation value; the real curves come from the Chrome.swift units.</summary>
public sealed record EaseOutSample(double Duration) : Marrow.UI.Port.Animation;

/// <summary>A cell like Core/Support/ObservedValue.swift: an unconstrained Value, so every set notifies.</summary>
public sealed class CellSample<TValue>(TValue value) : ObservableModel
{
    private TValue value = value;

    public TValue Value { get => Read(value); set => SetAlways(ref this.value, value); }
}

/// <summary>A model with a computed property reading another object's cell (App/AppModel.swift:292-294 shape).</summary>
public sealed class SelectionSample : ObservableModel
{
    private readonly Dictionary<string, CellSample<string>> cells = new(StringComparer.Ordinal); // @ObservationIgnored
    private string? selected;

    public string? Selected { get => Read(selected); set => Set(ref selected, value); }

    public void Put(string id, string title)
    {
        if (cells.TryGetValue(id, out var cell)) cell.Value = title;
        else cells[id] = new CellSample<string>(title);
    }

    public string SelectedTitle => Computed(() => Selected is { } id && cells.TryGetValue(id, out var cell) ? cell.Value : "");
}

/// <summary>A row control with its own @State (Expanded) and a disposal record, for the ForEach identity test.</summary>
public sealed class RowProbe(string id, List<string> disposed) : TextBlock, IDisposable
{
    public bool Expanded { get; set; }

    public void Dispose() => disposed.Add(id);
}

/// <summary>A tiny Avalonia visual-tree query, without Avalonia.VisualTree extension imports at call sites.</summary>
public static class VisualQuery
{
    public static IEnumerable<T> Descendants<T>(Visual root) where T : Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
