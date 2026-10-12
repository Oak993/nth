using System.Text;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// A small GBNF reader, recogniser and sampler for the tests of the lite-view grammar (web surfaces W17). It reads the subset
/// llama.cpp reads and the grammar uses: rules on one line, "literals", [classes] with ^ and ranges, rule names, ( groups ),
/// alternation and the * + ? repeats. It rejects what llama.cpp would: an undefined rule, a rule defined twice, a name with
/// characters outside letters, digits and dashes, an unterminated literal or class. Matching is a memoised set-of-end-positions
/// walk, so the grammar's ambiguity costs nothing; sampling draws a random string from the grammar, with a depth guard that
/// picks each rule's shortest way out.
/// </summary>
public sealed class GbnfGrammar
{
    private abstract record Node;

    private sealed record Literal(string Text) : Node;

    private sealed record CharClass(bool Negated, List<(char From, char To)> Ranges) : Node;

    private sealed record Reference(string Name) : Node;

    private sealed record Sequence(List<Node> Items) : Node;

    private sealed record Choice(List<Node> Options) : Node;

    private sealed record Repeat(Node Item, bool AtLeastOne, bool AtMostOne) : Node;

    private readonly Dictionary<string, Node> rules = new(StringComparer.Ordinal);
    private readonly List<string> order = [];
    private readonly Dictionary<string, int> minimum = new(StringComparer.Ordinal);

    public IReadOnlyList<string> RuleNames => order;

    public string Root => order[0];

    public static GbnfGrammar Parse(string text)
    {
        var grammar = new GbnfGrammar();
        var lines = text.Split('\n');
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number];
            if (line.Length == 0) continue;
            var arrow = line.IndexOf(" ::= ", StringComparison.Ordinal);
            if (arrow <= 0) throw new FormatException($"line {number + 1}: not a rule");
            var name = line[..arrow];
            CheckName(name, number + 1);
            if (grammar.rules.ContainsKey(name)) throw new FormatException($"line {number + 1}: {name} is defined twice");
            var reader = new Reader(line, arrow + 5, number + 1);
            var body = reader.ParseChoice();
            reader.End();
            grammar.rules[name] = body;
            grammar.order.Add(name);
        }
        if (grammar.order.Count == 0) throw new FormatException("no rules");
        foreach (var (name, node) in grammar.rules)
            foreach (var reference in References(node))
                if (!grammar.rules.ContainsKey(reference)) throw new FormatException($"{name} uses {reference}, which is not defined");
        grammar.ComputeMinimum();
        return grammar;
    }

    private static void CheckName(string name, int line)
    {
        if (name.Length == 0 || !name.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-'))
            throw new FormatException($"line {line}: {name} is not a rule name llama.cpp reads");
    }

    private static IEnumerable<string> References(Node node) => node switch
    {
        Reference reference => [reference.Name],
        Sequence sequence => sequence.Items.SelectMany(References),
        Choice choice => choice.Options.SelectMany(References),
        Repeat repeat => References(repeat.Item),
        _ => [],
    };

    /// <summary>Whether the whole text is a string of the root rule.</summary>
    public bool Matches(string text) => Matches(Root, text);

    public bool Matches(string rule, string text)
    {
        var memo = new Dictionary<(Node, int), HashSet<int>>();
        return Ends(rules[rule], text, 0, memo).Contains(text.Length);
    }

    /// <summary>How far into the text any part of the grammar got; where a text that does not fit went wrong.</summary>
    public int LongestPrefix(string text)
    {
        var memo = new Dictionary<(Node, int), HashSet<int>>();
        Ends(rules[Root], text, 0, memo);
        return memo.Values.Where(ends => ends.Count > 0).Select(ends => ends.Max()).DefaultIfEmpty(0).Max();
    }

    private HashSet<int> Ends(Node node, string text, int at, Dictionary<(Node, int), HashSet<int>> memo)
    {
        if (memo.TryGetValue((node, at), out var known)) return known;
        var result = new HashSet<int>();
        memo[(node, at)] = result; // a rule that reaches itself without consuming finds nothing new
        switch (node)
        {
            case Literal literal:
                if (string.CompareOrdinal(text, at, literal.Text, 0, literal.Text.Length) == 0 && at + literal.Text.Length <= text.Length) result.Add(at + literal.Text.Length);
                break;
            case CharClass cls:
                if (at < text.Length && cls.Ranges.Any(r => text[at] >= r.From && text[at] <= r.To) != cls.Negated) result.Add(at + 1);
                break;
            case Reference reference:
                result.UnionWith(Ends(rules[reference.Name], text, at, memo));
                break;
            case Sequence sequence:
            {
                var positions = new HashSet<int> { at };
                foreach (var item in sequence.Items)
                {
                    var next = new HashSet<int>();
                    foreach (var position in positions) next.UnionWith(Ends(item, text, position, memo));
                    positions = next;
                    if (positions.Count == 0) break;
                }
                result.UnionWith(positions);
                break;
            }
            case Choice choice:
                foreach (var option in choice.Options) result.UnionWith(Ends(option, text, at, memo));
                break;
            case Repeat repeat:
            {
                if (!repeat.AtLeastOne) result.Add(at);
                var frontier = new HashSet<int> { at };
                var seen = new HashSet<int>();
                while (frontier.Count > 0)
                {
                    var next = new HashSet<int>();
                    foreach (var position in frontier)
                        foreach (var end in Ends(repeat.Item, text, position, memo))
                        {
                            result.Add(end);
                            if (!repeat.AtMostOne && end != position && seen.Add(end)) next.Add(end);
                        }
                    frontier = next;
                }
                break;
            }
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Sampling.
    // ---------------------------------------------------------------------------------------------------------------------

    private void ComputeMinimum()
    {
        foreach (var name in order) minimum[name] = int.MaxValue / 4;
        for (var pass = 0; pass < 200; pass++)
        {
            var changed = false;
            foreach (var name in order)
            {
                var length = Shortest(rules[name]);
                if (length < minimum[name]) { minimum[name] = length; changed = true; }
            }
            if (!changed) break;
        }
    }

    private int Shortest(Node node) => node switch
    {
        Literal literal => literal.Text.Length,
        CharClass => 1,
        Reference reference => minimum[reference.Name],
        Sequence sequence => Math.Min(int.MaxValue / 4, sequence.Items.Sum(Shortest)),
        Choice choice => choice.Options.Min(Shortest),
        Repeat repeat => repeat.AtLeastOne ? Shortest(repeat.Item) : 0,
        _ => 0,
    };

    /// <summary>
    /// A random string of the root rule. <paramref name="depth"/> is how many nested rules may still open freely; once the text
    /// passes <paramref name="budget"/> characters every choice takes its shortest way out, so a sample always ends.
    /// </summary>
    public string Sample(Random random, int depth = 24, int budget = 1_500)
    {
        var text = new StringBuilder();
        Emit(rules[Root], random, depth, text, budget);
        return text.ToString();
    }

    /// <summary>How far below its start a sample may open rules before the grammar is declared unable to end (a shortest way out that loops).</summary>
    private const int DepthFloor = -300;

    private void Emit(Node node, Random random, int depth, StringBuilder text, int budget)
    {
        if (depth < DepthFloor) throw new InvalidOperationException("The grammar has no finite way to end from here.");
        var lean = depth <= 0 || text.Length > budget;
        switch (node)
        {
            case Literal literal:
                text.Append(literal.Text);
                break;
            case CharClass cls:
                text.Append(Pick(cls, random));
                break;
            case Reference reference:
                Emit(rules[reference.Name], random, depth - 1, text, budget);
                break;
            case Sequence sequence:
                foreach (var item in sequence.Items) Emit(item, random, depth, text, budget);
                break;
            case Choice choice:
            {
                var options = choice.Options;
                if (lean)
                {
                    var least = options.Min(Shortest);
                    options = options.Where(o => Shortest(o) == least).ToList();
                }
                Emit(options[random.Next(options.Count)], random, depth, text, budget);
                break;
            }
            case Repeat repeat:
            {
                var count = lean ? (repeat.AtLeastOne ? 1 : 0)
                    : repeat.AtMostOne ? random.Next(2) : (repeat.AtLeastOne ? 1 : 0) + random.Next(depth >= 5 ? 3 : 2);
                for (var i = 0; i < count; i++) Emit(repeat.Item, random, depth, text, budget);
                break;
            }
        }
    }

    private static char Pick(CharClass cls, Random random)
    {
        if (!cls.Negated)
        {
            var (from, to) = cls.Ranges[random.Next(cls.Ranges.Count)];
            return (char)random.Next(from, to + 1);
        }
        // A negated class: a printable ASCII letter or digit that is not excluded.
        const string pool = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ,.:;!?-+";
        var allowed = pool.Where(c => !cls.Ranges.Any(r => c >= r.From && c <= r.To)).ToArray();
        return allowed[random.Next(allowed.Length)];
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // The reader.
    // ---------------------------------------------------------------------------------------------------------------------

    private sealed class Reader(string text, int start, int line)
    {
        private int at = start;

        public void End()
        {
            Space();
            if (at != text.Length) throw Fail("unexpected " + text[at]);
        }

        public Node ParseChoice()
        {
            var options = new List<Node> { ParseSequence() };
            Space();
            while (at < text.Length && text[at] == '|')
            {
                at++;
                options.Add(ParseSequence());
                Space();
            }
            return options.Count == 1 ? options[0] : new Choice(options);
        }

        private Node ParseSequence()
        {
            var items = new List<Node>();
            while (true)
            {
                Space();
                if (at >= text.Length || text[at] is '|' or ')') break;
                items.Add(ParseItem());
            }
            if (items.Count == 0) throw Fail("an empty alternative");
            return items.Count == 1 ? items[0] : new Sequence(items);
        }

        private Node ParseItem()
        {
            var atom = ParseAtom();
            if (at < text.Length && text[at] is '*' or '+' or '?')
            {
                var mark = text[at++];
                return new Repeat(atom, mark == '+', mark == '?');
            }
            return atom;
        }

        private Node ParseAtom()
        {
            var c = text[at];
            if (c == '"') return ParseLiteral();
            if (c == '[') return ParseClass();
            if (c == '(')
            {
                at++;
                var inner = ParseChoice();
                Space();
                if (at >= text.Length || text[at] != ')') throw Fail("a missing )");
                at++;
                return inner;
            }
            var begin = at;
            while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '-')) at++;
            if (at == begin) throw Fail("unexpected " + c);
            return new Reference(text[begin..at]);
        }

        private Node ParseLiteral()
        {
            at++;
            var builder = new StringBuilder();
            while (true)
            {
                if (at >= text.Length) throw Fail("an unterminated literal");
                var c = text[at++];
                if (c == '"') break;
                builder.Append(c == '\\' ? Escape() : c);
            }
            return new Literal(builder.ToString());
        }

        private Node ParseClass()
        {
            at++;
            var negated = at < text.Length && text[at] == '^';
            if (negated) at++;
            var ranges = new List<(char, char)>();
            while (true)
            {
                if (at >= text.Length) throw Fail("an unterminated class");
                if (text[at] == ']') { at++; break; }
                var from = ClassChar();
                var to = from;
                if (at + 1 < text.Length && text[at] == '-' && text[at + 1] != ']')
                {
                    at++;
                    to = ClassChar();
                }
                ranges.Add((from, to));
            }
            if (ranges.Count == 0) throw Fail("an empty class");
            return new CharClass(negated, ranges);
        }

        private char ClassChar()
        {
            var c = text[at++];
            return c == '\\' ? Escape() : c;
        }

        private char Escape()
        {
            if (at >= text.Length) throw Fail("a dangling backslash");
            var c = text[at++];
            return c switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '"' or '\\' or '[' or ']' => c,
                _ => throw Fail("an escape llama.cpp does not read: \\" + c),
            };
        }

        private void Space()
        {
            while (at < text.Length && text[at] == ' ') at++;
        }

        private FormatException Fail(string what) => new($"line {line}: {what} at column {at + 1}");
    }
}
