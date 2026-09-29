using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/HydraDelegationStream.swift (B2 1fbf81e).
// R-FILE-1: an extension X in another file is a partial of X in that file's twin.
// delegation(fromEntry:) (:110-118) was ported first, per the orchestrator's ruling on
// unit Core.Models.Hydra part 4's STOP (PR-HYDRA-084/085/098): it is the sole dependency
// Hydra.cs's Delegations(string) needed from this file. This unit
// (Core.Models.HydraDelegationStream) adds the rest of the file: StreamedDelegations,
// StreamedDelegationsIn/StreamedDelegationsInBody, IsJSONWhitespace and
// HydraDelegationStreamParser.
public static partial class HydraPrompts
{
    // :9-12. Swift `var` struct fields, mutated in place while streamedDelegations(inBody:)
    // walks; ported as an immutable record per R-VAL-1, built fresh at each return point.
    public sealed record StreamedDelegations
    {
        public ValueList<HydraDelegation> Delegations { get; init; } = ValueList<HydraDelegation>.Empty; // :10
        public bool IsComplete { get; init; } // :11
    }

    // :16-19. The streaming opener: NARROWER than DelegationOpenerPattern (Hydra.cs :1092-1094),
    // which also accepts hydra-leaving. PR-HYDRA-081: reproduce the divergence, do not unify.
    [GeneratedRegex(@"```[ \t]*hydra(?:-sent)?[ \t]*\r?\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreamingDelegationOpenerPattern();

    // :16-19. Nil until the (narrower) opening fence is there. PR-HYDRA-081, PR-HYDRA-094.
    public static StreamedDelegations? StreamedDelegationsIn(string text)
    {
        var match = StreamingDelegationOpenerPattern().Match(text);
        if (!match.Success) return null;
        return StreamedDelegationsInBody(text[(match.Index + match.Length)..]);
    }

    // :29-101. Walks UTF-8 bytes (R-STR-1 windows_path note: never UTF-16 code units), so a
    // multi-byte character can never be mistaken for an ASCII delimiter; braces are ASCII, so
    // both ends of an entry sit on character boundaries. PR-HYDRA-094.
    public static StreamedDelegations StreamedDelegationsInBody(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var delegations = new List<HydraDelegation>();
        var index = 0;
        // :33
        while (index < bytes.Length && IsJSONWhitespace(bytes[index])) index++;
        // :34
        if (index >= bytes.Length) return new StreamedDelegations();
        // :35-45. The body opens an array, or is one bare entry; anything else is not readable yet.
        bool isArray;
        switch (bytes[index])
        {
            case (byte)'[':
                isArray = true;
                index++;
                break;
            case (byte)'{':
                isArray = false;
                break;
            default:
                return new StreamedDelegations();
        }
        var depth = 0;
        var inString = false;
        var escaped = false;
        var entryStart = index;
        // :50-51. The body starts right after the opener's newline, so its first byte opens a line.
        var atLineStart = true;
        // :52-99
        while (index < bytes.Length)
        {
            var b = bytes[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (b == (byte)'\\') escaped = true;
                else if (b == (byte)'"') inString = false;
            }
            else
            {
                switch (b)
                {
                    case (byte)'"':
                        inString = true;
                        break;
                    case (byte)'{':
                        if (depth == 0) entryStart = index;
                        depth++;
                        break;
                    case (byte)'}':
                        depth--;
                        if (depth == 0)
                        {
                            // :72-75. Braces are ASCII, so both ends sit on character boundaries.
                            if (JSONValue.Parse(bytes[entryStart..(index + 1)]) is { } json &&
                                Delegation(json) is { } delegation)
                                delegations.Add(delegation);
                            if (!isArray)
                                return new StreamedDelegations
                                {
                                    Delegations = new ValueList<HydraDelegation>(delegations),
                                    IsComplete = true,
                                };
                        }
                        else if (depth < 0)
                        {
                            // :80-84. A closer with nothing open: the block is broken past here.
                            return new StreamedDelegations
                            {
                                Delegations = new ValueList<HydraDelegation>(delegations),
                                IsComplete = true,
                            };
                        }
                        break;
                    case (byte)']' when depth == 0:
                        return new StreamedDelegations
                        {
                            Delegations = new ValueList<HydraDelegation>(delegations),
                            IsComplete = true,
                        };
                    case (byte)'`' when depth == 0 && atLineStart:
                        // :89-92. A fence opening a line outside any entry is the block's closer.
                        return new StreamedDelegations
                        {
                            Delegations = new ValueList<HydraDelegation>(delegations),
                            IsComplete = true,
                        };
                    default:
                        break;
                }
            }
            atLineStart = b == (byte)'\n';
            index++;
        }
        return new StreamedDelegations { Delegations = new ValueList<HydraDelegation>(delegations) };
    }

    // :110-118. One entry of a block read as a head's task: kept only with a non-empty
    // "prompt" (after trimming); titled from the prompt when "task" is absent or blank
    // (TextCleanup.SingleLine, limit 60), else the trimmed "task" as-is (even if it were
    // to trim to empty, task.isEmpty ? ... uses the Swift `task` local, which is the
    // trimmed-or-empty string, so an entry with an all-whitespace "task" still falls back
    // to the prompt title); "name" then "head", trimmed, blank treated as absent;
    // "project" then "repo", trimmed, blank treated as absent; "profile", trimmed, blank
    // treated as absent.
    public static HydraDelegation? Delegation(JSONValue entry)
    {
        var promptRaw = entry["prompt"]?.JSONValueString?.Trimmed();
        if (string.IsNullOrEmpty(promptRaw)) return null;
        var prompt = promptRaw;
        var task = entry["task"]?.JSONValueString?.Trimmed() ?? "";
        var name = entry["name"]?.JSONValueString?.Trimmed()
            ?? entry["head"]?.JSONValueString?.Trimmed();
        var announced = !string.IsNullOrEmpty(name) ? name : null;
        var project = entry["project"]?.JSONValueString?.Trimmed()
            ?? entry["repo"]?.JSONValueString?.Trimmed();
        var profile = entry["profile"]?.JSONValueString?.Trimmed();
        return new HydraDelegation
        {
            Task = task.Length == 0 ? TextCleanup.SingleLine(prompt, 60) : task,
            Prompt = prompt,
            Name = announced,
            Project = !string.IsNullOrEmpty(project) ? project : null,
            Profile = !string.IsNullOrEmpty(profile) ? profile : null,
        };
    }

    // :122-124. `fileprivate static func` -> `private static` (R-NAME-6).
    private static bool IsJSONWhitespace(byte b) =>
        b == (byte)' ' || b == (byte)'\n' || b == (byte)'\r' || b == (byte)'\t';
}

// :127-226. Reads only newly appended UTF-8 bytes. Completed/replaced messages get a fresh
// parser; the runtime's sent watermark prevents already dispatched entries repeating.
// PR-HYDRA-095. Kept as a mutable class (not R-VAL-1): the Swift type is a `final class`
// with `private(set) var` state fed incrementally across many Ingest calls for the life of
// one streaming reply, not a value.
public sealed class HydraDelegationStreamParser
{
    public Guid TurnID { get; }    // :130
    public string EntryID { get; } // :131

    // :132 Swift `[HydraDelegation]` is a value array: a saved snapshot never sees later appends, so each append
    // replaces the array instead of mutating a list the caller may have kept.
    private HydraDelegation[] _delegations = Array.Empty<HydraDelegation>();
    public IReadOnlyList<HydraDelegation> Delegations => _delegations; // :132

    public bool IsComplete { get; private set; } // :133

    private int _opener;      // :134
    private bool _inBody;     // :135
    private bool _shapeKnown; // :136
    private bool _isArray;    // :137
    private int _depth;       // :138
    private bool _inString;   // :139
    private bool _escaped;    // :140
    private bool _atLineStart = true; // :141
    private readonly List<byte> _entry = new(); // :142

    public HydraDelegationStreamParser(Guid turnID, string entryID) // :144-147
    {
        TurnID = turnID;
        EntryID = entryID;
    }

    // :149-155
    public void Ingest(string text)
    {
        if (IsComplete) return;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (_inBody) ReadBody(b); else ReadOpener(b);
            if (IsComplete) break;
        }
    }

    // :157-180. States 0-15: three backticks (0-2, extra backtick tolerated at 3), optional
    // spaces/tabs, case-insensitive "hydra" (4-7), an optional "-sent" suffix (8-12, spelling
    // s,e,n,t) -- never "-leaving" (PR-HYDRA-081, PR-HYDRA-095 failure_cases), optional
    // spaces/tabs, an optional CR, then LF (13-15). On mismatch resets to opener = (byte == 96
    // ? 1 : 0), so a new fence can begin immediately at the mismatching byte.
    private void ReadOpener(byte b)
    {
        var lower = b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b; // :158
        switch (_opener)
        {
            case >= 0 and <= 2: // :160-161
                _opener = b == 96 ? _opener + 1 : 0;
                break;
            case 3: // :162-165
                if (b == 32 || b == 9 || b == 96) return;
                if (lower == 104) { _opener = 4; return; }
                _opener = 0;
                break;
            case >= 4 and <= 7: // :166-168
            {
                byte expected = _opener switch { 4 => 121, 5 => 100, 6 => 114, _ => 97 };
                _opener = lower == expected ? _opener + 1 : (b == 96 ? 1 : 0);
                break;
            }
            case 8: // :169-170
                if (b == 45) _opener = 9; else ReadOpenerEnd(b);
                break;
            case >= 9 and <= 12: // :171-173
            {
                byte expected = _opener switch { 9 => 115, 10 => 101, 11 => 110, _ => 116 };
                _opener = lower == expected ? _opener + 1 : (b == 96 ? 1 : 0);
                break;
            }
            case 13 or 14: // :174-175
                ReadOpenerEnd(b);
                break;
            case 15: // :176-177
                if (b == 10) _inBody = true; else _opener = b == 96 ? 1 : 0;
                break;
            default: // :178
                break;
        }
    }

    // :182-189
    private void ReadOpenerEnd(byte b)
    {
        switch (b)
        {
            case 32 or 9: _opener = 14; break;
            case 13: _opener = 15; break;
            case 10: _inBody = true; break;
            default: _opener = b == 96 ? 1 : 0; break;
        }
    }

    // :191-225. Same rules as StreamedDelegationsInBody, fed one byte at a time.
    private void ReadBody(byte b)
    {
        if (!_shapeKnown) // :192-197
        {
            if (b == 32 || b == 9 || b == 13 || b == 10) return;
            _shapeKnown = true;
            _isArray = b == 91;
            if (_isArray) return;
        }
        if (_depth > 0) _entry.Add(b); // :198
        if (_inString)
        {
            if (_escaped) _escaped = false;
            else if (b == 92) _escaped = true;
            else if (b == 34) _inString = false;
        }
        else
        {
            switch (b)
            {
                case 34: _inString = true; break;
                case 123: // :206-208
                    if (_depth == 0) { _entry.Clear(); _entry.Add(b); }
                    _depth++;
                    break;
                case 125: // :209-218
                    _depth--;
                    if (_depth == 0)
                    {
                        if (JSONValue.Parse(SwiftText.DecodeUtf8Repairing(_entry.ToArray())) is { } json &&
                            HydraPrompts.Delegation(json) is { } delegation)
                            _delegations = [.. _delegations, delegation];
                        _entry.Clear();
                        if (!_isArray) IsComplete = true;
                    }
                    else if (_depth < 0) IsComplete = true;
                    break;
                case 93 when _depth == 0: IsComplete = true; break; // :219
                case 96 when _depth == 0 && _atLineStart: IsComplete = true; break; // :220
                default: break;
            }
        }
        _atLineStart = b == 10; // :224
    }
}
