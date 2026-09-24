using Avalonia.Media;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/ChatFont.swift (B2 1fbf81e), :3-10.
// The point sizes are the ED-059 table from B1:DroppyCode/UI/Common/ChatZoom.swift:44-57.
// Their visual parity remains UNVERIFIED under OD-0001.
public sealed class ChatFontTests
{
    [Theory]
    [InlineData(SwiftFont.TextStyle.LargeTitle, 26)]
    [InlineData(SwiftFont.TextStyle.Title, 22)]
    [InlineData(SwiftFont.TextStyle.Title2, 17)]
    [InlineData(SwiftFont.TextStyle.Title3, 15)]
    [InlineData(SwiftFont.TextStyle.Headline, 13)]
    [InlineData(SwiftFont.TextStyle.Subheadline, 11)]
    [InlineData(SwiftFont.TextStyle.Body, 13)]
    [InlineData(SwiftFont.TextStyle.Callout, 12)]
    [InlineData(SwiftFont.TextStyle.Footnote, 10)]
    [InlineData(SwiftFont.TextStyle.Caption, 10)]
    [InlineData(SwiftFont.TextStyle.Caption2, 10)]
    [Trait("Req", "PR-CHAT-193")] // UI/Common/ChatFont.swift:6-9; ED-059 sizes
    [Trait("Req", "PR-THEME-179")] // UI/Common/ChatFont.swift:4-9
    [Trait("Req", "UI-CHAT-082")] // UI/Common/ChatFont.swift:3-10
    [Trait("Decision", "ED-186")] // only ChatFont.Chat resolves .headline to semibold, ChatFont.swift:7
    public void Each_chat_style_keeps_its_plain_text_style_size_and_default_weight(SwiftFont.TextStyle style, int size)
    {
        var actual = ChatFont.Chat(style).Resolve();

        Assert.Equal(size, actual.FontSize);
        Assert.Equal(style == SwiftFont.TextStyle.Headline ? 600 : 400, (int)actual.FontWeight);
        Assert.Equal("Inter", actual.FontFamily.Name);
        Assert.Equal(new Uri("fonts:Inter"), actual.FontFamily.Key?.Source);
    }

    [Theory]
    [InlineData(SwiftFont.Weight.UltraLight, 100)]
    [InlineData(SwiftFont.Weight.Thin, 200)]
    [InlineData(SwiftFont.Weight.Light, 300)]
    [InlineData(SwiftFont.Weight.Regular, 400)]
    [InlineData(SwiftFont.Weight.Medium, 500)]
    [InlineData(SwiftFont.Weight.Semibold, 600)]
    [InlineData(SwiftFont.Weight.Bold, 700)]
    [InlineData(SwiftFont.Weight.Heavy, 800)]
    [InlineData(SwiftFont.Weight.Black, 900)]
    [Trait("Req", "PR-CHAT-193")] // UI/Common/ChatFont.swift:6-9
    [Trait("Req", "UI-CHAT-082")] // UI/Common/ChatFont.swift:6-9
    [Trait("Decision", "ED-186")] // an explicit weight wins over the chat builder's semibold default, ChatFont.swift:7
    public void Explicit_weight_wins_over_the_headline_default(SwiftFont.Weight weight, int expected)
    {
        var actual = ChatFont.Chat(SwiftFont.TextStyle.Headline, weight: weight).Resolve();

        Assert.Equal(13, actual.FontSize);
        Assert.Equal(expected, (int)actual.FontWeight);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-193")] // UI/Common/ChatFont.swift:6-9
    [Trait("Req", "UI-CHAT-082")] // UI/Common/ChatFont.swift:6-9
    public void Explicit_weight_applies_to_a_non_headline_style()
    {
        var actual = ChatFont.Chat(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Resolve();

        Assert.Equal(12, actual.FontSize);
        Assert.Equal(500, (int)actual.FontWeight);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-193")] // UI/Common/ChatFont.swift:6-9; App/Views/Chat/TimelineRows.swift:2151
    [Trait("Req", "PR-THEME-179")] // UI/Common/ChatFont.swift:6-9; UI/Markdown/MarkdownView.swift:1301
    [Trait("Req", "UI-CHAT-082")] // UI/Common/ChatFont.swift:6-9
    public void Monospaced_design_uses_the_same_style_size_and_the_Cascadia_Mono_face()
    {
        var actual = ChatFont.Chat(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced).Resolve();

        Assert.Equal(12, actual.FontSize);
        Assert.Equal(400, (int)actual.FontWeight);
        Assert.Equal("Cascadia Mono", actual.FontFamily.Name);
        Assert.Equal(new Uri("fonts:CascadiaMono"), actual.FontFamily.Key?.Source);
        Assert.Equal(FontStyle.Normal, actual.FontStyle);
    }

    [Fact]
    [Trait("Req", "PR-THEME-179")] // UI/Common/ChatFont.swift:4-9; UI/Common/AppZoom.swift:18-26
    [Trait("Req", "UI-CHAT-082")] // UI/Common/ChatFont.swift:4-9
    public void Chat_font_has_no_zoom_argument_or_per_conversation_size_state()
    {
        var methods = typeof(ChatFont).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var chat = Assert.Single(methods);
        Assert.Equal("Chat", chat.Name);
        Assert.Equal(new[] { "style", "weight", "design" }, chat.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(17, ChatFont.Chat(SwiftFont.TextStyle.Title2).Resolve().FontSize);
    }
}
