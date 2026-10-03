using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Shared bits for the HydraMergePopover tests (App/Views/Chat/HydraMergePopover.swift): merge note bodies in the
/// format AppModel+HydraMerge writes, their parsed notes, and small queries over the card's logical tree.
/// </summary>
public static class HydraMergeCardProbe
{
    public const string GitlabUrl = "https://gitlab.com/acme/app/-/merge_requests/7";

    // U+2212 MINUS SIGN, the only minus the files line accepts (HydraMergePopover.swift:60).
    public const string Minus = "\U00002212";

    // U+2014 EM DASH, the head line's separator (HydraMergePopover.swift:44).
    public const string EmDash = "\U00002014";

    public static string FilesLine(long files = 2, long additions = 3, long deletions = 1, string target = "main", string branch = "hydra/fix-a1b2c3") =>
        $"{files} file{(files == 1 ? "" : "s")} (+{additions} {Minus}{deletions}) landed on `{target}` from `{branch}`.";

    public static string HeadLine(string name, long index, string task, params string[] files) =>
        $"- {name} ({index}): {task} {EmDash} " +
        (files.Length == 0 ? "no files" : $"{files.Length} file{(files.Length == 1 ? "" : "s")}: {string.Join(", ", files)}");

    public static SwiftURL Url(string value) =>
        SwiftURL.Create(value) ?? throw new Xunit.Sdk.XunitException("bad fixture URL " + value);

    public static MergeRequestLink Link(string value = GitlabUrl) =>
        MergeRequestLink.Create(Url(value)) ?? throw new Xunit.Sdk.XunitException("bad merge request URL " + value);

    public static HydraMergeNote Parse(string body, string url = GitlabUrl) =>
        HydraMergeNote.Parse(body, Link(url)) ?? throw new Xunit.Sdk.XunitException("fixture body must parse as a merge note");

    /// <summary>A note with <paramref name="heads"/> heads (roster indices 0..n-1), each with one file.</summary>
    public static HydraMergeNote NoteWithHeads(int heads, string url = GitlabUrl)
    {
        var lines = new List<string> { FilesLine(), "Heads" };
        for (var i = 0; i < heads; i++)
            lines.Add(HeadLine(HydraRoster.Persona(i).Name, i, "Task " + i, "src/File" + i + ".cs"));
        return Parse(string.Join("\n", lines), url);
    }

    public static T[] Descendants<T>(ILogical root) where T : class =>
        LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    public static TextBlock? Text(ILogical root, string value) =>
        Descendants<TextBlock>(root).FirstOrDefault(block => block.Text == value);

    public static TextBlock RequiredText(ILogical root, string value) =>
        Text(root, value) ?? throw new Xunit.Sdk.XunitException("no TextBlock reading \"" + value + "\"; found: " +
            string.Join(" | ", Descendants<TextBlock>(root).Select(block => block.Text)));

    public static SwiftImage Symbol(ILogical root, string name) =>
        Descendants<SwiftImage>(root).First(image => image.SymbolName == name);

    /// <summary>The control's resolved foreground equals <paramref name="token"/> (light theme).</summary>
    public static void AssertForeground(Control control, SwiftColor token)
    {
        var brush = Assert.IsType<SolidColorBrush>(control switch
        {
            TextBlock text => text.Foreground,
            SwiftImage image => image.Foreground,
            _ => null,
        });
        Assert.Equal(token.Resolve(ThemeVariant.Light), brush.Color);
    }

    /// <summary>The text block carries exactly the font properties <paramref name="expected"/> applies.</summary>
    public static void AssertFont(TextBlock actual, SwiftFont expected)
    {
        var reference = new TextBlock();
        expected.Apply(reference);
        Assert.Equal(reference.FontSize, actual.FontSize);
        Assert.Equal(reference.FontWeight, actual.FontWeight);
        Assert.Equal(reference.FontStyle, actual.FontStyle);
        Assert.Equal(reference.FontFamily.Name, actual.FontFamily.Name);
        Assert.Equal(
            (reference.FontFeatures ?? []).Select(feature => feature.Tag + "=" + feature.Value),
            (actual.FontFeatures ?? []).Select(feature => feature.Tag + "=" + feature.Value));
    }

    public static Color Resolved(SwiftColor token) => token.Resolve(ThemeVariant.Light);

    public static Color SolidColor(IBrush? brush) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    /// <summary>The Border whose child is the StackPanel holding <paramref name="text"/> (a stat tile).</summary>
    public static Border TileOf(ILogical root, string text) =>
        Assert.IsType<Border>(Assert.IsType<StackPanel>(RequiredText(root, text).Parent).Parent);

    /// <summary>Shows <paramref name="content"/> in a headless window, laid out and rendered.</summary>
    public static Avalonia.Controls.Window Show(Control content, double width = 420, double height = 400)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Seeds FaviconCache's memory (the cached-favicon path); disposing removes every trace of the host.</summary>
    public static IDisposable SeedFavicon(string host, Avalonia.Media.IImage image) => new HydraMergeFaviconSeed(host, image, null);

    /// <summary>Makes FaviconCache.Image(host) await a pending task, so the async favicon path runs with no network.</summary>
    public static IDisposable SeedInFlightFavicon(string host, Task<Avalonia.Media.IImage?> pending) => new HydraMergeFaviconSeed(host, null, pending);
}

/// <summary>Reaches FaviconCache's private tables so a test never touches the network (R-TEST-10).</summary>
public sealed class HydraMergeFaviconSeed : IDisposable
{
    private readonly string key;

    public HydraMergeFaviconSeed(string host, Avalonia.Media.IImage? image, Task<Avalonia.Media.IImage?>? pending)
    {
        key = host;
        // The seed is the host's whole state: a failure or an icon another test's fetch left behind (FaviconCache.image checks the
        // memory and the failed set before the in-flight table, MarkdownView.swift:916-924) would otherwise bypass it.
        ((System.Collections.Generic.HashSet<string>)Field("failed")!).Remove(key);
        if (image is null) ((System.Collections.IDictionary)Field("memory")!).Remove(key);
        if (image is not null) ((System.Collections.IDictionary)Field("memory")!)[key] = image;
        if (pending is not null) ((System.Collections.IDictionary)Field("inFlight")!)[key] = pending;
    }

    private static object? Field(string name) => typeof(Marrow.UI.Markdown.FaviconCache)
        .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null);

    public void Dispose()
    {
        ((System.Collections.IDictionary)Field("memory")!).Remove(key);
        ((System.Collections.IDictionary)Field("inFlight")!).Remove(key);
        ((System.Collections.Generic.HashSet<string>)Field("failed")!).Remove(key);
    }
}
