# Markdown link grammar: bare-domain regex, TLD table, title resolution, stream veil

Verbatim extraction pass. Reference checkout `E:/Droppy/reference/droppy-code`, commit
`dd2b86efef9e77168f304a86e8a8411877578bbe`. Read-only; nothing under it was modified.

Every claim below is labelled **SOURCE FACT**, **ENGINEERING DECISION** or **UNRESOLVED**.

---

## 0. Correction to the task's file list (load-bearing)

**SOURCE FACT.** The task named `MarkdownParser.swift`, `LinkParagraphView.swift`,
`LinkTitles.swift` and `StreamVeil.swift` as the homes of the link grammar. The bare-domain
regex and the TLD table are in **neither**. They live in
`DroppyCode/UI/Markdown/MarkdownView.swift`, inside `enum RichLink` (declared at line 562,
runs to line 833).

Evidence (`grep -rn` over all `*.swift` in the checkout):

- `bareDomainPattern` - defined `MarkdownView.swift:751`, used only `MarkdownView.swift:779`
- `bareDomainTLDs` - defined `MarkdownView.swift:757`, used only `MarkdownView.swift:812`
- `autolinkedBareDomains` - defined `MarkdownView.swift:770`, used only `MarkdownView.swift:831`
- `namelessSegments` - defined `MarkdownView.swift:692`, used only `MarkdownView.swift:682`
- `isBareDisplay` - defined `MarkdownView.swift:737`, used only `MarkdownView.swift:607`
- `forgeReference` - defined `MarkdownView.swift:705`, used only `MarkdownView.swift:671`
- `prettyTitle` - defined `MarkdownView.swift:670`, used only `MarkdownView.swift:608`

`MarkdownParser.swift` (338 lines, read in full) contains **zero** link handling. It is
block-level only: `MarkdownBlock` cases `heading/paragraph/code/list/quote/table/rule`, plus
the streaming-reuse `Parse(blocks:stable:restart:)`. Its own doc comment says "Inline styling
is handled by `AttributedString`" (line 3). It is listed here only to record that it holds
nothing this pass was asked for.

`LinkParagraphView.swift` (550 lines, read in full) is the AppKit renderer, not the grammar:
it calls `RichLink.prettyAttributed` (line 106) and paints the result.

Line counts reconciled with `wc -l`: MarkdownParser.swift 338, LinkParagraphView.swift 550,
LinkTitles.swift 112, StreamVeil.swift 431, MarkdownView.swift 1499. All read in the ranges
cited; MarkdownView.swift was read in the ranges 200-330, 556-940, 940-1112, 1120-1260 (the
ranges containing every symbol reachable from the link grammar).

---

## 1. The pipeline, in order

**SOURCE FACT.** `MarkdownView.swift:826-832`, verbatim:

```swift
    private static func baseAttributed(_ source: String) -> AttributedString {
        let options = AttributedString.MarkdownParsingOptions(
            interpretedSyntax: .inlineOnlyPreservingWhitespace,
            failurePolicy: .returnPartiallyParsedIfPossible
        )
        return autolinkedBareDomains((try? AttributedString(markdown: source, options: options)) ?? AttributedString(source))
    }
```

Order, and it matters:

1. Foundation's `AttributedString(markdown:options:)` runs first, with
   `interpretedSyntax: .inlineOnlyPreservingWhitespace` and
   `failurePolicy: .returnPartiallyParsedIfPossible`. This is what produces `[title](url)`
   links, `<https://...>` autolinks, `` `code` `` runs, emphasis and strong emphasis. On a
   total parse failure the raw source becomes a plain `AttributedString`.
2. `autolinkedBareDomains` then runs **over that output**, adding links to bare domains that
   Foundation left alone.
3. `RichLink.build` (line 596) walks the result and rewrites each linked run's *display text*
   (`prettyTitle`) when the display was a bare URL, and sets `.stronglyEmphasized` on every
   link run.

**SOURCE FACT.** `build`, `MarkdownView.swift:596-621`, verbatim:

```swift
    nonisolated static func build(_ source: String) -> AttributedString {
        let base = baseAttributed(source)
        var result = AttributedString()
        for run in base.runs {
            let slice = AttributedString(base[run.range])
            guard let url = run.link else {
                result.append(slice)
                continue
            }
            let display = String(slice.characters)
            var linkSlice: AttributedString
            if isBareDisplay(display, url: url) {
                linkSlice = AttributedString(prettyTitle(for: url))
                linkSlice.link = url
            } else {
                linkSlice = slice
            }
            // Every link renders bold, whether the author titled it or the
            // URL was bare. Other intent bits (italic, code) are preserved.
            var intent = run.inlinePresentationIntent ?? []
            intent.insert(.stronglyEmphasized)
            linkSlice.inlinePresentationIntent = intent
            result.append(linkSlice)
        }
        return result
    }
```

**SOURCE FACT.** Every link run gets `.stronglyEmphasized` inserted; other intent bits
(`.emphasized`, `.code`) are preserved. When the display was bare, the slice is **replaced**
by `AttributedString(prettyTitle(for: url))`, so the original attributes of that slice are
dropped and only `link` plus the merged `intent` survive.

---

## 2. The bare-domain regex, verbatim

**SOURCE FACT.** `MarkdownView.swift:750-753`, verbatim:

```swift
    /// A bare domain as people type it in chat: getdroppy.app, www.droppy.app/pricing?a=1.
    private static let bareDomainPattern = try? NSRegularExpression(
        pattern: "(?<![A-Za-z0-9@._/-])((?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\\.)+[A-Za-z]{2,24})(/[^\\s<>()]*)?",
        options: [.caseInsensitive])
```

The Swift *source literal*, character for character (verified with `od -c` on line 752):

```
"(?<![A-Za-z0-9@._/-])((?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\\.)+[A-Za-z]{2,24})(/[^\\s<>()]*)?"
```

The **actual regex string** handed to `NSRegularExpression` (after Swift unescapes `\\` to
`\`), which is what a port must reproduce:

```
(?<![A-Za-z0-9@._/-])((?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24})(/[^\s<>()]*)?
```

Options: `[.caseInsensitive]` - exactly one option, nothing else. `try?`: a compile failure
yields `nil` and bare-domain autolinking is silently skipped for every paragraph
(`MarkdownView.swift:779-782`). **SOURCE FACT:** the pattern is valid ICU, so that branch is
not reached in practice; it is not dead code, it is a nil-guard.

Piece by piece (**SOURCE FACT**, reading the pattern):

- `(?<![A-Za-z0-9@._/-])` - negative lookbehind. The character immediately before the match
  must not be an ASCII letter, an ASCII digit, `@`, `.`, `_`, `/` or `-`. Inside the
  character class the `-` is last, so it is a literal hyphen, not a range.
- Group 1 `((?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24})` - one or more
  labels each followed by a literal dot, then a final all-alphabetic label of **2 to 24**
  characters. A label is a single alphanumeric, or an alphanumeric, then any run of
  alphanumerics-and-hyphens, then an alphanumeric (so no leading or trailing hyphen).
- Group 2 `(/[^\s<>()]*)?` - optional path/query: a literal `/` followed by any run of
  characters that are not whitespace, `<`, `>`, `(` or `)`.

**ENGINEERING DECISION for a port.** `.caseInsensitive` is redundant against these character
classes (they already span both cases), but it is present and should be carried over so the
pattern object is identical. In JavaScript the equivalent is the `i` flag; the `\s` class
differs slightly between ICU and JS. For exact parity on the path group, port `[^\s<>()]` as
an explicit negated set rather than relying on the host engine's `\s`. **UNRESOLVED:** whether
any real chat text contains a non-ASCII space inside a URL path such that the two engines
disagree.

---

## 3. The TLD table, complete and counted

**SOURCE FACT.** `MarkdownView.swift:755-766`. **111 entries, all distinct** (counted with
`grep -o '"[a-z]*"' | wc -l` = 111; `sort -u | wc -l` = 111; `uniq -d` empty). Per source
line: 758 -> 14, 759 -> 13, 760 -> 12, 761 -> 11, 762 -> 12, 763 -> 17, 764 -> 17, 765 -> 15;
14+13+12+11+12+17+17+15 = **111**.

Verbatim, as the source lays them out:

```swift
    /// TLDs a chat linkifier is sure of. Curated, not every TLD: a file name
    /// (MarkdownView.swift, index.html, package.json) has a bare domain's shape.
    private static let bareDomainTLDs: Set<String> = [
        "com", "org", "net", "edu", "gov", "mil", "info", "biz", "io", "ai", "app", "dev", "co", "me",
        "xyz", "site", "online", "store", "shop", "tech", "cloud", "tools", "blog", "fm", "gg", "so", "tv",
        "live", "news", "studio", "design", "social", "works", "codes", "run", "page", "link", "chat", "bot",
        "art", "fun", "space", "world", "email", "group", "team", "digital", "agency", "solutions", "software",
        "systems", "network", "media", "photo", "video", "games", "zone", "city", "life", "one", "plus", "pro",
        "nl", "de", "be", "uk", "fr", "es", "it", "se", "no", "dk", "fi", "is", "ch", "at", "pl", "ie", "eu",
        "ca", "us", "au", "nz", "jp", "cn", "in", "br", "mx", "za", "pt", "gr", "cz", "ro", "hu", "sk", "kr",
        "sg", "hk", "tw", "th", "my", "id", "ph", "vn", "tr", "il", "ae", "sa", "cl", "ar", "pe"
    ]
```

The 111 elements as a flat numbered list, in source order:

1. `com` 2. `org` 3. `net` 4. `edu` 5. `gov` 6. `mil` 7. `info` 8. `biz` 9. `io` 10. `ai`
11. `app` 12. `dev` 13. `co` 14. `me` 15. `xyz` 16. `site` 17. `online` 18. `store`
19. `shop` 20. `tech` 21. `cloud` 22. `tools` 23. `blog` 24. `fm` 25. `gg` 26. `so`
27. `tv` 28. `live` 29. `news` 30. `studio` 31. `design` 32. `social` 33. `works`
34. `codes` 35. `run` 36. `page` 37. `link` 38. `chat` 39. `bot` 40. `art` 41. `fun`
42. `space` 43. `world` 44. `email` 45. `group` 46. `team` 47. `digital` 48. `agency`
49. `solutions` 50. `software` 51. `systems` 52. `network` 53. `media` 54. `photo`
55. `video` 56. `games` 57. `zone` 58. `city` 59. `life` 60. `one` 61. `plus` 62. `pro`
63. `nl` 64. `de` 65. `be` 66. `uk` 67. `fr` 68. `es` 69. `it` 70. `se` 71. `no` 72. `dk`
73. `fi` 74. `is` 75. `ch` 76. `at` 77. `pl` 78. `ie` 79. `eu` 80. `ca` 81. `us` 82. `au`
83. `nz` 84. `jp` 85. `cn` 86. `in` 87. `br` 88. `mx` 89. `za` 90. `pt` 91. `gr` 92. `cz`
93. `ro` 94. `hu` 95. `sk` 96. `kr` 97. `sg` 98. `hk` 99. `tw` 100. `th` 101. `my`
102. `id` 103. `ph` 104. `vn` 105. `tr` 106. `il` 107. `ae` 108. `sa` 109. `cl` 110. `ar`
111. `pe`

It is a `Set<String>`, so source order is not semantic - but the 111 elements are.

Notable absences a port must **not** silently add (**SOURCE FACT**, by inspection): `swift`,
`html`, `json`, `md`, `txt`, `js`, `ts`, `py`, `rs`, `go`, `sh`, `yml`, `yaml`, `toml`,
`lock`, `png`, `svg`, `log`, `cfg`, `ini`, `xml`, `css`. The doc comment names exactly why:
`MarkdownView.swift`, `index.html`, `package.json`.

Notable presences that collide with real file extensions (**SOURCE FACT**, an upstream
consequence to reproduce, not to fix): `app` (`Foo.app`), `so` (`libssl.so` matches and
links), `in` (`config.in`), `pro` (`project.pro`), `one`, `link`, `page`, `run`, `dev`, `io`,
`me`, `co`, `is`, `it`, `at`, `no`, `my`, `id`.

---

## 4. `autolinkedBareDomains` - what becomes a link, verbatim

**SOURCE FACT.** `MarkdownView.swift:768-824`, verbatim:

```swift
    /// Links the bare domains Foundation leaves alone: `getdroppy.app` typed in chat
    /// reads as a link with its favicon, the same as a full URL does.
    private static func autolinkedBareDomains(_ parsed: AttributedString) -> AttributedString {
        var out = AttributedString()
        for run in parsed.runs {
            let slice = parsed[run.range]
            let text = String(slice.characters)
            if run.link != nil || (run.inlinePresentationIntent ?? []).contains(.code) {
                out.append(AttributedString(slice))
                continue
            }
            guard let pattern = bareDomainPattern else {
                out.append(AttributedString(slice))
                continue
            }
            let ns = text as NSString
            let matches = pattern.matches(in: text, options: [], range: NSRange(location: 0, length: ns.length))
            if matches.isEmpty {
                out.append(AttributedString(slice))
                continue
            }
            // Offsets are Characters throughout: the regex reports UTF-16 ranges, and
            // `Range(_:in:)` turns one back into the text's own indices, so a sentence with
            // an emoji or an accent before the domain still links the domain itself.
            let trimSet = ".,;:!?'\""
            var cursor = 0
            func piece(_ span: Range<Int>) -> AttributedString {
                let lo = slice.characters.index(slice.characters.startIndex, offsetBy: span.lowerBound)
                let hi = slice.characters.index(slice.characters.startIndex, offsetBy: span.upperBound)
                return AttributedString(slice[lo..<hi])
            }
            for match in matches {
                guard let range = Range(match.range, in: text) else { continue }
                let start = text.distance(from: text.startIndex, to: range.lowerBound)
                guard start >= cursor else { continue }
                var matchText = String(text[range])
                // Trailing punctuation belongs to the sentence, not the link.
                while let last = matchText.last, trimSet.contains(last) { matchText.removeLast() }
                let end = start + matchText.count
                if start > cursor { out.append(piece(cursor..<start)) }
                // Split at the first `/`: domain part vs the rest of the path/query.
                let slash = matchText.firstIndex(of: "/")
                let domainPart = slash.map { String(matchText[..<$0]) } ?? matchText
                let lastLabel = domainPart.split(separator: ".").last.map(String.init)?.lowercased() ?? ""
                if bareDomainTLDs.contains(lastLabel), let url = URL(string: "https://" + matchText) {
                    var linkSlice = piece(start..<end)
                    linkSlice.link = url
                    out.append(linkSlice)
                } else if end > cursor {
                    out.append(piece(start..<end))
                }
                cursor = end
            }
            if cursor < text.count { out.append(piece(cursor..<text.count)) }
        }
        return out
    }
```

### 4.1 Exclusions, in evaluation order

**SOURCE FACT**, `MarkdownView.swift:775`:

1. A run that already has a `link` attribute is copied through untouched. Foundation's own
   `[title](url)` and `<url>` links are therefore never re-scanned.
2. A run whose `inlinePresentationIntent` contains `.code` is copied through untouched.
   Inline `` `getdroppy.app` `` does **not** linkify.
3. Nothing else is excluded by attribute. Emphasis and strong-emphasis runs **are** scanned.

**ENGINEERING DECISION / ordering dependency to reproduce:** because scanning is per-run and
runs are split by attribute boundaries, a domain that straddles an attribute change (for
example `getdroppy.**app**`) is split across two runs and neither run matches the whole
domain. That is the upstream behaviour.

### 4.2 The trailing-punctuation trim set

**SOURCE FACT**, `MarkdownView.swift:792`. Swift literal `".,;:!?'\""`. The actual 8
characters, in order:

| # | char | codepoint |
|---|------|-----------|
| 1 | `.` | U+002E |
| 2 | `,` | U+002C |
| 3 | `;` | U+003B |
| 4 | `:` | U+003A |
| 5 | `!` | U+0021 |
| 6 | `?` | U+003F |
| 7 | `'` | U+0027 |
| 8 | `"` | U+0022 |

Trimming is a `while` loop from the end, so `example.com?!.` trims all three trailing
characters. **This set is different from the one in `isBareDisplay`** (section 6) - see the
gotchas.

The regex's path group already forbids `(`, `)`, `<`, `>` and whitespace, so those never
reach the trim loop. `]`, `}` and `)` are **not** in this trim set. Without a path a match
ends at the TLD, so `see example.com]` links only `example.com`. With a path,
`example.com/a]` links `example.com/a]` **including** the bracket. **Upstream defect to
reproduce, not to fix.**

### 4.3 The TLD decision

**SOURCE FACT**, `MarkdownView.swift:809-812`:

- Split `matchText` at the **first** `/`; everything before it is `domainPart`.
- `lastLabel` = the **last** dot-separated component of `domainPart`, lowercased; `""` when
  there is none.
- Link only if `bareDomainTLDs.contains(lastLabel)` **and**
  `URL(string: "https://" + matchText)` is non-nil.
- The scheme is always `https://`. There is no `http://` path and no scheme sniffing.
- The URL is built from `matchText` **after** punctuation trimming, so the link target and
  the link text are the same string.

### 4.4 Cursor and overlap rules

**SOURCE FACT**, `MarkdownView.swift:800-821`:

- `guard let range = Range(match.range, in: text) else { continue }` - a UTF-16 range that
  does not map to Character indices is skipped entirely (the run's `cursor` is not advanced).
- `guard start >= cursor else { continue }` - a match starting before the cursor is skipped.
  **UNRESOLVED:** whether this guard is reachable at all, given `NSRegularExpression.matches`
  returns non-overlapping matches and trimming only shortens. Port it verbatim regardless.
- Text between `cursor` and `start` is emitted as a plain slice (`if start > cursor`).
- A non-TLD match is re-emitted as plain text (`else if end > cursor`), so no characters are
  lost.
- After the loop, `if cursor < text.count { out.append(piece(cursor..<text.count)) }` emits
  the tail, which is where trimmed trailing punctuation comes back as prose.
- All offsets are **Character** counts, not UTF-16 and not bytes. `piece(_:)` indexes
  `slice.characters` by `offsetBy:`. The doc comment at lines 789-791 states the reason.

**ENGINEERING DECISION for a port.** In JavaScript, `String.prototype.matchAll` gives UTF-16
indices and JS strings are UTF-16, so a naive port is already in the wrong units for
astral-plane characters and combining marks. To reproduce Swift's behaviour exactly, convert
to an array of grapheme clusters (`Intl.Segmenter` with `granularity: 'grapheme'`) and index
by cluster, matching `text.distance(from:to:)` and `matchText.count`.

---

## 5. `prettyTitle` - how a bare link's display text is resolved

**SOURCE FACT.** `MarkdownView.swift:666-689`, verbatim:

```swift
    /// Short display title for a bare URL. Forge links collapse to their
    /// native reference (`!3190`, `#123`); a video link reads as the video's
    /// own title once it has been fetched; everything else keeps the host
    /// plus the last path segment, so a deep path never spills into chat.
    static func prettyTitle(for url: URL) -> String {
        if let reference = forgeReference(for: url) { return reference }
        if let title = LinkTitles.cached(for: url) { return capped(title, at: 48) }
        guard var host = url.host?.lowercased(), !host.isEmpty else {
            return compactFallback(url.absoluteString)
        }
        if host.hasPrefix("www.") { host.removeFirst(4) }
        let segments = (url.path.removingPercentEncoding ?? url.path)
            .split(separator: "/")
            .map(String.init)
            .filter { !$0.isEmpty }
        guard let last = segments.last else { return host }
        if namelessSegments.contains(last.lowercased()) { return host }
        let tail = last.count > 24 ? String(last.prefix(21)) + "\u{2026}" : last
        let title = segments.count == 1 ? host + "/" + tail : host + "/\u{2026}/" + tail
        if title.count > 40 {
            return String(title.prefix(19)) + "\u{2026}" + String(title.suffix(18))
        }
        return title
    }
```

(The three `\u{2026}` above are written in the source as the literal character `…`
U+2026 HORIZONTAL ELLIPSIS, one character, not three dots. Escapes are used here only so the
codepoint is unambiguous.)

**Resolution order (SOURCE FACT, strict):**

1. `forgeReference(for: url)` - forge issue/MR/PR reference. Wins over everything, including
   a fetched video title.
2. `LinkTitles.cached(for: url)` - a YouTube title already fetched, run through
   `capped(_:at: 48)`.
3. No host, or empty host -> `compactFallback(url.absoluteString)`.
4. Strip a leading `"www."` (exactly 4 characters removed) from the lowercased host.
5. Path segments = percent-decoded path (falling back to the raw path if decoding returns
   nil), split on `/`, empties filtered out.
6. No segments -> the bare host.
7. Last segment (lowercased) in `namelessSegments` -> the bare host.
8. `tail` = last segment, truncated to `prefix(21) + "…"` when its Character count exceeds
   **24**.
9. `title` = `host + "/" + tail` when there is exactly **1** segment, otherwise
   `host + "/…/" + tail`.
10. If `title.count > 40`: `String(title.prefix(19)) + "…" + String(title.suffix(18))`
    (19 + 1 + 18 = 38 characters out).
11. Otherwise `title`.

### 5.1 `namelessSegments` - 8 entries

**SOURCE FACT.** `MarkdownView.swift:691-694`, verbatim, **8 entries** (counted: 8):

```swift
    /// Path tails that carry no name of their own, so a URL ending in one shows its host alone.
    private static let namelessSegments: Set<String> = [
        "watch", "view", "index", "index.html", "index.php", "home", "default", "page"
    ]
```

The 8: `watch`, `view`, `index`, `index.html`, `index.php`, `home`, `default`, `page`.
Compared **lowercased**.

### 5.2 `capped`

**SOURCE FACT.** `MarkdownView.swift:696-700`, verbatim:

```swift
    /// Titles past the limit keep their head and end in a single ellipsis.
    private static func capped(_ title: String, at limit: Int) -> String {
        guard title.count > limit else { return title }
        return String(title.prefix(limit - 3)) + "…"
    }
```

Called only as `capped(title, at: 48)`, so: titles of 48 characters or fewer pass through;
longer ones become `prefix(45) + "…"` = **46 characters**. Note `limit - 3` with a
**one**-character ellipsis - the result is 46, not 48. **Upstream quirk; reproduce it.**

### 5.3 `forgeReference`

**SOURCE FACT.** `MarkdownView.swift:702-723`, verbatim:

```swift
    /// `!3190` for merge requests, `#123` for issues and pull requests.
    /// Matches GitLab (`/-/merge_requests/`, `/-/issues/`) and GitHub-style
    /// (`/pull/`, `/issues/`) paths on any host.
    static func forgeReference(for url: URL) -> String? {
        let segments = url.path.split(separator: "/").map(String.init)
        // GitLab nests the kind behind /-/: /group/project/-/merge_requests/3190
        if let dash = segments.firstIndex(of: "-"),
           dash + 2 < segments.count,
           let id = Int(segments[dash + 2]) {
            switch segments[dash + 1] {
            case "merge_requests": return "!\(id)"
            case "issues": return "#\(id)"
            default: break
            }
        }
        // GitHub style: /owner/repo/pull/123 or /owner/repo/issues/123
        if segments.count >= 2, let id = Int(segments.last ?? "") {
            let kind = segments[segments.count - 2]
            if kind == "pull" || kind == "issues" { return "#\(id)" }
        }
        return nil
    }
```

Exact rules (**SOURCE FACT**):

- Path split on `/` (empties dropped by `split`'s default `omittingEmptySubsequences: true`).
- **GitLab branch:** `segments.firstIndex(of: "-")` - the *first* literal `-` segment.
  Requires `dash + 2 < segments.count` and `segments[dash + 2]` parsing as `Int`. Then
  `segments[dash + 1]` switches: `"merge_requests"` -> `"!\(id)"`, `"issues"` -> `"#\(id)"`,
  anything else falls through to the GitHub branch.
- **GitHub branch:** `segments.count >= 2`, last segment parses as `Int`, and the
  second-to-last is exactly `"pull"` or `"issues"` -> `"#\(id)"`.
- The host is **never** checked. Any host matching these shapes collapses.
- The id is round-tripped through `Int`, so `/pull/007` renders `#7`. **Upstream defect to
  reproduce.**
- Prefix characters are exactly `!` (U+0021) and `#` (U+0023), with no space.

### 5.4 `compactFallback`

**SOURCE FACT.** `MarkdownView.swift:725-730`, verbatim:

```swift
    /// Hostless URLs (mailto:, custom schemes) have no host to lean on, so
    /// they only get the length cap.
    static func compactFallback(_ absolute: String) -> String {
        guard absolute.count > 40 else { return absolute }
        return String(absolute.prefix(19)) + "…" + String(absolute.suffix(18))
    }
```

Same 19 / ellipsis / 18 shape as step 10 of `prettyTitle`.

---

## 6. `isBareDisplay` - when the display text is replaced

**SOURCE FACT.** `MarkdownView.swift:737-748`, verbatim:

```swift
    static func isBareDisplay(_ display: String, url: URL) -> Bool {
        var d = display.trimmingCharacters(in: .whitespacesAndNewlines)
        if d.hasPrefix("<"), d.hasSuffix(">"), d.count >= 2 {
            d = String(d.dropFirst().dropLast())
        }
        while let last = d.last, ".,;:!?)]}>".contains(last) { d.removeLast() }
        if d == url.absoluteString { return true }
        if (d.hasPrefix("http://") || d.hasPrefix("https://")) && !d.contains(" ") && !d.contains("\n") {
            return true
        }
        return false
    }
```

Rules (**SOURCE FACT**):

1. Trim `.whitespacesAndNewlines` from both ends.
2. If wrapped in `<` ... `>` and at least 2 characters long, strip one character from each end.
3. Trim trailing characters from the set `".,;:!?)]}>"` - **10 characters**, in order:
   `.` `,` `;` `:` `!` `?` `)` `]` `}` `>`.
4. `true` if the result equals `url.absoluteString` exactly.
5. `true` if the result starts with `http://` or `https://` **and** contains no space
   (U+0020) and no newline (U+000A).
6. Otherwise `false`.

**GOTCHA (load-bearing).** This trim set (10 chars, includes `)` `]` `}` `>`, excludes `'`
and `"`) is **not** the trim set used in `autolinkedBareDomains` (8 chars, includes `'` and
`"`, excludes `)` `]` `}` `>`). Two different sets, two different call sites. A port that
shares one constant between them is wrong.

**Consequence for bare-domain autolinks (SOURCE FACT).** A bare domain autolinked in
section 4 has display `getdroppy.app` and URL `https://getdroppy.app`. `isBareDisplay`
returns `false` (the display is not the absolute string, and does not start with `https://`),
so `build` keeps the display text as typed - `getdroppy.app` stays `getdroppy.app`, it is
**not** rewritten by `prettyTitle`. Only Foundation-parsed bare `https://...` autolinks get
the `prettyTitle` treatment. **This is the observable upstream behaviour; reproduce it.**

---

## 7. `LinkTitles` - video title resolution, complete

`LinkTitles.swift` is 112 lines and was read in full.

### 7.1 `videoID(for:)`

**SOURCE FACT.** `LinkTitles.swift:10-30`, verbatim:

```swift
    /// The video id a link carries, or nil for anything that is not a video link:
    /// `youtube.com/watch?v=…`, `youtu.be/…`, `youtube.com/shorts/…`, `/embed/…`, `/live/…`.
    static func videoID(for url: URL) -> String? {
        guard let host = url.host?.lowercased() else { return nil }
        let bare = host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
        let segments = url.path.split(separator: "/").map(String.init)
        switch bare {
        case "youtu.be":
            guard let id = segments.first else { return nil }
            return checked(id)
        case "youtube.com", "m.youtube.com", "music.youtube.com", "youtube-nocookie.com":
            if segments.count >= 2, ["shorts", "embed", "live", "v"].contains(segments[0]) {
                return checked(segments[1])
            }
            let query = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems
            guard let value = query?.first(where: { $0.name == "v" })?.value else { return nil }
            return checked(value)
        default:
            return nil
        }
    }
```

- Host set after stripping a leading `www.`: **5 accepted hosts total** - `youtu.be`, plus the
  4-element case list `"youtube.com"`, `"m.youtube.com"`, `"music.youtube.com"`,
  `"youtube-nocookie.com"` (counted: 4 in the case, 1 in the `youtu.be` case).
- Path-prefix segment set: **4 elements**, in this literal order - `"shorts"`, `"embed"`,
  `"live"`, `"v"`.
- `youtu.be` takes the **first** path segment.
- Otherwise: if there are at least 2 segments and the first is one of the 4, take segment 1;
  else read the **first** query item named exactly `"v"` (case-sensitive) via
  `URLComponents(url:resolvingAgainstBaseURL: false)`.
- `m.youtube.com` and `music.youtube.com` are matched *after* `www.` stripping, so
  `www.m.youtube.com` becomes `m.youtube.com` and matches.

### 7.2 `checked`

**SOURCE FACT.** `LinkTitles.swift:70-76`, verbatim:

```swift
    /// A video id is long enough to be one and made of the characters YouTube uses; anything
    /// else (a bare `/watch`, a playlist link) is no video to ask a title for.
    private static func checked(_ value: String) -> String? {
        guard value.count >= 8,
              value.allSatisfy({ $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }) else { return nil }
        return value
    }
```

Minimum length **8** Characters. Allowed: `Character.isLetter` (Unicode letter, not just
ASCII), `Character.isNumber` (Unicode number, not just ASCII digits), `-`, `_`. **Upstream
quirk to reproduce:** `isLetter`/`isNumber` are Unicode-wide, so a Cyrillic or Arabic-digit
"id" of 8 or more characters passes.

### 7.3 `title(forVideo:)` and the oEmbed call

**SOURCE FACT.** `LinkTitles.swift:39-68`, verbatim:

```swift
    static func title(forVideo id: String) async -> String? {
        if let known = Store.shared.title(id) { return known }
        guard Store.shared.claim(id) else { return nil }
        guard let url = oembedURL(for: id),
              let (data, response) = try? await URLSession.shared.data(from: url),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let payload = try? JSONDecoder().decode(OEmbed.self, from: data) else {
            Store.shared.finish(id, title: nil)
            return nil
        }
        let title = payload.title.trimmingCharacters(in: .whitespacesAndNewlines)
        Store.shared.finish(id, title: title.isEmpty ? nil : title)
        return title.isEmpty ? nil : title
    }

    private struct OEmbed: Decodable {
        let title: String
    }

    private static func oembedURL(for id: String) -> URL? {
        var components = URLComponents(string: "https://www.youtube.com/oembed")
        components?.queryItems = [
            URLQueryItem(name: "url", value: "https://www.youtube.com/watch?v=\(id)"),
            URLQueryItem(name: "format", value: "json"),
        ]
        return components?.url
    }
```

**Exact network contract (SOURCE FACT):**

- Base URL string: `https://www.youtube.com/oembed`
- Query items, in this order: `url` = `https://www.youtube.com/watch?v=<id>`, then
  `format` = `json`
- Method: GET (`URLSession.shared.data(from:)`)
- Accepted status: **exactly 200**, nothing else in 2xx.
- Response JSON field read: **`title`** only. The struct is
  `private struct OEmbed: Decodable { let title: String }`.
- The title is trimmed of `.whitespacesAndNewlines`; an empty result is stored and returned
  as `nil`.
- On any failure (bad URL, transport error, non-200, decode failure) the id is marked
  finished with `nil`, so it is **never asked again for the process lifetime**.

### 7.4 The title store

**SOURCE FACT.** `LinkTitles.swift:78-111`, verbatim:

```swift
    private final class Store: @unchecked Sendable {
        static let shared = Store()

        private let lock = NSLock()
        private var titles: [String: String] = [:]
        private var claimed = Set<String>()

        func title(_ id: String) -> String? {
            lock.lock()
            defer { lock.unlock() }
            return titles[id]
        }

        /// Whether this caller is the one that should do the fetch.
        func claim(_ id: String) -> Bool {
            lock.lock()
            defer { lock.unlock() }
            return claimed.insert(id).inserted
        }

        /// The answer, title or none.
        func finish(_ id: String, title: String?) {
            lock.lock()
            defer { lock.unlock() }
            if let title {
                if titles.count >= 400 { titles.removeAll(keepingCapacity: true) }
                titles[id] = title
            }
            claimed.insert(id)
        }
    }
```

- Capacity literal: **400**. At 400 or more entries the whole map is **cleared**
  (`removeAll(keepingCapacity: true)`), not evicted LRU.
- **Upstream defect to reproduce:** after a clear, `titles` is empty but `claimed` still holds
  every id, so those videos are **never re-fetched** and their links fall back to host/path
  form permanently.
- `claim` is one-shot per id: a streaming row that restarts its task each token asks once.
- Not `@MainActor`; guarded by an `NSLock`, because `RichLink.build` runs off the main actor
  during warm-up (`MarkdownView.swift:238`, `@concurrent parseWarm`).

### 7.5 When titles are fetched

**SOURCE FACT.** `MarkdownView.swift:1222-1236`, verbatim:

```swift
    static func fetchLinkAssets(source: String, streaming: Bool, revision: Binding<Int>) async {
        let hosts = await MainActor.run { RichLink.linkHosts(for: source, streaming: streaming) }
        var changed = false
        for host in hosts where FaviconCache.cached(host: host) == nil {
            if await FaviconCache.image(for: host) != nil { changed = true }
        }
        let videos = await MainActor.run { RichLink.videoIDs(for: source, streaming: streaming) }
        for id in videos {
            if await LinkTitles.title(forVideo: id) != nil { changed = true }
        }
        if changed { await MainActor.run { RichLink.invalidate(source); revision.wrappedValue += 1 } }
    }
```

Order: **favicons first, sequentially, then video titles, sequentially.** Not concurrent.
The task is keyed by `RichLink.linkAssetKey(for:streaming:)` (`MarkdownView.swift:1144`,
`.task(id: hostKey)` at line 1195), which is (`MarkdownView.swift:649-657`):

```swift
    static func linkAssetKey(for source: String, streaming: Bool = false) -> String {
        let hosts = linkHosts(for: source, streaming: streaming)
        let videos = videoIDs(for: source, streaming: streaming)
        guard !(hosts.isEmpty && videos.isEmpty) else { return "" }
        return hosts.joined(separator: ",") + "|" + videos.joined(separator: ",")
    }
```

Exact key format: `hosts.joined(",") + "|" + videos.joined(",")`, or the empty string when
both are empty.

---

## 8. The pretty-string caches (parity-relevant state)

**SOURCE FACT.** `MarkdownView.swift:563-594`:

- `prettyCache = RecentCache<String, AttributedString>(limit: 1200)` - `@MainActor`.
- `streamingPretty: [(source: String, value: AttributedString)]` with
  `streamingSlots = 4` - a 4-slot FIFO holding the paragraphs still streaming, so a flush
  never evicts the shared cache.
- `prettyAttributed(_:streaming:)`: when `streaming`, look up the **last** matching slot, else
  build; if the array is already 4 long, `removeFirst()` before appending. When not
  streaming, look in `prettyCache` first; on a miss, **move** a matching streaming slot out
  (`remove(at:)`) rather than rebuilding, then insert into `prettyCache`.
- `warm(_:with:)` inserts only if `!prettyCache.contains(source)`.
- `invalidate(_:)` removes from `prettyCache` and removes **all** matching streaming slots.

**SOURCE FACT.** `MarkdownView.swift:221-235`, warm-up limits: `remainingBytes` starts at
`1_048_576`; a text is skipped if its `utf8.prefix(262_145).count` exceeds `262_144` or the
remaining budget; at most **24** texts per warm pass (`if missing.count == 24 { break }`);
candidates are taken from `texts.prefix(warmCandidateLimit)`.

---

## 9. Favicons attached to link runs

**SOURCE FACT.** `LinkParagraphView.swift:133-147`, verbatim:

```swift
            if let url = run.link {
                if let host = url.host?.lowercased(),
                   let icon = FaviconCache.cached(host: host) {
                    let attachment = NSTextAttachment()
                    attachment.image = icon
                    attachment.bounds = NSRect(x: 0, y: base.descender + 1, width: 14, height: 14)
                    out.append(NSAttributedString(attachment: attachment))
                    out.append(NSAttributedString(string: " ", attributes: [
                        .font: base,
                        .foregroundColor: textColor,
                    ]))
                }
                // Colour and underline come from the view's `linkTextAttributes`.
                out.append(NSAttributedString(string: string, attributes: [
                    .font: font,
                    .link: url,
                ]))
            }
```

- The icon is drawn **only** when already in the cache; it is never fetched from here.
- Attachment bounds: `x: 0`, `y: base.descender + 1`, `width: 14`, `height: 14`.
- One literal `" "` (single U+0020) between the icon and the link text, in the **base** font
  and the paragraph's text colour.
- Link runs carry only `.font` and `.link`; colour and underline come from
  `linkTextAttributes` set per render (`LinkParagraphView.swift:483-487`):
  `.foregroundColor: Chrome.accentNSColor`, `.underlineStyle: 0`,
  `.cursor: NSCursor.pointingHand`. **Underline style is literally `0` - links are not
  underlined.**

**SOURCE FACT.** Favicon sources, in order (`MarkdownView.swift:939-951`):

1. `https://icons.duckduckgo.com/ip3/<host>.ico` - accepted only on an HTTP status in
   `200...299` **and** `NSImage(data:)` non-nil.
2. `https://www.google.com/s2/favicons?domain=<host>&sz=64` - accepted on any response that
   returns data (no status check).
3. Otherwise nil.

Cache limits (`MarkdownView.swift:930-935`): `memory` cleared wholesale when
`memory.count >= 300`; failures recorded in a `failed` set that is never cleared;
`inFlight` dedupes concurrent asks.

**SOURCE FACT.** `FaviconCache.isPublic(host:)`, `MarkdownView.swift:1048-1080`. Hosts never
asked about:

- empty, or exactly `"localhost"`
- containing `":"` (an IPv6 literal)
- ending in one of `privateSuffixes` - **11 entries**, verbatim and counted:
  `".local"`, `".localhost"`, `".localdomain"`, `".internal"`, `".intranet"`, `".lan"`,
  `".home"`, `".home.arpa"`, `".test"`, `".invalid"`, `".corp"`
- containing no `"."` at all
- a dotted quad of 4 labels that all parse as `Int` in `0...255`, whose first two octets fall
  in: `(0, _)`, `(10, _)`, `(127, _)`, `(169, 254)`, `(192, 168)`, `(172, 16...31)`,
  `(100, 64...127)` - **7 rejected ranges**. Everything else returns `true`.

Icon post-processing (`MarkdownView.swift:953-1096`): downscale so the long side is at most
**64**; `removeWhitePlate` flood-fills from the border treating a pixel as white when
`alpha > 200 && min(r,g,b) >= 236 && max-min <= 20`, then feathers neighbours with
`min(r,g,b) >= 160` into alpha `1 - min/255`, dropping anything with `alpha <= 0.02`; the
final raster is **28x28** presented at **14x14** points.

---

## 10. Which renderer a paragraph gets

**SOURCE FACT.** `MarkdownView.swift:307-324`, verbatim:

```swift
        case .paragraph(let text):
            if RichLink.containsLinks(in: text, streaming: streaming) {
                InlineText(text, hasLinks: true)
            } else if !streaming, listDepth == 0, mentionsTarget(in: text) {
                InlineText(text, hasLinks: true)
            } else if listDepth == 0, let segments = mentionSegments(in: text) {
                VeiledText(segments: segments)
                    .modifier(MarkdownBlockSelection())
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                InlineText(text, hasLinks: false)
            }
```

`containsLinks` (`MarkdownView.swift:732-735`):
`prettyAttributed(source, streaming: streaming).runs.contains { $0.link != nil }`.

Inside `InlineText` (`MarkdownView.swift:1131-1133`):
`let hosts = RichLink.linkHosts(...)`; `let hasLinkText = hasLinks ?? !hosts.isEmpty`.
`true` -> the AppKit `LinkParagraphView`; `false` -> a SwiftUI `VeiledText`. So **a paragraph
with any link renders through AppKit and takes the `LinkTextVeil` path; a link-free paragraph
renders through SwiftUI `Text` and takes the `VeiledText` path.**

`linkHosts` (`MarkdownView.swift:623-634`) and `videoIDs` (`MarkdownView.swift:636-647`) both
return first-seen order, de-duplicated by a `Set`; hosts are lowercased and empties dropped.

---

## 11. StreamVeil - the streaming-reveal rules and every timing constant

`StreamVeil.swift` is 431 lines and was read in full.

### 11.1 The constants, verbatim

**SOURCE FACT.** `StreamVeil.swift:40-44`:

```swift
    static let shortestFade: TimeInterval = 0.12
    static let longestFade: TimeInterval = 0.40
    private static let curve = 1.6
    private static let cadenceSeed: TimeInterval = 0.16
    private static let gapCeiling: TimeInterval = 1.0
```

| Name | Value | Unit | Visibility |
|---|---|---|---|
| `shortestFade` | `0.12` | seconds | internal `static let` |
| `longestFade` | `0.40` | seconds | internal `static let` |
| `curve` | `1.6` | exponent | `private static let` |
| `cadenceSeed` | `0.16` | seconds | `private static let` |
| `gapCeiling` | `1.0` | seconds | `private static let` |

**SOURCE FACT.** `shortestFade` and `longestFade` are internal but referenced **only** at
`StreamVeil.swift:79` - a `grep -rn` over the whole checkout finds no other use. They are not
dead (line 79 uses them) but nothing outside this file reads them.

Further literals in the same machine:

| Where | Literal | Meaning |
|---|---|---|
| `StreamVeil.swift:76` | `0.7` and `0.3` | EMA weights: `cadence = cadence * 0.7 + gap * 0.3` |
| `StreamVeil.swift:79` | `3` | `cadence * 3` before clamping |
| `StreamVeil.swift:131` | `1 + 0.3 * Double(max(0, active - 2))` | the backlog boost |
| `StreamVeil.swift:178` | `CAFrameRateRange(minimum: 24, maximum: 60, preferred: 30)` | SwiftUI veil clock |

### 11.2 The rules, verbatim

**SOURCE FACT.** `StreamVeil.swift:52-85`:

```swift
    func advance(_ text: String, at now: Date) {
        let characters = Array(text)
        guard let previous else {
            self.previous = characters
            return
        }
        if characters != previous {
            let prefix = Self.commonPrefix(previous, characters)
            chunks = chunks.compactMap { chunk in
                let upper = min(chunk.range.upperBound, prefix)
                guard upper > chunk.range.lowerBound else { return nil }
                var kept = chunk
                kept.range = chunk.range.lowerBound..<upper
                return kept
            }
            if characters.count > prefix {
                if let lastAppend {
                    let gap = min(now.timeIntervalSince(lastAppend), Self.gapCeiling)
                    cadence = cadence * 0.7 + gap * 0.3
                }
                lastAppend = now
                let duration = min(max(cadence * 3, Self.shortestFade), Self.longestFade)
                chunks.append(Chunk(range: prefix..<characters.count, started: now, duration: duration))
            }
            self.previous = characters
        }
        prune(at: now)
    }
```

```swift
    func spans(at now: Date) -> [Span] {
        prune(at: now)
        let boost = Self.boost(chunks.count)
        return chunks.map { chunk in
            let progress = min(1, max(0, now.timeIntervalSince(chunk.started) * boost / chunk.duration))
            return Span(range: chunk.range, alpha: 1 - pow(1 - progress, Self.curve))
        }
    }
```

```swift
    private func prune(at now: Date) {
        let boost = Self.boost(chunks.count)
        chunks.removeAll { now.timeIntervalSince($0.started) * boost >= $0.duration }
    }

    /// A backed-up stream (three or more chunks fading at once) dissolves faster.
    private static func boost(_ active: Int) -> Double {
        1 + 0.3 * Double(max(0, active - 2))
    }

    private static func commonPrefix(_ a: [Character], _ b: [Character]) -> Int {
        let limit = min(a.count, b.count)
        var index = 0
        while index < limit, a[index] == b[index] { index += 1 }
        return index
    }
```

```swift
    var settlesAt: Date? {
        let boost = Self.boost(chunks.count)
        return chunks.map { $0.started.addingTimeInterval($0.duration / boost) }.max()
    }

    func reset() {
        previous = nil
        chunks = []
        lastAppend = nil
        cadence = Self.cadenceSeed
    }

    func step(_ text: @autoclosure () -> String, active: Bool, at now: Date) -> Bool {
        if active { advance(text(), at: now) }
        return isFading(at: now)
    }

    func isFading(at now: Date) -> Bool {
        prune(at: now)
        return !chunks.isEmpty
    }
```

**The exact computations (SOURCE FACT):**

- `cadence` starts at `cadenceSeed` = `0.16` s and is reset to it by `reset()`.
- On each append with a known `lastAppend`: `gap = min(now - lastAppend, 1.0)`, then
  `cadence = cadence * 0.7 + gap * 0.3`. The **first** append after attach has no
  `lastAppend`, so the cadence is not updated for it, but `lastAppend` is set.
- `duration = min(max(cadence * 3, 0.12), 0.40)` - fixed **at arrival**, never revised.
- `boost(active) = 1 + 0.3 * max(0, active - 2)`, computed from the **current** chunk count:
  0, 1 or 2 chunks -> `1.0`; 3 -> `1.3`; 4 -> `1.6`; 5 -> `1.9`; n -> `1 + 0.3(n-2)`.
- `progress = clamp((now - chunk.started) * boost / chunk.duration, 0, 1)`.
- `alpha = 1 - pow(1 - progress, 1.6)`. The text is drawn at opacity `alpha`; the doc comment
  (line 16) phrases it "The dissolve eases as (1 - p)^1.6, so the text shows through at
  1 - veil".
- A chunk is pruned when `(now - started) * boost >= duration`.
- `settlesAt` = the max over chunks of `started + duration / boost`.
- All offsets are **Characters** of the plain text handed in (`Array(text)`), matching the
  units used in `autolinkedBareDomains`.

**The attach/rewrite rules (SOURCE FACT):**

- The **first** `advance` adopts the text as the baseline and registers **no** chunk, so text
  already on screen (a row scrolled back, a thread switched back mid-reply) never fades.
- On a change: `prefix = commonPrefix(previous, characters)`. Existing chunks are clipped to
  `lowerBound..<min(upperBound, prefix)` and **dropped entirely** when that would be empty or
  inverted (the comment at lines 63-65 says a range with upper below lower traps).
- A chunk is registered only when `characters.count > prefix`, with range
  `prefix..<characters.count`.
- `advance` is idempotent for unchanged text (`if characters != previous`), so a SwiftUI
  `body` may call it on every evaluation.
- `step` only feeds text while `active`; once `active` goes false the in-flight chunks finish
  and nothing new is registered.

**UPSTREAM DEFECT to reproduce, not to fix.** `boost` is recomputed from the live chunk count
on every frame, but each chunk's `duration` is frozen at arrival. When the backlog drains (say
4 chunks -> 2), `boost` falls from `1.6` to `1.0`, so `progress` for a surviving chunk
**decreases** and its text becomes *less* opaque than the frame before. The ramp can run
backwards. Reproduce the formula exactly.

**UPSTREAM QUIRK.** `prune` computes `boost` from `chunks.count` *before* removing anything,
while `spans` prunes first and then computes `boost` from the post-prune count. The two can
therefore disagree within one call sequence. Port the call order verbatim.

### 11.3 The SwiftUI clock

**SOURCE FACT.** `StreamVeil.swift:161-194`:

- `VeilClock.shared`, `@MainActor @Observable`, holds `now = Date.now` and one `CADisplayLink`.
- `run(until:)`: `deadline = max(deadline ?? date, date)`; starts the link only if
  `link == nil` and `NSScreen.main` exists.
- `link.preferredFrameRateRange = CAFrameRateRange(minimum: 24, maximum: 60, preferred: 30)` -
  **24 / 60 / 30**, added to `.main` in `.common` mode.
- `tick()` returns early - and does **not** advance `now` - while
  `ScrollActivity.shared.isScrolling`. Otherwise `now = .now`; once `now >= deadline` the link
  is invalidated and both `link` and `deadline` cleared.

**SOURCE FACT.** `VeiledText.body` (`StreamVeil.swift:273-279`): calls
`veil.step(source.characters, active: veiled, at: .now)`; a truthy result renders
`fadingText()`, which calls `clock.run(until: settlesAt)`, reads `clock.now` purely to
register the observation, and then paints `text(at: .now)` - the **real** time, so the first
frame after a flush is never stale (comment at lines 281-283).

`settledText()` (lines 294-307): `if !veiled { veil.reset() }` - a finished paragraph drops its
baseline; a still-streaming one keeps it so a quiet beat does not make the next flush arrive
unveiled.

`text(at:)` (lines 309-338): for each span with `alpha < 1`, the range is
`span.range.clamped(to: 0..<count)` over the styled string's **characters**, and
`styled[lower..<upper].foregroundColor = base.opacity(span.alpha)` where
`base = dimmed ? Color.secondary : Color.primary`.

### 11.4 The AppKit veil (`LinkTextVeil`)

**SOURCE FACT.** `StreamVeil.swift:344-431`. Same `StreamVeil` instance semantics, painted as
**temporary attributes** on the layout manager so no glyph and no line changes:

- `update(_:active:)` feeds `view.string`; when fading it paints and starts a
  `view.displayLink(target:selector:)` added to `.main` in `.common` mode. **No
  `preferredFrameRateRange` is set here** - unlike `VeilClock`, this link runs at the display's
  own rate.
- `tick()` also bails while `ScrollActivity.shared.isScrolling`.
- `paint`: `clear(view)` first, then for each span with `alpha < 1`, the range is clamped to
  `0..<string.count` (Characters), converted with `NSRange(lower..<upper, in: string)`, then
  `storage.enumerateAttributes(in:)` per sub-run: the colour is `Chrome.accentNSColor` when the
  run has a `.link` attribute, else `attributes[.foregroundColor] as? NSColor ?? .labelColor`,
  applied as
  `layout.addTemporaryAttribute(.foregroundColor, value: color.withAlphaComponent(span.alpha), forCharacterRange: runRange)`.
- `painted` tracks the union of painted ranges; `clear` removes the temporary attribute over
  `NSIntersectionRange(painted, 0..<storage.length)` and resets `painted`.
- `settle(_:keepText:)` invalidates the link, clears, and calls `veil.reset()` only when
  `keepText` is false. `detach` calls `settle(view, keepText: false)`.
- `LinkParagraphView.updateNSView` calls `view.streamVeil.update(view, active: veiled)`
  **after** `view.render(...)` - the comment at `LinkParagraphView.swift:56` says "After the
  text is in place: the veil reads what the storage holds now." `dismantleNSView` calls
  `view.streamVeil.detach(view)`.

---

## 12. Gotchas a porter will get wrong

1. **The grammar is in `MarkdownView.swift`, not in any of the four named files.** Anyone
   porting from the named file list will ship no link grammar at all.
2. **Two different trailing-punctuation trim sets.** `autolinkedBareDomains` uses 8
   characters `.,;:!?'"`; `isBareDisplay` uses 10 characters `.,;:!?)]}>`. They are not the
   same set and must not be unified.
3. **Bare domains keep their typed text.** `isBareDisplay` returns false for
   `getdroppy.app`, so `prettyTitle` is applied only to Foundation's `https://...` autolinks,
   never to the bare-domain autolinks this pass's regex creates.
4. **All offsets are grapheme clusters**, not UTF-16 and not bytes, in both
   `autolinkedBareDomains` and `StreamVeil`. A JS port indexing UTF-16 will misplace links
   and fades after any emoji.
5. **Order is load-bearing:** Foundation markdown parse -> bare-domain autolink over its runs
   -> `build`'s display rewrite and bold. Runs that are already links or `.code` are skipped
   in step two, so a domain inside inline code never links, and a domain split by an emphasis
   boundary never links.
6. **`prettyTitle` resolution is a strict cascade** and `forgeReference` wins over a fetched
   video title.
7. **`capped(_:at: 48)` produces 46 characters, not 48** (`limit - 3` with a one-character
   ellipsis).
8. **`forgeReference` round-trips the id through `Int`**, so `/pull/007` renders `#7`.
9. **`LinkTitles.Store` clears `titles` wholesale at 400 but keeps `claimed`**, so cleared
   videos are never re-fetched.
10. **`FaviconCache.memory` clears wholesale at 300**, and `failed` is never cleared.
11. **The two veil clocks differ**: the SwiftUI one is pinned to 24/60/30 fps, the AppKit one
    is not pinned at all.
12. **`boost` is retroactive**, so the fade ramp can run backwards when the backlog drains.
13. **Links are never underlined** (`.underlineStyle: 0`) and are always bold
    (`.stronglyEmphasized` inserted on every link run).
14. **The favicon is drawn only from the cache**; the first render of a new host has no icon
    and the paragraph is rebuilt later through the `revision` bump.
15. **`MarkdownParser.swift` has no link code at all** - do not look for the grammar there, and
    do not add it there in the port.
