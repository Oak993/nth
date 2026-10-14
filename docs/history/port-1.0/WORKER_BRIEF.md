# Worker brief: Marrow Windows port

You are porting one small piece of a macOS app ("Droppy Code", Swift) to Windows ("Marrow",
C# / .NET 10 / Avalonia 12.1.2). It is an EXACT copy, not a redesign. You get one task packet at a
time from `porting/tasks/T-*.md`. Do only that packet.

## Where things are

- Project (you work here): `E:\Droppy\port`
- Original Swift source, READ-ONLY: `E:\Droppy\reference\droppy-code`
- Your task: the packet file you were given, in `porting/tasks/`
- Code goes in `src/Marrow.<Layer>/`, tests in `tests/Marrow.<Layer>.Tests/`

## The rules. Breaking any of these rejects your work.

1. **Copy, do not invent.** Every behaviour, label, number, default, key name and error message must
   come from the Swift file named in your packet. If it is not in the source, do not add it.
2. **Strings are verbatim.** Copy user-visible text exactly: same words, punctuation, capitals and
   ellipsis character. Never paraphrase.
3. **Numbers are exact.** Sizes, paddings, radii, durations, opacities, colours, timeouts: copy the
   literal value. Never round or "improve".
4. **Tests first.** Write the tests from the packet's "Expected" lines, then STOP and report so the
   orchestrator can check them. Only then implement. Never change a test to make it pass.
5. **Upstream bugs are copied, not fixed.** If the packet says "Upstream defect", reproduce it.
6. **Never implement anything marked DO NOT IMPLEMENT / quarantined (BLK-xxxx).** Leave a comment
   `// BLOCKED BLK-xxxx: owner decision pending` and move on.
7. **Stay in your layer.** `Marrow.Core` uses nothing but .NET, no Avalonia and no other Marrow
   project. `Marrow.Services` and `Marrow.UI` use only `Marrow.Core`. `Marrow.App` uses all.
   Tests enforce this; do not add project references.
8. **Do not edit protected files.** Never change anything under `porting/`, any `.csproj`,
   `Directory.*.props`, `Marrow.sln`, `LayeringTests.cs`, `WORKER_BRIEF.md`, `HANDOFF.md`,
   `marrow.json`. Need a package or a reference? Say so in your report; do not add it.
9. **Never touch the reference folder** `E:\Droppy\reference\droppy-code`. Read only.
10. **Never commit, push, or run git commands that change history.** The orchestrator commits.
11. **Never read credential files** (`~/.claude/.credentials.json`, `~/.codex/auth.json`,
    `~/.grok/auth.json`, `~/.commandcode/auth.json`, any `auth.json`). Never send a prompt to a real
    AI CLI. Only `--version` or `--help` if a packet explicitly asks.
12. **Never run the app against real data.** Before running anything, set
    `MARROW_PORT_DATA_DIR` to a throwaway folder, e.g.
    `$env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-test-$(Get-Random)"`.
    The owner's working Marrow app lives in `%LOCALAPPDATA%\Marrow`. Never touch it.

## How to translate Swift

- `@MainActor` means the UI thread: use `Dispatcher.UIThread` in App/UI. State changes happen there.
- Swift `Codable` means `System.Text.Json`. Use the EXACT Swift property names as JSON keys
  (`[JsonPropertyName("...")]`). Dates are ISO-8601. Keys are written sorted.
- Swift `value(key, default: x)` means: missing key OR wrong type gives `x`, silently, and never throws.
- Swift `Lenient<T>` means: one bad element becomes null and does not fail the whole list.
- `UserDefaults` keys mean settings keys: keep the exact key string and the exact default.
- Keychain means Windows DPAPI via the shared secret store. Never fall back to plain text.
- If a Swift API has no obvious Windows twin, STOP and say so. Do not guess an equivalent.

## Done means all of these pass

```
dotnet build Marrow.sln -c Debug      (warnings are errors)
dotnet test Marrow.sln                (nothing failed, nothing skipped)
node porting/tools/check_protected.mjs
node porting/tools/verify.mjs --gate A
node porting/tools/verify.mjs --gate B
```

## Your report, every time

- Each requirement id -> the test that proves it.
- Anything the source did not settle, with the Swift file and line.
- Every guess you made. The right number is zero; if it is not zero, say so plainly.
- The exact commands you ran and their real output. Never claim a pass you did not see.
