using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A ModelsSettingsPage in a real headless window on an offline AppModel, with only the named providers available and
/// every catalogue load held back (R-TEST-10): the registry's once-per-launch attempt gate is closed for every provider
/// except those named in <c>ungated</c>, which must be API-key providers that hold no key, so a load that is let through
/// installs the offline seed and returns before any request. Frames and time are manual: nothing animates until
/// <see cref="Advance"/>.
/// </summary>
internal sealed class ModelsSettingsSectionRig : IDisposable
{
    private readonly AppModelHarness harness = new();
    private readonly IDisposable actor;
    private readonly IDisposable time;
    private readonly IDisposable frames;
    private readonly HashSet<ProviderKind> attempted;
    private bool watching;

    public ModelsSettingsSectionRig(ProviderKind[] shown, Action<AppModel>? seed = null, string query = "", ProviderKind[]? ungated = null, int height = 1600)
    {
        Model = harness.MakeModel();
        actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        time = SwiftClock.Provider.Override(new ManualTimeProvider());
        frames = FrameClock.Source.Override(new ManualFrameSource());
        foreach (var provider in ungated ?? [])
        {
            if (!provider.IsAPIKeyBased || Model.Settings.ApiKey(provider).Length != 0)
                throw new InvalidOperationException(provider + " could reach a real CLI or the network on a catalogue load.");
        }
        attempted = (HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Model.Providers)!;
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (ungated is null || !ungated.Contains(provider)) attempted.Add(provider);
        var statuses = ValueMap<ProviderKind, ProviderStatus>.Empty;
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            statuses = statuses.Mutating(map => map[provider] = shown.Contains(provider)
                ? new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/never-launch") }
                : new ProviderStatus());
        typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(Model.Providers, statuses);
        seed?.Invoke(Model);
        var host = new Border { Padding = new Thickness(20) };
        AppEnvironment.SetAppModel(host, Model);
        Page = new ModelsSettingsPage(query);
        host.Child = Page;
        Window = new Avalonia.Controls.Window { Width = 700, Height = height, Content = host };
        Window.Show();
        Sync();
    }

    public AppModel Model { get; }
    public ModelsSettingsPage Page { get; }
    public Avalonia.Controls.Window Window { get; }
    public InMemorySettingsStore Defaults => harness.Defaults;

    /// <summary>The transaction (the animation of the withAnimation block) the last change of the chosen models was made in; null when none.</summary>
    public Animation? ModelListTransaction { get; private set; }

    /// <summary>Starts recording <see cref="ModelListTransaction"/>: a view body that reads the chosen models runs inside the transaction of the change.</summary>
    public void WatchModelList()
    {
        if (watching)
        {
            ModelListTransaction = null;
            return;
        }
        watching = true;
        var probe = new Border();
        probe.Track(() => Model.Settings.ModelList, _ => ModelListTransaction = Transaction.Current);
        var host = (Border)Window.Content!;
        host.Child = null;
        host.Child = new StackPanel { Children = { Page, probe } };
        Sync();
        ModelListTransaction = null;
    }

    /// <summary>The registry's once-per-launch attempt gate: a provider in it is not loaded by an unforced ask.</summary>
    public bool IsGated(ProviderKind provider) => attempted.Contains(provider);

    public void Sync()
    {
        for (var i = 0; i < 3; i++)
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Moves the manual clock and frame source on, then lays out again.</summary>
    public void Advance(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        ((ManualTimeProvider)SwiftClock.Provider.Current).Advance(span);
        ((ManualFrameSource)FrameClock.Source.Current).Advance(span);
        Sync();
    }

    /// <summary>Marks a provider's catalogue as loading, as the registry does while a load is out (LoadingCatalogs).</summary>
    public void SetLoading(ProviderKind provider, bool loading)
    {
        var current = Model.Providers.LoadingCatalogs;
        typeof(ProviderRegistry).GetProperty("LoadingCatalogs")!.SetValue(Model.Providers,
            current.Mutating(set => { if (loading) set.Add(provider); else set.Remove(provider); }));
        Sync();
    }

    /// <summary>Empties a provider's catalogue (the registry seeds the API providers at start-up and never empties one).</summary>
    public static Action<AppModel> ClearCatalogue(ProviderKind provider) => model =>
    {
        var registry = model.Providers;
        typeof(ProviderRegistry).GetProperty("Catalogs")!.SetValue(registry, registry.Catalogs.Mutating(map => map.Remove(provider)));
    };

    public static IEnumerable<T> Of<T>(Control control) where T : class => LogicalQuery.SelfAndDescendants<T>(control);

    public static string TextOf(Control control) => string.Join("\n", Of<TextBlock>(control).Select(text => text.Text ?? ""));

    /// <summary>The section of the provider whose header carries its display name.</summary>
    public ProviderModelsSection SectionOf(ProviderKind provider) =>
        Of<ProviderModelsSection>(Page).Single(section => Of<Button>(section).Any(button => AutomationProperties.GetName(button) == provider.DisplayName));

    public Button HeaderOf(ProviderKind provider) =>
        Of<Button>(SectionOf(provider)).First(button => AutomationProperties.GetName(button) == provider.DisplayName);

    /// <summary>Puts a control in the window's place of the page (the page is left, as when another settings page is chosen).</summary>
    public void Mount(Control control)
    {
        ((Border)Window.Content!).Child = control;
        Sync();
    }

    public void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Sync();
    }

    public void Dispose()
    {
        try
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            frames.Dispose();
            time.Dispose();
            actor.Dispose();
            harness.Dispose();
        }
    }
}
