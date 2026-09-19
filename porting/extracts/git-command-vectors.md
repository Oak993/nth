# Git command argument vectors and checkpoint shape (verbatim extraction)

Pinned reference: `E:/Droppy/reference/droppy-code`, commit `dd2b86efef9e77168f304a86e8a8411877578bbe`. Read-only; nothing under it was modified.

Files read COMPLETELY for this pass (line counts reconciled with `wc -l`):

| File | `wc -l` | Ranges read |
| --- | --- | --- |
| `DroppyCode/Services/Git/Git.swift` | 723 | 1-180, 180-400, 400-600, 600-723 |
| `DroppyCode/Services/Git/TurnDiff.swift` | 65 | 1-65 |
| `DroppyCode/Services/Git/TouchedPaths.swift` | 60 | 1-60 |
| `DroppyCode/Services/Git/WorkingTreeWatch.swift` | 353 | 1-190, 190-353 |
| `DroppyCode/Services/Git/DiffParser.swift` | 167 | 1-167 |
| `DroppyCode/Services/Git/RevertPreview.swift` | 42 | 1-42 |
| `DroppyCode/Services/Git/FileChangeSummary+Diff.swift` | 19 | 1-19 |

Supporting files read for the identity split and the phase literals (the answers are not inside the named set):

| File | Why | Lines read |
| --- | --- | --- |
| `DroppyCode/Core/Support/Shell.swift` (458 lines) | `Shell.run(_:_:…)`, `Shell.run(tool:…)`, `ShellResult`, `ShellError`, `LoginEnvironment` | 1-145, 325-458 |
| `DroppyCode/Core/Models/MergeRequestLink.swift` (62 lines) | `MergeRequestLink.Forge` cases | 1-62 |
| `DroppyCode/App/Runtime/ThreadRuntime.swift` | the only two `Git.checkpointRef` call sites | 1215-1260, 2270-2295 |

---

## 0. Adjudication of the critic's four charges against R036

### 0.1 "an `add -A` timeout" — critic is RIGHT, R036 is wrong (SOURCE FACT)

R036 `feature_notes[0]` states, verbatim:

> "Every git invocation is prefixed with `-c core.quotepath=false -c color.ui=false` before the caller's arguments; default timeout 120 s, 300 s for push, fetch, pull, worktree add, the forge CLIs, `add -A` and diff."

There is no single "`add -A` timeout". `git add -A` is invoked at **six** distinct sites with **two** different timeouts:

| Line | Vector (caller arguments only) | Timeout |
| --- | --- | --- |
| `Git.swift:164` | `["add", "-A"]` | **120 s (default, not passed)** |
| `Git.swift:253` | `["add", "-A", "--ignore-errors", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 300 s |
| `Git.swift:448` | `["add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 300 s |
| `Git.swift:459` | `["add", "-A", "--", "."]` | 300 s |
| `Git.swift:484` | `["add", "-A", "--", "."]` | 300 s |
| `Git.swift:707` | `["add", "-A", "--", "."]` | 300 s |

`Git.swift:163-166` is `commitAll(message:)` and passes no `timeout:`, so it takes `run`'s default of `120`:

```swift
    func commitAll(message: String) async throws {
        try Self.check(await run(["add", "-A"]))
        try Self.check(await run(["commit", "-F", "-"], input: Data(message.utf8)))
    }
```

The same R036 sentence is also wrong about two more subcommands:

- **diff**: only `diff(from:to:binary:paths:)` (`Git.swift:558`, `Git.swift:566`) uses 300 s. Three other `git diff` invocations run at 120 s: `Git.swift:158` (`["diff", "HEAD", "--no-ext-diff"]`), `Git.swift:300` (`["diff", "--name-only", "-z", from, to]`), `Git.swift:586` (`["diff", "--numstat", "-z", "--no-renames", "--no-ext-diff", base, end, "--"] + paths`).
- **worktree add**: `addWorktree(at:branch:base:)` (`Git.swift:124`) runs at **120 s**; only `addDetachedWorktree(at:commit:)` (`Git.swift:679`) runs at 300 s.

The correct rule is in §2: the timeout belongs to the call site, never to the subcommand.

### 0.2 "an unreachable error string presented as shipped UI text" — critic is RIGHT, R036 is wrong (SOURCE FACT)

R036 `ui_elements[1]` states, verbatim:

> "Error text surfaced to the user: \"The request could not be merged.\" and \"git hash-object answered for <n> of <m> files.\""

`"The request could not be merged."` is **unreachable**. `Git.swift:207-226`:

```swift
    func mergePullRequest(_ link: MergeRequestLink) async throws {
        let number = String(link.number)
        var lastFailure = "The request could not be merged."
        for attempt in 0..<6 {
            let result: ShellResult
            switch link.forge {
            case .gitlab: result = try await Shell.run(tool: "glab", ["mr", "merge", number, "--yes"], in: directory, timeout: 300)
            case .github: result = try await Shell.run(tool: "gh", ["pr", "merge", number, "--merge"], in: directory, timeout: 300)
            case .gitea: result = try await Shell.run(tool: "tea", ["pulls", "merge", number], in: directory, timeout: 300)
            }
            if result.succeeded { return }
            lastFailure = result.failureMessage
            let text = (result.output + result.errorOutput).lowercased()
            guard link.forge == .gitlab, text.contains("405") || text.contains("not allowed") || text.contains("checking") else { break }
            try? await Task.sleep(for: .seconds(3 + attempt * 2))
        }
        throw ShellError(lastFailure)
    }
```

Proof: `0..<6` is non-empty, so the body runs at least once. The only ways out of the body are (a) `return` on success, (b) an error thrown by `Shell.run` itself, which propagates and never reaches `throw ShellError(lastFailure)`, or (c) falling through to `lastFailure = result.failureMessage`, which overwrites the initializer before any `break` or loop exit. Every value ever passed to `throw ShellError(lastFailure)` is therefore a `ShellResult.failureMessage`. The literal is dead initializer text, not shipped UI text.

`"git hash-object answered for \(hashes.count) of \(existing.count) files."` (`Git.swift:647`) **is** reachable and is genuine user-facing error text: `git hash-object --stdin-paths` reads newline-separated paths and `workingBlobs` joins absolute paths with `"\n"` (`Git.swift:643`), so a path containing a newline, or a short reply, produces the mismatch. Port it verbatim.

ENGINEERING DECISION for the port: reproduce `"The request could not be merged."` as the initializer value (it is source text and costs nothing), but do not list it as a user-visible string and do not localize it.

### 0.3 "omits the exact argument vectors of six shipped commands" — right in substance (SOURCE FACT)

R036's `feature_notes` never give the vector for at least these seven sites: `isRepository` (`rev-parse --is-inside-work-tree`), `hasCommits` (`rev-parse --verify --quiet HEAD`), `treeHash` (`rev-parse <ref>^{tree}`), `commitHash` (`rev-parse --verify <ref>`), `commitWork` (`commit-tree <tree> -p <parent> -F -`), `isAncestor` (`merge-base --is-ancestor <a> <b>`), `changedPaths` (`diff --name-only -z <from> <to>`). §3 lists **all 72** invocation sites exhaustively, which supersedes the question of how many were missing.

### 0.4 "leaves the parentless checkpoint, the two phase literals and the git/forge identity split undocumented" — right on all three (SOURCE FACT)

R036 describes `captureCheckpoint` but never states that the commit has **no parent**; it names `checkpointRef(thread:turn:phase:)` but never gives the two `phase` literals (they are not in `Git.swift` at all); and it lists `Shell.run(tool:)` under `calls_into` without recording that forge CLIs take a different executable resolution, a different environment and **no `-c` prefix**. §4, §5 and §6 settle all three.

---

## 1. How every git invocation is assembled

`Git` is a `Sendable` struct with exactly one stored property (`Git.swift:20-26`):

```swift
struct Git: Sendable {
    let directory: URL

    init(_ path: String) {
        directory = URL(fileURLWithPath: path)
    }
```

`Git.swift:28`: `private static var executable: URL { LoginEnvironment.git }`

`Git.swift:29-47` is the single funnel for every git process:

```swift
    @discardableResult
    func run(
        _ arguments: [String],
        environment extra: [String: String] = [:],
        input: Data? = nil,
        timeout: TimeInterval = 120
    ) async throws -> ShellResult {
        var environment = LoginEnvironment.current
        environment["GIT_TERMINAL_PROMPT"] = "0"
        environment["GIT_OPTIONAL_LOCKS"] = "0"
        environment.merge(extra) { _, new in new }
        return try await Shell.run(
            Self.executable,
            ["-c", "core.quotepath=false", "-c", "color.ui=false"] + arguments,
            in: directory,
            environment: environment,
            input: input,
            timeout: timeout
        )
    }
```

**SOURCE FACT — the invariant prefix.** Every git argv is exactly

```
[<LoginEnvironment.git path>, "-c", "core.quotepath=false", "-c", "color.ui=false"] + <caller arguments>
```

Four prefix elements in that order, always, with no conditional inclusion.

**SOURCE FACT — the invariant environment.** `LoginEnvironment.current`, then `GIT_TERMINAL_PROMPT="0"`, then `GIT_OPTIONAL_LOCKS="0"`, then `extra` merged in with `extra` winning every collision (`{ _, new in new }`). `extra` is used for exactly two things in this file: `["GIT_INDEX_FILE": <scratch index path>]` and `Git.identity` (§4.3).

**SOURCE FACT — the working directory.** Always `directory`, the `Git` instance's own path, passed as `in: directory`. It is **not** resolved to the repository root. Construction sites: `Git(thread.worktreePath ?? project.path)` (`ThreadRuntime.swift:1224-1225`, `:1776`, `:3361`), `Git(project.path)` (`AppModel.swift:947`, `:1118`), `Git(checkout)` / `Git(copy)` (`AppModel+Hydra.swift:490`, `:722`, `:783`, `:930`), `Git(item.directory)` (`AppModel.swift:984`), `Git(root)` (`ThreadRuntime.swift:1977`, `:2278`).

`Git.swift:49-57` — the two wrappers above `run`:

```swift
    func output(_ arguments: [String]) async throws -> String {
        let result = try await run(arguments)
        try Self.check(result)
        return result.output
    }

    static func check(_ result: ShellResult) throws {
        if !result.succeeded { throw ShellError(result.failureMessage) }
    }
```

**Ordering dependency (SOURCE FACT):** `output(_:)` takes no `timeout:` and no `environment:` parameter, so **every call routed through `output(_:)` runs at the 120 s default with no extra environment.** 16 of the 66 git sites go through `output(_:)`.

`ShellResult.failureMessage` (`Shell.swift:14-19`):

```swift
    var failureMessage: String {
        let error = errorOutput.trimmingCharacters(in: .whitespacesAndNewlines)
        let message = error.isEmpty ? trimmedOutput : error
        return message.isEmpty ? "The command failed with exit code \(status)." : message
    }
```

`"The command failed with exit code \(status)."` is the real fallback error string the UI shows, and it lives in `Shell.swift`, not `Git.swift`.

---

## 2. Timeouts: the exact rule

**SOURCE FACT.** `run`'s default is `timeout: TimeInterval = 120`. A timeout belongs to the **call site**, never to the git subcommand. The complete set of git call sites passing an explicit timeout is **thirteen**, and every one of them passes `300`:

`Git.swift:169`, `:253`, `:286`, `:290`, `:404`, `:448`, `:459`, `:484`, `:558`, `:566`, `:679`, `:707`. No other git site passes a timeout, so the other **53** git sites run at 120 s.

The **six** forge-CLI sites (`Git.swift:181`, `:186`, `:191`, `:216`, `:217`, `:218`) each pass `timeout: 300` explicitly.

Enforcement (`Shell.swift:80-85`, `:107-115`): a `Task` sleeps `timeout` seconds then calls `terminate(process)`, which sends `SIGTERM` and, one second later, `SIGKILL` to the process group when `getpgid(pid) == pid`, else to the pid. A timed-out process therefore yields a non-zero `ShellResult`, **not** a thrown timeout error. After the process ends, output collection gets a further hard-coded `await collector.finished(within: 3)` (`Shell.swift:103`).

---

## 3. Every invocation, in file order: 72 sites (66 git, 6 forge CLI)

Working directory for every row is `Git.directory`. "Vector" is the caller argument array only; prepend `["-c", "core.quotepath=false", "-c", "color.ui=false"]` for every git row (never for a forge-CLI row). "TO" is the timeout in seconds.

| # | Line | Function | Vector (exact, ordered) | TO | stdin | Extra env |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 64 | `isRepository()` | `["rev-parse", "--is-inside-work-tree"]` | 120 | - | - |
| 2 | 68 | `repositoryRoot()` | `["rev-parse", "--show-prefix"]` | 120 | - | - |
| 3 | 77 | `hasCommits()` | `["rev-parse", "--verify", "--quiet", "HEAD"]` | 120 | - | - |
| 4 | 81 | `status()` | `["status", "--porcelain=v2", "--branch"]` | 120 | - | - |
| 5 | 102 | `branches()` | `["for-each-ref", "--sort=-committerdate", "--format=%(HEAD)%09%(refname:short)%09%(upstream:short)", "refs/heads"]` | 120 | - | - |
| 6 | 114 | `switchBranch(_:)` | `["switch", <name>]` | 120 | - | - |
| 7 | 118 | `createBranch(_:)` | `["switch", "-c", <name>]` | 120 | - | - |
| 8 | 121-124 | `addWorktree(at:branch:base:)` | `["worktree", "add", "-b", <branch>, <path>]`, then `+ [<base>]` when `base != nil` | 120 | - | - |
| 9 | 128 | `removeWorktree(at:)` | `["worktree", "remove", "--force", <path>]` | 120 | - | - |
| 10 | 132 | `remoteURL()` | `["remote", "get-url", "origin"]` | 120 | - | - |
| 11 | 150 | `listFiles()` | `["ls-files", "--cached", "--others", "--exclude-standard"]` | 120 | - | - |
| 12 | 157 | `workingChanges()` | `["status", "--short"]` | 120 | - | - |
| 13 | 158 | `workingChanges()` | `["diff", "HEAD", "--no-ext-diff"]` | 120 | - | - |
| 14 | 164 | `commitAll(message:)` | `["add", "-A"]` | 120 | - | - |
| 15 | 165 | `commitAll(message:)` | `["commit", "-F", "-"]` | 120 | `Data(message.utf8)` | - |
| 16 | 169 | `push()` | `["push", "-u", "origin", "HEAD"]` | 300 | - | - |
| 17 | 181 | `createPullRequest` .gitlab | **glab** `["mr", "create", "--title", <title>, "--description", <body>]` then `+ ["--source-branch", <source>]` if non-nil then `+ ["--target-branch", <target>]` if non-nil; note `"--yes"` sits in the literal before the optionals (see §6.2) | 300 | - | - |
| 18 | 186 | `createPullRequest` .github | **gh** `["pr", "create", "--title", <title>, "--body", <body>]` `+ ["--head", <source>]?` `+ ["--base", <target>]?` | 300 | - | - |
| 19 | 191 | `createPullRequest` .gitea | **tea** `["pulls", "create", "--title", <title>, "--description", <body>]` `+ ["--head", <source>]?` `+ ["--base", <target>]?` | 300 | - | - |
| 20 | 216 | `mergePullRequest` .gitlab | **glab** `["mr", "merge", <number>, "--yes"]` | 300 | - | - |
| 21 | 217 | `mergePullRequest` .github | **gh** `["pr", "merge", <number>, "--merge"]` | 300 | - | - |
| 22 | 218 | `mergePullRequest` .gitea | **tea** `["pulls", "merge", <number>]` | 300 | - | - |
| 23 | 234 | `defaultBranch()` | `["symbolic-ref", "--short", "refs/remotes/origin/HEAD"]` | 120 | - | - |
| 24 | 238 | `defaultBranch()` | `["rev-parse", "--verify", "--quiet", "refs/remotes/origin/\(candidate)"]` for `candidate` in `["main", "master"]`, in that order, stopping at the first success | 120 | - | - |
| 25 | 252 | `captureTree(paths:)` | `["read-tree", "HEAD"]`, only when `await hasCommits()` | 120 | - | `GIT_INDEX_FILE` |
| 26 | 253 | `captureTree(paths:)` | `["add", "-A", "--ignore-errors", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 300 | `nulSeparated(paths)` | `GIT_INDEX_FILE` |
| 27 | 254 | `captureTree(paths:)` | `["write-tree"]` | 120 | - | `GIT_INDEX_FILE` |
| 28 | 260 | `treeHash(of:)` | `["rev-parse", "\(ref)^{tree}"]` | 120 | - | - |
| 29 | 264 | `commitHash(of:)` | `["rev-parse", "--verify", <ref>]` (`ref` defaults to `"HEAD"`) | 120 | - | - |
| 30 | 269 | `commitWork(_:parent:message:)` | `["commit-tree", <tree>, "-p", <parent>, "-F", "-"]` | 120 | `Data(message.utf8)` | **none** (user's own identity) |
| 31 | 275 | `updateRef(_:to:)` | `["update-ref", <ref>, <commit>]` | 120 | - | - |
| 32 | 282 | `updateRef(_:to:expecting:)` | `["update-ref", <ref>, <commit>, <old ?? "">]` | 120 | - | - |
| 33 | 286 | `pushBranch(_:)` | `["push", "-u", "origin", "refs/heads/\(name):refs/heads/\(name)"]` | 300 | - | - |
| 34 | 290 | `fetch()` | `["fetch", "--quiet", "origin"]` | 300 | - | - |
| 35 | 294 | `isAncestor(_:of:)` | `["merge-base", "--is-ancestor", <commit>, <other>]` | 120 | - | - |
| 36 | 300 | `changedPaths(from:to:)` | `["diff", "--name-only", "-z", <from>, <to>]` | 120 | - | - |
| 37 | 305 | `dirtyPaths()` | `["status", "--porcelain=v1", "-z", "--untracked-files=all"]` | 120 | - | - |
| 38 | 323 | `ignoredPaths(among:)` | `["check-ignore", "-z", "--stdin"]` | 120 | `nulSeparated(paths)` | - |
| 39 | 347 | `presentPaths(among:in:)` | `["cat-file", "--batch-check=%(objecttype)", "-Z"]` | 120 | `"\(ref):\(path)" + 0x00` per path | - |
| 40 | 373 | `hasOperationInProgress()` | `["rev-parse", "--path-format=absolute", "--git-dir"]` | 120 | - | - |
| 41 | 383 | `resetSoft(to:)` | `["reset", "--soft", "--quiet", <commit>]` | 120 | - | - |
| 42 | 389 | `resetIndex(paths:)` | `["reset", "--quiet", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 120 | `nulSeparated(paths)` | - |
| 43 | 395 | `checkoutPaths(from:_:)` | `["checkout", "--quiet", <ref>, "--"] + paths` | 120 | - | - |
| 44 | 400 | `removePaths(_:)` | `["rm", "--quiet", "--"] + paths` | 120 | - | - |
| 45 | 404 | `pullFastForward()` | `["pull", "--ff-only", "--quiet"]` | 300 | - | - |
| 46 | 423 | `captureCheckpoint(_:)` | `["commit-tree", <tree>, "-m", "Droppy Code checkpoint"]` — **no `-p`** | 120 | - | `Git.identity` (4 vars) |
| 47 | 428 | `captureCheckpoint(_:)` | `["update-ref", <ref>, <commit>]` | 120 | - | - |
| 48 | 448 | `captureTree(scratch:paths:)` | `["add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 300 | `nulSeparated(paths)` | `GIT_INDEX_FILE` = scratch |
| 49 | 449 | `captureTree(scratch:paths:)` | `["write-tree"]` | 120 | - | `GIT_INDEX_FILE` = scratch |
| 50 | 457 | `captureTree(scratch:paths:)` | `["read-tree", "HEAD"]` (fallback branch, only when no real index file and `hasCommits()`) | 120 | - | `GIT_INDEX_FILE` = scratch |
| 51 | 459 | `captureTree(scratch:paths:)` | `["add", "-A", "--", "."]` | 300 | - | `GIT_INDEX_FILE` = scratch |
| 52 | 460 | `captureTree(scratch:paths:)` | `["write-tree"]` | 120 | - | `GIT_INDEX_FILE` = scratch |
| 53 | 482 | `captureTree()` | `["read-tree", "HEAD"]` (only when no real index file and `hasCommits()`) | 120 | - | `GIT_INDEX_FILE` = temp |
| 54 | 484 | `captureTree()` | `["add", "-A", "--", "."]` | 300 | - | `GIT_INDEX_FILE` = temp |
| 55 | 485 | `captureTree()` | `["write-tree"]` | 120 | - | `GIT_INDEX_FILE` = temp |
| 56 | 529 | `indexPath()` | `["rev-parse", "--path-format=absolute", "--git-path", "index"]` | 120 | - | - |
| 57 | 558 | `diff(from:to:binary:paths:)` | see §7.1, single-call branch | 300 | - | - |
| 58 | 566 | `diff(from:to:binary:paths:)` | see §7.1, chunked branch | 300 | - | - |
| 59 | 586 | `previewRestore(base:end:paths:)` | `["diff", "--numstat", "-z", "--no-renames", "--no-ext-diff", <base>, <end>, "--"] + paths` | 120 | - | - |
| 60 | 607-609 | `restore(_:from:)` | `["restore", "--source", <ref>, "--worktree", "--staged", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 120 | absolute paths `joined(separator: "\0")` — **no trailing NUL** | - |
| 61 | 615-617 | `restore(_:from:)` | `["rm", "-q", "--cached", "--ignore-unmatch", "--pathspec-from-file=-", "--pathspec-file-nul"]` | 120 | absolute paths `joined(separator: "\0")` — **no trailing NUL** | - |
| 62 | 625 | `blobs(in:at:root:)` | `["ls-tree", "-r", "-z", "--full-name", <ref>, "--"] + paths.map { root.appendingPathComponent($0).path }` | 120 | - | - |
| 63 | 643 | `workingBlobs(at:root:)` | `["hash-object", "--stdin-paths"]` | 120 | absolute paths `joined(separator: "\n")` — **no trailing newline** | - |
| 64 | 652 | `deleteCheckpoints(thread:)` | `["for-each-ref", "--format=%(refname)", <prefix>]` | 120 | - | - |
| 65 | 655 | `deleteCheckpoints(thread:)` | `["update-ref", "--stdin"]` | 120 | `"delete <ref>\n"` per ref, concatenated | - |
| 66 | 669-671 | `commitTree(_:message:)` | `["commit-tree", <tree>, "-m", <message>]`, then `+ ["-p", "HEAD"]` **only when `await hasCommits()`** | 120 | - | `Git.identity` (4 vars) |
| 67 | 679 | `addDetachedWorktree(at:commit:)` | `["worktree", "add", "--detach", <path>, <commit>]` | 300 | - | - |
| 68 | 684 | `pruneWorktrees()` | `["worktree", "prune"]` | 120 | - | - |
| 69 | 693 | `apply(_:)` | `["apply", "--binary", "--whitespace=nowarn", "-"]` | 120 | `Data(patch.utf8)` | - |
| 70 | 705 | `apply(_:)` | `["read-tree", "HEAD"]` (only when no real index file and `hasCommits()`) | 120 | - | `GIT_INDEX_FILE` = apply temp |
| 71 | 707 | `apply(_:)` | `["add", "-A", "--", "."]` | 300 | - | `GIT_INDEX_FILE` = apply temp |
| 72 | 708 | `apply(_:)` | `["apply", "--3way", "--binary", "--whitespace=nowarn", "-"]` | **120** | `Data(patch.utf8)` | `GIT_INDEX_FILE` = apply temp |

Counts: **72 invocation sites**, of which **66 are git** and **6 are forge CLI** (`#17-#22`). Sites at 300 s: **19** (13 git, 6 forge). Sites at 120 s: **53** (all git).

Note on row 72: the three-way apply, the most expensive of the two apply attempts, runs at the **120 s default**, while the `add -A` that prepares its index runs at 300 s. UPSTREAM DEFECT to reproduce, not to fix.

### 3.1 `nulSeparated` (the exact stdin encoding for pathspec files)

`Git.swift:353-358`:

```swift
    private static func nulSeparated(_ paths: [String]) -> Data {
        paths.reduce(into: Data()) { data, path in
            data.append(contentsOf: path.utf8)
            data.append(0)
        }
    }
```

**SOURCE FACT — trailing-NUL asymmetry.** `nulSeparated` appends a NUL **after every** path, so the payload ends with a NUL. Rows 60 and 61 (`restore`) do **not** use `nulSeparated`; they use `joined(separator: "\0")`, so those payloads have **no trailing NUL**, and they carry **absolute** paths while every `nulSeparated` caller passes the paths it was given. Row 63 uses `joined(separator: "\n")`, absolute, no trailing newline. Reproduce all three encodings exactly.

---

## 4. The checkpoint: ref namespace, commit shape, phase literals

### 4.1 Ref namespace (SOURCE FACT)

`Git.swift:409-411`:

```swift
    static func checkpointRef(thread: UUID, turn: Int, phase: String) -> String {
        "refs/droppy-code/checkpoints/\(thread.uuidString.lowercased())/\(turn)-\(phase)"
    }
```

Exact template: `refs/droppy-code/checkpoints/<uuid lowercased, hyphenated>/<turn>-<phase>`.

`UUID.uuidString` is the canonical 36-character hyphenated form in **upper** case; `.lowercased()` makes the whole thing lower case, hyphens kept. Example, literal shape: `refs/droppy-code/checkpoints/3f2ea2c1-9b4d-4a77-9a2e-77c0f2b9d001/0-start`.

Deletion prefix, `Git.swift:651`, built independently of `checkpointRef` but must agree with it:

```swift
        let prefix = "refs/droppy-code/checkpoints/\(thread.uuidString.lowercased())/"
```

Note the **trailing slash**, which `checkpointRef` does not produce; `for-each-ref` takes that as the pattern (row 64), then row 65 feeds `update-ref --stdin` one `"delete <ref>\n"` line per matched ref:

```swift
        guard let refs = try? await output(["for-each-ref", "--format=%(refname)", prefix]) else { return }
        let commands = refs.split(separator: "\n").map { "delete \($0)\n" }.joined()
        guard !commands.isEmpty else { return }
        _ = try? await run(["update-ref", "--stdin"], input: Data(commands.utf8))
```

(`Git.swift:649-656`) — `delete <ref>` is given with **no old-value field**, so it is an unconditional delete. Both calls are best-effort (`try?`), and `deleteCheckpoints` is `async` but not `throws`.

### 4.2 The commit is PARENTLESS (SOURCE FACT — the critic's charge is upheld)

`Git.swift:415-429`:

```swift
    func captureCheckpoint(_ ref: String) async throws {
        let tree = try await currentTree()
        let watch = await watch()
        let commit: String
        if let known = watch?.commit(of: tree) {
            commit = known
        } else {
            let result = try await run(["commit-tree", tree, "-m", "Droppy Code checkpoint"], environment: Self.identity)
            try Self.check(result)
            commit = result.trimmedOutput
            watch?.remember(commit: commit, of: tree)
        }
        try Self.check(await run(["update-ref", ref, commit]))
    }
```

There is **no `-p`** anywhere in this vector and no conditional that could add one. A checkpoint commit is therefore a **root commit with zero parents**, holding a single tree and nothing of the repository's history. Contrast the two other `commit-tree` sites in the same file:

| Site | Vector | Parents |
| --- | --- | --- |
| `captureCheckpoint` (`:423`) | `["commit-tree", tree, "-m", "Droppy Code checkpoint"]` | **0, always** |
| `commitWork` (`:269`) | `["commit-tree", tree, "-p", parent, "-F", "-"]` | exactly 1, always, named by the caller |
| `commitTree` (`:669-671`) | `["commit-tree", tree, "-m", message]` `+ ["-p", "HEAD"]` when `hasCommits()` | 1 when the repo has commits, else 0 |

Consequences a parity port must keep: a checkpoint ref is a garbage-collection anchor for one tree only; `git log <checkpointRef>` shows exactly one commit; diffing two checkpoints is a pure tree-to-tree diff with no merge base; and `deleteCheckpoints` removing the refs is what makes those trees collectible.

Message literal, verbatim and exact: `Droppy Code checkpoint` (no trailing newline, no period; `commit-tree -m` appends its own newline).

Reuse path: `watch?.commit(of: tree)` returns a previously made checkpoint commit **only when the watch's last snapshot is that same tree** (`WorkingTreeWatch.swift:334-339`), so a turn beginning on an unchanged tree costs the `update-ref` alone (row 47) and no `commit-tree`.

### 4.3 The checkpoint identity (SOURCE FACT)

`Git.swift:660-663`:

```swift
    private static let identity = [
        "GIT_AUTHOR_NAME": "Droppy Code", "GIT_AUTHOR_EMAIL": "droppy-code@localhost",
        "GIT_COMMITTER_NAME": "Droppy Code", "GIT_COMMITTER_EMAIL": "droppy-code@localhost",
    ]
```

Four keys, exactly these names and values. Applied at exactly **two** sites: `captureCheckpoint` (`:423`) and `commitTree` (`:671`). It is **not** applied to `commitWork` (`:269`) or to `commitAll` (`:165`) — those commit in the user's own configured identity. That split is deliberate and documented in the source comment at `Git.swift:266`: "A commit of `tree` on `parent` in the user's own name, for a branch to carry."

Note the placement oddity: `identity` is declared under the `// MARK: - Copies of the checkout` section (`Git.swift:658`), well below its first use in the `// MARK: - Checkpoints` section. That is a Swift type-scope reference, not an ordering bug; a port that needs declaration-before-use must hoist it.

### 4.4 The two phase literals (SOURCE FACT)

`phase` is a plain `String` parameter with no default and no enum. `Git.swift` never supplies a value. Across the whole repository there are exactly **two** call sites, both in `ThreadRuntime.swift`, and the literals are:

1. `"start"` — `ThreadRuntime.swift:1226`:
   ```swift
        let checkpointRef = Git.checkpointRef(thread: threadID, turn: turnIndex, phase: "start")
   ```
2. `"end"` — `ThreadRuntime.swift:2281`:
   ```swift
                let ref = Git.checkpointRef(thread: threadID, turn: turn.index, phase: "end")
   ```

That is the complete set: **2 literals, `"start"` and `"end"`.** No other string is ever passed.

Turn number source: the start ref uses `turnIndex` (the turn being opened); the end ref uses `turn.index` of the turn being finished.

Conditional inclusion and ordering (SOURCE FACT):

- The **start** checkpoint runs in a detached `Task` at `.userInitiated` priority, guarded by `await git.isRepository()`, concurrently with session start-up (`ThreadRuntime.swift:1237-1240`). Only if `captureCheckpoint` does not throw is `turn.baseCheckpoint` set to the ref (`ThreadRuntime.swift:1249-1251`). Both the checkpoint and the session start are awaited before any text reaches the model.
- The **end** checkpoint is attempted **only when `turn.baseCheckpoint != nil`** (`ThreadRuntime.swift:2280-2283`), and `endCheckpoint` is set only when it succeeds. So an `-end` ref never exists without its matching `-start` ref, but a `-start` ref can exist alone.
- Both use `try?`, so a failed checkpoint silently degrades the turn to "no diff", never an error.

---

## 5. Working-tree snapshot pipeline feeding the checkpoint

`captureCheckpoint` calls `currentTree()` (`Git.swift:433-439`):

```swift
    func currentTree() async throws -> String {
        guard let indexPath = await indexPath(), let watch = await watch() else { return try await captureTree() }
        return try await watch.currentTree(indexPath: indexPath) { scratch, paths in
            guard let scratch else { return try await captureTree() }
            return try await captureTree(scratch: scratch, paths: paths)
        }
    }
```

`watch()` (`Git.swift:463-466`) returns `WorkingTreeWatch.shared(for: directory.path, gitDirectory: (indexPath as NSString).deletingLastPathComponent)`.

`WorkingTreeWatch` is **macOS FSEvents** (`WorkingTreeWatch.swift:1`, `import CoreServices`) and is nil for any non-local volume (`WorkingTreeWatch.swift:91-93`, `.volumeIsLocalKey`). Exact literals and bounds it carries:

- scratch index path: `FileManager.default.temporaryDirectory.appendingPathComponent("droppy-code-index-\(UUID().uuidString)")` (`WorkingTreeWatch.swift:118`)
- dispatch queue label: `"droppy.working-tree-watch"`, QoS `.utility` (`WorkingTreeWatch.swift:119`)
- cookie file prefix: `<gitRoot>/droppy-code-sync-` followed by the decimal cookie number (`WorkingTreeWatch.swift:131`)
- `changedLimit = 64` (`WorkingTreeWatch.swift:78`); past it, changed directories collapse to their top-level component
- watch registry `limit = 32` directories, oldest evicted (`WorkingTreeWatch.swift:83`)
- cookie wait timeout: `try? await Task.sleep(for: .seconds(1))` (`WorkingTreeWatch.swift:259`); a cookie that never arrives tears the stream down for good
- FSEvents flags: `kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagNoDefer | kFSEventStreamCreateFlagFileEvents`, latency `0`, since `kFSEventStreamEventIdSinceNow` (`WorkingTreeWatch.swift:136-140`)
- lost-event mask: `kFSEventStreamEventFlagMustScanSubDirs | kFSEventStreamEventFlagKernelDropped | kFSEventStreamEventFlagUserDropped` (`WorkingTreeWatch.swift:176`)

PLATFORM NOTE (ENGINEERING DECISION territory for the Windows port, recorded here as a fact, not a decision): everything in `WorkingTreeWatch` is an optimisation. `Git.currentTree()` falls back to `captureTree()` whenever `indexPath()` or `watch()` is nil, and `captureTree()` alone produces the identical tree hash. A port may legitimately start with `watch == nil` everywhere and still be byte-identical in output, paying full `add -A` cost per checkpoint.

Temporary index file names, verbatim:

- `droppy-code-index-<UUID().uuidString>` — `Git.swift:249` (`captureTree(paths:)`), `Git.swift:472` (`captureTree()`), `WorkingTreeWatch.swift:118` (the shared scratch)
- `droppy-code-apply-<UUID().uuidString>` — `Git.swift:701` (`apply(_:)`)

All under `FileManager.default.temporaryDirectory`, each removed in a `defer` (`Git.swift:250`, `:473`, `:702`) except the watch's scratch, which is removed in `tearDown()` (`WorkingTreeWatch.swift:167`).

`indexPath()` fast path (`Git.swift:514-521`): when `<directory>/.git` **is a directory** and neither `GIT_DIR` nor `GIT_INDEX_FILE` is set in `LoginEnvironment.current`, it returns `dotGit + "/index"` with **no git process at all**. Otherwise it stamps the `.git` file and consults `Git.worktreeIndexPaths`, a `Mutex<[String: (stamp: GitFileStamp, path: String?)]>` that is wiped wholesale (`removeAll`) once it reaches **64** entries (`Git.swift:530-535`). `GitFileStamp` (`Git.swift:489-503`) is `(systemFileNumber as UInt64?, modificationDate, size, overrides)` where `overrides = (environment["GIT_DIR"] ?? "") + "\u{0}" + (environment["GIT_INDEX_FILE"] ?? "")`.

---

## 6. The git / forge-CLI identity split

### 6.1 Two different `Shell.run` overloads (SOURCE FACT)

| | git (`Git.run`) | forge CLI (`Shell.run(tool:)`) |
| --- | --- | --- |
| Overload | `Shell.run(_ executable: URL, _ arguments:, in:, environment:, input:, timeout:, outputLimit:)` (`Shell.swift:36-43`) | `Shell.run(tool name: String, _ arguments:, in:, environment:, input:, timeout:)` (`Shell.swift:121-127`) |
| Executable | `LoginEnvironment.git` | `LoginEnvironment.which(name)`; **throws `ShellError("\(name) was not found on your PATH.")` when nil** (`Shell.swift:128-130`) |
| Argument prefix | `["-c", "core.quotepath=false", "-c", "color.ui=false"]` always prepended | **none** |
| Environment | `LoginEnvironment.current` + `GIT_TERMINAL_PROMPT="0"` + `GIT_OPTIONAL_LOCKS="0"` + `extra` | `environment: nil` at every call site, so `Shell.run` uses `process.environment = environment ?? LoginEnvironment.current` (`Shell.swift:48`) — **neither `GIT_TERMINAL_PROMPT` nor `GIT_OPTIONAL_LOCKS` is set** |
| stdin | `input:` used at 8 sites | `input:` never passed; `process.standardInput = FileHandle.nullDevice` (`Shell.swift:56`) |
| Working directory | `in: directory` | `in: directory` (same value) |
| Timeout | per site, 120 or 300 | 300 at all six sites |
| `outputLimit` | never passed (nil, full output kept) | not a parameter of this overload |

**SOURCE FACT — the practical consequence.** A forge CLI that wants to prompt for credentials on a TTY is not suppressed by `GIT_TERMINAL_PROMPT=0`, because that variable is only set for git. The only thing stopping a prompt is `stdin` being `/dev/null`, which makes the CLI fail rather than hang — and the failure text (`failureMessage`) is what the user sees. UPSTREAM BEHAVIOUR to reproduce, not to fix.

`LoginEnvironment.git` resolution (`Shell.swift:394-410`):

```swift
    static var git: URL {
        resolvedGit.withLock { $0 } ?? which("git") ?? URL(fileURLWithPath: "/usr/bin/git")
    }

    private static let resolvedGit = Mutex<URL?>(nil)

    private static func developerGit(environment: [String: String]) async -> URL {
        let onPath = which("git", in: environment) ?? URL(fileURLWithPath: "/usr/bin/git")
        guard onPath.path == "/usr/bin/git",
              let result = try? await Shell.run(URL(fileURLWithPath: "/usr/bin/xcode-select"), ["-p"], environment: environment, timeout: 8),
              result.succeeded else { return onPath }
        let developer = result.trimmedOutput
        let candidate = URL(fileURLWithPath: developer).appendingPathComponent("usr/bin/git")
        guard !developer.isEmpty, FileManager.default.isExecutableFile(atPath: candidate.path) else { return onPath }
        return candidate
    }
```

So git is resolved **once**, at login-environment load, and may be the Xcode developer-directory binary rather than `/usr/bin/git`; the forge CLIs are resolved **per call** by a PATH walk in `which(_:in:)` (`Shell.swift:411-424`), which returns the first executable match walking `PATH.split(separator: ":")` in order, or, when the name contains `/`, the tilde-expanded path itself if executable.

`augmentedPath` appends exactly **12** directories, in this order, to whatever `PATH` says, skipping any already present (`Shell.swift:437-449`, the list itself at `:440-445`): `/opt/homebrew/bin`, `/opt/homebrew/sbin`, `/usr/local/bin`, `<home>/.local/bin`, `<home>/.bun/bin`, `<home>/.cargo/bin`, `<home>/.npm-global/bin`, `<home>/.volta/bin`, `/usr/bin`, `/bin`, `/usr/sbin`, `/sbin`. Counted: 12.

### 6.2 Forge selection and the three CLIs (SOURCE FACT)

`Git.swift:202-206`:

```swift
    func forge() async -> MergeRequestLink.Forge {
        let remote = (await remoteURL() ?? "").lowercased()
        if remote.contains("gitlab") { return .gitlab }
        if remote.contains("github") { return .github }
        return .gitea
    }
```

Three cases, in this priority order: substring `"gitlab"` wins over substring `"github"`, and everything else, including no remote at all, falls to `.gitea`. `MergeRequestLink.Forge` has exactly **three** cases: `.gitlab`, `.github`, `.gitea` (`MergeRequestLink.swift:6-11`), with the comment that `tea` serves Gitea, Forgejo and Codeberg alike.

CLI name literals, exactly **three**: `"glab"`, `"gh"`, `"tea"`.

`createPullRequest(title:body:source:target:)` in full (`Git.swift:174-198`):

```swift
    func createPullRequest(title: String, body: String, source: String? = nil, target: String? = nil) async throws -> URL? {
        let result: ShellResult
        switch await forge() {
        case .gitlab:
            var arguments = ["mr", "create", "--title", title, "--description", body, "--yes"]
            if let source { arguments += ["--source-branch", source] }
            if let target { arguments += ["--target-branch", target] }
            result = try await Shell.run(tool: "glab", arguments, in: directory, timeout: 300)
        case .github:
            var arguments = ["pr", "create", "--title", title, "--body", body]
            if let source { arguments += ["--head", source] }
            if let target { arguments += ["--base", target] }
            result = try await Shell.run(tool: "gh", arguments, in: directory, timeout: 300)
        case .gitea:
            var arguments = ["pulls", "create", "--title", title, "--description", body]
            if let source { arguments += ["--head", source] }
            if let target { arguments += ["--base", target] }
            result = try await Shell.run(tool: "tea", arguments, in: directory, timeout: 300)
        }
        try Self.check(result)
        let text = result.output + "\n" + result.errorOutput
        let match = text.firstMatch(of: #/https://\S+/#)
        return match.flatMap { URL(string: String($0.output)) }
    }
```

Exact ordered vectors (base literal first, then the two conditional pairs in `source`-then-`target` order):

- glab: `["mr", "create", "--title", <title>, "--description", <body>, "--yes"]` `+ ["--source-branch", <source>]?` `+ ["--target-branch", <target>]?` — **`--yes` sits at index 6, before the optionals**, which is the ordering a port must not "tidy".
- gh: `["pr", "create", "--title", <title>, "--body", <body>]` `+ ["--head", <source>]?` `+ ["--base", <target>]?` — note gh uses `--body`, the other two use `--description`; gh gets **no** `--yes`-equivalent.
- tea: `["pulls", "create", "--title", <title>, "--description", <body>]` `+ ["--head", <source>]?` `+ ["--base", <target>]?`

URL extraction: the regex is the Swift regex literal `#/https://\S+/#`, applied to `stdout + "\n" + stderr` (that order, that separator), and the **first** match is converted with `URL(string:)`; a failed conversion yields `nil`, not an error.

`mergePullRequest` retry arithmetic, exactly (`Git.swift:210-222`): `for attempt in 0..<6`, i.e. at most **6** attempts. Retry happens **only** when `link.forge == .gitlab` **and** the lowercased `stdout + stderr` contains any of the three substrings `"405"`, `"not allowed"`, `"checking"`. Backoff is `try? await Task.sleep(for: .seconds(3 + attempt * 2))`, which for attempts 0..4 gives **3, 5, 7, 9, 11 seconds** (the sleep after attempt 5 never runs because the loop ends). Total worst-case wall time: 6 x 300 s of CLI plus 35 s of sleeping.

---

## 7. Command constructions that are computed rather than literal

### 7.1 `diff(from:to:binary:paths:)` (`Git.swift:544-572`)

```swift
        var arguments = paths == nil ? [] : ["--literal-pathspecs"]
        arguments += ["diff", "--no-ext-diff", "-M"]
        if binary { arguments.append("--binary") }
        arguments += [from, to]
        guard let paths, paths.count > Self.diffPathChunk else {
            if let paths { arguments += ["--"] + paths }
            let result = try await run(arguments, timeout: 300)
            try Self.check(result)
            return result.output
        }
        var output = ""
        var start = 0
        while start < paths.count {
            let chunk = Array(paths[start..<min(start + Self.diffPathChunk, paths.count)])
            let result = try await run(arguments + ["--"] + chunk, timeout: 300)
            try Self.check(result)
            if !output.isEmpty, !output.hasSuffix("\n") { output += "\n" }
            output += result.output
            start += chunk.count
        }
        return output
```

Full argv, with the invariant git prefix:

```
git -c core.quotepath=false -c color.ui=false [--literal-pathspecs] diff --no-ext-diff -M [--binary] <from> <to> [-- <path>...]
```

- `--literal-pathspecs` is included **iff `paths != nil`** (including an empty array), and it is a git-global option placed **after** the `-c` pairs and **before** the `diff` subcommand.
- `--binary` is included iff `binary == true`, and sits after `-M`, before `<from> <to>`.
- `Git.diffPathChunk = 400` (`Git.swift:538`). Chunking triggers on `paths.count > 400` strictly, so exactly 400 paths is a **single** call. Chunk size is `min(start + 400, paths.count) - start`.
- Chunk outputs are concatenated with a `"\n"` inserted **only** when the accumulated text is non-empty and does not already end in a newline.

### 7.2 `previewRestore(base:end:paths:)` (`Git.swift:582-596`)

`end` is `known` when given, otherwise `try await captureTree(paths: paths)`. The numstat vector is row 59. Then `blobs(in: end, …)` (row 62) and `workingBlobs(…)` (row 63) supply the kept test: `file.isKept = left[file.path] != now[file.path]`.

UPSTREAM DEFECT to reproduce (SOURCE FACT, and the point R036 raised as unresolved): `paths` is documented as "relative to this directory", and `git diff -- <paths>` resolves them against the process cwd (= `Git.directory`), but `RevertPreview` then keys blobs by the **repository-relative** paths git prints, and `blobs`/`workingBlobs` rebuild absolute paths from `repositoryRoot()`. When `Git.directory` is a subdirectory of the repository, the two path spaces differ and `left[path]` / `now[path]` both miss, making `isKept` compare `nil != nil` = `false`, i.e. every file is reported restorable. Call sites in practice bind `Git` to `thread.worktreePath ?? project.path` (`ThreadRuntime.swift:1776`), which is normally a repository root, so it does not bite. Reproduce as-is.

### 7.3 `repositoryRoot()` (`Git.swift:67-74`)

```swift
        let prefix = try await output(["rev-parse", "--show-prefix"])
        var root = directory
        for _ in prefix.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: "/") {
            root.deleteLastPathComponent()
        }
        return root
```

The root is derived by deleting one component of `directory` per `/`-separated segment of the prefix. It never resolves symlinks, so it stays in whatever path form the caller supplied.

### 7.4 `updateRef(_:to:expecting:)` compare-and-swap (`Git.swift:281-283`)

`["update-ref", ref, commit, old ?? ""]` — a **third positional argument**, the expected old value. `""` means "the ref must not exist yet". This is the only compare-and-swap in the file.

### 7.5 `hasOperationInProgress()` — exactly six markers (`Git.swift:372-379`)

```swift
        guard let gitDirectory = try? await output(["rev-parse", "--path-format=absolute", "--git-dir"]).trimmingCharacters(in: .whitespacesAndNewlines),
              !gitDirectory.isEmpty else { return true }
        for marker in ["rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG"] {
            if FileManager.default.fileExists(atPath: gitDirectory + "/" + marker) { return true }
        }
        return false
```

**6 markers**, in this order: `rebase-merge`, `rebase-apply`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `BISECT_LOG`. One git process, then six `fileExists` calls, paths joined with a literal `"/"`. Failure-to-resolve returns `true` (busy) by design.

### 7.6 `dirtyPaths()` record consumption (`Git.swift:304-318`)

Records come from `status --porcelain=v1 -z --untracked-files=all`, split on `"\0"` with `omittingEmptySubsequences: false`. Each record with `count > 3` contributes `String(entry.dropFirst(3))`. When the two-character code `entry.prefix(2)` contains `"R"` or `"C"`, one **extra** following record is consumed and, when non-empty, appended as the origin path.

### 7.7 `ignoredPaths(among:)` accepts exit status 1 (`Git.swift:322-325`)

`result.succeeded || result.status == 1` — status 1 from `check-ignore` means "nothing matched" and is treated as success; the output is then split on `"\0"` with `omittingEmptySubsequences: true`.

### 7.8 `presentPaths(among:in:)` (`Git.swift:341-352`)

stdin is `"\(ref):\(path)"` + `0x00` per path, in the caller's order. The reply is split on `"\0"` with `omittingEmptySubsequences: false` and **zipped positionally** with `paths`; a path is present iff its answer does **not** end with `" missing"` (leading space included). `trackedPaths(among:)` is exactly `presentPaths(among: paths, in: "HEAD")` (`Git.swift:336`).

### 7.9 `apply(_:)` conflict extraction (`Git.swift:710-714`)

Conflicts are `merged.errorOutput` lines beginning with the two characters `"U "`, each yielding `String(line.dropFirst(2)).trimmingCharacters(in: .whitespaces)`. With no such line, it throws `ShellError(merged.failureMessage)`. A successful plain apply returns `[]` without ever building the scratch index.

### 7.10 `workingChanges()` truncation (`Git.swift:156-161`)

```swift
        let summary = (try? await output(["status", "--short"])) ?? ""
        var patch = (try? await output(["diff", "HEAD", "--no-ext-diff"])) ?? ""
        if patch.utf8.count > 40_000 { patch = String(patch.prefix(40_000)) }
        return (summary, patch)
```

UPSTREAM DEFECT to reproduce: the **test** is on UTF-8 **bytes** (`patch.utf8.count > 40_000`) while the **cut** is on **Characters** (`patch.prefix(40_000)`), so a patch of 45,000 bytes made of multi-byte characters is not actually shortened to 40,000 bytes. Both literals are `40_000`.

### 7.11 `remoteWebURL()` rewriting (`Git.swift:137-146`)

In order: drop a trailing `".git"` (4 characters); if the remote starts with `"git@"` and contains a `":"`, rewrite to `"https://<host>/<rest after colon>"` where host is the text between index 4 and the first colon; else if it starts with `"ssh://git@"`, drop those 10 characters and prepend `"https://"`. Return `URL(string:)` only when the result starts with `"http"`, else `nil`.

### 7.12 `branches()` format string (`Git.swift:100-107`)

`let format = "%(HEAD)%09%(refname:short)%09%(upstream:short)"`, passed as `"--format=\(format)"`. Fields split on `"\t"` with `omittingEmptySubsequences: false`; `isCurrent` iff field 0 == `"*"`; upstream is field 2 when `parts.count > 2` and the field is non-empty; rows with fewer than 2 fields are dropped. Sort is `--sort=-committerdate`, scope is the single pattern `refs/heads`.

### 7.13 `status()` parsing keys (`Git.swift:80-95`)

Prefix literals, exact, trailing space included: `"# branch.head "`, `"# branch.upstream "`, `"# branch.ab "`. The head value `"(detached)"` maps `branch` to `nil`. In the ab line, a token starting `"+"` sets `ahead`, a token starting `"-"` sets `behind`, both via `Int(token.dropFirst()) ?? 0`. Any line not starting with `"#"` increments `changedFiles` by 1.

---

## 8. The other six files in the bundle: facts a porter needs alongside the vectors

### 8.1 `RevertPreview.swift` (42 lines)

`init(numstat:)` parses `git diff --numstat -z` output: split on `"\0"`, each entry split on `"\t"` with `maxSplits: 2, omittingEmptySubsequences: false`. It **throws `CocoaError(.fileReadCorruptFile)`** when `fields.count != 3`, when `fields[2]` is empty, when a non-binary count fails `Int(...)`, or when a count is negative. Binary detection is `fields[0] == "-" && fields[1] == "-"`, and a binary file gets `additions = 0, deletions = 0`. `restorable` is `files.filter { !$0.isKept }`; `additions`/`deletions` sum over `restorable` only, not over all files.

### 8.2 `TouchedPaths.swift` (60 lines)

`buildOutputNames` is a `Set<String>` with exactly **15** elements (`TouchedPaths.swift:48-52`), in source order: `-Xcc`, `DerivedData`, `.build`, `node_modules`, `__pycache__`, `.pytest_cache`, `.mypy_cache`, `.ruff_cache`, `xcuserdata`, `.swiftpm`, `ModuleCache`, `.gradle`, `.turbo`, `.parcel-cache`, `.commandcode`.

`isBuildOutput(_:)` splits the path on `"/"` (`omittingEmptySubsequences: true`) and returns true if **any** component is `".DS_Store"`, ends with `".noindex"`, or is in that 15-element set. So the total rule set is 15 exact names plus 2 dynamic tests.

`relative(_:root:)` rejects a path containing `"://"`, expands a leading `"~"` via `expandingTildeInPath`, standardizes both sides, requires the result to start with `root + "/"`, and returns `nilIfEmpty` of the remainder. `normalize(_:root:)` is the non-rejecting variant: it returns the path unchanged when it is not under the root.

`matches(_:touched:)` is a suffix match in **both** directions: `touched.contains(path) || touched.contains { $0.hasSuffix("/" + path) || path.hasSuffix("/" + $0) }`.

### 8.3 `DiffParser.swift` (167 lines)

Header-line dispatch, only while `hunk == nil || line.hasPrefix("@@")`: `"diff --git "` starts a file; `"new file mode"` -> `.added`; `"deleted file mode"` -> `.deleted`; `"rename from "` -> `.renamed` + `oldPath`; `"rename to "` -> `path`; `"Binary files"` or `"GIT binary patch"` -> `isBinary = true`; exactly `"--- /dev/null"` -> `.added`; exactly `"+++ /dev/null"` -> `.deleted`, any other `"+++ "` -> `path = stripPrefix(dropFirst(4))`; `"@@"` opens a hunk.

Body lines: `"+"` -> `.addition` (newNumber, then `newLine += 1`); `"-"` -> `.deletion` (oldNumber, then `oldLine += 1`); `"\\"` -> `.note` with `String(line.dropFirst(2))` (drops backslash **and** the following space); `" "` or empty -> `.context` with both numbers, both counters advance; a final empty line whose `rawLine.endIndex == patch.endIndex` is skipped.

`headerPath` prefers the text after the **last** `" b/"` (`range(of:options:.backwards)`). `stripPrefix` removes surrounding double quotes when both present and `count >= 2`, then a leading `"a/"` or `"b/"`. `hunkStarts` defaults to `1` for a missing or unparsable token.

### 8.4 `FileChangeSummary+Diff.swift` (19 lines)

One-to-one mapping, order preserved, nothing filtered or de-duplicated; totals are plain sums with no cap.

### 8.5 `TurnDiff.swift` (65 lines)

`merge(snapshot:providerPatch:edits:touched:root:repositoryRoot:providerFiles:)`. `repositoryRoot` defaults to `root`. `reportedPath` resolves a path against `root` then `TouchedPaths.normalize(_, root: repositoryRoot)`. When `touched != nil`, the known set is grown with every reported file's `path` and `oldPath` and every non-empty edit path; snapshot files not in that set are dropped. Snapshot paths are normalized with `TouchedPaths.normalize` directly; non-snapshot paths go through `reportedPath`. With a snapshot present, reported files are added only when `file.path.hasPrefix("/")` and no entry exists yet. Edits are added only when the path is not already authoritative and (`snapshot == nil || path.hasPrefix("/")`). `append` merges by path, renumbering appended hunk ids to `(existing.hunks.last?.id ?? 0) + 1` — **the same value for every hunk in the batch**, an upstream id collision to reproduce. Result is sorted by `path` ascending.

### 8.6 `WorkingTreeWatch.swift` (353 lines)

Covered in §5. One more exact literal: the environment marker used by the login-shell read is `"__DROPPY_CODE_ENVIRONMENT__"` and the script is `"printf '__DROPPY_CODE_ENVIRONMENT__'; /usr/bin/env -0; printf '__DROPPY_CODE_ENVIRONMENT__'"`, run as `<userShell> -l -i -c <script>` with an 8 s timeout (`Shell.swift:365-369`) — that is `Shell.swift`, not the watch, but it is what populates `LoginEnvironment.current` for every vector above.

---

## 9. Labelled summary of dispositions

- SOURCE FACT: R036's "300 s for ... `add -A`" is wrong; `commitAll`'s `add -A` is 120 s. Its "worktree add" and "diff" timeout claims are wrong for the same reason.
- SOURCE FACT: `"The request could not be merged."` is unreachable; R036 listing it as user-facing text is wrong.
- SOURCE FACT: `"git hash-object answered for <n> of <m> files."` is reachable user-facing text; R036 is right about that half.
- SOURCE FACT: the checkpoint commit is parentless; the two phase literals are `"start"` and `"end"`; the checkpoint ref template is `refs/droppy-code/checkpoints/<uuid lowercased>/<turn>-<phase>`.
- SOURCE FACT: git and the forge CLIs differ in executable resolution, argument prefix, environment and stdin, as tabulated in §6.1.
- UPSTREAM DEFECTS recorded for reproduction, not repair: the 120 s three-way `apply` (row 72); the byte-test/character-cut truncation in `workingChanges()`; the `previewRestore` path-space mismatch for a `Git` bound below the repository root; the repeated hunk id in `TurnDiff.append`.
- UNRESOLVED: none for this pass. Every vector, timeout, literal and count above was read directly from the pinned source.
