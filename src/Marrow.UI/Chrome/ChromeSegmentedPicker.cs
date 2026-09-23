using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Chrome/ChromeSegmentedPicker.swift (B2 1fbf81e), :1-82.

namespace Marrow.UI.Chrome
{
    /// <summary>
    /// :8-13 One option of the strip: its title, read out and shown unless <c>Symbol</c> draws in its place.
    /// </summary>
    public sealed record ChromeSegmentedOption<TValue>
    {
        /// <summary>
        /// The value this option represents.
        /// </summary>
        public TValue Value { get; }

        /// <summary>
        /// The text shown to the user and read aloud; the accessibility label even when a symbol is drawn.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// :11-12 An SF Symbol shown instead of the title; the title stays the accessibility label.
        /// </summary>
        public string? Symbol { get; }

        /// <summary>
        /// :20 Explicit constructor taking a value, title, and optional symbol.
        /// </summary>
        public ChromeSegmentedOption(TValue value, string title, string? symbol = null)
        {
            Value = value;
            Title = title;
            Symbol = symbol;
        }
    }

    /// <summary>
    /// :16-44 The row control: one small button per option, the chosen one filled. A code-first Avalonia control
    /// (ED-095, R-VIEW-1): the body is built once in the constructor.
    /// </summary>
    public sealed class ChromeSegmentedPicker<TValue> : Decorator
    {
        /// <summary>
        /// Constructor taking explicit options and a binding.
        /// :20-23 Constructor init(options, selection)
        /// </summary>
        public ChromeSegmentedPicker(ChromeSegmentedOption<TValue>[] options, Binding<TValue> selection)
        {
            // Build the UI once (R-VIEW-1, ED-095).
            Child = BuildBody(options, selection);

            // Prevent stretching to fill parent space.
            this.HorizontalAlignment = HorizontalAlignment.Left;
            this.VerticalAlignment = VerticalAlignment.Top;
        }

        /// <summary>
        /// :25-27 Initializer taking (value, title) pairs and mapping them to options with no symbol.
        /// </summary>
        public ChromeSegmentedPicker((TValue value, string title)[] options, Binding<TValue> selection)
            : this(
                options: options.Select(opt => new ChromeSegmentedOption<TValue>(opt.value, opt.title)).ToArray(),
                selection: selection)
        {
        }

        /// <summary>
        /// :29-43 Builds the body: HStack(spacing: 2) containing one segment per option, padded and wrapped.
        /// </summary>
        private static Control BuildBody(ChromeSegmentedOption<TValue>[] options, Binding<TValue> selection)
        {
            // :30 HStack(spacing: 2) - segments will be arranged with 2pt spacing.
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
            };

            // :31-34 ForEach over options, each rendering a ChromeSegment.
            foreach (var option in options)
            {
                var segment = new ChromeSegment<TValue>(option, selection);
                stack.Children.Add(segment);
            }

            // :37 .padding(.horizontal, 3)
            var padded = new Border
            {
                Padding = new Thickness(3, 0),
                Child = stack,
            };

            // :38 .frame(height: Chrome.capsuleContentHeight)
            padded.Height = Chrome.CapsuleContentHeight;
            padded.VerticalAlignment = VerticalAlignment.Center;

            // :39 .chromeGlassCapsule()
            var glassed = padded.ChromeGlassCapsule();

            // :40 .fixedSize() - make sure it sizes to content
            glassed.HorizontalAlignment = HorizontalAlignment.Left;
            glassed.VerticalAlignment = VerticalAlignment.Top;

            // :42 .background { NoWindowDragArea() } - create a container with the drag area behind
            var dragArea = new NoWindowDragArea();

            // Create a panel that holds both the drag area and the glassed content
            // The drag area spans the full size of its container, the content is above it
            var container = new Panel();
            container.Children.Add(dragArea);
            container.Children.Add(glassed);

            // :41 .accessibilityElement(children: .contain) - the whole strip is one accessible container.
            AutomationProperties.SetAccessibilityView(container, AccessibilityView.Control);

            return container;
        }
    }

    /// <summary>
    /// A Button that exposes its selection state through UI Automation (ISelectionItemProvider).
    /// </summary>
    internal sealed class SelectableSegmentButton : Button
    {
        // The state object that holds whether this button is selected.
        private readonly SegmentSelectionState _state;

        public SelectableSegmentButton(SegmentSelectionState state)
        {
            _state = state;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SelectableSegmentPeer(this, _state);

        /// <summary>
        /// Automation peer for the selectable segment button.
        /// </summary>
        private sealed class SelectableSegmentPeer : ControlAutomationPeer, ISelectionItemProvider, IInvokeProvider
        {
            private readonly SegmentSelectionState _state;

            public SelectableSegmentPeer(Control owner, SegmentSelectionState state) : base(owner)
            {
                _state = state;
            }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

            bool ISelectionItemProvider.IsSelected => _state.IsSelected;
            ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
            void ISelectionItemProvider.AddToSelection() { }
            void ISelectionItemProvider.RemoveFromSelection() { }
            void ISelectionItemProvider.Select() { }
            void IInvokeProvider.Invoke() => ((Button)Owner).Focus();
        }
    }

    /// <summary>
    /// Mutable state holder for segment selection. This allows the automation peer to read the current state.
    /// </summary>
    internal sealed class SegmentSelectionState
    {
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// :47-82 One segment: a small capsule button, filled while chosen.
    /// A file-local control class that builds its visual tree in the constructor (ED-095, R-VIEW-1).
    /// Inherits from Decorator to manage a single child (the button).
    /// </summary>
    internal sealed class ChromeSegment<TValue> : Decorator
    {
        /// <summary>
        /// Observable hover state model to enable animation tracking (R-OBS-1, ED-070).
        /// Stores a simple boolean to indicate hover state, which animates when changed within Transaction.WithAnimation.
        /// </summary>
        private sealed class HoverState : MainActorModel
        {
            private bool isHovering;

            /// <summary>
            /// Whether the segment is currently being hovered.
            /// This boolean is animated when changed within a Transaction.WithAnimation block (0.1 seconds ease-out).
            /// </summary>
            public bool IsHovering
            {
                get => Read(isHovering);
                set => Set(ref isHovering, value);
            }
        }
        // :48-51 The segment's state and data.
        private readonly ChromeSegmentedOption<TValue> _option;
        private readonly Binding<TValue> _selection;
        private bool _isSelected;

        // :52 @State private var isHovering = false
        // Use an observable model with animated opacity to enable smooth hover transitions
        private readonly HoverState _hoverState = new();

        // The selectable button that handles clicks.
        private SelectableSegmentButton? _button;

        // The state holder that the automation peer will read.
        private readonly SegmentSelectionState _selectionState = new();

        // The border that holds the background color (for styling).
        private Border? _capsuleBorder;

        // The text or image label.
        private Control? _labelContent;

        /// <summary>
        /// :54-74 Constructor: stores the option and selection binding, builds the visual tree.
        /// </summary>
        public ChromeSegment(ChromeSegmentedOption<TValue> option, Binding<TValue> selection)
        {
            _option = option;
            _selection = selection;
            _isSelected = EqualityComparer<TValue>.Default.Equals(option.Value, selection.Value);
            _selectionState.IsSelected = _isSelected;

            // Build the visual tree once (ED-095, R-VIEW-1).
            Child = BuildSegmentBody();

            // Prevent stretching to fill parent space - segments should size to content.
            this.HorizontalAlignment = HorizontalAlignment.Left;
            this.VerticalAlignment = VerticalAlignment.Center;

            // Track changes to the selection binding and update visuals accordingly (R-VIEW-5).
            this.Track(
                () => EqualityComparer<TValue>.Default.Equals(_option.Value, _selection.Value),
                isNowSelected => UpdateSelected(isNowSelected));

            // Track hover state changes and update background with animation (R-VIEW-5).
            // The IsHovering boolean is animated when changed within Transaction.WithAnimation blocks.
            this.Track(
                () => _hoverState.IsHovering,
                _ => UpdateBackgroundColor());
        }

        /// <summary>
        /// Builds the segment's visual tree.
        /// :55-71 Button with label, styling, and event handlers.
        /// </summary>
        private Control BuildSegmentBody()
        {
            // :55 Button(action: select) with label
            // Use SelectableSegmentButton to expose selection state through automation
            _button = new SelectableSegmentButton(_selectionState)
            {
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            // :56 Build label content first
            _labelContent = BuildLabel();

            // :56-62 Create a container for the label with padding and frame
            var labelWithPadding = new Border
            {
                Padding = new Thickness(9, 0), // :60 .padding(.horizontal, 9)
                MinWidth = 26, // :61 .frame(minWidth: 26)
                Height = 22, // :62 .frame(height: 22)
                VerticalAlignment = VerticalAlignment.Center,
                Child = _labelContent,
            };

            // Apply font styling to the label
            ApplyLabelStyling(_labelContent);

            // :63 .background(Capsule(...).fill(...))
            // Create the capsule background border
            _capsuleBorder = new Border
            {
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(11), // :85 Capsule(style: .continuous) - half the height (22 / 2 = 11)
                Child = labelWithPadding,
            };

            // Update background color for current state
            UpdateBackgroundColor();

            // :64 .contentShape(Capsule(...)) - the capsule is the hit shape
            // :66 .buttonStyle(.plain)
            _button.Content = _capsuleBorder;
            _button.Click += (_, _) =>
            {
                // :33 withAnimation(Chrome.hover) { selection = option.value }
                Transaction.WithAnimation(Chrome.Hover, () =>
                {
                    _selection.Value = _option.Value;
                });
            };

            // :67-69 .onHover { hovering in ... }
            // Track hover state changes and animate the background color (R-VIEW-4, R-VIEW-5).
            // Use the OnHover extension method which provides proper hover event handling.
            // The IsHovering boolean will be animated over 0.1 seconds when changed.
            _button.OnHover(hovering =>
            {
                Transaction.WithAnimation(Chrome.Hover, () =>
                {
                    _hoverState.IsHovering = hovering;
                });
            });

            // :71 .accessibilityLabel(Text(verbatim: title))
            AutomationProperties.SetName(_button, _option.Title);

            return _button;
        }

        /// <summary>
        /// :74-81 Build the label: Image(systemName: symbol) if a symbol, else Text(verbatim: title).
        /// </summary>
        private Control BuildLabel()
        {
            if (_option.Symbol is not null)
            {
                // :77 Image(systemName: symbol)
                return SwiftImage.SystemName(_option.Symbol);
            }
            else
            {
                // :79 Text(verbatim: title)
                var text = new TextBlock
                {
                    Text = _option.Title,
                    MaxLines = 1,
                };
                return text;
            }
        }

        /// <summary>
        /// Apply font, color, and line limit styling to the label.
        /// </summary>
        private void ApplyLabelStyling(Control labelControl)
        {
            // :57 .font(.system(.callout, weight: isSelected ? .semibold : .medium))
            var font = SwiftFont.System(SwiftFont.TextStyle.Callout,
                weight: _isSelected ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Medium);

            // Apply font to the actual control (might be TextBlock or SwiftImage)
            if (labelControl is TextBlock tb)
            {
                font.Apply(tb);
                // :58 .foregroundStyle(isSelected ? Chrome.primaryText : Chrome.secondaryText)
                var foreground = (_isSelected ? Chrome.PrimaryText : Chrome.SecondaryText);
                tb.Foreground = foreground.Brush(tb);
                // :59 .lineLimit(1)
                tb.MaxLines = 1;
            }
            else if (labelControl is SwiftImage img)
            {
                // For images, apply foreground color as well
                var foreground = (_isSelected ? Chrome.PrimaryText : Chrome.SecondaryText);
                img.ForegroundStyle(foreground);
            }
        }

        /// <summary>
        /// Update the background color based on selection and hover state.
        /// :63 .background(Capsule(...).fill(isSelected ? Chrome.accent.opacity(0.22) : Chrome.overlay(isHovering ? 0.08 : 0)))
        /// When the IsHovering boolean is animated through a Transaction.WithAnimation block, the background color
        /// smoothly transitions over 0.1 seconds from no-fill to 8% opacity (or vice versa).
        /// </summary>
        private void UpdateBackgroundColor()
        {
            if (_capsuleBorder is null)
                return;

            SwiftColor bgColor;
            if (_isSelected)
            {
                // :63 When selected, show accent at 22% opacity, regardless of hover state
                bgColor = Chrome.Accent.Opacity(0.22);
            }
            else
            {
                // :63 When not selected, show label color at opacity based on hover state
                // The IsHovering boolean is animated, so this update will be animated
                bgColor = Chrome.Overlay(_hoverState.IsHovering ? 0.08 : 0);
            }
            _capsuleBorder.Background = bgColor.Brush(_capsuleBorder);
        }

        /// <summary>
        /// Called when the selection binding changes.
        /// </summary>
        private void UpdateSelected(bool isNowSelected)
        {
            _isSelected = isNowSelected;
            _selectionState.IsSelected = isNowSelected;

            if (_labelContent is not null)
            {
                ApplyLabelStyling(_labelContent);
            }
            UpdateBackgroundColor();

            // :70 .accessibilityAddTraits(isSelected ? .isSelected : [])
            // The automation peer reads _selectionState.IsSelected which we just updated
        }
    }
}
