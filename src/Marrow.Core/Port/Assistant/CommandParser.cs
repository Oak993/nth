// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// Splits one shell command line into the simple commands it runs, for PowerShell, cmd and POSIX shells: pipelines,
// &&, ||, ;, newlines, subshells, $( ) and backtick substitutions, script blocks, grouping, here-docs and here-strings.
// Nested code is parsed too and its parts are listed before the part that holds them (they run first). Anything the
// parser cannot read with confidence is a Problem, which the classifier turns into Unknown (asks). Shell wrappers
// (powershell -Command / -EncodedCommand, cmd /c, bash -c) are opened by the classifier, which knows the programs.
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>Which shell's grammar a command is read with. Auto reads it with all three and keeps the strictest class.</summary>
public enum ShellKind { Auto, PowerShell, Cmd, Posix }

/// <summary>One word of a simple command, with its quotes removed. Dynamic: part of it is only known when it runs.</summary>
public sealed record CommandWord(string Text, bool Dynamic = false, bool Quoted = false);

/// <summary>A redirection: output to a file (Append for &gt;&gt;) or input from one.</summary>
public sealed record CommandRedirect(string Target, bool Append, bool Dynamic, bool Input);

/// <summary>One simple command. Nested: it came from inside another command (a substitution, block or subshell).</summary>
public sealed record CommandPart(ShellKind Shell, ValueList<CommandWord> Words, ValueList<CommandRedirect> Redirects, bool Nested)
{
    /// <summary>Text fed on standard input by a here-doc or here-string, if any.</summary>
    public string? Stdin { get; init; }
}

/// <summary>Every simple command of a line, in the order they run, and anything that could not be read.</summary>
public sealed record ParsedCommand(ShellKind Shell, ValueList<CommandPart> Parts, ValueList<string> Problems)
{
    public bool HasProblems => Problems.Count > 0;
}

/// <summary>The shell command splitter of 12.1a.</summary>
public static class CommandParser
{
    /// <summary>How deep substitutions, blocks and wrappers may nest before the command is Unknown.</summary>
    public const int MaxDepth = 8;

    /// <summary>The longest command read; anything longer is Unknown.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>Splits <paramref name="text"/> with <paramref name="shell"/>'s grammar (not Auto).</summary>
    public static ParsedCommand Parse(string text, ShellKind shell) => Parse(text, shell, 0, nested: false);

    internal static ParsedCommand Parse(string text, ShellKind shell, int depth, bool nested)
    {
        if (shell == ShellKind.Auto) throw new ArgumentException("Parse one grammar at a time.", nameof(shell));
        var parts = new List<CommandPart>();
        var problems = new List<string>();
        if (depth > MaxDepth) problems.Add("nested too deep");
        else if (text.Length > MaxLength) problems.Add("command too long");
        else new Scanner(text, shell, depth, nested, parts, problems).Run();
        return new ParsedCommand(shell, new ValueList<CommandPart>(parts), new ValueList<string>(problems));
    }

    private sealed class Scanner(string s, ShellKind shell, int depth, bool nested, List<CommandPart> parts, List<string> problems)
    {
        private int i;
        private readonly List<CommandWord> words = [];
        private readonly List<CommandRedirect> redirects = [];
        private readonly StringBuilder word = new();
        private bool wordStarted, wordDynamic, wordQuoted;
        private (bool Append, bool Input)? pendingRedirect;
        private readonly List<(string Delimiter, bool Quoted, bool StripTabs)> pendingHeredocs = [];
        private string? stdin;
        private bool stopParsing;

        private bool Ps => shell == ShellKind.PowerShell;
        private bool Cmd => shell == ShellKind.Cmd;
        private bool Posix => shell == ShellKind.Posix;

        private char At(int k) => k < s.Length ? s[k] : '\0';

        public void Run()
        {
            while (i < s.Length)
            {
                var c = s[i];
                if (stopParsing) { StopParsingChar(c); continue; }
                switch (c)
                {
                    case ' ' or '\t' or '\r' or '\f' or '\v':
                        EndWord(); i++; continue;
                    case '\n':
                        EndWord(); i++; ReadHeredocBodies(); EndPart(); continue;
                }
                if (!wordStarted && c == '#' && (Ps || Posix)) { SkipComment(); continue; }
                if (Ps && c == '<' && At(i + 1) == '#') { SkipBlockComment(); continue; }
                if (Separator(c)) continue;
                if (Redirect(c)) continue;
                if (Quote(c)) continue;
                if (Escape(c)) continue;
                if (Expansion(c)) continue;
                if (Group(c)) continue;
                Append(c); i++;
            }
            EndWord();
            if (pendingRedirect is not null) problems.Add("redirection without a target");
            if (pendingHeredocs.Count > 0) problems.Add("here-doc without a body");
            EndPart();
        }

        // ---- words and parts ----

        private void Append(char c) { word.Append(c); wordStarted = true; }

        private void Append(string text) { word.Append(text); wordStarted = true; }

        private void EndWord()
        {
            if (!wordStarted) return;
            var text = word.ToString();
            word.Clear();
            var dynamic = wordDynamic;
            var quoted = wordQuoted;
            wordStarted = wordDynamic = wordQuoted = false;
            if (pendingRedirect is { } target)
            {
                pendingRedirect = null;
                if (target.Input || !IsNullDevice(text, quoted)) redirects.Add(new CommandRedirect(text, target.Append, dynamic, target.Input));
                return;
            }
            if (Ps && !quoted && text == "--%" ) { stopParsing = true; return; }
            words.Add(new CommandWord(text, dynamic, quoted));
        }

        private bool IsNullDevice(string text, bool quoted) =>
            !quoted && (text.Equals("$null", StringComparison.OrdinalIgnoreCase) || text == "/dev/null"
                        || text.Equals("nul", StringComparison.OrdinalIgnoreCase));

        private void EndPart()
        {
            EndWord();
            if (words.Count > 0 || redirects.Count > 0 || stdin is not null)
                parts.Add(new CommandPart(shell, new ValueList<CommandWord>(words), new ValueList<CommandRedirect>(redirects), nested) { Stdin = stdin });
            words.Clear();
            redirects.Clear();
            stdin = null;
            stopParsing = false;
        }

        private void Nested(string code, ShellKind grammar)
        {
            var inner = Parse(code, grammar, depth + 1, nested: true);
            parts.AddRange(inner.Parts);
            problems.AddRange(inner.Problems);
        }

        // ---- separators ----

        private bool Separator(char c)
        {
            if (Ps)
            {
                if (c == ';') { i++; EndPart(); return true; }
                if (c == '|') { i += At(i + 1) == '|' ? 2 : 1; EndPart(); return true; }
                if (c == '&')
                {
                    if (At(i + 1) == '&') { i += 2; EndPart(); return true; }
                    if (!wordStarted && words.Count == 0) { words.Add(new CommandWord("&")); i++; return true; } // call operator
                    i++; EndPart(); return true; // background job
                }
                return false;
            }
            if (Cmd)
            {
                if (c == '&') { i += At(i + 1) == '&' ? 2 : 1; EndPart(); return true; }
                if (c == '|') { i += At(i + 1) == '|' ? 2 : 1; EndPart(); return true; }
                return false;
            }
            if (c == ';') { i += At(i + 1) == ';' ? 2 : 1; EndPart(); return true; }
            if (c == '|') { i += At(i + 1) is '|' or '&' ? 2 : 1; EndPart(); return true; }
            if (c == '&' && At(i + 1) != '>')
            {
                i += At(i + 1) == '&' ? 2 : 1; EndPart(); return true;
            }
            return false;
        }

        // ---- redirections ----

        private bool Redirect(char c)
        {
            if (c == '>' || (Posix && c == '&' && At(i + 1) == '>'))
            {
                if (Posix && (At(i + 1) == '(') && c == '>') { i++; ProcessSubstitution(); return true; }
                // A stream number written just before the operator ("2>") is not a word.
                var text = word.ToString();
                if (wordStarted && !wordQuoted && !wordDynamic && (text.All(char.IsAsciiDigit) || (Ps && text == "*")))
                {
                    word.Clear(); wordStarted = false;
                }
                else EndWord();
                if (c == '&') i++;
                i++;
                var append = false;
                if (At(i) == '>') { append = true; i++; }
                else if (Posix && At(i) == '|') i++;
                if (At(i) == '&')
                {
                    // >&1, 2>&1, >&-: a stream merge, no file.
                    i++;
                    while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '-')) i++;
                    return true;
                }
                pendingRedirect = (append, false);
                return true;
            }
            if (c == '<')
            {
                if (Posix && At(i + 1) == '(') { i++; ProcessSubstitution(); return true; }
                EndWord();
                if (Posix && At(i + 1) == '<' && At(i + 2) == '<') { i += 3; HereString(); return true; }
                if (Posix && At(i + 1) == '<') { i += 2; HeredocStart(); return true; }
                i++;
                pendingRedirect = (false, true);
                return true;
            }
            return false;
        }

        private void HereString()
        {
            while (At(i) is ' ' or '\t') i++;
            // Read the next word with the normal rules, then take it as standard input.
            var before = words.Count;
            while (i < s.Length && s[i] is not (' ' or '\t' or '\n' or ';' or '|' or '&'))
            {
                var c = s[i];
                if (Quote(c) || Escape(c) || Expansion(c)) continue;
                Append(c); i++;
            }
            EndWord();
            if (words.Count > before)
            {
                stdin = words[^1].Text;
                words.RemoveAt(words.Count - 1);
            }
        }

        private void HeredocStart()
        {
            var strip = false;
            if (At(i) == '-') { strip = true; i++; }
            while (At(i) is ' ' or '\t') i++;
            var delimiter = new StringBuilder();
            var quoted = false;
            while (i < s.Length && s[i] is not (' ' or '\t' or '\n' or ';' or '|' or '&' or '<' or '>'))
            {
                var c = s[i];
                if (c is '\'' or '"')
                {
                    quoted = true;
                    var close = s.IndexOf(c, i + 1);
                    if (close < 0) { problems.Add("unterminated here-doc delimiter"); i = s.Length; break; }
                    delimiter.Append(s, i + 1, close - i - 1);
                    i = close + 1;
                    continue;
                }
                if (c == '\\') { quoted = true; i++; if (i < s.Length) delimiter.Append(s[i++]); continue; }
                delimiter.Append(c); i++;
            }
            if (delimiter.Length == 0) { problems.Add("here-doc without a delimiter"); return; }
            pendingHeredocs.Add((delimiter.ToString(), quoted, strip));
        }

        private void ReadHeredocBodies()
        {
            if (pendingHeredocs.Count == 0) return;
            var bodies = new StringBuilder();
            foreach (var (delimiter, quoted, strip) in pendingHeredocs)
            {
                var body = new StringBuilder();
                var closed = false;
                while (i < s.Length)
                {
                    var end = s.IndexOf('\n', i);
                    var line = end < 0 ? s[i..] : s[i..end];
                    i = end < 0 ? s.Length : end + 1;
                    var test = line.TrimEnd('\r');
                    if (strip) test = test.TrimStart('\t');
                    if (test == delimiter) { closed = true; break; }
                    body.Append(line).Append('\n');
                }
                if (!closed) problems.Add("here-doc not closed");
                var text = body.ToString();
                // An unquoted delimiter expands $( ) and backticks inside the body: that code runs.
                if (!quoted) ScanForSubstitutions(text);
                bodies.Append(text);
            }
            pendingHeredocs.Clear();
            stdin = (stdin ?? "") + bodies;
        }

        private void ScanForSubstitutions(string text)
        {
            for (var k = 0; k < text.Length; k++)
            {
                if (text[k] == '\\') { k++; continue; }
                if (text[k] == '$' && k + 1 < text.Length && text[k + 1] == '(')
                {
                    var close = FindClose(text, k + 1, '(', ')');
                    if (close < 0) { problems.Add("unbalanced $( in here-doc"); return; }
                    Nested(text[(k + 2)..close], shell);
                    k = close;
                }
                else if (text[k] == '`')
                {
                    var close = text.IndexOf('`', k + 1);
                    if (close < 0) { problems.Add("unbalanced backtick in here-doc"); return; }
                    Nested(text[(k + 1)..close], shell);
                    k = close;
                }
            }
        }

        private void ProcessSubstitution()
        {
            // At '(' of <( or >( : the inner command runs.
            var close = FindClose(s, i, '(', ')');
            if (close < 0) { problems.Add("unbalanced process substitution"); Nested(s[(i + 1)..], shell); i = s.Length; return; }
            Nested(s[(i + 1)..close], shell);
            i = close + 1;
            Append("<(...)");
            wordDynamic = true;
        }

        // ---- quotes ----

        // PowerShell also takes the typographic quotes as quotes: U+2018 LEFT SINGLE QUOTATION MARK, U+2019 RIGHT
        // SINGLE QUOTATION MARK, U+201A SINGLE LOW-9 QUOTATION MARK, U+201B SINGLE HIGH-REVERSED-9 QUOTATION MARK;
        // U+201C LEFT DOUBLE QUOTATION MARK, U+201D RIGHT DOUBLE QUOTATION MARK, U+201E DOUBLE LOW-9 QUOTATION MARK.
        private static bool IsSingleQuote(char c) =>
            c == '\'' || c == "\U00002018"[0] || c == "\U00002019"[0] || c == "\U0000201A"[0] || c == "\U0000201B"[0];

        private static bool IsDoubleQuote(char c) =>
            c == '"' || c == "\U0000201C"[0] || c == "\U0000201D"[0] || c == "\U0000201E"[0];

        private bool Quote(char c)
        {
            if (Ps && IsSingleQuote(c))
            {
                i++;
                wordStarted = wordQuoted = true;
                while (true)
                {
                    if (i >= s.Length) { problems.Add("unterminated quote"); return true; }
                    if (IsSingleQuote(s[i]))
                    {
                        if (IsSingleQuote(At(i + 1))) { word.Append('\''); i += 2; continue; }
                        i++; return true;
                    }
                    word.Append(s[i++]);
                }
            }
            if (Ps && IsDoubleQuote(c)) { PsDoubleQuoted(); return true; }
            if (Cmd && c == '"')
            {
                i++;
                wordStarted = wordQuoted = true;
                while (true)
                {
                    if (i >= s.Length) return true; // cmd closes an open quote at the end of the line
                    if (s[i] == '"') { i++; return true; }
                    if (s[i] == '%' && CmdVariable()) continue;
                    if (s[i] == '!' && CmdDelayed()) continue;
                    word.Append(s[i++]);
                }
            }
            if (Posix && c == '\'')
            {
                var close = s.IndexOf('\'', i + 1);
                wordStarted = wordQuoted = true;
                if (close < 0) { problems.Add("unterminated quote"); word.Append(s, i + 1, s.Length - i - 1); i = s.Length; return true; }
                word.Append(s, i + 1, close - i - 1);
                i = close + 1;
                return true;
            }
            if (Posix && c == '"') { PosixDoubleQuoted(); return true; }
            return false;
        }

        private void PsDoubleQuoted()
        {
            i++;
            wordStarted = wordQuoted = true;
            while (true)
            {
                if (i >= s.Length) { problems.Add("unterminated quote"); return; }
                var c = s[i];
                if (IsDoubleQuote(c))
                {
                    if (IsDoubleQuote(At(i + 1))) { word.Append('"'); i += 2; continue; }
                    i++; return;
                }
                if (c == '`') { i++; if (i < s.Length) word.Append(s[i++]); continue; }
                if (c == '$' && PsDollar()) continue;
                word.Append(c); i++;
            }
        }

        private void PosixDoubleQuoted()
        {
            i++;
            wordStarted = wordQuoted = true;
            while (true)
            {
                if (i >= s.Length) { problems.Add("unterminated quote"); return; }
                var c = s[i];
                if (c == '"') { i++; return; }
                if (c == '\\' && At(i + 1) is '"' or '\\' or '$' or '`' or '\n') { i++; if (s[i] != '\n') word.Append(s[i]); i++; continue; }
                if (c == '$' && PosixDollar()) continue;
                if (c == '`') { Backtick(); continue; }
                word.Append(c); i++;
            }
        }

        // ---- escapes ----

        private bool Escape(char c)
        {
            if (Ps && c == '`')
            {
                i++;
                if (At(i) == '\r') i++;
                if (At(i) == '\n') { i++; return true; } // line continuation
                if (i < s.Length) Append(s[i++]);
                return true;
            }
            if (Cmd && c == '^')
            {
                i++;
                if (At(i) == '\r') i++;
                if (At(i) == '\n') { i++; return true; }
                if (i < s.Length) Append(s[i++]);
                return true;
            }
            if (Posix && c == '\\')
            {
                i++;
                if (At(i) == '\n') { i++; return true; }
                if (i < s.Length) { Append(s[i++]); wordQuoted = true; }
                return true;
            }
            return false;
        }

        // ---- expansions ----

        private bool Expansion(char c)
        {
            if (Ps && c == '$') return PsDollar();
            if (Ps && c == '@' && !wordStarted) return PsAt();
            if (Cmd && c == '%') return CmdVariable();
            if (Cmd && c == '!') return CmdDelayed();
            if (Cmd && c == '@' && !wordStarted && words.Count == 0) { i++; return true; } // @echo off style prefix
            if (Posix && c == '$') return PosixDollar();
            if (Posix && c == '`') { Backtick(); return true; }
            return false;
        }

        private bool PsDollar()
        {
            var next = At(i + 1);
            if (next == '(')
            {
                var close = FindClose(s, i + 1, '(', ')');
                if (close < 0) { problems.Add("unbalanced $("); Nested(s[(i + 2)..], ShellKind.PowerShell); i = s.Length; return true; }
                Nested(s[(i + 2)..close], ShellKind.PowerShell);
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (next == '{')
            {
                var close = s.IndexOf('}', i + 2);
                if (close < 0) { problems.Add("unterminated ${"); close = s.Length - 1; }
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (char.IsLetterOrDigit(next) || next is '_' or '?' or '^' or '$')
            {
                var k = i + 2;
                if (char.IsLetterOrDigit(next) || next == '_')
                    while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is '_' or ':')) k++;
                Append(s[i..k]);
                wordDynamic = true;
                i = k;
                return true;
            }
            return false;
        }

        private bool PsAt()
        {
            var next = At(i + 1);
            if (next is '(' or '{')
            {
                var close = FindClose(s, i + 1, next, next == '(' ? ')' : '}');
                if (close < 0) { problems.Add("unbalanced @" + next); Nested(s[(i + 2)..], ShellKind.PowerShell); i = s.Length; return true; }
                Nested(s[(i + 2)..close], ShellKind.PowerShell);
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (next is '\'' or '"' && (At(i + 2) == '\n' || (At(i + 2) == '\r' && At(i + 3) == '\n')))
            {
                // Here-string: @' ... '@ (literal) or @" ... "@ (expands $( )).
                var closeMark = "\n" + next + "@";
                var bodyStart = s.IndexOf('\n', i) + 1;
                var close = s.IndexOf(closeMark, bodyStart - 1, StringComparison.Ordinal);
                if (close < 0) { problems.Add("unterminated here-string"); close = s.Length; }
                var body = close <= bodyStart ? "" : s[bodyStart..close].TrimEnd('\r');
                if (next == '"') ScanPsSubexpressions(body);
                wordStarted = wordQuoted = true;
                word.Append(body);
                if (next == '"' && body.Contains('$', StringComparison.Ordinal)) wordDynamic = true;
                i = Math.Min(s.Length, close + closeMark.Length);
                return true;
            }
            if (char.IsLetter(next) || next == '_')
            {
                // Splatting: @params passes a hashtable whose keys and values are only known when it runs.
                var k = i + 1;
                while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_')) k++;
                Append(s[i..k]);
                wordDynamic = true;
                i = k;
                return true;
            }
            return false;
        }

        private void ScanPsSubexpressions(string text)
        {
            for (var k = 0; k < text.Length; k++)
            {
                if (text[k] == '`') { k++; continue; }
                if (text[k] == '$' && k + 1 < text.Length && text[k + 1] == '(')
                {
                    var close = FindClose(text, k + 1, '(', ')');
                    if (close < 0) { problems.Add("unbalanced $( in here-string"); return; }
                    Nested(text[(k + 2)..close], ShellKind.PowerShell);
                    k = close;
                }
            }
        }

        private bool CmdVariable()
        {
            // %NAME% and %NAME:x=y%; %~dp0, %1 and the loop variables %%i.
            var next = At(i + 1);
            if (next is '%' or '~' || char.IsAsciiDigit(next))
            {
                var k = i + 1;
                while (k < s.Length && s[k] == '%') k++;
                while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is '~' or '$' or ':')) k++;
                Append(s[i..k]);
                wordDynamic = true;
                i = k;
                return true;
            }
            var close = s.IndexOf('%', i + 1);
            if (close <= i + 1 || s.AsSpan(i + 1, close - i - 1).IndexOfAny(" \t\n") >= 0) return false;
            Append(s[i..(close + 1)]);
            wordDynamic = true;
            i = close + 1;
            return true;
        }

        private bool CmdDelayed()
        {
            var close = s.IndexOf('!', i + 1);
            if (close <= i + 1 || s[(i + 1)..close].Contains(' ', StringComparison.Ordinal)) return false;
            Append(s[i..(close + 1)]);
            wordDynamic = true;
            i = close + 1;
            return true;
        }

        private bool PosixDollar()
        {
            var next = At(i + 1);
            if (next == '(' && At(i + 2) == '(')
            {
                var close = s.IndexOf("))", i + 3, StringComparison.Ordinal);
                if (close < 0) { problems.Add("unbalanced $(("); close = s.Length - 2; }
                var inner = s[(i + 3)..Math.Max(i + 3, close)];
                if (inner.Contains("$(", StringComparison.Ordinal) || inner.Contains('`', StringComparison.Ordinal)) problems.Add("substitution inside arithmetic");
                Append(s[i..Math.Min(s.Length, close + 2)]);
                wordDynamic = true;
                i = Math.Min(s.Length, close + 2);
                return true;
            }
            if (next == '(')
            {
                var close = FindClose(s, i + 1, '(', ')');
                if (close < 0) { problems.Add("unbalanced $("); Nested(s[(i + 2)..], ShellKind.Posix); i = s.Length; return true; }
                Nested(s[(i + 2)..close], ShellKind.Posix);
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (next == '{')
            {
                var close = s.IndexOf('}', i + 2);
                if (close < 0) { problems.Add("unterminated ${"); close = s.Length - 1; }
                var inner = s[(i + 2)..close];
                if (inner.Contains("$(", StringComparison.Ordinal) || inner.Contains('`', StringComparison.Ordinal)) problems.Add("substitution inside ${ }");
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (next == '\'')
            {
                // $'...' ANSI-C quoting: escapes are not decoded here, so the value is unknown.
                var k = i + 2;
                while (k < s.Length && s[k] != '\'') k += s[k] == '\\' ? 2 : 1;
                if (k >= s.Length) { problems.Add("unterminated quote"); k = s.Length - 1; }
                Append(s[(i + 2)..Math.Max(i + 2, k)]);
                wordDynamic = wordQuoted = true;
                i = k + 1;
                return true;
            }
            if (char.IsLetterOrDigit(next) || next is '_' or '@' or '*' or '#' or '?' or '$' or '!' or '-')
            {
                var k = i + 1;
                if (char.IsLetter(next) || next == '_') while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_')) k++;
                else k++;
                Append(s[i..k]);
                wordDynamic = true;
                i = k;
                return true;
            }
            return false;
        }

        private void Backtick()
        {
            var k = i + 1;
            var code = new StringBuilder();
            while (k < s.Length && s[k] != '`')
            {
                if (s[k] == '\\' && k + 1 < s.Length) { code.Append(s[k + 1]); k += 2; continue; }
                code.Append(s[k++]);
            }
            if (k >= s.Length) problems.Add("unbalanced backtick");
            Nested(code.ToString(), ShellKind.Posix);
            Append("`...`");
            wordDynamic = true;
            i = Math.Min(s.Length, k + 1);
        }

        // ---- grouping, blocks and subshells ----

        private bool Group(char c)
        {
            if (c == ')' || (Ps && c == '}'))
            {
                problems.Add("unbalanced " + c);
                i++;
                return true;
            }
            if (c == '(')
            {
                if (Cmd && wordStarted) return false; // echo a(b) is literal in cmd
                if (Posix && wordStarted) problems.Add("function definition");
                var close = FindClose(s, i, '(', ')');
                if (close < 0) { problems.Add("unbalanced ("); Nested(s[(i + 1)..], shell); i = s.Length; return true; }
                Nested(s[(i + 1)..close], shell);
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (Ps && c == '{')
            {
                var close = FindClose(s, i, '{', '}');
                if (close < 0) { problems.Add("unbalanced {"); Nested(s[(i + 1)..], shell); i = s.Length; return true; }
                Nested(s[(i + 1)..close], shell);
                Append(s[i..(close + 1)]);
                wordDynamic = true;
                i = close + 1;
                return true;
            }
            if (Posix && c == '{' && !wordStarted && (At(i + 1) is ' ' or '\t' or '\n'))
            {
                i++; // the { of a { ...; } group is a keyword; its commands are read as usual
                return true;
            }
            if (Posix && c == '}' && !wordStarted && words.Count == 0)
            {
                i++;
                return true;
            }
            if (Posix && c == '{') wordDynamic = true; // brace expansion: a{b,c}
            return false;
        }

        private void StopParsingChar(char c)
        {
            // PowerShell's --% passes the rest of the line through literally, up to a newline or a pipe.
            if (c == '\n' || c == '|') { stopParsing = false; return; }
            if (c is ' ' or '\t') { EndWordLiteral(); i++; return; }
            if (c == '%' && CmdVariable()) return; // --% still expands %VAR%
            word.Append(c); wordStarted = true; i++;
        }

        private void EndWordLiteral()
        {
            if (!wordStarted) return;
            words.Add(new CommandWord(word.ToString(), wordDynamic, wordQuoted));
            word.Clear();
            wordStarted = wordDynamic = wordQuoted = false;
        }

        private void SkipComment()
        {
            while (i < s.Length && s[i] != '\n') i++;
        }

        private void SkipBlockComment()
        {
            var close = s.IndexOf("#>", i + 2, StringComparison.Ordinal);
            if (close < 0) { problems.Add("unterminated block comment"); i = s.Length; return; }
            i = close + 2;
        }

        /// <summary>The index of the bracket that closes the one at <paramref name="open"/>, or -1.</summary>
        private int FindClose(string text, int open, char openChar, char closeChar)
        {
            var level = 0;
            for (var k = open; k < text.Length; k++)
            {
                var c = text[k];
                if (Ps && c == '`') { k++; continue; }
                if (Posix && c == '\\') { k++; continue; }
                if (Cmd && c == '^') { k++; continue; }
                if ((Ps && IsSingleQuote(c)) || (Posix && c == '\''))
                {
                    k++;
                    while (k < text.Length && !((Ps && IsSingleQuote(text[k])) || (Posix && text[k] == '\''))) k++;
                    continue;
                }
                if (c == '"' || (Ps && IsDoubleQuote(c)))
                {
                    k++;
                    while (k < text.Length && !(text[k] == '"' || (Ps && IsDoubleQuote(text[k]))))
                    {
                        if ((Ps && text[k] == '`') || (Posix && text[k] == '\\')) k++;
                        k++;
                    }
                    continue;
                }
                if (c == openChar) level++;
                else if (c == closeChar && --level == 0) return k;
            }
            return -1;
        }
    }
}
