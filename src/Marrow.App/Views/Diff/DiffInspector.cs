using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :1-692.

/// <summary>:4-155 <c>struct DiffInspector: View</c>: the thread's diff, file by file.</summary>
public sealed class DiffInspector : Decorator
{
    private readonly ThreadRuntime runtime;
    private readonly Grid root;
    private readonly ScrollViewer scrollViewer;
    private readonly StackPanel filesPanel;
    private readonly Border stateContainer;
    private readonly AnimatedScalar stateOpacity;
    private readonly AnimatedScalar filesOpacity;
    private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    private ValueList<DiffFile> files = ValueList<DiffFile>.Empty;
    private FileChangeSummary summary = FileChangeSummaryDiff.Create(ValueList<DiffFile>.Empty);
    private ValueList<TurnRecord> changedTurns = ValueList<TurnRecord>.Empty;
    private bool isLoading;
    private string? scrollTarget;
    private long scrollRequest;
    private long scrolledRequest;
    private string? loadKey;
    private TaskHandle? loadTask;
    private AuroraDiffLoadingState? loadingState;
    private readonly List<AuroraDiffLoadingState> exitingStates = [];

    // :5, :22-121
    public DiffInspector(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        Width = DiffPopoverCoordinator.Width;
        Height = DiffPopoverCoordinator.Height;
        filesPanel = new StackPanel { Spacing = 10 };
        scrollViewer = new ScrollViewer
        {
            Content = filesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        stateContainer = new Border
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        stateOpacity = new AnimatedScalar(stateContainer, 1, value => stateContainer.Opacity = value);
        filesOpacity = new AnimatedScalar(scrollViewer, 1, value => scrollViewer.Opacity = value);
        // :24 VStack(spacing: 0): the chrome row at its own height, the rest of the 540 to the empty state or the scroll view
        // (a vertical StackPanel measures the scroll view without a limit, so it would never scroll).
        root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Child = root;
        runtime.PropertyChanged += RuntimePropertyChanged;
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Release();
        KeyDown += HandleKeyDown;
        BuildRoot();
    }

    // ED-NEW-perf-memory-1: the runtime outlives every inspector the Changes popover shows, so a closed one must stop
    // listening or each open leaves a live listener that rebuilds a chrome row on every streamed change and re-parses the diff.
    // The constructor subscribes (an inspector that is built and used before it is shown still follows the runtime);
    // leaving the visual tree unsubscribes and ends the load; coming back resubscribes and catches up on what it missed.
    private bool released;

    private void Attach()
    {
        runtime.PropertyChanged -= RuntimePropertyChanged;
        runtime.PropertyChanged += RuntimePropertyChanged;
        LoadForCurrentKey();
        if (!released) return;
        released = false;
        BuildRoot();
    }

    /// <summary>Stops following the runtime and ends the load; also what the popover calls before it drops the content.</summary>
    public void Release()
    {
        runtime.PropertyChanged -= RuntimePropertyChanged;
        loadTask?.Cancel();
        if (released) return;
        released = true;
        loadKey = null; // a load that was cut short runs again on the next attach
    }

    // :18-20
    private string LoadKey =>
        runtime.DiffRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-"
        + (runtime.DiffSelection?.ToString() ?? "all") + "-"
        + runtime.DiffFocusRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // :22-108
    private void BuildRoot()
    {
        var leaving = files.Count != 0 || !isLoading ? loadingState : null;
        if (leaving is not null) loadingState = null;
        Control body = files.Count == 0 ? stateContainer : scrollViewer;
        // Keep the real content owner and active exit overlays mounted while rebuilding chrome. Detaching a
        // reused owner would cancel AnimatedScalar, freezing content at its partial arrival opacity.
        foreach (var child in root.Children.ToArray())
            if (!ReferenceEquals(child, body) && !(child is AuroraDiffLoadingState exit && exitingStates.Contains(exit)))
            {
                // A reused body must not retain an interrupted arrival opacity after its scalar stops on detach.
                if (ReferenceEquals(child, stateContainer)) stateOpacity.Set(1, null);
                if (ReferenceEquals(child, scrollViewer)) filesOpacity.Set(1, null);
                root.Children.Remove(child);
            }
        if (files.Count != 0 || !isLoading || !ReferenceEquals(stateContainer.Child, loadingState)) stateContainer.Child = null;

        var menu = new ChromeTextMenu(
            "plusminus",
            SelectionTitle(),
            "Choose which changes to show",
            BuildSelectionMenu);
        var close = new ChromeIconButton(
            "xmark",
            "Hide changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges),
            () => runtime.IsDiffVisible = false);
        var more = new ChromeMenuButton("ellipsis", "More", BuildMoreMenu);

        // :26 HStack(spacing: 10) holding the menu, a flexible Spacer(minLength: 8), the counts when there are files, and the
        // capsule. A horizontal StackPanel gives a spacer no free width, so the row is a grid: the 8-point minimum sits in the
        // star column between two 10-point gaps, and it takes everything the other items leave.
        var hasFiles = files.Count != 0;
        var chromeRow = new Grid
        {
            Margin = new Thickness(14, 12, 14, 10),
            ColumnDefinitions = hasFiles
                ? new ColumnDefinitions("Auto,10,*,10,Auto,10,Auto,10,Auto")
                : new ColumnDefinitions("Auto,10,*,10,Auto"),
        };
        chromeRow.ColumnDefinitions[2].MinWidth = 8; // :45 Spacer(minLength: 8)
        SwiftGlass.SetContainerSpacing(chromeRow, 2); // :25 GlassEffectContainer(spacing: 2)
        void Place(Control item, int column)
        {
            item.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; // HStack's default alignment
            Grid.SetColumn(item, column);
            chromeRow.Children.Add(item);
        }

        Place(menu, 0);
        if (hasFiles) // :46-54
        {
            Place(new TextBlock
            {
                Text = summary.Files.Count == 1
                    ? "1 file"
                    : summary.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " files",
                FontFamily = DiffFaces.Inter,
                FontSize = 11,
                Foreground = Chrome.SecondaryText.Brush(this),
            }, 4);
            Place(new DiffStatLabel
            {
                Additions = (int)summary.Additions,
                Deletions = (int)summary.Deletions,
            }, 6);
        }

        Place(new ChromeCapsule(() =>
        {
            var trailing = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
            };
            trailing.Children.Add(more);
            trailing.Children.Add(new ChromeDivider());
            trailing.Children.Add(close);
            return trailing;
        }), hasFiles ? 8 : 4);
        Grid.SetRow(chromeRow, 0);
        root.Children.Insert(0, chromeRow);

        if (files.Count == 0)
        {
            var state = new StackPanel
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Spacing = 8,
            };
            if (isLoading)
            {
                // DEVIATION OD-0113: loader 5 is a glass placeholder, not an icon. The label below is retained.
                loadingState ??= new AuroraDiffLoadingState();
            }
            else
            {
                state.Children.Add(new SwiftImageContentUnavailable("doc.text.magnifyingglass"));
                state.Children.Add(new TextBlock
                {
                    Text = "No changes yet",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                });
                state.Children.Add(new TextBlock
                {
                    Text = "Files the agent changes appear here after each turn.",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                });
            }

            stateContainer.Child = isLoading ? loadingState : state;
            Grid.SetRow(stateContainer, 1);
            if (!root.Children.Contains(stateContainer)) root.Children.Add(stateContainer);
            root.Children.Move(root.Children.IndexOf(stateContainer), 1);
        }
        else
        {
            filesPanel.Children.Clear();
            foreach (var file in files)
            {
                var isCollapsed = collapsed.Contains(file.Id);
                filesPanel.Children.Add(new DiffFileCard(file, isCollapsed, () => ToggleCollapsed(file.Id)));
            }

            // :98 .padding(12) is on the lazy stack inside the scroll view, so it scrolls with the cards.
            filesPanel.Margin = new Thickness(12);
            Grid.SetRow(scrollViewer, 1);
            if (!root.Children.Contains(scrollViewer)) root.Children.Add(scrollViewer);
            root.Children.Move(root.Children.IndexOf(scrollViewer), 1);
            // :100-106 .onChange(of: scrollRequest): the scroll runs once for each request, not on every redraw.
            if (scrollTarget is not null && scrolledRequest != scrollRequest)
            {
                var requested = scrollRequest;
                scrolledRequest = requested;
                MainActor.Post(() =>
                {
                    if (requested != scrollRequest || scrollTarget is null) return;
                    scrollViewer.UpdateLayout(); // :102 after layout, so the card is there to scroll to
                    var target = filesPanel.Children
                        .OfType<Control>()
                        .FirstOrDefault(control => control.Tag as string == scrollTarget);
                    if (target is not null)
                        scrollViewer.Offset = new Vector(0, Math.Max(0, target.Bounds.Y + filesPanel.Margin.Top));
                });
            }
        }
        if (leaving is not null)
        {
            // Locked pane.frame: content starts 50 ms into the loader's exit and fades over 300 ms.
            var opacity = files.Count == 0 ? stateOpacity : filesOpacity;
            opacity.Set(0, null);
            opacity.Set(1, Animation.TimingCurve(1.0 / 3, 0, 2.0 / 3, 1, .3).Delay(.05));
            exitingStates.Add(leaving);
            Grid.SetRow(leaving, 1); root.Children.Add(leaving);
            leaving.Complete(() => { exitingStates.Remove(leaving); root.Children.Remove(leaving); });
        }
    }

    // :27-44
    private Control BuildSelectionMenu()
    {
        var menu = new StackPanel { Spacing = 2 };
        // :34 isChecked: the row keeps the checkmark column whether or not it is the selection (Popovers.swift:276-281).
        menu.Children.Add(new PopoverItem("All changes", () =>
        {
            runtime.ClearDiffFocus();
            runtime.DiffSelection = null;
        }, isChecked: runtime.DiffSelection is null));
        foreach (var turn in changedTurns)
        {
            var selectedTurn = turn;
            menu.Children.Add(new PopoverItem(
                "Turn " + (turn.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                () =>
                {
                    runtime.ClearDiffFocus();
                    runtime.DiffSelection = selectedTurn.Id;
                },
                isChecked: runtime.DiffSelection == turn.Id)); // :39
        }

        return menu;
    }

    // :29-32
    private string SelectionTitle()
    {
        if (runtime.DiffSelection is { } id)
        {
            var turn = runtime.Turns.FirstOrDefault(candidate => candidate.Id == id);
            if (turn is not null)
                return "Turn " + (turn.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return "All changes";
    }

    // :55-64
    private Control BuildMoreMenu()
    {
        var menu = new StackPanel { Spacing = 2 };
        menu.Children.Add(new PopoverItem("Expand all", action: () =>
        {
            collapsed.Clear();
            BuildRoot();
        }, symbol: "arrow.down.right.and.arrow.up.left"));
        menu.Children.Add(new PopoverItem("Collapse all", action: () =>
        {
            collapsed.Clear();
            foreach (var file in files) collapsed.Add(file.Id);
            BuildRoot();
        }, symbol: "arrow.up.left.and.arrow.down.right"));
        return menu;
    }

    // :91-96
    private void ToggleCollapsed(string id)
    {
        if (!collapsed.Add(id)) collapsed.Remove(id);
        BuildRoot();
    }

    // :110-120
    private void RuntimePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        var nextKey = LoadKey;
        if (nextKey != loadKey)
        {
            LoadForCurrentKey();
            // :29-32 the menu's title and :46 the rest of the body read the runtime as it changes, not once the load has run.
            BuildRoot();
            return;
        }

        BuildRoot();
    }

    // :110-120
    private void LoadForCurrentKey()
    {
        var key = LoadKey;
        if (key == loadKey) return;
        loadKey = key;
        loadTask?.Cancel();
        loadTask = MainActor.Spawn(async () =>
        {
            if (!runtime.HasCachedDiff(runtime.DiffSelection))
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(180));
                if (SwiftTask.IsCancelled) return;
            }

            await Load();
        });
    }

    // :123-154
    private async Task Load()
    {
        var selection = runtime.DiffSelection;
        changedTurns = runtime.TurnsWithChanges();
        isLoading = true;
        BuildRoot();
        try
        {
            var parsed = await runtime.ParsedDiff(selection);
            if (SwiftTask.IsCancelled) return;

            var focusEdits = runtime.DiffFocusEdits;
            var missing = await TaskHandle.Detached(() =>
                Task.FromResult(FocusedFiles(focusEdits, parsed))).Value;
            if (SwiftTask.IsCancelled) return;

            files = missing.Count == 0
                ? parsed
                : new ValueList<DiffFile>(missing.Concat(parsed));
            summary = FileChangeSummaryDiff.Create(files);
            var focus = new HashSet<string>(
                runtime.DiffFocusEdits.Select(edit => edit.Path),
                SwiftStringComparer.Instance);
            var focused = focus.Count == 0
                ? Array.Empty<string>()
                : files.Where(file => TouchedPaths.Matches(file, focus)).Select(file => file.Id).ToArray();

            if (focused.Length != 0)
            {
                collapsed.Clear();
                foreach (var file in files)
                    if (!focused.Contains(file.Id, StringComparer.Ordinal))
                        collapsed.Add(file.Id);
                scrollTarget = focused[0];
                scrollRequest++;
            }
            else if (files.Count > 6 && collapsed.Count == 0)
            {
                foreach (var file in files.Skip(1)) collapsed.Add(file.Id);
            }
            else
            {
                var current = new HashSet<string>(files.Select(file => file.Id), SwiftStringComparer.Instance);
                collapsed.IntersectWith(current);
            }
        }
        finally
        {
            isLoading = false;
            BuildRoot();
        }
    }

    // :162-173
    internal static ValueList<DiffFile> FocusedFiles(IReadOnlyList<FileEdit> edits, IReadOnlyList<DiffFile> missingFrom)
    {
        var missing = new List<DiffFile>();
        foreach (var edit in edits)
        {
            if (edit.Path.Length == 0 || edit.Diff is not { Length: > 0 } patch) continue;
            var touched = new HashSet<string>(SwiftStringComparer.Instance) { edit.Path };
            if (missingFrom.Any(file => TouchedPaths.Matches(file, touched))
                || missing.Any(file => TouchedPaths.Matches(file, touched)))
                continue;
            var file = DiffParser.ParseHunks(patch, edit.Path);
            if (file.Hunks.Count != 0) missing.Add(file);
        }

        return new ValueList<DiffFile>(missing);
    }

    // :423-434
    internal static ValueList<DiffLineBlock> LineBlocks(IReadOnlyList<DiffLine> lines)
    {
        var blocks = new List<DiffLineBlock>();
        foreach (var line in lines)
        {
            var tinted = line.LineKind is DiffLine.Kind.Addition or DiffLine.Kind.Deletion;
            if (blocks.Count != 0 && blocks[^1].IsTinted == tinted)
            {
                var previous = blocks[^1];
                blocks[^1] = previous with { Lines = previous.Lines.Appending(line) };
            }
            else
            {
                blocks.Add(new DiffLineBlock { Lines = new ValueList<DiffLine>([line]), IsTinted = tinted });
            }
        }

        return new ValueList<DiffLineBlock>(blocks);
    }

    // :668-672
    private void HandleKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        runtime.IsDiffVisible = false;
        args.Handled = true;
    }

    // Port rendering for ContentUnavailableView's source image.
    private sealed class SwiftImageContentUnavailable : Control
    {
        private readonly SwiftImage image;

        public SwiftImageContentUnavailable(string symbol)
        {
            image = SwiftImage.SystemName(symbol).Font(SwiftFont.System(SwiftFont.TextStyle.Title2));
            Width = 32;
            Height = 32;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            image.Measure(new Size(Bounds.Width, Bounds.Height));
            image.Arrange(new Rect(Bounds.Size));
            image.Render(context);
        }
    }
}

/// <summary>:175-250 <c>private struct DiffFileCard: View, Equatable</c>: one file's card.</summary>
internal sealed class DiffFileCard : Decorator
{
    private readonly DiffFile file;
    private readonly bool isCollapsed;

    // :176-178, :192-231
    public DiffFileCard(DiffFile file, bool isCollapsed, Action toggle)
    {
        this.file = file;
        this.isCollapsed = isCollapsed;
        Tag = file.Id;

        var body = new StackPanel { Spacing = 0 };
        var header = new Button
        {
            Padding = new Thickness(12, 9),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = file.Id,
            Content = BuildHeader(file, isCollapsed),
        };
        header.Click += (_, _) => toggle();
        body.Children.Add(header);
        if (!isCollapsed)
        {
            if (file.IsBinary || file.Hunks.Count == 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = file.IsBinary ? "Binary file" : "Diff details unavailable",
                    Margin = new Thickness(12, 0, 12, 10),
                    Foreground = Chrome.SecondaryText.Brush(this),
                    FontFamily = DiffFaces.Inter,
                    FontSize = 10,
                });
            }
            else
            {
                body.Children.Add(new DiffLinesView(file) { Margin = new Thickness(0, 0, 0, 6) });
            }
        }

        Child = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = SwiftColor.Quaternary.Opacity(0.3).Brush(this),
            Child = body,
        }.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :229-230 `.rect(cornerRadius: 12, style: .continuous)`; a short block is in ED-275 (2)'s band (OD-0102)
    }

    // :182-190
    public static bool InputsEqual(DiffFileCard lhs, DiffFileCard rhs) =>
        lhs.file.Id == rhs.file.Id
        && lhs.file.Additions == rhs.file.Additions
        && lhs.file.Deletions == rhs.file.Deletions
        && lhs.file.Hunks.Count == rhs.file.Hunks.Count
        && lhs.file.FileChange == rhs.file.FileChange
        && lhs.file.IsBinary == rhs.file.IsBinary
        && lhs.isCollapsed == rhs.isCollapsed;

    // :194-208
    private static Control BuildHeader(DiffFile file, bool isCollapsed)
    {
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var chevron = SwiftImage.SystemName("chevron.right")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Semibold))
            .ForegroundStyle(Chrome.SecondaryText);
        chevron.RenderTransform = new RotateTransform(isCollapsed ? 0 : 90);
        chevron.RenderTransformOrigin = RelativePoint.Center;
        row.Children.Add(chevron);

        row.Children.Add(SwiftImage.SystemName(ChangeSymbol(file.FileChange))
            .ForegroundStyle(ChangeColor(file.FileChange)));

        row.Children.Add(new DiffFilePathText(file.Directory, file.Name)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        });

        row.Children.Add(new Border
        {
            MinWidth = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        });

        row.Children.Add(new DiffStatLabel
        {
            Additions = (int)file.Additions,
            Deletions = (int)file.Deletions,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        return row;
    }

    // :233-249
    private static string ChangeSymbol(DiffFile.Change change) => change switch
    {
        DiffFile.Change.Added => "plus.circle.fill",
        DiffFile.Change.Deleted => "minus.circle.fill",
        DiffFile.Change.Renamed => "arrow.right.circle.fill",
        DiffFile.Change.Modified => "pencil.circle.fill",
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };

    // :242-249
    private static SwiftColor ChangeColor(DiffFile.Change change) => change switch
    {
        DiffFile.Change.Added => SwiftColor.Hex("#34C759"),
        DiffFile.Change.Deleted => SwiftColor.Hex("#FF3B30"),
        DiffFile.Change.Renamed => SwiftColor.Hex("#007AFF"),
        DiffFile.Change.Modified => SwiftColor.Hex("#FF9500"),
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}

/// <summary>:202-205 The card path, with head truncation so its file name stays visible.</summary>
internal sealed class DiffFilePathText : Panel
{
    private readonly string directory;
    private readonly string name;
    private readonly TextBlock directoryText;
    private readonly TextBlock nameText;

    // DiffInspector.swift:202-205
    public DiffFilePathText(string directory, string name)
    {
        this.directory = directory;
        this.name = name;
        directoryText = new TextBlock
        {
            Text = directory,
            Foreground = Chrome.SecondaryText.Brush(this),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontFamily = DiffFaces.Inter,
            FontSize = 12,
        };
        nameText = new TextBlock
        {
            Text = name,
            Foreground = SwiftColor.Primary.Brush(this),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontFamily = DiffFaces.Inter,
            FontSize = 12,
        };
        Children.Add(directoryText);
        Children.Add(nameText);
    }

    // DiffInspector.swift:202-205
    protected override Size MeasureOverride(Size availableSize)
    {
        directoryText.Measure(Size.Infinity);
        nameText.Measure(Size.Infinity);
        return new Size(directoryText.DesiredSize.Width + nameText.DesiredSize.Width,
            Math.Max(directoryText.DesiredSize.Height, nameText.DesiredSize.Height));
    }

    // DiffInspector.swift:204-205
    protected override Size ArrangeOverride(Size finalSize)
    {
        var nameWidth = Math.Min(nameText.DesiredSize.Width, finalSize.Width);
        var directoryWidth = Math.Max(0, finalSize.Width - nameWidth);
        var characters = SwiftString.Characters(directory).ToArray();
        var visibleDirectory = directory;
        if (directoryText.DesiredSize.Width > directoryWidth)
        {
            visibleDirectory = "";
            for (var start = characters.Length - 1; start >= 0; start--)
            {
                var candidate = "\u2026" + string.Concat(characters.Skip(start));
                directoryText.Text = candidate;
                directoryText.Measure(Size.Infinity);
                if (directoryText.DesiredSize.Width <= directoryWidth)
                    visibleDirectory = candidate;
                else
                    break;
            }
        }

        directoryText.Text = visibleDirectory;
        directoryText.Measure(Size.Infinity);
        var actualDirectoryWidth = Math.Min(directoryText.DesiredSize.Width, directoryWidth);
        directoryText.Arrange(new Rect(0, 0, actualDirectoryWidth, finalSize.Height));
        nameText.Arrange(new Rect(actualDirectoryWidth, 0, nameWidth, finalSize.Height));
        return finalSize;
    }
}

/// <summary>:252-412 <c>struct DiffLinesView: View, Equatable</c>: a file's hunks as rows.</summary>
public sealed class DiffLinesView : Decorator
{
    // :274
    private const long CollapsedLineLimit = 200;
    // :275
    private const long TintedChunk = 40;

    // :253-254, :390-411
    public DiffLinesView(DiffFile file, bool showsLineNumbers = true)
    {
        var totalLines = file.Hunks.Sum(hunk => (long)hunk.Lines.Count);
        var rows = new StackPanel { Spacing = 0 };
        var content = new StackPanel { Spacing = 0 };
        content.Children.Add(rows);

        if (totalLines > CollapsedLineLimit)
        {
            var showAll = new Button
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Margin = new Thickness(10, 8),
                Padding = new Thickness(10, 6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            var capsuleLabel = new TextBlock
            {
                Text = "Show all " + totalLines.ToString(System.Globalization.CultureInfo.InvariantCulture) + " lines",
                FontFamily = DiffFaces.Inter,
                FontSize = 10,
                Foreground = Chrome.SecondaryText.Brush(showAll),
                Margin = new Thickness(10, 6),
            };
            showAll.Content = new Border
            {
                CornerRadius = new CornerRadius(1000),
                Background = SwiftColor.Quaternary.Opacity(0.5).Brush(showAll),
                Child = capsuleLabel,
            };
            var expanded = false;
            showAll.Click += (_, _) =>
            {
                if (expanded) return;
                expanded = true;
                rows.Children.Clear();
                foreach (var section in MakeSections(file, showsLineNumbers, long.MaxValue))
                    AddSection(rows, section, showsLineNumbers);
                content.Children.Remove(showAll);
            };
            content.Children.Add(showAll);
        }

        foreach (var section in MakeSections(file, showsLineNumbers, CollapsedLineLimit))
            AddSection(rows, section, showsLineNumbers);

        Child = content;
    }

    // :258-260
    public static bool InputsEqual(DiffLinesView lhs, DiffLinesView rhs) =>
        ReferenceEquals(lhs, rhs);

    // :285-297
    public abstract record Section
    {
        private Section() { }

        // :286
        public sealed record Header(string Text, long Index) : Section;
        // :287
        public sealed record Tinted(ValueList<DiffLine> Lines, long Offset, long Count) : Section;
        // :288
        public sealed record Plain(ValueList<DiffLine> Value) : Section;

        // :290-296
        public string Id => this switch
        {
            Header header => "h" + header.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + header.Text,
            Tinted tinted => "t" + (tinted.Lines.Count == 0 ? "0" : tinted.Lines[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Plain plain => "p" + (plain.Value.Count == 0 ? "0" : plain.Value[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(),
        };
    }

    // :300-343
    public static ValueList<Section> MakeSections(DiffFile file, bool showsLineNumbers, long limit)
    {
        var sections = new List<Section>();
        var pending = new List<DiffLine>();
        var remaining = limit;

        void Flush()
        {
            if (pending.Count == 0) return;
            for (var start = 0; start < pending.Count; start += (int)TintedChunk)
            {
                var end = Math.Min(start + (int)TintedChunk, pending.Count);
                sections.Add(new Section.Tinted(
                    new ValueList<DiffLine>(pending.Skip(start).Take(end - start)),
                    start,
                    pending.Count));
            }

            pending.Clear();
        }

        for (var hunkIndex = 0; hunkIndex < file.Hunks.Count; hunkIndex++)
        {
            var hunk = file.Hunks[hunkIndex];
            if (hunk.Lines.Count == 0 || remaining <= 0) continue;

            var takeCount = (int)Math.Min(remaining, hunk.Lines.Count);
            var runs = DiffInspector.LineBlocks(hunk.Lines.Take(takeCount).ToArray());
            var continues = runs.Count != 0 && runs[0].IsTinted && pending.Count != 0;
            if (!continues)
            {
                Flush();
                if (showsLineNumbers)
                    sections.Add(new Section.Header(hunk.Header, hunkIndex));
            }

            foreach (var run in runs)
            {
                if (remaining <= 0) break;
                var take = (int)Math.Min(remaining, run.Lines.Count);
                if (run.IsTinted)
                {
                    pending.AddRange(run.Lines.Take(take));
                    remaining -= take;
                }
                else
                {
                    Flush();
                    var plain = new ValueList<DiffLine>(run.Lines.Take(take));
                    remaining -= take;
                    sections.Add(new Section.Plain(plain));
                }
            }
        }

        Flush();
        return new ValueList<Section>(sections);
    }

    // :345-388
    private static void AddSection(Panel rows, Section section, bool showsLineNumbers)
    {
        switch (section)
        {
            case Section.Header header:
                rows.Children.Add(new SelectableTextBlock
                {
                    Text = header.Text,
                    Foreground = Chrome.SecondaryText.Brush(rows),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                    Margin = new Thickness(10, 3),
                    // :355 `Color.accentColor`, named, not the `.tint` style: the app's accent colour (B3 ships no AccentColor
                    // asset, so the system's), which the window's tint does not change (ThemeTint, WindowManager.swift:186-192,
                    // sets the tint). OD-0088 (ED-253): not one of Chrome.accent's sites, so the pick does not reach it.
                    Background = SwiftColor.AccentColor.Opacity(0.06).Brush(rows),
                    // :406 .font(.system(size: 11.5, design: .monospaced)): the embedded face's key is fonts:CascadiaMono (SwiftFont.cs:79).
                    FontFamily = DiffFaces.Mono,
                    FontSize = 11.5,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                });
                break;
            case Section.Tinted tinted:
                var block = new StackPanel { Spacing = 0 };
                for (var index = 0; index < tinted.Lines.Count; index++)
                    block.Children.Add(new DiffLineRow(
                        tinted.Lines[index],
                        showsLineNumbers,
                        DiffLineRow.BlockRounding(tinted.Offset + index, tinted.Count)));
                rows.Children.Add(block);
                break;
            case Section.Plain plain:
                foreach (var line in plain.Value)
                    rows.Children.Add(new DiffLineRow(line, showsLineNumbers));
                break;
            default:
                throw new InvalidOperationException();
        }
    }
}

/// <summary>:416-421 <c>private struct DiffLineBlock: Identifiable</c>: a maximal run of lines that share tinting.</summary>
internal sealed record DiffLineBlock
{
    public required ValueList<DiffLine> Lines { get; init; } // :417
    public required bool IsTinted { get; init; } // :418
    // :420
    public long Id => Lines.Count == 0 ? 0 : Lines[0].Id;
}

/// <summary>
/// ED-NEW-lean-17: the inspector's two faces, made once. A <see cref="FontFamily"/> is immutable, so every text block
/// shares one instance instead of parsing the same name per line (three per diff row before).
/// </summary>
internal static class DiffFaces
{
    /// <summary>The embedded Inter face (SwiftFont.cs:79).</summary>
    public static readonly FontFamily Inter = new("fonts:Inter#Inter");
    /// <summary>:406 .font(.system(size: 11.5, design: .monospaced)): the embedded face's key is fonts:CascadiaMono (SwiftFont.cs:80).</summary>
    public static readonly FontFamily Mono = new("fonts:CascadiaMono#Cascadia Mono");
}

/// <summary>:436-513 <c>private struct DiffLineRow: View</c>: one diff line in fixed-width columns.</summary>
internal sealed class DiffLineRow : Decorator
{
    // :472
    private const double NumberColumnWidth = 30;
    // :473
    private const double MarkerColumnWidth = 14;

    // ED-NEW-lean-17: the gutter's colours were resolved per line against a throwaway control outside any window (one
    // new Control per brush), so they never followed the appearance. They are resolved once, the same way, into one shared
    // brush per line kind: the same colours, without a control and a brush per line.
    private static readonly Avalonia.Styling.ThemeVariant DetachedTheme = new Control().ActualThemeVariant;
    private static IBrush Detached(SwiftColor color) => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color.Resolve(DetachedTheme));
    private static readonly IBrush NumberBrush = Detached(Chrome.SecondaryText);
    private static readonly IBrush AdditionMarker = Detached(SwiftColor.Hex("#34C759"));
    private static readonly IBrush DeletionMarker = Detached(SwiftColor.Hex("#FF3B30"));
    private static readonly IBrush OtherMarker = Detached(SwiftColor.Secondary);
    private static readonly IBrush AdditionBackground = Detached(SwiftColor.Hex("#34C759").Opacity(0.12));
    private static readonly IBrush DeletionBackground = Detached(SwiftColor.Hex("#FF3B30").Opacity(0.12));

    // :437-442, :444-469
    public DiffLineRow(DiffLine line, bool showsLineNumbers, CornerRadius rounding = default)
    {
        var row = new Grid
        {
            // ED-NEW-lean-17: the columns "30,30,14,*" or "14,*" and the row "Auto", built without parsing the strings per line.
            ColumnDefinitions = showsLineNumbers
                ? new ColumnDefinitions
                {
                    new ColumnDefinition(NumberColumnWidth, GridUnitType.Pixel),
                    new ColumnDefinition(NumberColumnWidth, GridUnitType.Pixel),
                    new ColumnDefinition(MarkerColumnWidth, GridUnitType.Pixel),
                    new ColumnDefinition(1, GridUnitType.Star),
                }
                : new ColumnDefinitions
                {
                    new ColumnDefinition(MarkerColumnWidth, GridUnitType.Pixel),
                    new ColumnDefinition(1, GridUnitType.Star),
                },
            RowDefinitions = new RowDefinitions { new RowDefinition(GridLength.Auto) },
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var column = 0;
        if (showsLineNumbers)
        {
            var oldNumber = Number(line.OldNumber, NumberColumnWidth);
            Grid.SetColumn(oldNumber, column++);
            row.Children.Add(oldNumber);

            var newNumber = Number(line.NewNumber, NumberColumnWidth);
            Grid.SetColumn(newNumber, column++);
            row.Children.Add(newNumber);
        }

        var marker = new SelectableTextBlock
        {
            Text = Marker(line.LineKind),
            Width = MarkerColumnWidth,
            TextAlignment = TextAlignment.Center,
            Foreground = MarkerColor(line.LineKind),
            FontFamily = DiffFaces.Mono,
            FontSize = 11.5,
        };
        Grid.SetColumn(marker, column++);
        row.Children.Add(marker);

        var text = new SelectableTextBlock
        {
            Text = line.Text.Length == 0 ? " " : line.Text,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Foreground = line.LineKind == DiffLine.Kind.Note
                ? SwiftColor.Secondary.Brush(this)
                : SwiftColor.Primary.Brush(this),
            FontFamily = DiffFaces.Mono,
            FontSize = 11.5,
        };
        Grid.SetColumn(text, column);
        row.Children.Add(text);

        Child = new Border
        {
            CornerRadius = rounding,
            Padding = new Thickness(6, 1, 8, 1),
            Background = Background(line.LineKind),
            Child = row,
        };
    }

    // :450-455
    private static TextBlock Number(long? number, double width) => new SelectableTextBlock
    {
        Text = number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        Width = width,
        TextAlignment = TextAlignment.Right,
        Foreground = NumberBrush,
        FontFamily = DiffFaces.Mono,
        FontSize = 11.5,
    };

    // :477-488
    public static CornerRadius BlockRounding(long index, long count)
    {
        const double radius = 8;
        var top = index == 0;
        var bottom = index == count - 1;
        return new CornerRadius(top ? radius : 0, top ? radius : 0, bottom ? radius : 0, bottom ? radius : 0);
    }

    // :490-495
    private static string Marker(DiffLine.Kind kind) => kind switch
    {
        DiffLine.Kind.Addition => "+",
        DiffLine.Kind.Deletion => "\u2212",
        DiffLine.Kind.Context or DiffLine.Kind.Note => "",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // :498-503
    private static IBrush MarkerColor(DiffLine.Kind kind) => kind switch
    {
        DiffLine.Kind.Addition => AdditionMarker,
        DiffLine.Kind.Deletion => DeletionMarker,
        _ => OtherMarker,
    };

    // :506-511
    private static IBrush Background(DiffLine.Kind kind) => kind switch
    {
        DiffLine.Kind.Addition => AdditionBackground,
        DiffLine.Kind.Deletion => DeletionBackground,
        _ => Brushes.Transparent,
    };
}

/// <summary>:520-692 <c>@MainActor final class DiffPopoverCoordinator</c>: the thread's diff as a tall, wide popover.</summary>
public sealed class DiffPopoverCoordinator
{
    // :522
    public const double Width = 640;
    // :523
    public const double Height = 540;

    private readonly Popup popover;
    private WeakView? anchor;
    private WeakView? fallback;
    private ThreadRuntime? runtime;
    private ThreadRuntime? shownRuntime;
    private DiffInspector? inspector;
    private Control? shownOn;
    private bool desiredVisible;
    private long session;
    private long pendingSession;
    private long seenOpenRequest;
    private bool isShown;
    private IDisposable? pointerMonitor;
    private IDisposable? keyMonitor;
    private bool reopening;
    private long? notifiedCloseSession;

    // :534-539
    public DiffPopoverCoordinator()
    {
        popover = new Popup
        {
            Width = Width,
            Height = Height,
        };
        popover.Closed += (_, _) =>
        {
            // The popup really closed (its dismissal ran to its end, or its window went): the mirror of NSPopover.isShown follows it, so a
            // close that the Swift's stale-session rule lets skip the rest (CompletePopoverClose) cannot leave a closed popup marked shown.
            if (!popover.IsOpen) isShown = false;
            PopoverDidClose();
        };
    }

    // :542-545
    public void SetAnchor(Control view)
    {
        anchor = new WeakView(view);
        if (desiredVisible) Show();
    }

    // :551-571
    public void Sync(bool isVisible, ThreadRuntime runtime)
    {
        if (this.runtime is { } current && !ReferenceEquals(current, runtime))
        {
            Close();
            if (current.IsDiffVisible) current.IsDiffVisible = false;
            this.runtime = runtime;
            seenOpenRequest = runtime.DiffOpenRequest;
            if (runtime.IsDiffVisible) runtime.IsDiffVisible = false;
            return;
        }

        if (this.runtime is null)
        {
            this.runtime = runtime;
            seenOpenRequest = runtime.DiffOpenRequest;
            if (isVisible)
            {
                runtime.IsDiffVisible = false;
                return;
            }
        }

        this.runtime = runtime;
        desiredVisible = isVisible;
        if (isVisible) Show();
        else Close();
    }

    // :573-579
    public void Close()
    {
        StopMonitors(); // :574
        desiredVisible = false;
        session++;
        pendingSession = session;
        if (isShown)
        {
            HidePopover();
        }
    }

    // :583-586
    public void SetFallbackAnchor(Control view)
    {
        fallback = new WeakView(view);
        if (desiredVisible) Show();
    }

    // :599-612
    public void Reopen(ThreadRuntime runtime)
    {
        if (!ReferenceEquals(runtime, this.runtime))
        {
            Sync(runtime.IsDiffVisible, runtime);
            return;
        }

        if (runtime.DiffOpenRequest == seenOpenRequest) return;
        seenOpenRequest = runtime.DiffOpenRequest;
        desiredVisible = true;
        var resolved = ResolvedAnchor();
        if (isShown)
        {
            if (resolved is { } currentTarget && ReferenceEquals(shownOn, currentTarget.View)) return;
            StopMonitors(); // :606
            session++;
            pendingSession = session;
            reopening = true;
            try { HidePopover(); }
            finally { reopening = false; }
        }

        Show();
    }

    // :614-625
    private (Control View, PlacementMode Edge)? ResolvedAnchor()
    {
        if (runtime?.DiffAnchor?.Value is { } runtimeView && TopLevel.GetTopLevel(runtimeView) is not null)
        {
            return (runtimeView, runtime.DiffAnchorEdge == PreviewPanelEdge.MinY
                ? PlacementMode.Bottom
                : PlacementMode.Top);
        }

        var view = new[] { anchor?.Value, fallback?.Value }
            .FirstOrDefault(candidate => candidate is not null && TopLevel.GetTopLevel(candidate) is not null);
        return view is null ? null : (view, PlacementMode.Top);
    }

    // :627-636
    private void Show()
    {
        if (runtime is null || ResolvedAnchor() is not { } target) return;
        session++;
        if (isShown) return;
        ReleaseInspector(); // a reopen replaces the content; the old one stops listening (ED-NEW-perf-memory-1)
        inspector = new DiffInspector(runtime);
        popover.SetFixedContent(inspector, new Size(Width, Height));
        popover.PlacementTarget = target.View;
        popover.Placement = target.Edge;
        shownOn = target.View;
        shownRuntime = runtime;
        StartMonitors(); // :632
        isShown = true;
        Presentation.Presenter.Current.SetPresented(popover, true);
    }

    private void ReleaseInspector()
    {
        inspector?.Release();
        inspector = null;
    }

    // :640-658
    public void PopoverDidClose()
    {
        var closedSession = pendingSession;
        if (notifiedCloseSession == closedSession) return;
        notifiedCloseSession = closedSession;
        // :640-658. A reopen's close finishes after Show has incremented the session, as the Swift Task does.
        if (reopening) Dispatcher.UIThread.Post(() => CompletePopoverClose(closedSession));
        else CompletePopoverClose(closedSession);
    }

    // :606-612,640-658. The native Closed event owns notification; the recording seam needs one fallback only.
    private void HidePopover()
    {
        isShown = false;
        Presentation.Presenter.Current.SetPresented(popover, false);
        if (!popover.IsOpen && notifiedCloseSession != pendingSession) PopoverDidClose();
    }

    // :640-658. UPSTREAM DEFECT 67, REPRODUCED (PORT_CONTRACT section 8, ED-196, PR-GIT-137): a close that a reopen started reaches
    // here after the new show, and the Swift does not check the session before it stops the monitors, clears desiredVisible and
    // drops shownRuntime; only the flag and content clear is gated on session == pendingSession. So the reopened popover keeps its flag
    // and content but ignores outside clicks and Escape until it is closed another way (the X button, the tab, the changes shortcut,
    // the thread going away: all of which this method does not touch) and shown afresh.
    private void CompletePopoverClose(long closedSession)
    {
        _ = closedSession; // the Swift Task captures nothing: it reads the live session (:645)
        StopMonitors(); // :643
        desiredVisible = false; // :644
        if (session == pendingSession)
        {
            // Only the close that posted this may clear the flag and drop the content (:645-654).
            isShown = false; // the popup really closed: no newer show took it over
            if (shownRuntime is { IsDiffVisible: true } shown)
                shown.IsDiffVisible = false;
            ReleaseInspector(); // the visual-tree detach may never come if the popup is already gone
            popover.Child = null;
        }

        shownRuntime = null; // :656
    }

    // :662-666. The popup's content TopLevel is the Swift content window. Every other left/right press
    // dismisses before routing to its target, without setting Handled (port_impacts X2, PR-GIT-137).
    private void HandleMouseDown(TopLevel sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind is not
            (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return;
        if (popover.Child is Control content && ReferenceEquals(sender, TopLevel.GetTopLevel(content))) return;
        Close();
    }

    // :668-672. The app-wide monitor consumes Escape even when focus never entered the popup.
    private void HandleKeyDown(TopLevel sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    // :674-686. Class handlers on TopLevel tunnel through every app window and the popup's own root,
    // including windows opened after this monitor pair; registrations live only while the popover is shown.
    private void StartMonitors()
    {
        if (pointerMonitor is not null || keyMonitor is not null) return;
        pointerMonitor = InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(HandleMouseDown, RoutingStrategies.Tunnel);
        keyMonitor = InputElement.KeyDownEvent.AddClassHandler<TopLevel>(HandleKeyDown, RoutingStrategies.Tunnel);
    }

    // :688-691.
    private void StopMonitors()
    {
        pointerMonitor?.Dispose();
        pointerMonitor = null;
        keyMonitor?.Dispose();
        keyMonitor = null;
    }
}
