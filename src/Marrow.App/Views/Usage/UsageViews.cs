using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :1-396.

namespace Marrow.App.Views.Usage;

/// <summary>:7-80 <c>struct PlanLimitsView: View</c>: a provider's plan usage limits as the registry last read them.</summary>
public sealed class PlanLimitsView : Decorator
{
    /// <param name="provider">:9 <c>let provider: ProviderKind</c>.</param>
    // :11-79 body
    public PlanLimitsView(ProviderKind provider)
    {
        var content = new StackPanel { Spacing = 0 };
        Child = content;
        BankedResetsView? resets = null; // :32-39 stable SwiftUI identity while this branch exists.
        var hasPreviousLimits = false;
        PlanLimits? previousLimits = null;
        this.Track(
            () =>
            {
                var model = AppEnvironment.GetAppModel(this);
                return (model.Providers.PlanLimits.GetValueOrDefault(provider), model.Providers.LoadingLimits.Contains(provider));
            },
            state =>
            {
                var limitsChanged = hasPreviousLimits &&
                    !EqualityComparer<PlanLimits?>.Default.Equals(previousLimits, state.Item1);
                previousLimits = state.Item1;
                hasPreviousLimits = true;

                if (limitsChanged)
                {
                    Transaction.WithAnimation(Chrome.PanelSlide, () => Update(state.Item1, state.Item2));
                }
                else
                {
                    Update(state.Item1, state.Item2);
                }
            });

        void Update(PlanLimits? limits, bool isLoading)
        {
            // Keep the stateful reset control attached: moving it through a fresh parent would cancel its task.
            foreach (var child in content.Children.ToArray())
                if (!ReferenceEquals(child, resets)) content.Children.Remove(child);
            if (limits?.PlanLimitsResetCredits is null && resets is not null)
            {
                content.Children.Remove(resets);
                resets = null;
            }
            var insertionIndex = 0;
            void Add(Control child) => content.Children.Insert(insertionIndex++, child);
            var title = limits?.PlanName is { } planName
                ? "Plan usage limits \U000000B7 " + planName // U+00B7 MIDDLE DOT
                : "Plan usage limits";
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            header.Children.Add(Label(title, SwiftFont.Body, Chrome.SecondaryText));
            if (isLoading)
            {
                var spinner = Spinner();
                spinner.Margin = new Thickness(24, 0, 0, 0);
                Grid.SetColumn(spinner, 1);
                header.Children.Add(spinner);
            }
            Add(header);

            if (limits is { } available)
            {
                foreach (var window in available.Windows)
                {
                    var row = new LimitRow(window);
                    row.Margin = new Thickness(0, 14, 0, 0);
                    Add(row);
                }

                if (available.PlanLimitsResetCredits is { } credits)
                {
                    if (available.Windows.Count > 0)
                    {
                        var rule = PlainControls.Divider(); // :34-35 Divider().padding(.top, 14); a bare Separator drew nothing
                        rule.Margin = new Thickness(0, 14, 0, 0);
                        Add(rule);
                    }

                    if (resets is null) resets = new BankedResetsView(provider, credits);
                    else resets.Update(credits); // :217-218 preserve phase and settle as input changes.
                    resets.Margin = new Thickness(0, 14, 0, 0);
                    if (!content.Children.Contains(resets)) Add(resets);
                    else insertionIndex++;
                }

                if (available.Problem is { } problem)
                {
                    if (provider.IsAPIKeyBased)
                    {
                        var warning = SwiftImage.SystemName("exclamationmark.triangle.fill")
                            .Font(SwiftFont.Footnote.FontWeight(SwiftFont.Weight.Semibold));
                        warning.Foreground = Chrome.Warning.Brush(warning);
                        var note = Label(problem, SwiftFont.Subheadline, Chrome.SecondaryText);
                        note.MaxLines = 2;
                        note.TextTrimming = TextTrimming.CharacterEllipsis; // :53 .lineLimit(2)
                        note.TextWrapping = TextWrapping.Wrap;
                        note.Margin = new Thickness(6, 0, 0, 0);
                        var warningRow = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 14, 0, 0),
                        };
                        warningRow.ColumnDefinitions[2].MinWidth = 14;
                        warningRow.Children.Add(warning);
                        Grid.SetColumn(note, 1);
                        warningRow.Children.Add(note);
                        var spacer = new Border();
                        Grid.SetColumn(spacer, 2);
                        warningRow.Children.Add(spacer);
                        Add(warningRow);
                    }
                    else
                    {
                        var label = Label("Click here to login again", SwiftFont.Callout, Chrome.SecondaryText);
                        label.Margin = new Thickness(0, 0, 24, 0);
                        var login = new Button { Content = "Login" };
                        login.Click += (_, _) => Marrow.App.Port.ProviderLoginWatch.Open(AppEnvironment.GetAppModel(this).Providers, provider); // owner finding F2
                        var problemRow = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 14, 0, 0),
                        };
                        problemRow.Children.Add(label);
                        // :58-68 the button is the last item of the HStack, at its trailing end. The column belongs on the
                        // glass wrapper the grid holds, not on the button inside it: on the button the wrapper stayed in
                        // column 0 and stretched across the label.
                        var loginGlass = login.ChromeGlassCapsule();
                        Grid.SetColumn(loginGlass, 1);
                        problemRow.Children.Add(loginGlass);
                        Add(problemRow);
                    }
                }
            }
            else if (!isLoading)
            {
                var empty = Label(provider.DisplayName + " did not report plan limits.", SwiftFont.Callout, Chrome.SecondaryText);
                empty.Margin = new Thickness(0, 8, 0, 0);
                Add(empty);
            }

            Child = content;
        }
    }

    private static ProgressBar Spinner() => new()
    {
        IsIndeterminate = true,
        Width = 12, // .controlSize(.mini): the nth spinner (step 1 B) draws from 12 pt (was 10 pt, 2-chat-use.mov 145.0-145.6 s; PR-CHAT-099, OD-0136 amendment 2)
        Height = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Label(string value, SwiftFont font, SwiftColor color)
    {
        var label = new TextBlock { Text = value, Foreground = color.Brush(new TextBlock()) };
        font.Apply(label);
        label.Foreground = color.Brush(label);
        return label;
    }
}

/// <summary>:86-139 <c>struct CreditsView: View</c>: a pay-as-you-go balance as one settings row.</summary>
public sealed class CreditsView : Decorator
{
    private readonly ProviderKind provider;

    /// <param name="provider">:88 <c>let provider: ProviderKind</c>.</param>
    // :90-126 body
    public CreditsView(ProviderKind provider)
    {
        this.provider = provider;
        var hasPreviousCredits = false;
        ProviderCredits? previousCredits = null;
        this.Track(
            () =>
            {
                var model = AppEnvironment.GetAppModel(this);
                return (model.Providers.Credits.GetValueOrDefault(provider), model.Providers.LoadingCredits.Contains(provider));
            },
            state =>
            {
                var creditsChanged = hasPreviousCredits &&
                    !EqualityComparer<ProviderCredits?>.Default.Equals(previousCredits, state.Item1);
                previousCredits = state.Item1;
                hasPreviousCredits = true;

                if (creditsChanged)
                {
                    Transaction.WithAnimation(Animation.EaseOut(duration: 0.15), () => Update(state.Item1, state.Item2));
                }
                else
                {
                    Update(state.Item1, state.Item2);
                }
            });

        void Update(ProviderCredits? credits, bool isLoading)
        {
            var left = new StackPanel { Spacing = 2 };
            var title = new TextBlock { Text = "Remaining credits", Foreground = Chrome.PrimaryText.Brush(this) };
            SwiftFont.Body.Apply(title);
            left.Children.Add(title);

            if (Note(credits, isLoading) is { } note)
            {
                var noteLabel = new TextBlock
                {
                    Text = note,
                    Foreground = Chrome.SecondaryText.Brush(this),
                    TextWrapping = TextWrapping.Wrap,
                };
                SwiftFont.Subheadline.Apply(noteLabel);
                left.Children.Add(noteLabel);
            }

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (isLoading) right.Children.Add(CreditsSpinner());
            if (credits is { } balance)
            {
                var amount = new TextBlock { Text = balance.AmountText };
                SwiftFont.Body.FontWeight(SwiftFont.Weight.Semibold).MonospacedDigit().Apply(amount);
                amount.Foreground = (balance.IsDepleted ? Chrome.Danger : Chrome.PrimaryText).Brush(amount);
                amount.TextWrapping = TextWrapping.NoWrap;
                amount.MaxLines = 1;
                amount.TextTrimming = TextTrimming.CharacterEllipsis; // :116
                right.Children.Add(amount);
            }

            if (CreditsReader.TopUpURL(provider) is { } topUpURL)
            {
                var topUp = new Button { Content = "Top up" };
                topUp.Click += (_, _) => { if (!Marrow.Core.Port.ShellGuard.InTestHost) Process.Start(new ProcessStartInfo(topUpURL.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }; // R-TEST-10
                right.Children.Add(topUp.ChromeGlassCapsule());
            }

            var row = new DockPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
            };
            right.Margin = new Thickness(44, 0, 0, 0);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(left);
            Child = row;
        }
    }

    // :130-138 private func note(for credits: ProviderCredits?, isLoading: Bool) -> String?
    private string? Note(ProviderCredits? @for, bool isLoading)
    {
        if (@for is null) return isLoading ? null : provider.DisplayName + " did not report a balance.";
        if (@for.IsDepleted) return provider.DisplayName + " declines turns until the balance is topped up.";
        return @for.BreakdownText;
    }

    private static ProgressBar CreditsSpinner() => new()
    {
        IsIndeterminate = true,
        Width = 12, // .controlSize(.mini): the nth spinner (step 1 B) draws from 12 pt (was 10 pt, 2-chat-use.mov 145.0-145.6 s; PR-CHAT-099, OD-0136 amendment 2)
        Height = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>:141-192 <c>private struct LimitRow: View</c>.</summary>
internal sealed class LimitRow : Decorator
{
    /// <param name="window">:142 <c>let window: PlanLimits.Window</c>.</param>
    // :144-171 body
    public LimitRow(PlanLimits.Window window)
    {
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto,Auto"),
        };
        heading.ColumnDefinitions[2].MinWidth = 12; // :146-162 retain the spacer's second 12 gap even without a reset date.

        var title = new TextBlock
        {
            Text = window.Title,
            Foreground = Chrome.PrimaryText.Brush(this),
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis, // :150
            Margin = new Thickness(0, 0, 12, 0),
        };
        SwiftFont.Body.Apply(title);
        heading.Children.Add(title);

        if (window.ResetsAt is { } resetsAt)
        {
            var reset = new TextBlock
            {
                Text = "Resets " + ResetText(resetsAt),
                Foreground = Chrome.SecondaryText.Brush(this),
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis, // :156
                // :146 HStack(spacing: 12): the reset text is 12 from the spacer on its left (title, 12, Spacer(minLength: 8), 12,
                // reset) as well as 12 from the percentage, so the title keeps 32 points clear of it, not 20.
                Margin = new Thickness(12, 0, 12, 0),
            };
            SwiftFont.Callout.Apply(reset);
            Grid.SetColumn(reset, 2);
            heading.Children.Add(reset);
        }

        var percentage = new TextBlock
        {
            Text = SwiftNumber.TruncatingToInt(SwiftNumber.Rounded(window.Percent)).ToString(CultureInfo.InvariantCulture) + "%",
            Foreground = Chrome.SecondaryText.Brush(this),
        };
        SwiftFont.Callout.MonospacedDigit().Apply(percentage);
        Grid.SetColumn(percentage, 3);
        heading.Children.Add(percentage);

        var row = new StackPanel { Spacing = 8 };
        row.Children.Add(heading);
        if (window.Detail is { } detail)
        {
            var detailLabel = new TextBlock
            {
                Text = detail,
                Foreground = Chrome.SecondaryText.Brush(this),
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis, // :167
            };
            SwiftFont.Subheadline.Apply(detailLabel);
            row.Children.Add(detailLabel);
        }
        row.Children.Add(new UsageBar(window.Percent / 100, Tint(window.Percent), window.Title));
        Child = row;
    }

    // :173-177 private static func tint(for percent: Double) -> Color
    private static SwiftColor Tint(double @for)
    {
        if (@for >= 90) return Chrome.Danger;
        if (@for >= 75) return Chrome.Warning;
        return Chrome.Accent;
    }

    // :180-191 static func resetText(_ date: Date, now: Date = .now), with the default
    public static string ResetText(DateTimeOffset date) =>
        ResetText(date, SwiftDate.Now);

    // :180-191 static func resetText(_ date: Date, now: Date)
    public static string ResetText(DateTimeOffset date, DateTimeOffset now)
    {
        var seconds = SwiftDate.TimeIntervalSince(date, now);
        if (seconds < 0) seconds = 0;

        if (seconds < 86_400)
        {
            var wholeSeconds = SwiftNumber.TruncatingToInt(seconds);
            var hours = wholeSeconds / 3_600;
            var minutes = wholeSeconds % 3_600 / 60;
            return hours > 0
                ? "in " + hours.ToString(CultureInfo.InvariantCulture) + " hr " + minutes.ToString(CultureInfo.InvariantCulture) + " min"
                : "in " + Math.Max(1L, minutes).ToString(CultureInfo.InvariantCulture) + " min";
        }

        if (seconds < 6 * 86_400)
        {
            return SwiftFormatters.Formatted(
                date,
                DateFormatStyle.DateTime.Weekday().Hour().Minute());
        }

        return SwiftFormatters.Formatted(
            date,
            DateFormatStyle.DateTime.Month().Day());
    }
}

/// <summary>:195-198 <c>fileprivate struct BankedResetRowModel: Identifiable</c>: one banked reset.</summary>
internal sealed record BankedResetRowModel
{
    public required string Id { get; init; } // :196
    public PlanLimits.ResetCredit? Credit { get; init; } // :197
}

/// <summary>:204-308 <c>private struct BankedResetsView: View</c>: a provider's banked resets.</summary>
internal sealed class BankedResetsView : Decorator
{
    /// <summary>:209-215 <c>fileprivate enum Phase: Equatable</c> (R-ENUM-2).</summary>
    public abstract record Phase
    {
        private Phase() { }

        public sealed record Idle : Phase; // :210

        public sealed record Confirming(string Value) : Phase; // :211

        public sealed record Applying(string Value) : Phase; // :212

        public sealed record Done(PlanLimits.ResetOutcome Value) : Phase; // :213

        public sealed record Failed(string Value) : Phase; // :214
    }

    private readonly ProviderKind provider;
    private PlanLimits.ResetCredits credits;
    private Phase phase = new Phase.Idle();
    private TaskHandle? settle;
    // :232-235 the count keeps its identity across rebuilds, so its .contentTransition(.numericText()) can morph it.
    private MorphingTextBlock? countLabel;

    /// <param name="provider">:206 <c>let provider: ProviderKind</c>.</param>
    /// <param name="credits">:207 <c>let credits: PlanLimits.ResetCredits</c>.</param>
    // :226-257 body
    public BankedResetsView(ProviderKind provider, PlanLimits.ResetCredits credits)
    {
        this.provider = provider;
        this.credits = credits;
        Build();
        DetachedFromVisualTree += (_, _) => settle?.Cancel();
    }

    // :207,217-218,256: new input at the same view identity preserves both @State values.
    internal void Update(PlanLimits.ResetCredits value)
    {
        if (credits == value) return;
        credits = value;
        Build();
    }

    private void SetPhase(Phase next)
    {
        Transaction.WithAnimation(Chrome.PanelSlide, () =>
        {
            phase = next;
            Build();
        });
    }

    private void Build()
    {
        var content = new StackPanel { Spacing = 0 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var title = new TextBlock { Text = "Banked resets", Foreground = Chrome.PrimaryText.Brush(this) };
        SwiftFont.Body.Apply(title);
        var count = credits.AvailableCount switch
        {
            0 => "None available yet",
            1 => "1 available",
            _ => credits.AvailableCount.ToString(CultureInfo.InvariantCulture) + " available",
        };
        var countText = "\U000000B7 " + count; // U+00B7 MIDDLE DOT
        if (countLabel is null)
        {
            countLabel = new MorphingTextBlock { Text = countText, Foreground = Chrome.SecondaryText.Brush(this) };
            SwiftFont.Callout.Apply(countLabel);
        }
        else
        {
            (countLabel.Parent as Panel)?.Children.Remove(countLabel);
            // :235 .contentTransition(.numericText()): inside the panel slide of :255-256 the digits morph.
            countLabel.SetText(countText, Transaction.Current);
        }
        header.Children.Add(title);
        header.Children.Add(countLabel);
        AutomationProperties.SetName(header, "Banked resets, " + count);
        content.Children.Add(header);

        if (phase is Phase.Done done)
        {
            var isReset = done.Value == PlanLimits.ResetOutcome.Reset;
            var color = isReset ? Chrome.Success : Chrome.SecondaryText;
            var symbol = isReset ? "checkmark.circle.fill" : "info.circle";
            var glyph = SwiftImage.SystemName(symbol)
                .Font(SwiftFont.Subheadline.FontWeight(SwiftFont.Weight.Semibold))
                .ForegroundStyle(color)
                // :266 .symbolEffect(.bounce, value: phase) is recorded, never played: the glyph exists only in the .done branch,
                // which the phase change itself inserts (Applying -> Done), and a value-triggered symbol effect does not fire on
                // insertion; the phases never go from one .done to another, so the Swift glyph never bounces either.
                .SymbolEffect(SymbolEffectKind.Bounce, phase);
            var message = new TextBlock
            {
                Text = done.Value.Message,
                Foreground = color.Brush(this),
            };
            SwiftFont.Subheadline.Apply(message);
            var status = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 4, 0, 0),
                Children = { glyph, message },
            };
            content.Children.Add(status.Transition(SwiftTransition.BlurReplace));
        }
        else if (phase is Phase.Failed failed)
        {
            var status = new TextBlock
            {
                Text = failed.Value,
                Foreground = Chrome.Danger.Brush(this),
                Margin = new Thickness(0, 4, 0, 0),
            };
            SwiftFont.Subheadline.Apply(status);
            content.Children.Add(status.Transition(SwiftTransition.BlurReplace));
        }

        var listed = credits.Credits;
        var total = Math.Max(0L, credits.AvailableCount - listed.Count);
        var rowCount = listed.Count + total;
        for (var index = 0; index < rowCount; index++)
        {
            var credit = index < listed.Count ? listed[checked((int)index)] : null;
            var row = new BankedResetRowModel
            {
                Id = credit?.Id ?? "unlisted-" + (index - listed.Count).ToString(CultureInfo.InvariantCulture),
                Credit = credit,
            };
            var rowView = new BankedResetRow(
                row,
                index,
                rowCount > 1,
                phase,
                () => SetPhase(new Phase.Confirming(row.Id)),
                () => SetPhase(new Phase.Idle()),
                () => Spend(row));
            rowView.Margin = new Thickness(0, 12, 0, 0);
            // :251 .transition(.opacity.combined(with: .move(edge: .top))): a row fades while moving from or towards the top edge
            // (the blur-replace belongs to the controls inside the row, :352, :360, :364, and to the status line).
            content.Children.Add(rowView.Transition(SwiftTransition.Opacity.Combined(SwiftTransition.Move(Edge.Top))));
        }

        Child = content;
    }

    // :293-307 private func spend(_ row: BankedResetRowModel)
    private void Spend(BankedResetRowModel row)
    {
        SetPhase(new Phase.Applying(row.Id));
        settle?.Cancel();
        var model = AppEnvironment.GetAppModel(this);
        settle = MainActor.Spawn(async () =>
        {
            try
            {
                var outcome = await model.Providers.ConsumeResetCredit(provider, row.Credit?.Id);
                SetPhase(new Phase.Done(outcome));
            }
            catch (Exception error)
            {
                SetPhase(new Phase.Failed(SwiftErrors.LocalizedDescription(error)));
            }

            await SwiftTask.TrySleep(TimeSpan.FromSeconds(4));
            if (SwiftTask.IsCancelled) return;
            SetPhase(new Phase.Idle());
        });
    }
}

/// <summary>:313-371 <c>private struct BankedResetRow: View</c>: one banked reset and the button that spends it.</summary>
internal sealed class BankedResetRow : Decorator
{
    /// <param name="row">:314 <c>let row: BankedResetRowModel</c>.</param>
    /// <param name="index">:315 <c>let index: Int</c>.</param>
    /// <param name="several">:316 <c>let several: Bool</c>.</param>
    /// <param name="phase">:317 <c>let phase: BankedResetsView.Phase</c>.</param>
    /// <param name="onConfirm">:318 <c>let onConfirm: () -> Void</c>.</param>
    /// <param name="onCancel">:319 <c>let onCancel: () -> Void</c>.</param>
    /// <param name="onUse">:320 <c>let onUse: () -> Void</c>.</param>
    // :322-337 body
    public BankedResetRow(BankedResetRowModel row, long index, bool several, BankedResetsView.Phase phase, Action onConfirm, Action onCancel, Action onUse)
    {
        var label = row.Credit?.Title ?? (several ? "Reset " + (index + 1).ToString(CultureInfo.InvariantCulture) : "Reset");
        var title = new TextBlock
        {
            Text = label,
            Foreground = Chrome.PrimaryText.Brush(this),
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis, // :328
        };
        SwiftFont.Body.Apply(title);

        var detailText = row.Credit?.Detail ?? (row.Credit?.ExpiresAt is { } expiresAt
            ? expiresAt <= SwiftDate.Now ? "Expired" : "Expires " + LimitRow.ResetText(expiresAt)
            : "No expiry reported");
        var detail = new TextBlock
        {
            Text = detailText,
            Foreground = Chrome.SecondaryText.Brush(this),
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis, // :332
        };
        SwiftFont.Subheadline.Apply(detail);
        var labels = new StackPanel { Spacing = 2, Children = { title, detail } };

        Control control;
        if (phase is BankedResetsView.Phase.Confirming confirming && confirming.Value == row.Id)
        {
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += (_, _) => onCancel();
            var reset = new Button { Content = "Reset now" };
            reset.Click += (_, _) => onUse();
            buttons.Children.Add(cancel.ChromeGlassCapsule());
            buttons.Children.Add(ProminentGlassCapsule(reset)); // :350 .glassProminent ... .tint(Chrome.accent), not the plain glass of Cancel
            control = buttons.Transition(SwiftTransition.BlurReplace);
        }
        else if (phase is BankedResetsView.Phase.Applying applying && applying.Value == row.Id)
        {
            var progress = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            progress.Children.Add(new ProgressBar
            {
                IsIndeterminate = true,
                Width = 12, // .controlSize(.mini): the nth spinner (step 1 B) draws from 12 pt (was 10 pt, 2-chat-use.mov 145.0-145.6 s; PR-CHAT-099, OD-0136 amendment 2)
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var resetting = new TextBlock
            {
                Text = "Resetting\u2026", // U+2026 HORIZONTAL ELLIPSIS
                Foreground = Chrome.SecondaryText.Brush(this),
            };
            SwiftFont.Callout.Apply(resetting);
            progress.Children.Add(resetting);
            control = progress.Transition(SwiftTransition.BlurReplace);
        }
        else
        {
            var use = new Button
            {
                Content = "Use reset",
                IsEnabled = phase is not BankedResetsView.Phase.Applying,
            };
            use.Click += (_, _) => onConfirm();
            control = use.ChromeGlassCapsule().Transition(SwiftTransition.BlurReplace);
        }

        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        rowGrid.Children.Add(labels);
        var controlHost = new Border
        {
            Child = control,
            Margin = new Thickness(36, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(controlHost, 1);
        rowGrid.Children.Add(controlHost);
        Child = rowGrid;
    }

    // :350 Button("Reset now").buttonStyle(.glassProminent).tint(Chrome.accent): the capsule tinted with the accent instead of the
    // theme's glass wash, the recipe the port's other glassProminent capsules use (Tint(Chrome.accent, 0.3) over an accent
    // wash). Same shape as ChromeGlassCapsule: a capsule fill behind the button.
    private static Control ProminentGlassCapsule(Button button)
    {
        var fill = new Border { IsHitTestVisible = false }.Capsule(RoundedCornerStyle.Continuous);
        fill.Background = Chrome.Accent.Opacity(0.3).Brush(fill);
        SwiftGlass.SetEffect(fill, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 0.3).Interactive());
        return new Panel { Children = { fill, button } };
    }
}

/// <summary>:374-396 <c>struct UsageBar: View</c>: a thin capsule filled to a fraction.</summary>
public sealed class UsageBar : Decorator
{
    private readonly double fraction;
    private readonly SwiftColor tint;

    /// <param name="fraction">:375 <c>let fraction: Double</c>.</param>
    /// <param name="tint">:376 <c>let tint: Color</c>.</param>
    /// <param name="label">:379 <c>var label = "Used"</c>.</param>
    // :381-395 body
    public UsageBar(double fraction, SwiftColor tint, string label = "Used")
    {
        this.fraction = fraction;
        this.tint = tint;
        var track = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Background = Chrome.Overlay(0.1).Brush(this),
        };
        var fill = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = tint.Brush(this),
        };
        var bar = new Grid
        {
            Height = 5,
            Children = { track, fill },
        };
        bar.SizeChanged += (_, args) =>
        {
            var clamped = Math.Min(Math.Max(fraction, 0), 1);
            fill.Width = fraction > 0 ? Math.Max(5, args.NewSize.Width * clamped) : 0;
        };
        AutomationProperties.SetName(this, label);
        Child = bar;
    }

    // :392-394 accessibilityElement / accessibilityLabel / accessibilityValue.
    // UIA Value carries the spoken percentage; the track and fill are not separate elements.
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(UsageBar owner) : ControlAutomationPeer(owner), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => [];

        public string Value => SwiftNumber.TruncatingToInt(
            SwiftNumber.Rounded(Math.Min(Math.Max(((UsageBar)Owner).fraction, 0), 1) * 100))
            .ToString(CultureInfo.InvariantCulture) + "% used";
        public bool IsReadOnly => true;
        public void SetValue(string? value) => throw new InvalidOperationException("The usage value is read-only.");
    }
}
