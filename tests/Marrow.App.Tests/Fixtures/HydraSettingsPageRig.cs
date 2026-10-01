using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>What a Hydra settings test sees: the control under test, the model behind it and the popover requests.</summary>
public sealed record HydraSettingsScene(Control View, AppModel Model, RecordingPresenter Presenter, Avalonia.Controls.Window Window);

/// <summary>
/// Shared harness for the tests of App/Views/Settings/HydraSettingsPage.swift: a shown window around one of the
/// file's views, over an AppModelHarness model, with the popover seam recording (Avalonia headless cannot open a
/// Popup) and every provider's catalog already attempted so nothing reaches a provider CLI or the network.
/// </summary>
public static class HydraSettingsPageRig
{
    public static void Run(HeadlessSession headless, Func<AppModel, Control> create, Action<HydraSettingsScene> check, double width = 900, Action<AppModel>? arrange = null) =>
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var presenter = new RecordingPresenter();
            using var popovers = Presentation.Presenter.Override(presenter);
            var attempted = (HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model.Providers)!;
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases) attempted.Add(provider);
            arrange?.Invoke(model);
            var view = create(model);
            var host = new Border { Child = view };
            AppEnvironment.SetAppModel(host, model);
            var window = new Avalonia.Controls.Window { Width = width, Height = 1800, Content = host };
            try
            {
                window.Show();
                Sync();
                check(new HydraSettingsScene(view, model, presenter, window));
            }
            finally
            {
                window.Close();
                Sync();
            }
        });

    public static void Sync() => Dispatcher.UIThread.RunJobs();

    /// <summary>Moves the manual clock and frame source on, so an animation reaches its target.</summary>
    public static void Advance(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        ((ManualTimeProvider)SwiftClock.Provider.Current).Advance(span);
        ((ManualFrameSource)FrameClock.Source.Current).Advance(span);
        Sync();
    }

    public static IEnumerable<T> Of<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root);

    public static string Text(Control root) => string.Join("\n", Of<TextBlock>(root).Select(block => block.Text ?? ""));

    public static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Sync();
    }

    /// <summary>The settings row whose title (its first text) is exactly <paramref name="title"/>.</summary>
    public static ChromeRow Row(Control root, string title) =>
        Of<ChromeRow>(root).Single(row => Of<TextBlock>(row).First().Text == title);

    /// <summary>A row's detail line: the second text of the row's text column.</summary>
    public static string? Detail(ChromeRow row) => Of<TextBlock>(row).Skip(1).FirstOrDefault()?.Text;

    /// <summary>Marks every listed provider installed (and so ready) and every other one not.</summary>
    public static void SetInstalled(AppModel model, params ProviderKind[] installed)
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
        {
            var status = installed.Contains(provider)
                ? new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/never-launch") }
                : new ProviderStatus();
            typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[provider] = status));
        }
        Sync();
    }

    /// <summary>Sets a provider's catalog exactly (UpdateCatalog would merge the Claude seed and the ACP rows in).</summary>
    public static void SetCatalog(AppModel model, ProviderKind provider, params ModelOption[] options)
    {
        typeof(ProviderRegistry).GetProperty("Catalogs")!.SetValue(model.Providers,
            model.Providers.Catalogs.Mutating(map => map[provider] = new ValueList<ModelOption>(options)));
        Sync();
    }

    public static void SetLoading(AppModel model, ProviderKind provider)
    {
        typeof(ProviderRegistry).GetProperty("LoadingCatalogs")!.SetValue(model.Providers, model.Providers.LoadingCatalogs.Mutating(set => set.Add(provider)));
        Sync();
    }

    /// <summary>The last popover the view asked to present, with its content built.</summary>
    public static Popup LastPresented(HydraSettingsScene scene) =>
        scene.Presenter.Calls.Last(call => call.Presented).Popup;

    /// <summary>Raises the popup's Opened event, which the recording presenter never does.</summary>
    public static void RaiseOpened(Popup popup)
    {
        var field = typeof(Popup).GetField("Opened", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Popup.Opened backing field not found");
        ((EventHandler?)field.GetValue(popup))?.Invoke(popup, EventArgs.Empty);
    }

    /// <summary>Raises the popup's Closed event, as a dismissal from outside does.</summary>
    public static void RaiseClosed(Popup popup)
    {
        var field = typeof(Popup).GetField("Closed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Popup.Closed backing field not found");
        ((EventHandler<EventArgs>?)field.GetValue(popup))?.Invoke(popup, EventArgs.Empty);
    }

    /// <summary>Opens a picker the way a click does and returns the options its menu lists, in order.</summary>
    public static IReadOnlyList<PopoverItem> OpenPicker(HydraSettingsScene scene, Control picker)
    {
        var before = scene.Presenter.Calls.Count;
        Click(Of<Button>(picker).First());
        Assert.True(scene.Presenter.Calls.Count > before, "the picker asked for no popover");
        return Of<PopoverItem>(LastPresented(scene).Child!).ToArray();
    }

    public static string ItemTitle(PopoverItem item) => AutomationProperties.GetName(Of<Button>(item).First()) ?? "";

    public static void Choose(PopoverItem item) => Click(Of<Button>(item).First());

    /// <summary>What the pair editor draws under its name field, top to bottom: "section:Lead", "row:Provider", "note:..." and so on.</summary>
    public static IReadOnlyList<string> EditorOutline(Control editor) =>
        EditorChildren(editor).Skip(1).Select(child => child switch
        {
            Grid row => "row:" + Of<TextBlock>(row).First().Text,
            StackPanel label => "section:" + Of<TextBlock>(label).Single().Text,
            TextBlock note => "note:" + note.Text,
            _ => throw new InvalidOperationException("unexpected editor child " + child.GetType().Name),
        }).ToArray();

    public static IReadOnlyList<Control> EditorChildren(Control editor) => ((StackPanel)((Decorator)editor).Child!).Children;

    /// <summary>The control of the editor row titled <paramref name="row"/> under the section labelled <paramref name="section"/>; null when the row is absent.</summary>
    public static Control? EditorControl(Control editor, string section, string row)
    {
        string? current = null;
        foreach (var child in EditorChildren(editor).Skip(1))
        {
            if (child is StackPanel label) current = Of<TextBlock>(label).Single().Text;
            else if (child is Grid grid && current == section && Of<TextBlock>(grid).First().Text == row)
                return grid.Children.Single(candidate => Grid.GetColumn(candidate) == 2);
        }
        return null;
    }

    public static Control RequireEditorControl(Control editor, string section, string row) =>
        EditorControl(editor, section, row) ?? throw new InvalidOperationException($"no {section} / {row} row");

    /// <summary>The titles of the options a picker lists, in order.</summary>
    public static IReadOnlyList<string> PickerTitles(HydraSettingsScene scene, Control picker) =>
        OpenPicker(scene, picker).Select(ItemTitle).ToArray();

    /// <summary>Opens a picker and chooses the option with this title.</summary>
    public static void PickerChoose(HydraSettingsScene scene, Control picker, string title) =>
        Choose(OpenPicker(scene, picker).Single(item => ItemTitle(item) == title));
}
