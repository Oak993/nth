// PORT-ONLY: web surfaces W19, the rows of MCP Apps in the thread (DESIGN.md 7.6 "mounts an inline card in the tool row";
// "Each message then appears as its own row 'Message from shipyard' with the server's icon, not as a user bubble";
// ED-NEW-web-w19-3). No Swift counterpart.
//   - McpAppRow: the call's tool row as every tool call draws it, then the app's card below it. Each pass of the timeline
//     hands it the call again; the app hears what changed (arguments streaming, the input, the result, a cancel).
//   - AppMessageRow: what an app posted, on the left as the app's (its cube, "Message from shipyard"), never in the user's
//     bubble on the right.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

public sealed class McpAppRow : Decorator
{
    private readonly WorkGroup tool;
    private TimelineEntry entry;

    public McpAppRow(TimelineEntry entry, ThreadRuntime runtime, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(runtime);
        this.entry = entry;
        tool = new WorkGroup(new ValueList<TimelineEntry>([entry]), runtime, workingDirectory);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { tool } };
        if (entry.Item.TimelineItemContent is TimelineItem.Content.Tool call && McpAppCatalog.For(call.Value) is { } app
            && McpAppSessions.For(runtime, entry.Id, app) is { } session)
        {
            Session = session;
            Card = new InlineWebWidget(session);
            column.Children.Add(Card);
            session.Feed(call.Value);
            // The entry is the thread's live record: its call changes in place as arguments stream and the result lands.
            this.Track(() => this.entry.Item, item =>
            {
                if (item.TimelineItemContent is TimelineItem.Content.Tool now) Session?.Feed(now.Value);
            });
        }

        Child = column;
    }

    /// <summary>The app's session, or null when the MCP Apps host is not bound (then the row is the plain tool row).</summary>
    public McpAppSession? Session { get; }

    /// <summary>The app's card.</summary>
    public InlineWebWidget? Card { get; }

    public TimelineEntry Entry => entry;

    /// <summary>The same call, as it stands now.</summary>
    public void Update(TimelineEntry next)
    {
        ArgumentNullException.ThrowIfNull(next);
        entry = next;
        tool.Update(new ValueList<TimelineEntry>([next]), false);
        if (next.Item.TimelineItemContent is TimelineItem.Content.Tool call) Session?.Feed(call.Value);
    }
}

public sealed class AppMessageRow : Decorator
{
    public AppMessageRow(UserMessage message, string server)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(server);
        Message = message;
        Server = server;
        var glyph = new PaneGlyph(PaneGlyphKind.App, 13) { VerticalAlignment = VerticalAlignment.Center };
        glyph.Foreground = PaneTokens.Text2.Brush(glyph);
        var heading = new TextBlock
        {
            Text = McpAppUntrusted.MessageHeading(server),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        heading.Foreground = PaneTokens.Text2.Brush(heading);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { glyph, heading } };
        var text = TimelineRows.ChatText(message.Text, color: Chrome.PrimaryText.Opacity(0.9));
        var bubble = new Border
        {
            Padding = new Thickness(12, 9, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Children = { top, text } },
        };
        bubble.Background = InlineWebWidget.PlaceholderFill.Brush(bubble);
        bubble.BorderBrush = InlineWebWidget.CardRim.Brush(bubble);
        Child = bubble;
        Padding = new Thickness(0, 0, 96, 0);
        AutomationProperties.SetName(this, McpAppUntrusted.MessageHeading(server));
        AutomationProperties.SetHelpText(this, message.Text);
    }

    public UserMessage Message { get; }

    public string Server { get; }
}
