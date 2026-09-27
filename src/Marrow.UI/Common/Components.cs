using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

// Source: DroppyCode/UI/Common/Components.swift (B3 52bc792), :1-166.
namespace Marrow.UI.Common;

// :4-14
public sealed class ProviderIcon : Decorator
{
    public static readonly StyledProperty<ProviderKind> ProviderProperty =
        AvaloniaProperty.Register<ProviderIcon, ProviderKind>(nameof(Provider));
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ProviderIcon, double>(nameof(Size), 14);
    public ProviderKind Provider { get => GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    // :8-12
    public ProviderIcon() => Rebuild();
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProviderProperty || change.Property == SizeProperty) Rebuild();
    }
    private void Rebuild()
    {
        var image = SwiftImage.Named(Provider.IconName).Resizable().AspectRatio(ContentMode.Fit); // :9-11
        image.Width = Size; image.Height = Size; // :12
        Child = image;
    }
}

// :16-43
public static class RelativeTime
{
    // :19 @MainActor; recent two-generation cache, exactly 256 entries per generation.
    private static readonly RecentCache<DateTimeOffset, string> DayCache = new(limit: 256);
    // :22-35
    public static string Short(DateTimeOffset date, DateTimeOffset? now = null)
    {
        MainActor.AssumeIsolated(); // :21 @MainActor
        var seconds = ((now ?? SwiftDate.Now) - date).TotalSeconds; // :23
        switch (seconds)
        {
            case < 60: return "now"; // :25
            case < 3_600: return $"{(long)(seconds / 60)}m"; // :26
            case < 86_400: return $"{(long)(seconds / 3_600)}h"; // :27
            case < 604_800: return $"{(long)(seconds / 86_400)}d"; // :28
            default:
                if (DayCache.TryGetValue(date, out var day)) return day; // :30
                day = SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Month().Day()); // :31
                DayCache.Insert(day, date); // :32
                return day;
        }
    }
    // :37-41 B3 uses Duration.UnitsFormatStyle, no longer B2's hand-written units.
    public static string Duration(TimeSpan interval) => SwiftFormatters.DurationUnits(
        Math.Max(0, checked((long)Math.Round(interval.TotalSeconds, MidpointRounding.AwayFromZero))),
        [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes, SwiftFormatters.DurationUnit.Hours],
        SwiftFormatters.CurrentLocale.Current);
}

// :45-56
public sealed class DiffStatLabel : Decorator
{
    public static readonly StyledProperty<int> AdditionsProperty = AvaloniaProperty.Register<DiffStatLabel, int>(nameof(Additions));
    public static readonly StyledProperty<int> DeletionsProperty = AvaloniaProperty.Register<DiffStatLabel, int>(nameof(Deletions));
    public int Additions { get => GetValue(AdditionsProperty); set => SetValue(AdditionsProperty, value); }
    public int Deletions { get => GetValue(DeletionsProperty); set => SetValue(DeletionsProperty, value); }
    private readonly TextBlock additions = new();
    private readonly TextBlock deletions = new();
    // :49-54
    public DiffStatLabel()
    {
        SwiftFont.Caption.MonospacedDigit().Apply(additions); // :54
        SwiftFont.Caption.MonospacedDigit().Apply(deletions);
        additions.Track(() => ChromeTokens.Success, color => additions.Foreground = color.Brush(additions)); // :51
        deletions.Track(() => ChromeTokens.Danger, color => deletions.Foreground = color.Brush(deletions)); // :52
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { additions, deletions } }; // :50
        UpdateCounts();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AdditionsProperty || change.Property == DeletionsProperty) UpdateCounts();
    }
    private void UpdateCounts()
    {
        additions.Text = "+" + Additions.ToString(CultureInfo.InvariantCulture); // :51
        deletions.Text = "−" + Deletions.ToString(CultureInfo.InvariantCulture); // :52
    }
}

// :56-89
public sealed record ChipButtonStyle(bool IsActive = false)
{
    private static readonly FuncControlTemplate<Button> ChipTemplate = new((owner, scope) =>
        new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
    // :60-67 Apply the configuration to an existing button, retaining its click, keyboard and automation behaviour
    // and the ChipBody identity that owns Swift's @State isHovering. An isActive change does not replace that view.
    public Button Apply(Button button)
    {
        if (button.Content is Border { Tag: ChipBody previous })
            previous.SetActive(IsActive); // :66-67 isActive is an input; isHovering keeps the same @State storage.
        else
        {
            var label = button.Content as Control ?? new TextBlock { Text = button.Content?.ToString() ?? "" };
            button.Content = null; // Release the configuration label's logical parent before wrapping it.
            var body = new ChipBody(button, label, IsActive);
            button.Content = body.Surface;
        }
        button.Padding = default;
        button.BorderThickness = default;
        button.Background = Brushes.Transparent;
        button.Template = ChipTemplate;
        return button;
    }

    // :64-89
    private sealed class ChipBody : IDisposable
    {
        private readonly Button button;
        private bool active; // :66 configuration input; updates preserve the body's identity.
        private readonly AnimatedColor fill;
        private bool hovering; // :67
        public Control Label { get; }
        public Border Surface { get; }
        public ChipBody(Button button, Control label, bool active)
        {
            this.button = button; this.active = active; Label = label;
            if (label is TextBlock text) { text.MaxLines = 1; text.TextWrapping = TextWrapping.NoWrap; } // :72
            Surface = new Border { Child = label, Padding = new Thickness(9, 5), Tag = this }.Capsule(RoundedCornerStyle.Circular); // :73-78
            Surface.Font(SwiftFont.Callout); // :71 inherited default, label's explicit font can override it.
            fill = new AnimatedColor(Surface); Surface.Background = fill.Brush;
            button.PointerEntered += Entered; button.PointerExited += Exited; // :80
            button.PropertyChanged += Changed; // configuration.isPressed
            button.SizeChanged += Sized;
            UpdateHitShape(); // :79 contentShape(.capsule), including the button's otherwise rectangular hit area.
            Surface.Track(() => (ChromeTokens.Accent, ChromeTokens.PrimaryText, ChromeTokens.SecondaryText), _ => Draw(null));
            button.PropertyChanged += TintChanged;
            Draw(null);
        }
        // :66-67 Updating the input does not create a fresh @State hover value or fresh event subscriptions.
        public void SetActive(bool value) { active = value; Draw(null); }
        // :75, :84-87
        private void Draw(Animation? animation)
        {
            var tint = ChipButtonStyleHelper.GetTint(button) ?? ChromeTokens.Accent;
            Surface.SetValue(TextElement.ForegroundProperty, (active ? tint : ChromeTokens.SecondaryText).Brush(Surface)); // :75
            var color = active ? tint.Opacity(button.IsPressed ? 0.26 : 0.16) // :85
                : ChromeTokens.PrimaryText.Opacity(button.IsPressed ? 0.12 : hovering ? 0.07 : 0); // :86-87
            fill.Set(color, animation);
        }
        private void Entered(object? sender, Avalonia.Input.PointerEventArgs args) { hovering = true; Draw(Animation.EaseOut(0.12)); } // :80-81
        private void Exited(object? sender, Avalonia.Input.PointerEventArgs args) { hovering = false; Draw(Animation.EaseOut(0.12)); }
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == Button.IsPressedProperty) Draw(null); }
        private void TintChanged(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == ChipButtonStyleHelper.TintProperty) Draw(null); }
        private void Sized(object? sender, SizeChangedEventArgs args) => UpdateHitShape();
        private void UpdateHitShape() => button.Clip = SwiftShapes.CapsuleGeometry(new Rect(button.Bounds.Size), RoundedCornerStyle.Circular);
        public void Dispose() { button.PointerEntered -= Entered; button.PointerExited -= Exited; button.PropertyChanged -= Changed; button.PropertyChanged -= TintChanged; button.SizeChanged -= Sized; }
    }
}

// :92-97 Keeps the existing helper name, and exposes the .chip / .chip(active:) reusable forms.
public static class ChipButtonStyleHelper
{
    public static readonly AttachedProperty<SwiftColor?> TintProperty = AvaloniaProperty.RegisterAttached<Button, Control, SwiftColor?>("ChipTint", inherits: true);
    public static SwiftColor? GetTint(Control control) => control.GetValue(TintProperty);
    public static void SetTint(Control control, SwiftColor? tint) => control.SetValue(TintProperty, tint);
    public static ChipButtonStyle ChipStyle { get; } = new(); // :95
    public static ChipButtonStyle ChipStyleActive(bool active) => new(active); // :97-99
    public static Button Chip(this Button button, bool active = false) => new ChipButtonStyle(active).Apply(button);
}

// :101-168
public static class Workspace
{
    // :102-107
    public sealed record Editor
    {
        public required string Name { get; init; }
        public required string BundleID { get; init; }
        public string Id => BundleID; // :103
    }
    // :109-117 DEVIATION OD-0012 G4: Xcode and iTerm omitted; Terminal means Windows Terminal.
    public static IReadOnlyList<Editor> Editors { get; } = new ReadOnlyCollection<Editor>([
        new() { Name = "Visual Studio Code", BundleID = "com.microsoft.VSCode" },
        new() { Name = "Cursor", BundleID = "com.todesktop.230313mzl4w4u92" },
        new() { Name = "Zed", BundleID = "dev.zed.Zed" },
        new() { Name = "Terminal", BundleID = "com.apple.Terminal" },
        new() { Name = "Ghostty", BundleID = "com.mitchellh.ghostty" },
    ]);

    // :119-167 PORT-ONLY boundary implemented in Port/SwiftImage.cs (allowed foundation unit file).
    public sealed class Backend : WorkspacePlatform;
    public static Ambient<Backend> Platform { get; } = new(() => new Backend());
    private sealed class Cache(Backend backend)
    {
        public Lazy<IReadOnlyList<Editor>> Installed { get; } = new(() => new ReadOnlyCollection<Editor>(Editors.Where(e => Resolve(e, backend) is not null).ToArray())); // :139-141
        public Dictionary<string, IImage> Icons { get; } = new(StringComparer.Ordinal); // :150
        public Lazy<IImage?> ExplorerIcon { get; } = new(() => backend.ReadIcon(backend.Explorer)); // :163
    }
    private static readonly ConditionalWeakTable<Backend, Cache> IconCache = new();
    private static Cache CurrentCache => IconCache.GetValue(Platform.Current, backend => new Cache(backend));

    // :120-135 DEVIATION ED-089: direct -Command launch needs no script, so no temp file can be overwritten.
    public static void OpenTerminal(string command)
    {
        var backend = Platform.Current;
        var shell = backend.Which("pwsh.exe") ?? backend.Which("powershell.exe") ?? backend.WindowsPowerShell; // OD-0008 C5
        if (backend.Which("wt.exe") is { } terminal)
            backend.Launch(terminal, ["-w", "new", "new-tab", "-d", backend.Home, shell, "-NoExit", "-Command", command], backend.Home, false);
        else backend.Launch(shell, ["-NoExit", "-Command", command], backend.Home, true);
    }
    // :139-141 Once per process backend; tests get isolated caches with their own backend.
    public static IReadOnlyList<Editor> InstalledEditors => CurrentCache.Installed.Value;
    // :143-146 DEVIATION ED-087: executable registry/PATH resolution substitutes for Launch Services.
    public static void Open(string path, Editor editor)
    {
        var backend = Platform.Current;
        if (Resolve(editor, backend) is not { } executable) return;
        backend.Launch(executable, editor.BundleID == "com.apple.Terminal" ? ["-d", path] : [path], null, false);
    }
    // :153-160
    public static object? Icon(Editor editor)
    {
        MainActor.AssumeIsolated(); // :152 @MainActor
        var cache = CurrentCache;
        if (cache.Icons.TryGetValue(editor.BundleID, out var cached)) return cached;
        var backend = Platform.Current;
        if (Resolve(editor, backend) is not { } application || backend.ReadIcon(application) is not { } image) return null;
        var sized = new SizedIcon(image);
        cache.Icons[editor.BundleID] = sized;
        return sized;
    }
    // :163 DEVIATION OD-0012 G4: File Explorer's executable icon instead of Finder's.
    public static object? FileExplorerIcon => MainActor.AssumeIsolated(() => CurrentCache.ExplorerIcon.Value);
    // :165-167 The native backend forwards to the existing ShellWorkspace seam.
    public static void RevealInFileExplorer(string path) => Platform.Current.Reveal(path);

    private static string? Resolve(Editor editor, Backend backend)
    {
        var pair = editor.BundleID switch // :109-117 DEVIATION ED-087
        {
            "com.microsoft.VSCode" => ("Code.exe", "code"),
            "com.todesktop.230313mzl4w4u92" => ("Cursor.exe", "cursor"),
            "dev.zed.Zed" => ("Zed.exe", "zed"),
            "com.apple.Terminal" => ("wt.exe", "wt.exe"),
            _ => ((string, string)?)null, // Ghostty has no Windows build; unknown editor resolves nothing.
        };
        if (pair is not { } names) return null;
        foreach (var machine in new[] { false, true })
            if (backend.AppPath(names.Item1, machine) is { } registered && backend.Exists(registered)) return registered;
        return backend.Which(names.Item2);
    }
    // :158 NSImage's size changes its drawing size, not the source pixels.
    private sealed class SizedIcon(IImage image) : IImage
    {
        public Size Size => new(16, 16);
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) => image.Draw(context,
            new Rect(sourceRect.X * image.Size.Width / 16, sourceRect.Y * image.Size.Height / 16,
                sourceRect.Width * image.Size.Width / 16, sourceRect.Height * image.Size.Height / 16), destRect);
    }
}
