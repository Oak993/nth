# Independent review: codex/ma-1, codex/ma-2, codex/ma-4 (2026-10-05)

Reviewer: Claude (cloud session). Branch `cloud/review-ma-1-2-4`, from `claude/verify-terminal-20261001` (bfbbea40), with the three Codex branches merged in order (ma-1, ma-2, ma-4) so the tests compile against all of them together.
The merge conflicted only in `porting/engineering_decisions.md` (kept both sides); the merged tree builds (`Marrow.App.Tests`, 0 errors, 0 warnings) and every code file merged without a conflict, including the three edits to `ThreadRuntime.cs`.

Reviewed against: `multi-account-NOTES.md`, `multi-account-FEASIBILITY.md`, OD-0114 to OD-0116, and the interfaces of MA-5 (`Marrow.Core.Accounts`), MA-6 (`Accounts/Keys`) and MA-7 (`Accounts/Pills`).
Labels: SOURCE FACT (read in the code or run), ENGINEERING DECISION, UNRESOLVED.

## Verdicts

| Branch | Head | Verdict |
| --- | --- | --- |
| origin/codex/ma-1 (accounts store, isolation) | ed999363e | **FIX FIRST** (MA1-1, MA1-2) |
| origin/codex/ma-2 (limits, polling) | b6d85f6a4 | **MERGE** (with the T11 requirements MA2-1 to MA2-3) |
| origin/codex/ma-4 (handoff summary) | a7209c718 | **FIX FIRST** (MA4-1, MA4-2) |

No production code was changed on this branch (merge only). Five failing `REVIEW_` cases (four test methods) are the evidence for the four FIX FIRST findings; each was run and fails for the stated reason (list at the end).

## What was run

SOURCE FACT. Linux, `-p:TreatWarningsAsErrors=false -p:EnableWindowsTargeting=true`, `MARROW_PORT_DATA_DIR` set to a throwaway folder, filters end `&FullyQualifiedName!~Native`.
- Merged build: 0 errors, 0 warnings.
- Slice `Feature=MA-1|MA-2|MA-4`, `Port.Accounts`, `Port.Usage`, `MultiAccount`, `AutoContinueAccount`, Traceability: Core 30 of 30, Services 101 of 101, App 17 of 18. The one failure, `ThreadRuntimeHandoffTests.Runtime_archives_full_tool_output_...`, needs the ICU bridge Codex describes in `porting/reports/codex-ma-4.md` (`icu.dll` is a Windows library); it is an environment limit, not a finding.
- Traceability passes in App and Services with the review tests added.
- Not run: anything on Windows, any real CLI, any real service.

## Checks you asked for

| Check | Result | Evidence |
| --- | --- | --- |
| Migration keeps today's single sign-in untouched | **Mostly.** The migrated legacy account's environment equals the inherited one exactly, nothing is moved, copied or read (`AccountEnvironment.Build` returns a copy for `UsesLegacyCredentials`; test `Every_migrated_service_keeps_its_environment_exactly...`). **But** an unreadable or newer `multiAccounts` setting stops the app from starting (MA1-1). | `AccountEnvironment.cs`, `AccountStoreTests` |
| Per-account environments isolate, no fallback to the global login | **Yes for the services it covers.** A non-legacy account gets its own root (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `CURSOR_CONFIG_DIR`, `GROK_HOME`, `PI_CODING_AGENT_DIR`, OpenCode's four XDG roots plus `OPENCODE_CONFIG_DIR`, `HOME`/`USERPROFILE`/`APPDATA`/`LOCALAPPDATA` for the rest) and 27 inherited credential variables are removed; resume ids and Pi paths are owned by the account (`OwnsResume`); a disabled or wrong-service account is refused at every launch boundary; the Claude token reader never falls back to `~/.claude` for an explicit account; Grok status uses the selected root; API keys are per-account in `APIKeychain` (` / <guid N>` suffix) with no legacy fallback. UNRESOLVED: the removal list is a fixed list of names; a variable a CLI reads that is not on it (for example a Grok or OpenCode key name) would leak through. Not testable offline; worth a Windows pass per CLI. | `AccountEnvironment.cs`, `AccountEnvironmentTests`, `AccountReaderTests`, `AccountRegistryTests` |
| Credential-Manager CLIs stay single-account | **Yes.** `AccountCapabilities.SupportsMultipleAccounts` excludes Copilot, Antigravity (vault without selector) and Command Code (one personal identity, MA-0 section 6); the store rejects a second account and its validator rejects a document holding two (`Single_account_services_reject_a_second_identity`). One edge: after the user removes the legacy Copilot account, `Add` creates a non-legacy one whose `Build` rewrites `HOME`/`USERPROFILE`/`APPDATA` (the code comment says this is intended); it moves the CLI's config folder away from the user's. Low. | `Account.cs`, `AccountStore.cs` |
| Limits come only from what the service reports | **Yes.** The 5-hour window is detected from a reported `DurationMinutes` (Codex `windowDurationMins`, Z.ai `unit`/`number`, Claude `kind`/`five_hour` key, Command Code key name), never from a plan name; a missing report replaces the old snapshot with "limits not shared"; no window is invented. The only plan-name use is the owner's no-data Handshake default (95 or 98). The estimator measures percent deltas of one reported window over observed time and says so. | `AccountPlanLimitsSnapshot.cs`, `UsageRateEstimator.cs`, `PlanLimits.cs` diff |
| Polling is bounded | **Yes.** Per account at least 60 s between reads, 3 min while in use, 1 min within 5 points of the Handshake point, exponential back-off to 30 min on a missing report, 0 to 10 s jitter, global concurrency 2, `Retry-After` honoured (429 and 503), one flight per account, late results from a signed-out generation dropped, injected clock. 15 deterministic tests. Note: the embargo is per account, not per service (the field is named `serviceEmbargoes`), so a service-wide throttle on one account does not slow its siblings; the global cap of 2 still bounds the total. Low. | `AccountLimitsScheduler.cs`, `AccountLimitsSchedulerTests` |
| Summary keeps every user message and reply word for word | **Yes for the summary text.** Every `User`, `Assistant`, `Plan`, `Todos` item is appended in full, in item order; only re-readable output (file reads, searches that did not fail) is shortened outside the last three turns; command output, errors and failed reads are kept (head and tail past 8192 characters, rune-safe); all eight timeline content cases are handled; the full conversation is saved beside it and named in the text. Byte-exact assertion on both fixture chats. | `HandoffSummary.cs`, `HandoffSummaryTests` |
| The fidelity checker is real | **Partly.** `HandoffFixtures.AssertFidelity` compares every leaf of the original with the summary OR the archive, and its negative control fails on an empty archive; but the archive is the whole original, so the checker can only fail if the archive loses data. It does not test the summary's selection; the byte-exact user/assistant assertion in the same test does. And there is **no production read-back check** at all (MA4-3): the Handshake's step 4 has nothing to call. | `HandoffFixtures.cs` |
| No real CLI or network in tests | **Yes.** Login is a recorder (`AccountLoginRunner`), HTTP is `InProcessHttpHandler`, usage reads are injected, the clock is `ManualTimeProvider`, data is under `MARROW_PORT_DATA_DIR`, credentials are in-memory. Low: MA-4 commits an environment-specific C file (`tests/fixtures/handoff/icu-linux-bridge.c`) and six run logs under `porting/reports/ma-4/`; MA-2 widens the shared Traceability regex with a hard-coded `ED-406`. | tests and reports |

## Numbered findings

### codex/ma-1 (FIX FIRST)

**MA1-1 (high, test).** An unreadable or newer `multiAccounts` value makes `AccountStore` throw (by design: "the original value was preserved"), but `AppSettings.Accounts` is read in the `ProviderRegistry` constructor, which `AppModel` runs at start-up, so the whole app fails to start on a corrupt value or after a downgrade. "Today's single sign-in untouched" must hold here too: fall back to legacy-only behaviour (no accounts feature, nothing written) and keep the stored text. Test: `REVIEW_unreadable_or_newer_account_settings_must_not_stop_the_provider_registry_from_starting` (2 cases: garbage; `Version` 2). Both fail with `InvalidDataException`.

**MA1-2 (medium, test).** `ProviderRegistry.Environment(provider)` throws "No enabled account is available" when every account of a service is Off, and `InstalledCLIVersion`, `UpdateCLI`, `UpdateCLIIfDue` and `LoadCommands` call it unguarded (the guarded paths are only `Refresh`, `LoadCatalog`, `RefreshPlanLimits`). Turning a service Off must not break versioning or updating an installed CLI. Test: `REVIEW_the_installed_cli_version_can_still_be_read_when_every_account_of_the_service_is_off` fails with `InvalidOperationException`.

**MA1-3 (medium, design conflict with MA-2).** `PlanLimitsReader.ReaderClaudeLogin` returns null for any environment that contains `MARROW_ACCOUNT_ID` (MA-1: "do not collect their OAuth tokens"), while MA-2 added `allowHomeFallback: false` to read the explicit account's own `.credentials.json` under its `CLAUDE_CONFIG_DIR`. In the merged tree MA-1's guard wins, so MA-2's branch is dead and every poll of an explicit Claude account takes the CLI path (`ReadClaudeFromCLI` starts a process each read, up to once a minute per account). Decide one: either keep MA-1's rule and delete MA-2's dead parameter, or narrow the guard. No test (both behaviours are defensible); needs the lead's call.

**MA1-4 (medium, T11).** `Account.HasSignedIn` is false for every migrated legacy account for ever (only Marrow's own sign-in flow sets it), so it cannot drive MA-2's `SignedInAsync`; signed-in state must come from the registry's auth status. Also `Account` has no field for a per-key monthly cap or a per-service order mode, which MA-6 needs for API keys (see adapters).

**MA1-5 (low).** `Marrow.Core.Port.Accounts.HandshakePoint` duplicates `Marrow.Core.Accounts.HandshakePoint` (MA-5): same name, same 80 to 99 rule, different shape (`Percentage` versus `Percent`). A file importing both namespaces will not compile; unify on one in T11 (convert at the boundary).

**MA1-6 (low).** `AccountStore.Remove` can remove the legacy account; a thread with a native session id and no binding then throws "The original account for this conversation is unavailable" for ever. Acceptable if the UI warns; note for T11.

Verified good (no finding): stable ids, reopen-safe, 24-text-element names, default names numbered by identity, priority reorder atomic and validated, `Choose` refuses a disabled pin instead of substituting, provider change clears the binding, damaged binding becomes `Guid.Empty` and never reads as legacy, sign-in single-flight and retryable, no plaintext fallback on a refused key write.

### codex/ma-2 (MERGE)

**MA2-1 (T11, medium).** Nothing links MA-2 to MA-1: keys are `UsageAccountKey(provider, "default" or string)`; `ThreadRuntime.Observe(session)` still passes the default key, so in the merged tree a usage-limit event from a non-legacy account is attributed to `default`, and `AutoContinue.ReadResetTime(provider)` then reads `Providers.Environment(provider)`, which is the FIRST enabled account by priority, not the thread's. T11 must call `Observe(session, key)` with the thread's account and map ids (adapter below).

**MA2-2 (T11, low).** The scheduler has no notion of Off: T11 must call `SignedOut`/`SetInUse(false)` when an account is disabled.

**MA2-3 (T11, low).** Nothing measures the cost of one Handshake; `IAccountLimits.HandshakeCostPercent` has no producer, so Smart start uses the owner's plan defaults until something does.

**MA2-4 (low).** Traceability special-case `ED-406` in `DecisionId()`; the next unit will add its own. Prefer one rule for `ED-NEW-*`. Embargo per account (above). `DefaultHandshakePercent` treats any plan without `5x|10x|20x` as 95, including large ChatGPT plans; that is the owner's stated default for an unknown plan.

### codex/ma-4 (FIX FIRST)

**MA4-1 (high, test).** `ThreadRuntime.Handle` calls `HandoffJournal.Record` for EVERY provider event of EVERY thread, unconditionally: a synchronous `File.AppendAllText` per stream delta, tool output and notice, to `handoff-events.jsonl` beside the thread, unbounded, even for a user with one account per service who will never hand over. This contradicts "one account looks exactly like today" and adds main-thread file I/O to every token. Gate it on multi-account being in use for the thread (2 or more enabled accounts in the service, or a pill), coalesce the writes, and bound or prune the file. Test: `REVIEW_a_thread_of_a_single_account_user_writes_no_handoff_journal` fails (file exists).

**MA4-2 (high, test).** `ThreadRuntime.SaveHandoff` requires the whole runtime to be Idle with no running tool, so a summary can only be taken after the turn ended. The Handshake (NOTES: starts near the limit while the reply is running; the switch happens at a safe point between steps) needs a summary of a thread that is mid-turn but between steps, and `AccountHandoff.Start` also requires `IsQuiescent`. As built, the summary can only serve the hard-stop path the Handshake exists to avoid. Add a safe-point save (no open tool, approval or question, turn may be running). Test: `REVIEW_a_handoff_can_be_saved_between_two_steps_of_a_running_turn` fails with "A handoff requires a quiescent thread".

**MA4-3 (medium, T11).** There is no read-back check and no key facts: `HandoffSummary` has `Text`, `FullConversation`, `HandoffFile`, `Size` only. MA-5's step 4 needs `FindGaps(summary, readBack)` and the list of facts to check (files, open tasks, the user's latest instructions). The data is in `HandoffContext` (changed files, todos, last user message, last error); the function is missing.

**MA4-4 (medium).** The summary text is unbounded by design (every user and assistant message verbatim) and `AccountHandoff.Start` sends it as one user turn with no size check; a very long thread can exceed the new account's context. Needs a guard or a "read the archive" fallback (the file is already named in the text). Also `HandoffStore` keeps every checkpoint for ever (tests assert it), so each switch of a long thread leaves a full copy.

**MA4-5 (medium, T11).** `AccountHandoff.Start` sends the summary and returns the session; the new account's reply (the read-back) is delivered only through `session.OnEvent`, which the factory must wire before `Start`. Not wired or documented.

**MA4-6 (low).** Committed artefacts: `tests/fixtures/handoff/icu-linux-bridge.c` and `porting/reports/ma-4/*.log`. Remove or move out of the tree.

Verified good: item-order recent-turn selection that survives missing turn records, head and tail on rune boundaries, deterministic output, refuses a non-quiescent or running checkpoint, never overwrites destination transcripts, native copy limited to a version-gated explicit manifest of `projects/*.jsonl` or `sessions/*.jsonl[.zst]` with link checks and `CreateNew`.

## Adapters T11 needs (exact)

SOURCE FACT for the left-hand types; the right-hand types are mine (`src/Marrow.Core/Accounts`, branches cloud/ma-5, ma-6, ma-7).

1. **`IAccountStore` (MA-5) over `AccountStore` (MA-1).**
   - `Accounts`: one global priority-ordered list. MA-1 orders only within a service (`ForService`, `Priority`), so T11 must define the order of services (decision needed: it fixes which other service is proposed first).
   - `AccountInfo.Id` = `account.Id.ToString("N")`; `Service` = `account.Service.Id`; `Enabled` = `Enabled`.
   - `Name` = `store.DisplayName(id) ?? store.DefaultName(id)` (`DisplayName` is null with one account; MA-5 wants a name for the card text even then).
   - `Plan` (`PlanTier`) from `Account.Plan` (a label such as "Pro" or "Max (20x)"): Large when it matches `\b(5|10|20)x\b` (the same test as MA-2's `DefaultHandshakePercent`), Standard when a plan is known, Unknown otherwise.
   - `HandshakeAt` from `Defaults.HandshakePoint` (null stays null; Smart maps to `HandshakePoint.Smart`; a percentage to `HandshakePoint.At(p)`); `HandshakeOptions.General` from `GeneralHandshakePoint`.
2. **`IAccountLimits` (MA-5) over `AccountLimitsScheduler` + `UsageRateEstimator` (MA-2).**
   - Key: `UsageAccountKey(account.Service, legacy ? "default" : id.ToString("N"))` (the legacy account must use `Default(service)` because MA-2's reader takes the global login for it).
   - `Usage(id)`: `scheduler.Snapshot(key)?.Limits`; choose the binding window = highest `Percent` among windows with a finite value; `AccountUsage(percent, window.ResetsAt, IsOut: percent >= 100)`; null when not shared.
   - `SpendPercentPerMinute(id, thread)`: `estimator.PercentPerMinute(thread, key, bindingWindow.Id)`; T11 must call `estimator.Observe(thread, snapshot)` after each snapshot for the thread's active account.
   - `HandshakeCostPercent`: null (no producer; MA2-3).
   - Lifecycle calls T11 owns: `SignedInAsync` from registry auth status (not `HasSignedIn`, MA1-4), `SetInUse(key, active)`, `SetHandshakePercent(key, effective point)`, `SignedOut` when disabled, `LimitReachedAsync` from `HandshakeMachine.LimitHitAsync`.
3. **`IHandoffSummary` (MA-5) over `ThreadRuntime.SaveHandoff` (MA-4).** `BuildAsync(thread)` needs the runtime and the current diff and, as built, a quiescent thread (MA4-2). Map `HandoffSummary` (MA-4) to mine: `Text` to `Text`, `HandoffFile` to `FilePath`, `KeyFacts` derived from `HandoffContext` (changed files and touched paths, unfinished todos, the last user message, the last error). `FindGaps` = the key facts that the read-back does not contain (MA-5's in-memory fake does this); MA4-3.
4. **`IThreadAccountHost` (MA-5) over `ThreadRuntime` (MA-1, MA-2, MA-4, MA-7).**
   - `ActiveAccountId` = `thread.ProviderAccountID ?? legacy account id` (formatted "N").
   - `Pill`/`StepIndex`/`Pinned`: resolve `thread.PillRelay` (MA-7) with `PillRelayResolver`; `Pinned` needs a new thread field (none exists yet).
   - `AtSafePoint`: no signal exists in `ThreadRuntime`; add "tool finished, no approval or question" (also needed by MA4-2). `ReplyInFlight` = `Phase == Running`.
   - `StartStandbyAsync(target)`: build a `SessionConfiguration` as `EnsureSession` does and pass it through `Providers.PrepareSession(configuration, accountId, useAccountDefaults: false)`; the session-creation `switch` in `EnsureSession` must be extracted into a factory to be reusable.
   - `RequestBriefingAsync`: no code path sends a hidden turn to the current session and captures its reply without putting it on the timeline.
   - `SwitchAsync`: stop the old session, set `ProviderAccountID` (and clear `ProviderSessionID` unless native resume), apply the step's model, effort and fast mode, call `WithPillRelay(new PillThreadState(pill, step, account))`, resume the reply if asked.
   - `IStandbySession.ReceiveAsync`: `AccountHandoff.Start(summary, source, target, configuration)` sends the summary as the first turn; capture the reply by setting `OnEvent` inside the `createSession` factory (MA4-5). `FillGapsAsync` is a plain follow-up turn.
5. **MA-6 `IApiKeyStore` over MA-1 accounts of API services.** One key = one `Account`: `Nickname` = custom or default name; `Allowed` = `Enabled`; secret = `APIKeychain.Key(account, "")` (account name `"<Service> API Key / <guid N>"`, the legacy one unchanged). MA-6's `KeychainApiKeySecrets` uses a different suffix (`" [<keyId>]"`): drop it and use MA-1's, or the two will store the same key under two names. MA-1 lacks per-key `MonthlyCapUsd` and a per-service `KeyOrderMode`; add them to `Account`/`AccountStore`. `ApiKeyEntry.Id`/`PillStep.KeyId` = the account id.
6. **MA-6 `IKeyMovePolicy` and MA-5 `IDecisionStore`** have no MA-1/2/4 counterpart (Settings values and thread values); T11 maps them onto `AppSettings` and `ChatThread` per OD-0115 and OD-0116.
7. **`UsageLimit` events to the machine.** `ThreadRuntime.Handle(ProviderEvent.UsageLimit)` currently only calls `AutoContinue.NoteLimit`; T11 must also call `HandshakeMachine.LimitHitAsync(accountId, resetsAt)` and decide who owns the wait (the machine's Waiting phase or AutoContinue), not both.
8. **`ChatThread`.** MA-1 adds `ProviderAccountID` (kept in `providerAccountID`), MA-7 adds `PillID`, `PillStepIndex`, `PillStepAccountID`. They were written independently and will conflict textually in `Library.cs`; both are additive.

## Review tests (cloud/review-ma-1-2-4)

| File | Test | Fails today because |
| --- | --- | --- |
| `tests/Marrow.App.Tests/Review/ReviewMa1CorruptSettingsTests.cs` | `REVIEW_unreadable_or_newer_account_settings_must_not_stop_the_provider_registry_from_starting` (2 cases) | `InvalidDataException` from `new ProviderRegistry(settings)` (MA1-1) |
| `tests/Marrow.Services.Tests/Review/ReviewMa1DisabledServiceTests.cs` | `REVIEW_the_installed_cli_version_can_still_be_read_when_every_account_of_the_service_is_off` | `InvalidOperationException` from `Environment(provider)` (MA1-2) |
| `tests/Marrow.App.Tests/Review/ReviewMa4JournalScopeTests.cs` | `REVIEW_a_thread_of_a_single_account_user_writes_no_handoff_journal` | the journal file is written (MA4-1) |
| `tests/Marrow.App.Tests/Review/ReviewMa4SafePointTests.cs` | `REVIEW_a_handoff_can_be_saved_between_two_steps_of_a_running_turn` | `InvalidOperationException: A handoff requires a quiescent thread` (MA4-2) |

Run: `MARROW_PORT_DATA_DIR=<throwaway> dotnet test tests/Marrow.App.Tests -p:TreatWarningsAsErrors=false -p:EnableWindowsTargeting=true --filter "Review=MA-1|Review=MA-4"` (and the same for `Marrow.Services.Tests`). They carry `Review` and `Rule R-TEST-4` traits so Traceability reads them. Each should pass once its finding is fixed.
