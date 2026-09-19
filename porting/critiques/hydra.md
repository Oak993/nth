# Critique: hydra

**Verdict:** NEEDS_REREAD — R031 must re-read Hydra.swift for the prompt corpus verbatim and R014 must re-classify HydraRunMetricsView; R005 has one inverted tie-break rule and several undescribed behaviors in AppModel+Hydra.swift.

## Contradictions (a report disagrees with the source) (5)

1. R031 (Hydra.swift, state_owned) says "HydraPrompts.verificationMarkers (28 markers)" while its own feature_notes says "verificationMarkers (34 exact substrings...)" and lists 34. The source has exactly 34 entries (DroppyCode/Core/Models/Hydra.swift:1128-1137). The state_owned count is wrong.

2. R005 (AppModel+Hydra.swift, feature_notes) says "The head cap is [pair?.maxHeads, settings.hydraMaxHeads].compactMap{$0}.min(); with neither, heads run uncapped." R031 (Hydra.swift) says the opposite for the runtime effect: HydraLaunch.hasRoom uses `maxHeads ?? uncappedConcurrency` with uncappedConcurrency = 8 (Hydra.swift:168-173). A port following R005 alone would let unbounded heads run, which the source comment explicitly guards against (21 simultaneous heads forcing a force quit).

3. R005 (HydraCookbook+Resolve.swift) says model(named:) ties are "broken by the later position in registry.models(for:)". The source breaks ties toward the EARLIER position: `matches.max { ... return options.firstIndex(lhs)! > options.firstIndex(rhs)! }` makes the higher-index element compare as smaller, so max() returns the lowest-index option (DroppyCode/App/HydraCookbook+Resolve.swift:52-61). R005 has the direction inverted.

4. R031 (Hydra.swift, feature_notes) says copilotAgents "deliberately passes no tools list for the scout", implying the worker does get one, and its persisted_fields lists "tools" among the emitted Copilot customAgents keys. In the source BOTH the worker and the scout are built with `tools: nil` (Hydra.swift:797 and :808), so the `tools` key is never emitted for Copilot at all. R031's persisted_fields list is wrong for that provider.

5. R005 lists "HydraPanel.finishHold's numeric value is defined elsewhere, so the real auto-clear delay (finishHold + 0.2 s) is not fixed by this file" as unresolved, while R013 fixes it at 2.0 (HydraPanel.swift:40). Left unreconciled the port has no number for the auto-clear delay (2.2 s).

## Gaps (referenced but never pinned down) (19)

1. HydraRunMetricsView.swift (R014) is described as shipped UI ("an equatable card hung under the Hydra panel's strip") but it is UNREACHABLE: `grep -rn "HydraRunMetrics" --include=*.swift` returns hits only inside DroppyCode/App/Views/Chat/HydraRunMetricsView.swift itself. No view, panel or settings page constructs HydraRunMetricsView, HydraRunMetrics or HydraRunMetricsReader. R014 parked this as an unresolved question instead of answering it with a repo search, so 398 lines are queued for the port as shipped behavior.

2. DroppyCode/App/AppModel+Hydra.swift:193-215 `setHydra(_:for:)` is listed in R005's key_symbols but no report describes what it does when turning Hydra ON: it looks up `settings.hydraPair(for: thread.provider, model: thread.model)`, pins `thread.hydraPairID`, and then REWRITES the chat's `model` to `pair.orchestratorModel` (when the provider's catalogue has it) and `effort` to `pair.orchestratorEffort`. Right-clicking the Hydra mark and choosing "Turn Hydra on for this chat" can therefore silently change the chat's model. Nobody in R005 or R013 states this.

3. DroppyCode/App/AppModel+Hydra.swift:155-178 `keepsModel = pair.map { !$0.sendsHeadsElsewhere || elsewhere } ?? true`: when a pair names a worker provider that is not installed or is switched off, the heads fall back to the lead's provider AND the pair's `workerModel` and `workerEffort` are discarded (set to nil). R005 describes only the user-facing fallback note, never this silent discard of the pair's head model/effort.

4. DroppyCode/App/AppModel+Hydra.swift:163-170: the effort tempering is gated on `settings.hydraTempersHeadEffort && !elsewhere`. R005 lists hydraTempersHeadEffort under persisted_fields but never states the gate, so a port would temper effort for heads running on another provider, which the source never does.

5. DroppyCode/App/AppModel+Hydra.swift:387-405 `insertHydraHead`: a delegation's `profile` resolves to a HydraHeadProfile that overrides the head's PROVIDER (`routed?.provider ?? launch?.headsProvider`), MODEL (`native?.model ?? routed?.model ?? launch?.workerModel ?? ...`) and EFFORT (`routed?.effort ?? launch?.workerEffort ?? ...`), and appends " · <profile name>" to the title with `info.profile = routed?.name`. R005 mentions head profiles only in calls_into; the whole routing precedence chain is undocumented in the family.

6. DroppyCode/App/AppModel+HydraMerge.swift:181-184: `func title(_ name:) { several ? "<project.name>: " + name : name }` prefixes EVERY merge note title and every HydraMergeRecord.detail when more than one project merges, not just the pill's stage words. R005's ui_elements attribute the project prefix only to HydraMergeStage.words, so a port would produce "Hydra merged !123" where the app produces "gaze-site: Hydra merged !123".

7. DroppyCode/Core/Models/Hydra.swift:914-946 `mergeStatus` pending text is entirely missing from R031: "<N files> changed since then is/are not merged yet; it goes/they go out by itself/themselves once you finish this answer with every head back.", "Nothing has changed since, so no merge is pending and none is coming.", and the no-previous-merge variant "Merge state: <N files> changed in this chat is/are not merged yet; ...". R031 covered only the three outcome sentences.

8. DroppyCode/Core/Models/Hydra.swift:851-870 `teamStatus`: R031 records only "(working for Nm)". The source also emits "(working)" with no interval when `Int(elapsed/60) == 0`, and "(done)" with no parenthetical detail when `info.landing == nil`. Both are missing from the report's string list.

9. DroppyCode/Core/Models/Hydra.swift:609-727 and :728-741: the exact text of `howToWork`, `howToReport`, `briefRule`, `verificationRule`, `projectRule(_:)`, `autoMergeRule`, `reviewRule`, `reportStyleRule`, `workerPrompt`, `scoutPrompt` and the whole `policy(for:_:)` template is never captured. R031 names them as symbols and paraphrases a handful of fragments. These strings are the native-provider system prompt and the agent `prompt` field sent over the CLI protocol; paraphrase is not portable.

10. DroppyCode/Core/Models/Hydra.swift:1259-1262 `fallbackReportNote` opener "[Hydra] Your heads reported back below." is not quoted anywhere; R031 only says the note is prefixed "[Hydra] ". Same for the exact two `more` variants at :1255-1258.

11. DroppyCode/Core/Models/Hydra.swift:1000-1005 `delegations(fromBody:)` returns nil (not []) when the JSON parses but EVERY entry is rejected by delegation(fromEntry:); only a literally empty array returns []. That difference is what drives the retry over earlier fences and ultimately `unreadableBlockMessage`. R031 says only "entries with no usable prompt are dropped".

12. DroppyCode/Core/Models/HydraDelegationStream.swift:110-114: the `name`/`head` and `project`/`repo` fallbacks are `??` over the OPTIONAL string, so a present-but-empty "name" (or "project") shadows the "head" (or "repo") alias and the field ends up nil. R031 says only that empty values normalise to nil.

13. AppSettings defaults for this family are stated nowhere in the four reports (DroppyCode/App/AppSettings.swift:743-760): hydraEnabled default TRUE, hydraDefaultEnabled TRUE, hasSeenHydraIntro FALSE, hydraIsolateHeads TRUE, hydraTempersHeadEffort TRUE, hydraAutoMerge FALSE, hydraReviewHeads FALSE, hydraAutoClearFinished FALSE, hydraShowsHeadDetails FALSE, hydraMaxHeads has NO default and is passed through HydraPair.clampedCap on load (1...8). Hydra shipping ON by default is a behavioral fact the port cannot guess.

14. Migration behavior of the per-chat switch is unstated (DroppyCode/Core/Models/Library.swift:174-181, 220, 258-260): `ChatThread.hydraEnabledIsExplicit` decodes with default false, and when it is false the stored `hydraEnabled` is IGNORED and forced to true; a fresh thread seeds hydraEnabled from `UserDefaults.object(forKey: "hydraDefaultEnabled") as? Bool ?? true` read directly, not through AppSettings. R005 lists ChatThread.hydraEnabled as a persisted field with none of this.

15. Persisted pair list is decoded as `[Lenient<HydraPair>]` and compacted (AppSettings.swift:776, Coding.swift:11-17), so a pair whose stored JSON fails to decode is silently dropped rather than failing the load. R031 documented HydraPair's tolerant field decode but not this outer per-element salvage.

16. Cross-file defect nobody noticed: HydraReportsPopover.withoutLeadInstructions only strips the final paragraph when it starts with one of nine `leadPrefixes` (HydraReportsPopover.swift:190-201), but reportMessage's closing paragraph can begin with "<Name>'s changes would not apply on their own; ..." (Hydra.swift:1271) or "<A> and <B> both changed <path>: ..." (Hydra.swift:1267) when no head landed cleanly. In that case the lead's whole instruction block is rendered inside the last head's report card.

17. DroppyCode/App/Views/Chat/HydraPanel.swift:279-284: "Clear finished heads" is the only popover action that does NOT set `isPresented = false`, so the heads popover stays open after clearing while "Stop all heads" and head selection close it. R013 lists both items without this asymmetry.

18. DroppyCode/App/Views/Chat/HydraButton.swift:212-226: the right-click items carry checkmark state (`isChecked: isOn` / `isChecked: !isOn`) and the "Turn Hydra on" item in the Hydra-off-in-Settings branch writes `settings.hydraEnabled = true` AND calls `setHydra(true, ...)`. R013 lists the item labels only, so the checkmarks and the two-write action are unrecorded.

19. DroppyCode/App/AppModel+Hydra.swift:487-489 `makeHydraCopy` awaits the lead runtime's pending `hydraLanding` task before capturing the tree, so a copy is never made mid-patch. R005 documents landing serialization but not this ordering constraint on copy creation.

## Thin reports (3)

1. R014 — for DroppyCode/App/Views/Chat/HydraRunMetricsView.swift (398 lines) the report documents metrics, phase math and card layout in detail but never establishes reachability; a one-line repo grep shows zero call sites, so the file is dead code and the report presents it as shipped UI. The port contract explicitly requires classifying unreachable code with evidence. The report also leaves "where it is instantiated" as an unresolved question that the reference checkout answers definitively.

2. R031 — for DroppyCode/Core/Models/Hydra.swift (1345 lines) the majority of the file by volume is exact prompt text (policy(for:), howToWork, howToReport, briefRule, verificationRule, projectRule, autoMergeRule, reviewRule, reportStyleRule, workerPrompt, scoutPrompt, fallbackPolicy body). The report captures a handful of fragments and otherwise paraphrases, while the port contract requires these verbatim. It also miscounts verificationMarkers (28 vs 34) and misstates the Copilot `tools` emission, both of which are simple transcription errors indicating the tail of the file was summarized rather than transcribed.

3. R005 — for DroppyCode/App/AppModel+Hydra.swift (1082 lines) the landing/merge/watchdog machinery is genuinely well covered and verified accurate, but three shipped behaviors in the first 400 lines are absent entirely (setHydra adopting the remembered pair's lead model and effort, the keepsModel discard of worker model/effort on provider fallback, and the whole head-profile routing precedence in insertHydraHead). The HydraCookbook+Resolve.swift section (69 lines) also inverts the catalogue tie-break direction, which suggests the comparator was read but not evaluated.

## Cross-cutting findings (10)

1. Hydra is ON by default on a fresh install (AppSettings.swift:743, hydraEnabled default true) and new chats inherit hydraEnabled = true from UserDefaults key "hydraDefaultEnabled" (Library.swift:195-201). The Windows port must ship the same defaults or the whole feature silently changes state.

2. Every Hydra persisted type decodes tolerantly through `container.value(_:default:)` (Coding.swift) with deliberate defaults, most importantly HydraHeadInfo.status defaulting to .stopped and never .completed (Hydra.swift:404-406) and HydraPair.maxHeads clamped to 1...8 on decode (Hydra.swift:90). The port's schema needs the same per-field fallbacks rather than a strict decoder.

3. Concurrency ceiling: with no pair cap and no Hydra-page cap, eight heads run at once (HydraLaunch.uncappedConcurrency = 8, Hydra.swift:168) and the rest wait. This is a hard product constraint, not an implementation detail.

4. Scratch/path handling is heavily POSIX: isScratchPath (AppModel+HydraMerge.swift:158-173) keys on /tmp, /private/tmp, /var/folders, /private/var/folders, ~/Library and dot-directories directly under home, and relies on NSString.standardizingPath dropping a leading /private only while the file exists. hydraProject (AppModel+Hydra.swift:281-299) requires `expanded.hasPrefix("/")` and walks upward terminating at "/". Neither has a Windows equivalent and no report proposed one.

5. Worktree copy folder naming is the sweeper's contract: "<longest project slug>-<persona slug>[-<round>]-<8 lowercase hex of thread UUID>" (AppModel+Hydra.swift:494-497 and the orphan matcher at :1040-1053). Change the naming on Windows and the orphan sweep either deletes nothing or deletes the wrong folder.

6. Three separate code paths parse the SAME lead-authored strings, so the writers and readers must stay byte-identical in the port: Hydra.swift reportMessage/landingLine -> HydraReportsPopover.parse; AppModel+HydraMerge merge note -> HydraMergePopover.parse (files line regex requires U+2212 MINUS and backticked target/branch); HydraPrompts fallbackPolicy fence -> HydraDelegationStream/delegations. A paraphrased prompt or an ASCII hyphen breaks the UI silently.

7. The fence dialects diverge on purpose and the port must keep both: Hydra.swift delegationOpener accepts ```hydra, ```hydra-sent and ```hydra-leaving (Hydra.swift:996-998), while the streaming reader and the hand-written state machine accept only ```hydra and ```hydra-sent (HydraDelegationStream.swift:17 and the 16-state readOpener).

8. Native-provider wiring is three different shapes from one HydraLaunch: Claude `--agents` JSON (keys description/prompt/background/tools/model/effort, model skipped when == "default"), Copilot customAgents (name/displayName/description/prompt/infer/model/reasoningEffort, model skipped when == "auto", tools NEVER emitted), Codex config overrides (agents.max_concurrent_threads_per_session, agents.default_subagent_model, agents.default_subagent_reasoning_effort, features.multi_agent always true, features.multi_agent_v2.expose_spawn_agent_model_overrides only with routable profiles). Hydra.swift:742-846.

9. GitLab merge requests do not go through the shared forge layer: `glab mr create --title <t> --description <b> --yes [--repo <web url, user/password stripped>] --source-branch <s> --target-branch <t>` with a 300 s timeout, preceded by `glab auth status` (30 s), one auth retry and one `--recover` retry (AppModel+HydraMerge.swift:329-377). The Windows port needs the same flag set and the same two retry conditions.

10. The auto-merge never checks out a branch: it write-trees the team's paths, commits with commitWork(tree, parent: head), and moves the ref with updateRef(expecting: ownBranch ? nil : head) after re-reading commitHash (AppModel+HydraMerge.swift:213-243). Any port that uses `git checkout`/`git commit` here changes user-visible behavior.
