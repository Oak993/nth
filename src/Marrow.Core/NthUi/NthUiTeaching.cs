using System.Text;

// PORT-ONLY: web surfaces W17, Teaching (DESIGN.md 7.1 "The rule taught to models", 7.3 "Teaching: a compact reference and
// six worked examples in a system prompt block (API providers ...) and in initialize.instructions of nth-ui (CLI
// providers), plus a skill with the full reference. Our own words and examples; nothing copied from any other product";
// ED-NEW-web-w17-1). No Swift counterpart. This file writes the words. One composition, three outputs:
//   SystemBlock    the block an API provider's system prompt carries (rules, how to show, reference, mistakes, examples);
//   McpInstructions  the nth-ui server's initialize.instructions: a self-sufficient core under 2,000 bytes first (some
//                  hosts cut a server's instructions at 2 KB), then the same reference and examples for hosts that keep it;
//   Reference      the on-demand skill (ui_reference): docs/nth-ui.md by section, and all the worked examples.
// What the text says follows what the session offers (NthUiTeachingOptions): a view is shown with ui_show when that tool
// exists and in a fenced nth-ui block otherwise; artifacts the same way; the html widget and ask_user only when offered;
// a small local model gets the lite subset and three examples. Everything is ASCII so a byte limit is a character limit.

namespace Marrow.Core.NthUi;

/// <summary>Settings > Tabula > "Visual answers" (DESIGN.md 7.7): how readily the model reaches for a live view.</summary>
public enum VisualAnswerLevel
{
    /// <summary>A live view only when the reader asks for one.</summary>
    Fewer,

    /// <summary>When it is clearly clearer than text (the default).</summary>
    Balanced,

    /// <summary>Whenever numbers, options, comparisons, plans or steps are involved.</summary>
    More,
}

/// <summary>What a session offers, which decides what the teaching says.</summary>
public sealed record NthUiTeachingOptions
{
    /// <summary>Full for every capable model; Lite for small local models (DESIGN 7.3).</summary>
    public NthUiProfile Profile { get; init; } = NthUiProfile.Full;

    /// <summary>ui_show and ui_update are offered as tools; otherwise views go in fenced nth-ui blocks.</summary>
    public bool LiveTools { get; init; }

    /// <summary>The artifact tools are offered; otherwise artifacts go in fenced nth-artifact blocks.</summary>
    public bool ArtifactTools { get; init; } = true;

    /// <summary>html_widget is offered (or, without tools, the nth-html fence).</summary>
    public bool HtmlWidget { get; init; }

    /// <summary>ask_user is offered.</summary>
    public bool AskUser { get; init; }

    /// <summary>ui_reference is offered, so the text may point to it.</summary>
    public bool Reference { get; init; }

    public VisualAnswerLevel Visual { get; init; } = VisualAnswerLevel.Balanced;

    /// <summary>
    /// Settings > Tabula > "Live answers in chat" (W10). False: nth draws no live view in the chat, so the teaching says to answer in text or an
    /// artifact and never writes a live view, a fenced nth-ui or nth-html block, or a call of ui_show, ui_update or html_widget.
    /// </summary>
    public bool LiveAnswers { get; init; } = true;

    public bool IsLite => Profile == NthUiProfile.Lite;
}

public static class NthUiTeaching
{
    /// <summary>The limit the MCP instructions' core stays under (bytes and characters alike: it is ASCII).</summary>
    public const int McpCoreLimit = 2_000;

    /// <summary>The sentence the teaching carries while Settings > Tabula > "Live answers in chat" is off (W10).</summary>
    public const string LiveOff = "Live views in the chat are turned off. Do not call ui_show, ui_update or html_widget, and do not write nth-ui or nth-html blocks in your answer. Use text, or an artifact when the reader will keep it.";

    public const string ShowTool = "ui_show";
    public const string UpdateTool = "ui_update";
    public const string HtmlTool = "html_widget";
    public const string AskTool = "ask_user";
    public const string ReferenceTool = "ui_reference";

    /// <summary>The topics ui_reference answers.</summary>
    public static IReadOnlyList<string> Topics { get; } = ["language", "components", "actions", "examples", "lite", "errors"];

    /// <summary>The one-line description of each topic, for the tool's description.</summary>
    public static string TopicSummary(string topic) => topic switch
    {
        "language" => "lines, values, expressions and their functions, fmt patterns, state and let, and how views stream",
        "components" => "every component with its values, options and flags",
        "actions" => "set, toggle, copy, open, export, send, context and call",
        "examples" => "twelve worked views with the sentence written before each",
        "lite" => "the small set for local models, with three examples",
        "errors" => "what happens to a line that is wrong, and the limits",
        _ => "",
    };

    /// <summary>
    /// The description for the ui_show tool (W24), so every provider sees the essentials with the tool itself, whether or not
    /// it shows the server's instructions.
    /// </summary>
    public const string ShowToolDescription =
        "Show a live view in the chat: a card with controls, a table that recomputes, a chart, a checklist, a form, a diagram or a map. "
        + "Pass the view as lines in ui: one component per line, children indented two spaces, \"double quoted\" text, name=value attributes, "
        + "and = expression for computed values. Use it when the reader will change numbers, compare, tick or step through something; "
        + "keep plain answers as text. One view per answer, after a sentence that answers the question. Call ui_reference for the language.";

    // ------------------------------------------------------------------------------------------------------------------
    // The three outputs.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The block for an API provider's system prompt.</summary>
    public static string SystemBlock(NthUiTeachingOptions options)
    {
        var text = new StringBuilder();
        text.Append("LIVE ANSWERS AND ARTIFACTS (nth)\n\n").Append(Rules(options));
        text.Append("\n\n").Append(Showing(options));
        text.Append("\n\n").Append(ReferenceSection(options));
        text.Append("\n\n").Append(Mistakes(options));
        text.Append("\n\n").Append(ExamplesSection(options));
        return text.ToString();
    }

    /// <summary>
    /// The nth-ui server's initialize.instructions: the core (under <see cref="McpCoreLimit"/>) and, after a blank line, the
    /// reference, the mistakes and the examples.
    /// </summary>
    public static string McpInstructions(NthUiTeachingOptions options)
    {
        var text = new StringBuilder(Core(options));
        text.Append("\n\n").Append(ReferenceSection(options));
        text.Append("\n\n").Append(Mistakes(options));
        text.Append("\n\n").Append(ExamplesSection(options));
        return text.ToString();
    }

    /// <summary>
    /// The self-sufficient start of the MCP instructions: when to use each shape, how to show a view, the heart of the
    /// language, and where the rest is. Always under <see cref="McpCoreLimit"/> characters.
    /// </summary>
    public static string Core(NthUiTeachingOptions options)
    {
        var text = new StringBuilder();
        if (!options.LiveAnswers)
        {
            text.Append("nth shows answers two ways: plain text, or an artifact in the Tabula pane beside it.\n");
            text.Append("- Text is the default: facts, explanations, opinions, short code.\n");
            text.Append(options.ArtifactTools
                ? "- An artifact (artifact_create; artifact_update for changes, edits for small ones) when the reader will keep, run or reuse it: a page or app, component, diagram, document, program or files.\n"
                : "- An artifact, in a fenced block whose first line is ```nth-artifact id=name kind=html title=\"Title\", when the reader will keep, run or reuse it.\n");
            if (options.AskUser) text.Append("- ask_user when you cannot go on without the reader's choice.\n");
            text.Append(LiveOff).Append('\n');
            text.Append("An artifact of kind nth-ui is written in the view language: one component per line, children indented two spaces; \"double quotes\"; name=value attributes; state n = 3 | let x = n * 2 | card \"Title\" | stat \"Label\" = x | table | chart.\n");
            text.Append(options.Reference ? "Call ui_reference for the view language." : "The reference follows.");
            return text.ToString();
        }
        text.Append("nth shows answers three ways: plain text, a live view in the chat, or an artifact in the Tabula pane beside it.\n");
        text.Append("- Text is the default: facts, explanations, opinions, short code.\n");
        text.Append("- A live view when the reader changes numbers or options, compares items, sees a chart or map, ticks things off or steps through a plan. ")
            .Append(options.Visual switch
            {
                VisualAnswerLevel.Fewer => "The reader prefers fewer: only when asked for one. ",
                VisualAnswerLevel.More => "The reader likes them: lean toward one whenever numbers, options or plans are involved. ",
                _ => "",
            })
            .Append("One sentence first, at most one view, nothing repeating it after.\n");
        if (options.ArtifactTools)
            text.Append("- An artifact (artifact_create; artifact_update for changes, edits for small ones) when the reader will keep, run or reuse it: a page or app, component, diagram, document, program or files.\n");
        else
            text.Append("- An artifact, in a fenced block whose first line is ```nth-artifact id=name kind=html title=\"Title\", when the reader will keep, run or reuse it.\n");
        if (options.HtmlWidget)
            text.Append(options.LiveTools
                ? "- html_widget only when no component fits (a game, a simulation).\n"
                : "- A fenced block that starts with ```nth-html, holding one HTML page, only when no component fits (a game, a simulation).\n");
        if (options.AskUser) text.Append("- ask_user when you cannot go on without the reader's choice.\n");
        text.Append(options.LiveTools
            ? "Show a view with ui_show (its ui argument holds the view); if you cannot call tools, write it in a fenced block that starts with ```nth-ui.\n"
            : "Show a view in a fenced block that starts with ```nth-ui and ends with ```; the block comes last.\n");
        text.Append("View language: one component per line, children indented two spaces; \"double quotes\"; name=value attributes; bare flags; = expression for computed values. No JSON, HTML or comments.\n");
        text.Append("state n = 3 (reader can change) | let x = n * 2 (computed) | stepper n label=\"N\" min=1 max=9 | slider n label=\"N\" min=0 max=100 step=5 unit=\"%\" | segmented n options=\"a:A,b:B\" | toggle t label=\"T\" | ")
            .Append("card \"Title\" | text \"...\" | stat \"Label\" = x | table cols=\"A,B\" > row \"x\" = fmt(x, \"$0\") (flag total) | chart bar|line|area|pie|scatter x=\"a,b\" y=[1,2] | ")
            .Append("chips \"T\" bind=v > chip \"x\" | checklist \"T\" > item \"x\" | callout \"...\" tone=info | followups > chip \"next question\"\n");
        text.Append(options.Reference
            ? "Call ui_reference for every component, the expression functions, actions and more examples."
            : "The reference and examples follow.");
        return text.ToString(); // the tests hold every combination of options under McpCoreLimit
    }

    /// <summary>The on-demand reference (ui_reference). Null for a topic that does not exist.</summary>
    public static string? Reference(string? topic)
    {
        switch ((topic ?? "").Trim().ToLowerInvariant())
        {
            case "language": return Sections(1, 2, 3, 4, 7);
            case "components": return Sections(5);
            case "actions": return Sections(6);
            case "errors": return Sections(9, 10);
            case "lite": return Sections(8) + "\n\n" + LiteExamplesText();
            case "examples": return ExamplesReference();
            default: return null;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The sections.
    // ------------------------------------------------------------------------------------------------------------------

    private static string Rules(NthUiTeachingOptions options)
    {
        var text = new StringBuilder();
        if (!options.LiveAnswers)
        {
            text.Append("You can answer with plain text or an artifact in the Tabula pane beside the chat. Choose by what the reader will do with the answer.\n");
            text.Append("- Plain text is the default: facts, explanations, opinions, short code, anything the reader just reads.\n");
            text.Append(options.ArtifactTools
                ? "- An artifact (artifact_create) when the reader will keep, run, edit or reuse what you make: a page or small app, a component, a diagram, a document, a program, several files. Short answers and small snippets stay in the chat.\n"
                : "- An artifact, in a fenced nth-artifact block, when the reader will keep, run, edit or reuse what you make: a page or small app, a component, a diagram, a document, a program, several files. Short answers and small snippets stay in the chat.\n");
            if (options.AskUser)
                text.Append("- When you cannot go on without a choice from the reader, call ask_user (single choice, several choices, text or number) instead of asking in prose.\n");
            text.Append(LiveOff);
            return text.ToString();
        }
        if (options.IsLite)
        {
            text.Append("You can answer with plain text or, when it helps, one small live view the reader can use.\n");
            text.Append("- Plain text is the default: facts, explanations, opinions, short code. Never add a view to look clever.\n");
            text.Append("- A live view when the reader changes numbers or options, compares things, or ticks things off. Write the sentence that answers first, then the view, at most one, and do not repeat its contents in the text.\n");
            text.Append(options.ArtifactTools
                ? "- An artifact (artifact_create) when the reader will keep, run or reuse what you make: a page, a program, a document. Short answers stay in the chat.\n"
                : "- An artifact, in a fenced nth-artifact block, when the reader will keep, run or reuse what you make: a page, a program, a document. Short answers stay in the chat.\n");
            text.Append(Level(options.Visual));
            return text.ToString().TrimEnd('\n');
        }
        text.Append("You can answer with plain text, a live view inside the chat, or an artifact in the Tabula pane beside it. Choose by what the reader will do with the answer.\n");
        text.Append("- Plain text is the default. Facts, explanations, opinions, short code, anything the reader just reads: write text. Never add a view to look clever.\n");
        text.Append("- A live view when the reader will change numbers or options, compare things side by side, see data as a chart or on a map, tick things off, or step through a plan. Write one or two sentences that answer the question first, then the view. At most one view per answer, and do not repeat its contents in the text.\n");
        text.Append(options.ArtifactTools
            ? "- An artifact (artifact_create) when the reader will keep, run, edit or reuse what you make: a page or small app, a component, a diagram, a document, a program, several files. Short answers and small snippets stay in the chat.\n"
            : "- An artifact, in a fenced nth-artifact block, when the reader will keep, run, edit or reuse what you make: a page or small app, a component, a diagram, a document, a program, several files. Short answers and small snippets stay in the chat.\n");
        if (options.HtmlWidget)
            text.Append(options.LiveTools
                ? "- An HTML widget (html_widget) only when no component fits, such as a small game or a custom simulation: one self-contained page, no network, no outside scripts.\n"
                : "- An HTML widget, in a fenced nth-html block, only when no component fits, such as a small game or a custom simulation: one self-contained page, no network, no outside scripts.\n");
        if (options.AskUser)
            text.Append("- When you cannot go on without a choice from the reader, call ask_user (single choice, several choices, text or number) instead of asking in prose.\n");
        text.Append(Level(options.Visual));
        return text.ToString().TrimEnd('\n');
    }

    private static string Level(VisualAnswerLevel level) => level switch
    {
        VisualAnswerLevel.Fewer =>
            "The reader asked for fewer visual answers: show a live view only when they ask for one (a chart, a calculator, a table they can change, \"show me\"). Otherwise write text.\n",
        VisualAnswerLevel.More =>
            "The reader likes visual answers: lean toward a live view whenever numbers, options, comparisons, plans or steps are involved. Still one per answer, and never for a plain fact.\n",
        _ => "",
    };

    private static string Showing(NthUiTeachingOptions options)
    {
        if (!options.LiveAnswers)
        {
            var saving = new StringBuilder();
            if (options.ArtifactTools)
                saving.Append("SAVING AN ARTIFACT\nCall artifact_create with kind (html, react, svg, mermaid, markdown, code, nth-ui or project), a title and the whole content. Every change is a new version: call artifact_update.");
            else
                saving.Append("SAVING AN ARTIFACT\nWrite a fenced block whose first line is three backticks, nth-artifact, id=short-name kind=html title=\"Pricing calculator\" (kind is html, react, svg, mermaid, markdown, code or nth-ui); the whole content follows, then a line with three backticks.");
            return saving.ToString();
        }
        var text = new StringBuilder("SHOWING A VIEW\n");
        if (options.LiveTools)
            text.Append("Call ui_show with the view text as its ui argument. Write your sentence first, then call it, and add nothing after it that repeats the view. To change a view you already showed, call ui_update with its id (its description says how). If you cannot call tools, write the view in a fenced block instead: a line with three backticks and nth-ui, the view lines, then a line with three backticks.\n");
        else
            text.Append("Write the view in a fenced block: a line with three backticks and the word nth-ui, then the view lines, then a line with three backticks. Write your sentence first; the block comes last and nothing after it repeats the view. nth draws each line as it arrives.\n");
        if (options.HtmlWidget && !options.IsLite && !options.LiveTools)
            text.Append("An HTML widget is a fenced block that starts with three backticks and nth-html, holding one complete HTML page.\n");
        if (options.ArtifactTools)
            text.Append("\nSAVING AN ARTIFACT\nCall artifact_create with kind (html, react, svg, mermaid, markdown, code, nth-ui or project), a title and the whole content. Every change is a new version: call artifact_update, with edits (find and replace pairs, each find copied exactly) for small changes. artifact_list and artifact_read show what exists.");
        else
            text.Append("\nSAVING AN ARTIFACT\nWrite a fenced block whose first line is three backticks, nth-artifact, id=short-name kind=html title=\"Pricing calculator\" (kind is html, react, svg, mermaid, markdown, code or nth-ui); the whole content follows and a line with three backticks closes it. Use the same id again for a new version. For several files use kind=project, with one fenced block inside for each file, each naming path=.");
        return text.ToString().TrimEnd('\n');
    }

    private static string ReferenceSection(NthUiTeachingOptions options) => options.IsLite
        ? LiteReference
        : options.Reference
            ? FullReference + "\n\nFor every option of every component, the expression functions and twelve worked examples, call ui_reference with a topic ("
                + string.Join(", ", Topics) + ")."
            : FullReference;

    private static string Mistakes(NthUiTeachingOptions options)
    {
        var text = new StringBuilder("KEEP VIEWS VALID\n");
        text.Append("- Declare every state a control sets, and put computed numbers in a stat, table cell or chart with = expression, so they move when the reader does.\n");
        text.Append("- Quote text. Write attributes as name=value with no spaces. Use \\\" for a quote inside text.\n");
        text.Append("- Put row lines inside a table, chip lines inside chips or followups, item lines inside list or checklist")
            .Append(options.IsLite ? ".\n" : ", stop lines inside map.\n");
        text.Append("- A chart's x labels and y values have the same length.\n");
        text.Append("- Keep it small: one card of about three to eight parts. A table beats a long list of numbers. One chart unless asked.\n");
        text.Append("- A view is not JSON, HTML or Markdown, and has no comments.");
        return text.ToString();
    }

    private static string ExamplesSection(NthUiTeachingOptions options)
    {
        if (!options.LiveAnswers) return "EXAMPLES\nNo view is needed in the chat. Answer in text; save an artifact when the reader will keep it.";
        var text = new StringBuilder();
        var examples = options.IsLite ? NthUiExamples.Lite : NthUiExamples.Six;
        text.Append("EXAMPLES\nEach shows the reader's question, what you write first, then the view as a fenced block");
        if (options.LiveTools) text.Append(" (with ui_show you pass the same lines as ui, without the fence lines)");
        text.Append(".\n");
        var number = 0;
        foreach (var example in examples)
        {
            number++;
            text.Append('\n').Append(number).Append(". The reader asks: ").Append(example.Ask).Append('\n');
            text.Append("You write: ").Append(example.Lead).Append('\n');
            text.Append("```nth-ui\n").Append(example.View).Append("\n```\n");
            if (!options.IsLite) text.Append("Why: ").Append(example.Why).Append('\n');
        }
        text.Append("\nNo view needed:\n");
        foreach (var (ask, answer) in NthUiExamples.PlainText) text.Append("- ").Append(ask).Append(" -> ").Append(answer).Append('\n');
        return text.ToString().TrimEnd('\n');
    }

    private static string ExamplesReference()
    {
        var text = new StringBuilder("Twelve worked examples. Each shows the reader's question, the sentence written before the view, then the view.\n");
        var number = 0;
        foreach (var example in NthUiExamples.Six.Concat(NthUiExamples.More))
        {
            number++;
            text.Append('\n').Append(number).Append(". The reader asks: ").Append(example.Ask).Append('\n');
            text.Append("You write: ").Append(example.Lead).Append('\n');
            text.Append("```nth-ui\n").Append(example.View).Append("\n```\n");
            text.Append("Why: ").Append(example.Why).Append('\n');
        }
        return text.ToString().TrimEnd('\n');
    }

    private static string LiteExamplesText()
    {
        var text = new StringBuilder("Lite examples:\n");
        foreach (var example in NthUiExamples.Lite)
        {
            text.Append("\nThe reader asks: ").Append(example.Ask).Append('\n');
            text.Append("You write: ").Append(example.Lead).Append('\n');
            text.Append("```nth-ui lite\n").Append(example.View).Append("\n```\n");
        }
        return text.ToString().TrimEnd('\n');
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The compact references.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The compact full reference: every component in one line of its own, at the cost of a few hundred tokens.</summary>
    public static string FullReference { get; } =
        """
        THE VIEW LANGUAGE
        A view is lines of text, one component per line; a child is indented two spaces under its parent. A line is a head word, then values: "quoted text", numbers, name=value attributes (no spaces around =), bare flags, and = expression for a computed value. Every line also takes id=name and when=condition. A line nth cannot read shows as plain text, so keep to these forms.

        Values: state name = 3 declares something the reader can change; let name = expression computes from states and lets. A control (stepper, slider, segmented, select, toggle, textfield, date) sets the state named by its first value. What the reader changes stays in the view: you hear of it only through send, context, call or a form's submit.
        Expressions: numbers, "text", true, false, [lists], {key: value}, + - * / %, == != < <= > >=, and, or, not, a ? b : c, list[0], object.key, and the functions min max round floor ceil abs sqrt pow clamp fmt len sum avg map filter sort reverse slice range join contains str num. A lambda only in map, filter and sort: map(xs, x => x * 2). fmt(1234.5, "$#,##0") gives $1,235; fmt(0.256, "0.0%") gives 25.6%; fmt(n, "0.0a") gives 1.2k.
        Text and layout: text "..." (flags muted strong small) | heading "..." level=2 | card "Title" subtitle="..." icon=name | section "Title" | stack | row | grid cols=2 | divider | badge "..." tone=info | stat "Label" = expression delta="+8%" unit="GB" | callout "..." title="..." tone=info (neutral, info, success, warning, danger)
        Data: table cols="A,B" holding row "x" "y" = expression (flags total, muted) | chart bar (or line, area, pie, scatter) x="a,b" y=[1,2] title="..." unit="$" min max stacked; several series: series="S1,S2" y=[[1,2],[3,4]] or child lines series "Name" y=[1,2]; chart ... editable=points bind=listState lets the reader drag the points | list "Title" and checklist "Title" bind=name holding item "text" value=x (flag done) | timeline holding event "Title" time="Mon" (flag done, tone=...) | map "Title" zoom=5 holding stop "Name" lat=38.7 lon=-9.1 and route "Name" via="A,B"
        Choices: chips "Title" bind=name (flag single) holding chip "Label" value=x (flag selected) | tabs bind=name holding tab "Label" value=x | steps "Title" holding step "Name" | segmented n options="a:Label A,b:Label B" | select n options="..." | stepper n min max step unit | slider n min max step unit | toggle n label="..." | textfield n placeholder="..." (flag multiline) | date n | form "Title" submit="Send" holding controls (submitting sends their values as the reader's next message). Controls take label="...".
        Media: code lang=python, diagram "Title" (Mermaid) and svg take their body as indented lines, written as they are | image "https://..." alt="..."
        Actions, inside an actions line or inside button "Label" (flag primary): set name = expression | toggle name | copy "Label" value=table (or view) | open "Label" url="https://..." | export csv "Label" | send "text" | context "key" = expression | call server.tool {"arg": 1}
        End of the view: followups holding chip "next question" lines, at the top level after the card.
        """;

    /// <summary>The compact lite reference for small models: only the lite subset.</summary>
    public static string LiteReference { get; } =
        """
        THE VIEW LANGUAGE (SMALL SET)
        One component per line; children are indented two spaces. Use only these:
        card "Title"                         a container for the lines under it
        state n = 3                          a value the reader can change
        let x = n * 2                        a value computed from states
        stepper n label="N" min=1 max=9      slider n label="N" min=0 max=100 step=5 unit="%"
        text "A sentence"                    grey text is  text muted "A sentence" ; computed text is  text = expression
        table cols="A,B"                     then lines indented under it:  row "x" "y"   or   row total "Total" = fmt(x, "$0")
        chart bar x="a,b" y=[1,2]            bar, line, area, pie or scatter; unit="$"; for several lines of data put  series "Name" y=[1,2]  lines indented under it
        chips "Show" bind=v                  then lines indented under it:  chip "Label" value=x selected
        checklist "Title" bind=v             then lines indented under it:  item "Label" value=x done
        followups                            at the top level after the card, then lines indented under it:  chip "Next question"
        Expressions: numbers, "text", + - * / %, == != < <= > >=, and, or, not, a ? b : c, [lists], list[0], and min max round floor ceil abs sum len fmt. fmt(1234.5, "$#,##0") gives $1,235; fmt(0.25, "0%") gives 25%.
        A computed value goes after = (row "Each" = fmt(each, "$0.00")). Declare a state before the control that sets it. Keep each line's options in the order shown. What the reader changes stays in the view.
        """;

    // ------------------------------------------------------------------------------------------------------------------
    // docs/nth-ui.md, by section.
    // ------------------------------------------------------------------------------------------------------------------

    private static readonly Lazy<string[]> Document = new(LoadDocument);

    /// <summary>The reference page docs/nth-ui.md, as text.</summary>
    public static string ReferencePage => string.Join("\n", Document.Value);

    private static string[] LoadDocument()
    {
        using var stream = typeof(NthUiTeaching).Assembly.GetManifestResourceStream("Marrow.Core.NthUiReference.md")
            ?? throw new InvalidOperationException("The nth-ui reference page is not compiled in.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    /// <summary>The numbered sections of the page ("## 5. Components"), in the order asked, each from its heading to the next one.</summary>
    private static string Sections(params int[] numbers)
    {
        var lines = Document.Value;
        var parts = new List<string>();
        foreach (var number in numbers)
        {
            var start = Array.FindIndex(lines, line => line.StartsWith("## " + number + ". ", StringComparison.Ordinal));
            if (start < 0) throw new InvalidOperationException($"The reference page has no section {number}.");
            var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
            if (end < 0) end = lines.Length;
            parts.Add(string.Join("\n", lines[start..end]).TrimEnd('\n'));
        }
        return string.Join("\n\n", parts);
    }
}
