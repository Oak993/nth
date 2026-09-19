# Port impact register

What the Windows port must do differently, derived from adjudicating every critic finding
against the pinned source. One entry per resolved finding that carries a port consequence.

These feed directly into the parity matrix and the platform adaptation register.

Source: the repair pass. 264 entries.

1. [providers-and-protocols] C1: sendsVersion:false omits the `jsonrpc` member (R042/R044 left it unresolved): Build the envelope per connection. Codex app-server frames carry NO `jsonrpc` member; ACP and Copilot must carry `"jsonrpc":"2.0"`. Reproduce the params-elision rule and the per-connection integer id counter starting at 1.

2. [providers-and-protocols] C2: PlanLimits.Window.detail looks unused; R041 asks who sets it, R040 answers for Z.ai; never reconciled: Keep `detail` on the model, render only when present, produce it for Z.ai alone with those two exact formats. Do not invent detail strings for any other provider.

3. [providers-and-protocols] C3: which readers call PlanLimitsReader.windowTitle(minutes:): Port as one pure function with those five literals, the inclusive 40320...44640 range and the max(1,...) clamp; call from exactly those three readers. Z.ai derives minutes from a unit code (ZaiAPI.swift:113-119: 3->*60, 4->*1440, 6->*10080, 5->*43200, else nil, each only when number > 0).

4. [providers-and-protocols] C4: R044's header claims session delete was verified live, but no session.delete call exists: Do NOT implement Copilot session deletion. Session state accumulates in the CLI's own store and Droppy Code never prunes it. Reproduce that, including the unbounded growth.

5. [providers-and-protocols] G1: ClaudeSession handshake argv is not a delta of the session argv: Spawn the catalog/command probe with that exact 9-token vector. The session vector would hang the probe on --permission-prompt-tool stdio and leave a session file behind on every catalog refresh. Keep the 20s watchdog, the double terminate, and the literal request_id "handshake".

6. [providers-and-protocols] G2: handshake() requires a non-nil `commands` array or it throws, which also kills the model catalog: Reproduce the coupling: one probe, one guard, missing `commands` => both outputs lost. Do not split models from commands. Reproduce both error strings verbatim including trailing periods, the try?-swallow, and the unset attemptedCatalogs so it retries.

7. [providers-and-protocols] G3: Copilot listModels skips models whose policy.state is "disabled": Apply the != "disabled" filter or the picker offers models the CLI refuses. Keep "unconfigured" models (annotated, not filtered). Use U+00D7 and U+00B7 exactly. The Auto row is prepended unconditionally and never filtered.

8. [providers-and-protocols] G4: Copilot readPlanLimits has no feature note - skip rules, percent formula, date fallback, second request: Port all four skip conditions, both percent branches with the exact clamping asymmetry, the two-attempt date parse, the nil-on-no-windows return, and the second getCurrentAuth round trip on the same connection before close.

9. [providers-and-protocols] G5: Copilot authStatus non-happy paths undocumented: Distinguish 'cannot reach the CLI' (.unknown) from 'reached it, not signed in' (.signedOut). A Windows spawn failure or missing copilot.cmd must map to .unknown, never .signedOut. Reproduce the "GitHub" fallback base, the " · no Copilot access" suffix and the protocol error string with its embedded backticks.

10. [providers-and-protocols] G6: the opencode auth fallback is `result.succeeded ? .signedOut : .unknown`, not unconditional .signedOut: Carry the exit status into this decision - a non-zero `opencode auth list` must surface as .unknown, not 'Not signed in'. Match the literal U+25CF code point, strip ANSI first (Windows CLIs emit VT into pipes), keep the case-sensitive "Environment" section cut.

11. [providers-and-protocols] G7: the nine Command Code seed ids and names were never recorded, though they are a persisted identity and an equality key: Transcribe all nine rows byte-for-byte including ampersands, U+00B7 separators, '1M' casing and effort arrays - the seed doubles as an equality sentinel, so any drift silently changes when the live catalog is fetched. Same pattern at ProviderRegistry.swift:463 (== [copilotAuto]) and :465 (== piSeed, the single 'default' row with efforts [off,minimal,low,medium,high,xhigh,max], defaultEffort "medium").

12. [providers-and-protocols] G8: Antigravity's runtimeMode/interactionMode are written every send and never read: Reproduce the behavior, not the intent: a mid-thread mode change cannot reach a running `agy`, and even a restart uses the original modes while picking up the updated model/effort. Contrast PiSession.swift:279-288, where the same switch does take effect because the gate file is rewritten every turn. Carry the dead properties if porting literally, but do not wire them up.

13. [providers-and-protocols] G9: ACP applyConfigOptions nils effortConfigID and fastConfigID at the top of every call: Reproduce the reset and its asymmetry exactly - mode and model ids survive a partial update, effort and fast do not. Do not repair it by resetting all four or none; either change alters which sliders disappear.

14. [providers-and-protocols] G10: ACP config_option_update, agent_thought_chunk, plan and available_commands_update are handled but never named: Treat the ACP agent as able to push a live model/effort/Fast change mid-session, and route config_option_update through the same path as the set_config_option reply (sharing the G9 reset). Let available_commands_update clear the menu on an empty array, emit todos from `plan` even when the parse yields nothing, and do NOT feed ACP usage_update into the spend ledger.

15. [providers-and-protocols] G11: OpenAICompatibleSession cancellation is uncovered - interrupt/stop/finishInterrupted and approveAllRemaining: No child process exists here, so the process-kill path cannot be reused. Cancel the turn task, round task and SSE drain, and critically resolve every outstanding approval promise as a DECLINE - an in-flight permission dialog is silently answered 'no' on stop. Reproduce the per-turn reset of approveAllRemaining so 'Approve for turn' stays honest, the isRunning composition, and the literal tool result "Stopped.".

16. [providers-and-protocols] G12: refreshCredits lacks refreshPlanLimits's watchdog and defer, and leaves creditsFetchedAt on an empty key: Reproduce both asymmetries rather than unifying them: the credits spinner can hang where the plan-limits spinner cannot, a forced credits refresh during an in-flight one is a no-op, and clearing an empty key leaves the stale timestamp so the next non-empty call is still subject to the 60s gate.

17. [providers-and-protocols] T1 (thin): R044's static half, CopilotSession.swift:400-475: Implement the three static reads against one shared connect helper including the temporary-directory working dir. Each opens and closes its own process, so the port pays a `copilot` spawn per Settings refresh, per catalog load and per usage-panel open. Do not optimize with a cached connection - that would change when models.list reflects a plan change.

18. [providers-and-protocols] X1: three JSON-RPC dialects on one connection class, with different envelopes and framings: One framer with two modes and one envelope switch, wired per connection: Copilot => contentLength + jsonrpc; ACP => lines + jsonrpc; Codex => lines + NO jsonrpc; Pi/Command Code/Antigravity/Claude => lines, not JSON-RPC. On Windows the lines framer must split on 0x0A only and must tolerate a trailing 0x0D reaching the JSON parser, because the source does not strip CR.

19. [providers-and-protocols] X2: -32601 messages differ by provider and must not be unified: Keep four distinct emission points, not one helper with one string. The product name is embedded in two wire messages, so exact parity means keeping the literal 'Droppy Code does not support ' there.

20. [providers-and-protocols] X3: the droppy-code client identity rides four handshakes on different key paths: Five carriers, each with its own shape. Two are ids the app parses back itself (ClaudeSession.swift:337-338 matches pendingControl by id string; PlanLimits.swift:278-284 switches on the two literal ids), so they are a contract with the app's own reader - changing them breaks response routing, not just a label.

21. [providers-and-protocols] X4: RuntimeMode/InteractionMode rawValues are an on-disk contract with the shipped TypeScript: Keep the camelCase raw values byte-identical on both sides of the file. The gate file is the only channel, so a serializer that lowercases or snake_cases enum names would silently turn Full access into Supervised. Hand-write the gate object rather than using a generic enum serializer, and keep write-temp-then-rename semantics because the extension polls while the app rewrites every turn.

22. [providers-and-protocols] X5: four 'install a helper file only when the bytes differ' patterns: Three writers and two scratch trees to relocate to the Windows data dir. The PiCLI hard-coded "Droppy Code" vs AppInfo.name split must be decided deliberately, since the path is handed to `pi -e <path>` at launch. Keep the byte-compare-before-write and the atomic write - the CLI may be reading the file concurrently.

23. [providers-and-protocols] X6: runtime-mode changes reach providers three ways and only some survive without a restart: Do not assume a uniform 'apply mode' path. Antigravity and the ACP launch flags require a process restart; Copilot requires a session restart only across the Auto boundary; Claude, Copilot (non-Auto), ACP, Codex, Pi and Command Code apply live. Document this once so the UI knows which switches force a relaunch.

24. [providers-and-protocols] X7: how many providers can interrupt over the wire, and what replaces SIGTERM on Windows: Five wire interrupts port directly. Windows has no SIGTERM with delivery semantics; TerminateProcess is the practical equivalent and behaves like SIGKILL. That is acceptable for parity BECAUSE the source already relies only on server-side or on-disk persistence of completed turns (comments at AntigravitySession.swift:166-173 and CommandCodeSession.swift:232-233) and neither provider flushes anything on the signal. Preserve the flag-before-kill ordering so the chat does not read 'Antigravity stopped unexpectedly'. Observable difference to accept: the source's exit code is 130, Windows reports whatever TerminateProcess was given, so any code keying on 130 must be relaxed.

25. [providers-and-protocols] X8: MCP configuration reaches providers by several different channels from the same settings: Seven channels to implement and keep distinct; do not collapse them into one config writer. Windows specifics: the Antigravity id list must stay as separate argv elements or command-line quoting will mangle them; both env-var channels must survive environment construction; and the Codex mcp_servers retry path must be reproduced verbatim including both substring matches, because it is the only thing keeping an older `codex app-server` usable.

26. [providers-and-protocols] X9: two UserDefaults keys plus an alternate suite carry the provider cache, with a one-line migration: Carry the two persisted keys and the alternate-suite key verbatim so an installed build's data still loads. The whole-blob rewrite means no per-provider partial writes - a crash mid-write loses every provider's cache together, which is upstream behavior. Run the orderedEfforts remap on every load, not once: it is the only legacy normalization and there is no version stamp on the blob. Reproduce the CaptureRun suppression so capture runs never touch the real cache.

27. [mcp] C1. R037: 'Claude, Codex and Gemini omit an empty env or headers object entirely': gemini-settings.json must write "headers": {} for every remote server (every OAuth server has empty headers, since MCPStore registers routes with headers: [:] and .oauth resolve sets only url+oauthUpstream, MCP.swift:164-167). copilot.json must always carry headers on remote and env on local, plus tools:["*"] on both. claude.json and codex.json must omit when empty. Do not normalize the four writers.

28. [mcp] C2. R038: value(for:of:) is 'draft, else Keychain for secret, else stored value, else defaultValue': Strict if/else-if on field kind. Under a capture run or a missing credential-store item a secret field shows field.defaultValue ?? "", never the plaintext in mcp.json. Keep all three helpers separate.

29. [mcp] C3. R037 leaves the 240s mcp-remote deadline unresolved though its own notes settle it: Implement the branch exactly but do not design any 240s wait into built-in server UX. The real OAuth retry comes from the HTTP path: MCPProbeError.unauthorizedOAuth at MCPProbe.swift:254, caught at MCPStore.swift:323-333.

30. [mcp] G1. MCPCatalog field kinds never recorded: Port every field's kind verbatim. "instance", "root", "path" go to plaintext mcp.json; the other nine to DPAPI storage. Treating .path as secret breaks filesystem/sqlite; treating postgres/mongodb "url" as text leaks a DSN to disk.

31. [mcp] G2. Callback page paraphrased, not captured verbatim: Ship byte-for-byte including the 1296-char path, the dark color-scheme meta, the 1500ms window.close and the exact CSS. Reproduce the three status/statusText pairs and the four headers in order. The em dash in the failure <title> is upstream text.

32. [mcp] G3. MCPOAuthError base+detail composition rule missing: Carry base and detail separately and render per-case. callbackFailed and network must not prefix their base when a detail exists.

33. [mcp] G4. scopes_supported omitted; only authorization_servers[0] used: Read scopes_supported and thread it into both scope sites; use only authorization_servers[0]; keep the 2-candidate and up-to-4-candidate orders; accept a bare resource_metadata.

34. [mcp] G5. serverInfo field path never named: Persist only serverInfo.version; null when absent. Send clientInfo.version "1.0". Cap tools/list at 10 pages.

35. [mcp] G6. 'unknown error' fallback missing from UI strings: Port both fallbacks as the bare string but keep the two renderings distinct; hard-code the four MCPHub prefixed literals rather than deriving them. The `brew install uv` string is the one place in this table needing a Windows-specific substitute.

36. [mcp] G7. 30-second per-request HTTP timeout in MCPProbe.postJSON absent: Do not unify. The probe's remote leg is 30s flat while the hub's varies 30/60/120, and the proxy holds an hour-long upstream request so SSE sessions survive.

37. [mcp] G8. proxy-port written as a side effect of the first read of MCPProxy.port, which happens inside resolve(): Still make the port lazy and self-persisting — resolve() is a legal second trigger and the ordering rests only on MCPStore.init's statement order. Reproduce the 48123 fallback and the exact route URL shape.

38. [mcp] G9. repickPort rewrites proxy-port but nothing re-runs writeAll; configs point at a dead port: Reproduce, do not fix: the port must not re-export on repick. On Windows the same collision arises when two builds share %LOCALAPPDATA%\<AppName>\mcp\proxy-port.

39. [mcp] G10. The bridge never kills a spawned child: Reproduce: the bridge .mjs must not kill children. On Windows the leak is worse — an orphaned npx/node child of a Pi or Command Code host survives until Task Manager, with no process-group kill applied. Record as an upstream defect.

40. [mcp] G11. The bridge omits MCP-Protocol-Version that MCPHub and MCPProbe both send: The bridge is shipped text: copy it character for character, including the missing header, the lowercase session header, the version-less clientInfo and the result/error-keyed SSE acceptance. Normalizing the three clients changes what remote servers see from Pi and Command Code.

41. [mcp] G12. Proxy forwards the client method verbatim; copies back only 3 response headers, dropping WWW-Authenticate: Relay any method. Forward exactly those 5 request headers, copy back exactly those 3 plus framing. 502 never 404 for an unknown route. Reproduce the 12-case phrase table. OAuth discovery cannot work through a proxy route because the challenge header is stripped.

42. [mcp] G13. readBody on early EOF relays a truncated body: Reproduce the silent truncation — do not add a 400 for short bodies. Reproduce the four refusals with exact statuses and exact JSON bodies, the max(0,...) parse, and the Latin-1 head decode.

43. [mcp] G14. MCPHub's failed set is read only by tools(); restart policy is stdio-only: Scope the restart policy to stdio. Do not add a failed-set check to call(). Do not clear initialized anywhere but stop(). The asymmetry is observable: a listTools failure hides the server from the tool list yet a direct call relaunches it.

44. [mcp] G15. Probe success writes states[] unconditionally but the connection fields only if a row exists: Reproduce the guard exactly — do not hoist the three writes out and do not move the states[] write in. The divergence between the connected badge and enabledServers is upstream behavior.

45. [mcp] G16. SecItemUpdate carries no kSecAttrAccessible; only the Add fallback sets it: Windows DPAPI has no accessibility attribute, so that attribute has no analogue — but reproduce the update-then-add shape, the trimming rules, the empty-value-deletes behavior, the exact account string "MCP <serverID> <fieldKey>", and the two reserved field names, which are persisted keys.

46. [mcp] G17. Dead/unreachable code nobody classified: MCPProxy.stop, refreshIfNeeded, Route.headers, the isOAuth branch: stop() may be a no-op or omitted, but never shut the proxy down where Droppy does not. Add no proactive token refresh. Keep Route.headers in the model while knowing nothing populates it. Keep the managed.json migration, unused parameter and all, so an upgrading user's ~/.cursor/mcp.json, ~/.config/opencode/opencode.json and ~/.gemini/settings.json are cleaned once.

47. [mcp] G18. Callback listener sets requiredLocalEndpoint but not requiredInterfaceType: Bind to 127.0.0.1 with an OS-assigned port and no interface-type constraint. Reproduce the 300s timeout, GET-only and /callback-only checks, the 1 MiB head cap, the outcome order, the send-page-then-resume ordering, and the 32/24-byte sizes with S256.

48. [mcp] T1. R037's MCPOAuth.swift entry is thin for a 929-line file: Treat MCPOAuth.swift as fully re-read. The port needs the exact registration body, the exact query-item and form-field orders, the client-reuse issuer check, and the single-flight refresh gate.

49. [mcp] X1. Path literals: AppInfo.name, Storage.root, MCPPaths.directory: %LOCALAPPDATA%\<AppName>\ for Storage.root, %LOCALAPPDATA%\<AppName>\mcp\ for MCPPaths.directory. Putting mcp.json or custom-mcp.json inside mcp\ silently loses every connection and custom server.

50. [mcp] X2. Directory-case collision: bridge writes .../<AppInfo.name>/MCP while MCPPaths.directory is <Storage.root>/mcp: Windows is case-insensitive, so the port is in the collapsed case: write droppy-mcp-bridge.mjs into the same %LOCALAPPDATA%\<AppName>\mcp\ folder as the provider configs, and derive it from the OS app-data root rather than the capture-aware storage root. Keep the write-only-if-changed check.

51. [mcp] X3. Complete file inventory under MCPPaths.directory and what writeAll deletes: Port all six names. Delete exactly the four provider configs on an empty enabled list. Never delete proxy-port. Keep managed.json read-and-delete-once.

52. [mcp] X4. The consumer side of every generated artifact is unrecorded: These are the exact argv vectors and env vars. Three are easy to get wrong: --allowed-mcp-server-names takes ids as separate argv elements; --additional-mcp-config takes the whole compact JSON as ONE argument, not a path; gemini-settings.json belongs to Antigravity via GEMINI_CLI_SYSTEM_SETTINGS_PATH.

53. [mcp] X5. The env var carrying the bridge path is never named: Both names are persisted contract with shipped JavaScript and keep the DROPPY_CODE_ prefix exactly. On Windows the bridge path must be importable by Node — a bare drive-letter path fails ERR_UNSUPPORTED_ESM_URL_SCHEME in await import(...), so hand Pi and Command Code a file:// URL. That is a genuine porting decision, not a parity break, since the macOS POSIX path works as-is.

54. [mcp] X6. MCPHub's only consumer left unresolved: Scope the hub to the in-app model-API provider only. Wire terminateAll to the port's app-quit path so no npx/node child survives the app.

55. [mcp] X7. Token expiry mis-stated as unconditional; the proxy's 401 replay is a byte-identical retry: Reproduce all of it including the useless replay. No force-refresh parameter on the token getter; no synthesized expires_at when the provider omits expires_in. Keep the 90-second window and the single-flight gate keyed by serverID.

56. [mcp] X8. Custom-server decoder: Variable has no default for isSecret; blast radius is every custom server: Reproduce the failure mode: a variable missing isSecret, or a server missing id/name/kind, drops the entire customs list. A port that defaults isSecret to false would load files Droppy rejects, and a user moving between them would see different custom servers. Keep ISO-8601 on mcp.json only; sorted-keys pretty printing on both.

57. [hydra] C1: R031 state_owned says verificationMarkers has 28 markers (its own notes say 34).: Ship all 34 strings in this order; keep the trailing space in 'tsc '; keep substring (not word-boundary) matching so 'gradle'/'pytest'/'eslint' fire inside longer words; keep the identity return and the exact appended sentence.

58. [hydra] C2: R005 says heads run uncapped with no pair/page cap; R031 says hasRoom uses maxHeads ?? 8.: Model two distinct values: (1) the nil-able launch cap, which is what goes into prompt text and Codex config; (2) a Droppy-run scheduling ceiling of 8 used only for app-run heads. Do not clamp native heads to 8 and do not tell a lead about a cap when none is set.

59. [hydra] C3: R005 says model(named:) breaks ties toward the LATER position in registry.models(for:).: Tie-break to the lowest index in registry.models(for:). Compare versions parsed from the display name only, with absent = -1. Return on the first names[] entry that matched anything.

60. [hydra] C4: R031 implies the Copilot worker gets a tools list and lists 'tools' among emitted Copilot keys.: Emit no tools key for any Copilot agent. Keep Claude's scout tools array exactly as listed and keep the two different sentinels: "default" (Claude), "auto" (Copilot).

61. [hydra] C5: R005 leaves finishHold unresolved; R013 fixes it at 2.0 (cited as HydraPanel.swift:40).: Auto-clear delay is exactly 2200 ms, stage hold exactly 2000 ms; derive the former as hold + 200 ms. Re-check the three conditions after the sleep and perform the five-part runtime cleanup.

62. [hydra] G1: HydraRunMetricsView.swift is documented as shipped UI by R014 but may be unreachable.: Do not port the file. Record it as dead in the port inventory so it is not re-queued; porting it would add a card macOS never shows.

63. [hydra] G2: setHydra(_:for:) rewrites the chat's model and effort when Hydra is turned on.: The per-chat toggle is not pure: it re-pins hydraPairID, writes the app-wide hydraDefaultEnabled key, and can change model and effort. Reproduce the catalogue guard and the `?? existing effort` fallback exactly, and the else-if effort-only branch.

64. [hydra] G3: keepsModel discards workerModel and workerEffort when the pair's worker provider is unavailable.: On worker-provider fallback: drop workerModel, drop workerEffort, then run the tempering path as if the pair set no effort. Post the note once per pair id per lead with the exact wording. Keep the countsAsInstalled/isInstalled asymmetry between the predicate and the wording.

65. [hydra] G4: effort tempering is gated on hydraTempersHeadEffort && !elsewhere; no report stated the gate.: Temper only when the setting is on AND heads run on the lead's own provider. With heads elsewhere and no pair effort, send no effort at all. Use integer (count-1)/2 for the middle rung and the literal "medium" when present.

66. [hydra] G5: insertHydraHead head-profile routing precedence is undocumented in the family.: Implement all four precedence chains exactly including the native short-circuits. Use U+00B7 as the title separator. Persist the profile's canonical name.

67. [hydra] G6: AppModel+HydraMerge title(_:) prefixes every merge note title and record detail with '<project>: ' when several projects merge.: Apply the project prefix to note titles and record details, not only to the pill's stage words, and reproduce the catch-branch asymmetry.

68. [hydra] G7: mergeStatus pending text is entirely missing from R031.: Emit all six shapes verbatim including the nil case, and keep the .failed branch's omission of the pending sentence. Compute the unmerged count as a distinct-path set with the same filters. Use the OS locale short-time format for when().

69. [hydra] G8: teamStatus also emits '(working)' with no interval and '(done)' with no detail.: Ship all nine shapes, truncating integer minutes, and the plain ', ' join with a trailing period.

70. [hydra] G9: the prompt corpus (howToWork, howToReport, briefRule, verificationRule, projectRule, autoMergeRule, reviewRule, reportStyleRule, workerPrompt, scoutPrompt, policy(for:_:), fallbackPolicy) was paraphrased, not transcribed.: Store these as exact string resources byte for byte, including U+201C/U+201D in quoted(), U+2212 in landingLine, U+00B7 middle dots, and the em dash in merge note head lines. Do not reflow, fix grammar or localize. The fenced ```hydra example inside fallbackPolicy must survive the port's own templating.

71. [hydra] G10: fallbackReportNote opener and the two `more` variants are not quoted anywhere (critique cites Hydra.swift:1259-1262 / 1255-1258).: Reproduce the assembly including the unconditional space and the \n\n---\n\n terminator, plus fallbackTurnNote's empty-string case and fallbackPreamble's bare '---' substitution.

72. [hydra] G11: delegations(fromBody:) returns nil, not [], when the JSON parses but every entry is rejected (critique cites Hydra.swift:1000-1005).: Return a three-state result (nil / empty / non-empty) from the body parser and drive the fence-walk retry off nil. Collapsing 'all rejected' into an empty list would give the lead a silent no-op instead of unreadableBlockMessage.

73. [hydra] G12: a present-but-empty 'name' shadows the 'head' alias, and 'project' shadows 'repo'.: Check the alias only when the primary key is absent or is not a string, never when it is an empty string; a port that falls through on emptiness would name heads the macOS app leaves unnamed.

74. [hydra] G13: AppSettings defaults for this family are stated nowhere in the four reports.: Ship these exact defaults; Hydra ON by default is the most consequential. Keep hydraMaxHeads genuinely absent (not 0, not 8) when unset and clamp on read as well as write. Add the three keys the critique omitted.

75. [hydra] G14: migration behavior of the per-chat switch is unstated.: Add the explicit flag with a false decode default and the force-true migration, and set it true on load so records self-heal. Seed new chats by reading the raw settings key at construction time, not from a cached snapshot.

76. [hydra] G15: hydraPairs is decoded as [Lenient<HydraPair>] and compacted, so a bad pair is dropped rather than failing the load.: Decode the pair list element-by-element discarding failures, with provider as the only strict field. A strict whole-list decoder would wipe every pair when a newer build wrote an unknown provider; a fully-lenient one would resurrect providerless pairs.

77. [hydra] G16: cross-file defect -- withoutLeadInstructions' nine leadPrefixes miss two closing sentences.: Reproduce the nine prefixes and the last-paragraph rule exactly, including the failure. Do not add prefixes for (4), (5) or reportStyleRule -- that would change what the popover shows.

78. [hydra] G17: 'Clear finished heads' is the only heads-popover action that does not close the popover.: Keep the popover open after clearing and close it on the other two actions. Reproduce the header string including the waiting suffix and the two visibility gates.

79. [hydra] G18: the Hydra right-click items carry checkmark state and the Settings-off item performs two writes.: Render the two per-chat items as checkable items driven by isOn/!isOn and keep the Settings-off branch's double write. Reproduce the tooltip assembly with the U+00B7 separator.

80. [hydra] G19: makeHydraCopy awaits the lead runtime's pending hydraLanding before capturing the tree.: Serialize copy creation behind the lead's pending landing and keep the 3 s per-checkout capture cache with in-flight coalescing, or a burst of heads each pays for a full add -A + write-tree. Keep the exact starting-point commit message.

81. [hydra] Cross-cutting 5: worktree copy folder naming is '<longest project slug>-<persona slug>[-<round>]-<8 hex>'.: Keep writer and sweeper consistent: space-only slugging on write, longest-prefix + 8-hex + optional numeric round + persona-set membership + 600 s grace on sweep. On Windows, any sanitizing added to the writer's slug MUST be added to the sweeper or the orphan sweep stops matching and leaks worktrees.

82. [hydra] Cross-cutting 4: scratch/path handling is heavily POSIX with no Windows equivalent proposed.: ENGINEERING DECISION required: the source offers no Windows form. The port needs a re-expressed root set (%TEMP%, %LOCALAPPDATA%\\Temp, the app's worktrees folder, %APPDATA%/%LOCALAPPDATA% in place of ~/Library) plus a dotted-folder-under-profile rule; the absolute-path test must accept a drive-letter or UNC root instead of a leading '/', and the upward walk must terminate at the drive or share root. The /private trick has no analogue and should be dropped, not emulated.

83. [hydra] Cross-cutting 9: GitLab merge requests bypass the shared forge layer.: Reproduce the vector order, both timeouts (30 s / 300 s), the throw-means-nil quirk in the auth pre-check, both retry predicates and the substring heuristics including all four authentication co-words.

84. [hydra] Cross-cutting 10: the auto-merge never checks out a branch.: Implement with plumbing (write-tree, commit-tree, update-ref with an expected old value) rather than porcelain. Any use of git checkout / git commit here changes user-visible behavior.

85. [hydra] Cross-cutting 7: the fence dialects diverge on purpose (critique cites Hydra.swift:996-998).: Port both dialects and both readers, including the state machine's exact acceptance (three backticks with extra backticks tolerated at state 3, optional spaces/tabs, case-insensitive 'hydra', optional '-sent', optional spaces/tabs, optional CR, then LF), and the two card timings.

86. [hydra] Cross-cutting 8: native-provider wiring is three shapes from one HydraLaunch.: Emit all three shapes exactly, including Claude's scout-only tools array, Copilot's absent tools key, the two different default-model sentinels, and Codex's always-present empty 'agents' object.

87. [runtime-and-state] C1: unsentSessionID hold-back claimed universal; API-key branch contradicts it: Keep two ID-recording paths. CLI/ACP: in-memory hold-back promoted only after a successful send. API-key: unconditional write when start() returns. Do not unify.

88. [runtime-and-state] C2: Shell excludedKeys 13 vs 15, readers 51 vs 52: Port 52 / 25 / 15 exactly. Semantics (Shell.swift:139-143): anything not in readers makes the whole command a writer, and a chain with one writing part is writing.

89. [runtime-and-state] C3: SimpleDiff 200_000 cap claimed identical to ThreadRuntime.fileEdits cap: Apply an input-size gate in the diff producer and an output-size gate at both ThreadRuntime cappers. One gate on one operand changes which edits keep their hunk.

90. [runtime-and-state] C4/G17: capture-run early return called a short-circuit of the entire launch: Keep the ordering if capture mode is ported. If dropped, still keep the CaptureRun.defaults indirection, since AppModel.notify and threadNeedsAttention hard-bail on the same flag.

91. [runtime-and-state] C5: R007 implies handedOff's UUID feeds the notice wording: Do not build UI on the payload. The three outcome strings interpolate nothing.

92. [runtime-and-state] G1: ProviderEvent switch (2079-2195) never described case by case: Port all 30 arms with these exact guards and orderings; the full per-case transcript is in the written report.

93. [runtime-and-state] G2: API vs CLI session-start differ in three unreported ways: Reproduce both branches separately, including the API branch's missing start-guard, missing hold-back, missing notice and never-firing retry. Port the two lostConversation probes as case-insensitive substring tests on the CLI's message text.

94. [runtime-and-state] G3: unreachable `default: DeepSeekSession` arm in the API session factory: Write the API factory as an exhaustive 3-way dispatch with an assert/throw fallback. A fourth API provider added to isAPIKeyBased would otherwise be silently instantiated as DeepSeek.

95. [runtime-and-state] G4: handleLocalCommand's recordCommand side effect feeding the recency ranking: Persist a per-provider list capped at 100 under "recentSlashCommands", written from all three sites, and reproduce the three-key comparator exactly.

96. [runtime-and-state] G5: interrupt()'s .starting fast path: Port three distinct behaviours (idle no-op, .starting drop-and-finish, running interrupt plus 8 second watchdog). The 8 seconds is a literal.

97. [runtime-and-state] G6: resolve(_:option:) mutates ProposedPlan.state: Port the middle write gated on all four conditions, in that order. Any approve-role option accepts; every other role dismisses.

98. [runtime-and-state] G7: startTurn's implicit enqueues: Port all four exits and reproduce the hydraHeads/hydraBrief loss on deferral rather than fixing it.

99. [runtime-and-state] G8: warmSession leaves warmup non-nil after a throwing ensureSession: Reproduce the sticky per-runtime latch. Do not add a reset on failure.

100. [runtime-and-state] G9: discardThreadState teardown ordering vs releaseIdleRuntime: Port both orders and the persistenceEnabled latch, since the ordering only has meaning through it. Gate idle release behind all eight conditions.

101. [runtime-and-state] G10: AutoContinue's user-facing controls and post-wait wording: Ship the automatic resume plus the strings the wait actually writes (AutoContinue.swift:94, :95, :103, :104) and the resumesAt gate on idle release. Do NOT ship Cancel / New thread or the three outcome strings unless the owner chooses to depart from parity; flag it as a decision.

102. [runtime-and-state] G11: AutoContinue waits never cancelled or re-armed at teardown: Reproduce: process-lifetime waits, no persistence, no cancel on delete, no relaunch recovery, notice text surviving on disk.

103. [runtime-and-state] G12: R033's counts for excludedKeys and readers: See C2.

104. [runtime-and-state] G13: diffLimit vs editDiffLimit are two constants: See C3.

105. [runtime-and-state] G14: chatZoom's shipped default left undocumented: Port the 7-element table verbatim, default index 3, clamp on read under "chatZoom".

106. [runtime-and-state] G15: codesign requirement never quoted; leaf[subject.OU] absent: None of it ports. Windows needs its own trust gate (Authenticode pinned to the publisher plus a SHA256SUMS hash); record in COMPATIBILITY.md that team-ID pinning has no analogue.

107. [runtime-and-state] G16: Haptics.perform filed as an open question rather than a platform dependency: Add NSHapticFeedbackManager to the macOS-dependency list beside hdiutil/ditto/spctl/SecStaticCode. Keep a single no-op shim at the same call sites rather than deleting them.

108. [runtime-and-state] G17: capture-run early return does not short-circuit applicationWillTerminate: See C4.

109. [runtime-and-state] Thin: R008's 32 KB for 3637 lines, with the event switch, both start branches and four mutating methods left behaviourless: Treat this repair file, not R008, as the protocol and session-lifecycle spec for ThreadRuntime.

110. [runtime-and-state] CC1: Cmd+1..9 hardcoded in AppCommands and reserved in Shortcuts: Keep the two lists in sync as one unit but translate the table to Windows virtual-key codes. The persisted "keyboardShortcuts" blob holds raw NSEvent.ModifierFlags bits and macOS key codes and is unreadable on Windows: define a new format and start from defaults. Keep both refusal strings verbatim.

111. [runtime-and-state] CC2: two RuntimeMode defaults ship and the difference is deliberate security behavior: Two distinct constants. The decoder fallback must swallow type mismatches too and land on supervised; reusing the settings default silently grants full machine access on a corrupt field.

112. [runtime-and-state] CC3: R003's ChatThread open questions answered by Library.swift: Use "New thread" for all three roles and "hydraDefaultEnabled" as the key; reproduce the unconditional hydraEnabledIsExplicit write.

113. [runtime-and-state] CC4: R047's on-disk path questions answered by R033: Choose the Windows layout deliberately and keep the two-root split (app data vs home state directory), because worktree paths are handed to git and appear in user-visible strings.

114. [runtime-and-state] CC5: three notions of home and defaults coexist: Drop the 'two homes' claim. If capture mode is kept, reproduce exactly which keys are redirected, including ProviderRegistry's split between "recentSlashCommands" (redirected) and the catalog caches (not).

115. [runtime-and-state] CC6: the notification userInfo contract spans three bundles: Port both payload shapes, both identifier schemes and the routing branch order exactly.

116. [runtime-and-state] CC7: notification suppression is a multi-gate system: All four must ship. If the port uses the OS toast pipeline unconditionally, fold gate 3's condition into gate 2 and record the departure.

117. [runtime-and-state] CC8: RecentCache is generational, not LRU: Implement the generational cache exactly; an LRU substitute changes hit rate and memory ceiling. Carry all 17 limits and both cost limits. `value(for:)` needs write access.

118. [runtime-and-state] CC9: em-dash flattening happens on several passes; a port doing it once will double-clean or miss: One idempotent helper called at all 7 sites; keep the byte prescan and keep the en dash. Do not try to deduplicate the passes.

119. [runtime-and-state] CC10: persisted state outside Application Support that the port must relocate: Add the two ProviderRegistry keys to the migration table. "keyboardShortcuts" cannot be carried over at all - its contents are macOS AppKit constants. The four token-activity JSON files sit under Storage.root and move with it.

120. [chat-and-composer] C1. R051 presents passesProseThrough prose-pass-through as shipped; R052 says markdownBlockSelection defaults true. One must be wrong.: SOURCE FACT + ENGINEERING DECISION. The port implements one behavior only: an AppKit-equivalent text surface that always takes its own hit test, shows a pointing-hand cursor over links and an I-beam over prose, and never passes prose clicks through to a whole-reply selection layer. Per-block text selection is always ON. Do NOT implement a whole-reply drag-selection mode; whole-reply copy goes through the row menu's `Copy message` / `Copy as markdown` items (TimelineRows.swift:219-220).

121. [chat-and-composer] C2. R052 (MarkdownView) leaves markdownProseRuns undetermined; R052 (ProseRunView) asserts it conditionally. Neither states the settled consequence.: SOURCE FACT. The port must implement BOTH renderers and switch on isStreaming alone. Finished prose = the single-text-surface renderer (ProseRunView semantics). Streaming prose = the per-block view stack. Code/table/rule always use the block renderer. No user-facing setting controls this.

122. [chat-and-composer] C3. R012 describes the row-expansion reveal as one mechanism; R010 documents TimelineRevealBox as two closures.: SOURCE FACT. Two distinct reveal entry points. `action` must apply the three-way guard (jump button hidden OR pinned to bottom OR distanceFromBottom < viewportHeight) before revealing; `follow` must reveal unconditionally and also set pinned-to-bottom and bottom-anchored-on-growth. Wire the four row expansions to `action` and only the working-indicator chevron to `follow`. Keep WorkGroup's call outside its animation transaction and the other three inside theirs.

123. [chat-and-composer] G1. EffortSlider pair caption strings never recorded.: SOURCE FACT. Emit the two strings byte-for-byte including the U+00B7 separators and the lowercased effort name. Render the caption only when a pair is active AND the effort list is non-empty; a single-level model in a pair shows no caption.

124. [chat-and-composer] G2. `private var title` returns literal "Standard" when efforts.isEmpty, and the title button still renders.: SOURCE FACT. Show the literal "Standard" as the card title for a model with no reasoning levels, with the same 17pt medium type and the chevron rule, above the literal "This model has one reasoning level." sized to the thumb height.

125. [chat-and-composer] G3. The (isMax, fastMode) -> TrackLook.Kind mapping is unstated.: SOURCE FACT. Implement the exact 2x2 mapping; gate `isMax` on `count > 1` so a one-stop track never reads as maximum. Apply the pair gradient ahead of the kind's own fill, and drive supercharged-without-pair at 30 fps and everything else at 60.

126. [chat-and-composer] G4. Model-list and pair-list detail line compositions undocumented.: SOURCE FACT. Reproduce the literal "New chats only" for cross-provider rows once the thread has history, the three-part joined pair detail with the U+00B7 separator, the models component only when the pair carries a custom name, the "Loading models…" empty state, and the 440-point list cap.

127. [chat-and-composer] G5. FollowUpQueueTab reorder gesture parameters and cursor-stack contract missing.: SOURCE FACT + ENGINEERING DECISION. Reproduce the 1-point minimum distance and the global coordinate space, the vertical-only settle, the 32-point fallback height, the 0.25 pair band and the 0.6 unlink fraction. Windows has no NSCursor push/pop stack: model it as an explicit LIFO cursor-owner stack and mirror the same four push/pop sites.

128. [chat-and-composer] G6. The four in-flight git activity labels shown as the BranchMenu title are never recorded.: SOURCE FACT. The branch control's own label doubles as the progress indicator: show the four literals in place of the branch name while running, clear in a finally-equivalent so a thrown error still restores the branch name, fall back to "Detached". Cap the branch list at 25.

129. [chat-and-composer] G7. The Hydra button is gated on `model.settings.hydraEnabled, !thread.isHelper`; R009 recorded only hydraEnabled.: SOURCE FACT. A helper/head thread must never show the team button, even with the Hydra setting on; it still shows the permission menu. The button animates in and out with softAppear when the setting flips.

130. [chat-and-composer] G8. The minimap condition drops the `paneSize == .zero` case.: SOURCE FACT. Show the rail by default on the unmeasured first frame; hide it only once a real measurement comes back below the threshold, and subtract both docked-panel reserves before comparing to 900.

131. [chat-and-composer] G9. The changes-popover lifecycle in ComposerArea is entirely undescribed.: SOURCE FACT. The popover is owned by the composer area, not by the tab, driven by three runtime signals plus four lifecycle hooks. Port the two-anchor arrangement (tab rect, else composer-area rect when the queue holds the slot). Hiding the chat column must close it. 640x540 fixed.

132. [chat-and-composer] G10. Which control does interrupt() vs takeBackRunningTurn() vs interruptAndSend() is unstated.: SOURCE FACT. Three different actions on one running turn; do not merge them. Stop button = plain interrupt. Escape = take back with the four preconditions and the 200-poll/10-second watchdog, falling back to plain interrupt. Return = interrupt-and-send, which becomes queue-behind or dispatch-to-head whenever heads are working and never interrupts in those cases.

133. [chat-and-composer] G11. Key precedence while the suggestion list is open is only half described.: SOURCE FACT. While the list is open, Tab, Return AND Cmd-Return all commit the highlighted item; arrows move the clamped selection; Escape dismisses and latches `dismissedSuggestions`; backspace-at-start alone falls through. History recall must be suppressed while the list is open.

134. [chat-and-composer] G12. The rewindable rule that decides whether revert/edit controls exist at all is unreported.: SOURCE FACT. Compute the rewindable set ONCE per timeline body. Every revert affordance must disappear the instant a turn starts and reappear when idle, and never appear on a provider without rewind support. Reproduce all six confirmation strings verbatim.

135. [chat-and-composer] G13. WorkingBlockView's earlierEntries mode is unreported.: SOURCE FACT. Implement the second mode: same indicator, clock frozen to the segment's first/last entry dates, no thinking rows, bar fed from the segment's own entries. Do not hard-code "Earlier steps"; derive the label from `hasTools` and `finishedAt` exactly as the source does.

136. [chat-and-composer] G14. The empty state's scroll-chrome side effects are missing.: SOURCE FACT. Three-state empty handling. While history loads, render nothing. On entering the empty state, zero the chrome's scroll-travel value and hide the jump-to-end button.

137. [chat-and-composer] G15. The reveal-the-end hook has four call sites, not two.: SOURCE FACT. Wire all four expansions, not two. Fire only on open. Keep WorkGroup's call outside its animation transaction.

138. [chat-and-composer] G16. R012 collapses action() and follow() into one 'revealTimelineEnd action'.: See C3.

139. [chat-and-composer] G17. The bare-domain autolinker regex and TLD table are paraphrased where verbatim is required.: SOURCE FACT. Port the regex character-for-character including the negative lookbehind class, the {2,24} bound and case-insensitivity (.NET Regex accepts it as written). Port all 111 TLDs as a case-insensitive set - not a fuller public-suffix list, or `MarkdownView.swift` and `package.json` start linkifying. Skip runs that are already links or inline code, strip the seven trailing punctuation characters, split at the first slash before the TLD test, prefix https://.

140. [chat-and-composer] G18. The 'Show the message' help string is conditional, not fixed.: SOURCE FACT. A report or merge row with no details is a disabled control with NO tooltip at all.

141. [chat-and-composer] G19. fly() returns before onLanded() under reduce motion and on nil cgImage / NSScreen.main; the armed drop is already consumed.: SOURCE FACT. Reproduce the three early returns and the take-before-fly ordering: under reduced motion (or a failed image/screen lookup) the armed drop is silently discarded and no animation plays, which is correct because the chip is drawn by layout regardless. Do not make chip visibility depend on the completion and do not 'fix' the reduce-motion path.

142. [chat-and-composer] T1. R010 is thin: drops rewindable, earlierEntries, empty-state resets, and does not tie action/follow to callers.: ENGINEERING DECISION. Treat R010 as incomplete rather than wrong. The reveal box must be a stable identity whose closures are refreshed per layout pass, not a value re-created per render.

143. [chat-and-composer] T2. R016 is uneven: TrackEffect maths captured to the coefficient, EffortSliderCard paraphrased; 28x28 hedge unreconciled.: SOURCE FACT. Grip 28x22; send button 28x28.

144. [chat-and-composer] X1. Five unresolved questions ask for TimelineMetrics' literals.: SOURCE FACT. Define the eight metrics once and derive every badge, pill, notice, todo row, merge pill and head pill from them. The port should centralize 96 and 820, but must keep the same numbers at all eight 96 sites and all four 820 sites.

145. [chat-and-composer] X2. Storage.attachmentLimit left unresolved by R015 and R016.: SOURCE FACT. One constant, value 8, reached from the composer, the prompt editor and the storage-side picker alike. Interpolate it into the three notices; do not hard-code the digit.

146. [chat-and-composer] X3. ThreadChangesTab.overlap left unresolved by R015 and R016.: SOURCE FACT. 14 points of tab-behind-pill overlap, shared by both tabs, expressed as a negative stack spacing plus matching extra bottom padding on each tab.

147. [chat-and-composer] X4. AttachmentPreviewSlot vs AttachmentPreviewCoordinator.: SOURCE FACT. Port both, and keep the laziness: rows hold a cheap box; the real popover is constructed on first open and inherits whichever anchor the box had already captured.

148. [chat-and-composer] X5. LinkTextVeil has no call site in R052's files; HydraHeadHoverPopover show/hide callers unknown in R051's.: SOURCE FACT. The veil belongs to the streaming renderer only; do not wire it into the finished-prose surface. The head-hover popover must be driven from BOTH renderers with identical hover logic, hidden on hover-end, on mention-exit and on view disappearance.

149. [chat-and-composer] X6. Because markdownProseRuns is always true, ProseRunView's rendering is what a reader sees for every finished reply; both renderings must exist and switch on isStreaming.: SOURCE FACT. Port both renderers verbatim, including the deliberate visual step at the end of streaming: SF-Symbol task circles and accent dots become ballot-box and bullet characters in label colour, and the quote capsule rule disappears in favour of a 14-point indent. Do not normalize the two. Windows: U+2611/U+2610/U+2022/U+25E6 exist in Segoe UI / Segoe UI Symbol; replace the SF Symbols with equivalent vector glyphs, not with the same Unicode characters, or streaming and finished become indistinguishable.

150. [chat-and-composer] X7. Popover dismissal is implemented the same way five times and must be ported as one mechanism.: SOURCE FACT + ENGINEERING DECISION. Do not collapse these into one helper. The port needs (i) a fixed-content popover primitive that pre-measures once, clips top-aligned and never republishes its size - badge, link, head-hover, attachment, diff; (ii) a live-resizing popover for the prompt editor; (iii) a caret-anchored, focus-preserving suggestion panel sized 340 wide by min(340, 44 + max(n,1)*26); (iv) a dismissal policy per site. On Windows, substitute the NSEvent LOCAL monitors with an app-scoped message filter - they never see other applications' events.

151. [chat-and-composer] X8. macOS primitives with no Windows equivalent, needing one substitution strategy each.: SOURCE FACT + ENGINEERING DECISION. Per primitive: (a) an explicit LIFO cursor-owner stack mirroring each push/pop site one-for-one; (b) a composition-timer abstraction - the veil clock throttled to ~30 fps and paused during scrolling, the AppKit veil left at the compositor's rate; (c) one replicated-dot-field control with the nine literals, used in three places, drawing nothing under reduced motion; (d) a critically-damped spring integrator (m=1, k=170, c=26) run to its settling time over size, position and corner radius together; (e) haptics have no Windows equivalent - drop them silently, do not substitute sound; (f) one text-layout stack that owns its storage explicitly and one measurement path.

152. [chat-and-composer] X9. Three caches key on whole message text and must be sized together.: SOURCE FACT. Port the exact limits; they are tuned against each other (the source records that four streaming slots caused twenty heads to evict each other's parse on every flush). Reproduce the two wholesale flushes as flushes, not LRU eviction - the 301st favicon really does drop all 300.

153. [chat-and-composer] X10. Every editable surface keeps its edit state on ThreadRuntime rather than in the view.: SOURCE FACT. Hold all three on the per-thread runtime object, not in row state. Scrolling a row out of a virtualized list, or leaving and re-entering the thread, must leave the editor open exactly where it was; the thread must not be treated as idle while any editor is open or the draft is non-empty. Clear each edit automatically when the turn or follow-up it points at disappears.

154. [shell-chrome-and-theme] C1: RowDrag.settle API - pastCentre and Bool-returning move closure vs R025's settle(...swap:) with Void closure: 4th optional param pastCentre default 0; move callback returns bool; break on false WITHOUT advancing index or compensation; return whether anything moved; strict `>` including the slot*pastCentre term; caller passes fallbackHeight = rowHeight + 1 = 29 and only re-snapshots peers + fires the alignment haptic when settle returned true (SidebarView.swift:795-800). A void callback reorders forever past a refused move.

155. [shell-chrome-and-theme] C2: R024 says the FloatingSidebarPanel resize is clamped with minSize as the lower bound: Reproduce both arithmetics separately; do not unify. Accept that a sub-240/sub-320 size can reach the settings store and is only floored again on read.

156. [shell-chrome-and-theme] C3: R049 and R053 name two different preference stores without saying they are the same: One settings-store indirection for the whole port. sidebarWidth, sidebarStartsOpen, wallpaperSoftness, wallpaperFile, settledSectionCollapsed, sidebarPanelOrigin and sidebarPanelSize all live in it.

157. [shell-chrome-and-theme] C4: a running Hydra head breathes on its card but not on its helper row: Reproduce the asymmetry exactly: no breath ring on a running head's helper row, ring on its card. Ring colour = persona colour at 0.55, lineWidth 1.5.

158. [shell-chrome-and-theme] C5: R053 calls AppTheme a 25-case enum: 26 themes. Theme ids on disk are the camelCase case names; match them exactly or saved settings break.

159. [shell-chrome-and-theme] G1: resize can persist sub-minimum size; resize clamp anchored on dragOrigin ?? rest: Default resting origin (12, 72). Resize clamps against the live (possibly dragged) origin. Size per tick, origin on release, no animation while dragging.

160. [shell-chrome-and-theme] G2: SidebarHelperRow's head glyph omits isRunning: See C4.

161. [shell-chrome-and-theme] G3: every thread row carries .accessibilityValue(Text(isSettled ? "Settled" : "")): UIA Value = 'Settled' when settled, empty string otherwise; IsSelected + Button roles per the trait line.

162. [shell-chrome-and-theme] G4: R025 calls the PlacementCache fingerprint an 18-field Fingerprint: 17 fields in that order, compared as a whole ordered list; hydraIndex from the optional hydra record. Sizing the key off 18 fields is wrong.

163. [shell-chrome-and-theme] G5: the 0.3 glass tint opacity on every chrome capsule and circle: Chrome controls carry the theme surface at exactly 0.3 alpha; sheets and the floating panel at 0.22 dark / 0.16 light. Three different numbers, do not collapse.

164. [shell-chrome-and-theme] G6: no metrics recorded for SidebarSymbol and SidebarIconBadge: 14 pt monochrome fit glyph (16.1 pt at scale 1.15) in a 20x20 box at the system label colour * 0.85; the row title beside it uses primaryText at 0.96 selected / 0.8 otherwise (SidebarView.swift:1362).

165. [shell-chrome-and-theme] G7: WindowDragView overrides acceptsFirstMouse and swallows mouseUp: Drag strip: accept the window-activating click, start a window move on button-down, and swallow button-up so the release never reaches the UI behind it. Chrome controls need the inverse region that refuses the caption drag but passes the press through.

166. [shell-chrome-and-theme] G8: Chrome.sheetInset = 0 makes every branch of the RootView padding conditional zero: Implement as zero and record the branch as currently inert. The collapsed traffic-light leading is therefore just chromeHorizontalPadding = 14.

167. [shell-chrome-and-theme] G9: titlebarHeight, collapsedTrafficLightTop, trafficLightsFitWidth left symbolic by every report: Caption bar: height 44, buttons 14 px square on a 23 px pitch, leading 18/14, top 16/21, sidebar must lay out at >= 88 before the buttons move in, content top inset 72, window corner radius 26, veil 72, sidebar top and edge handle inset 30.

168. [shell-chrome-and-theme] G10: the defaults suite and the exact launch flags were never captured: One store indirection with exactly these two entry points, exact argv token matching, next argv element is the folder, single parse per process, derived <folder>/storage redirection. Capture mode is tooling, not shipped behaviour.

169. [shell-chrome-and-theme] G11: settledCollapsed's default and key string are undocumented: Key string 'settledSectionCollapsed', default true. On a fresh install the gap and the Settled header with its count are built; the settled rows are not.

170. [shell-chrome-and-theme] G12: sidebarPanelOrigin/Size persist as [Double]; malformed-array behaviour unstated: Two-element double array under those exact key names; remove on nil; accept only count == 2 on read; a malformed value is silently ignored and not repaired until the next write.

171. [shell-chrome-and-theme] G13: settledFirst's updatedAt fallback left unresolved: Settled key = settledAt ?? updatedAt, descending, strict greater-than, stable sort. Do not substitute a sentinel date for a missing settledAt - the fallback is the migration behaviour.

172. [shell-chrome-and-theme] G14: ShortcutStore.label optionality and the palette finish row's title/symbol left open: Exactly three finish titles: 'Reopen thread', 'Settle thread', 'Archive thread' (lower-case 'thread'); icons 'arrow.uturn.backward', 'checkmark.circle', 'archivebox'. No chord means the trailing label view is omitted entirely, not rendered empty. hint() prepends a space before the '('.

173. [shell-chrome-and-theme] G15: KeyChord.allowedModifiers unresolved though it gates two shipped key paths: Mask observed modifiers to {Ctrl, Alt, Shift, Win} before comparing or persisting; ignore CapsLock, NumLock, Scroll and the extended/numpad flag. Popover Return/keypad-Enter/Escape require an empty masked set. The digit chord requires the masked set to equal that one modifier exactly, over 1..9 in that order. Escape-to-close and the close chord belong to the secondary window class, and Escape is skipped while a text field has focus.

174. [shell-chrome-and-theme] G16: ThemeManager.current's didSet posts unconditionally, including on a no-op write: Reproduce the unconditional re-post; an equality short-circuit changes observable behaviour for layer-backed cells.

175. [shell-chrome-and-theme] G17: PopoverMenu applies .presentedChrome() itself: The menu container itself supplies capsule button shape and the theme tint - do not re-apply per call site. 210/360/6. For the three non-palette themes the tint is ABSENT, not the system accent forced in.

176. [shell-chrome-and-theme] G18: RowAction timing differs between PopoverItem and RowActionMenuButtons; the two Settle paths differ: Three timings to reproduce: confirmation rows act immediately then close after the hold; plain popover rows close then act one turn later (so sheet-presenting actions survive); context-menu rows act synchronously. Context menu must never capture the row - it posts a finish request with the 80 ms fallback; the popover calls the row's own handlers so the ghost flies.

177. [shell-chrome-and-theme] G19: HeadCardsRow adds a chevron row to cardRows while the slotHeights fallback does not: Copy both formulas verbatim including the 6-per-row fallback, the 7-per-row unmeasured default and the missing chevron row. Measured-height keys are the literal strings 'heads-<id>' and 'helpers-<id>' (SidebarView.swift:838); each block adds a trailing + 1.

178. [shell-chrome-and-theme] G20: RowFrames.thread(at:) picks in unspecified dictionary order when frames overlap: Upstream non-determinism; a hash-map implementation reproduces it, an ordered list or a z-order hit test does not. Do not 'fix' it by taking the topmost row.

179. [shell-chrome-and-theme] G21: makeCaptureWindow uses the default windowClass, not ThreadWindow: Capture-run only. Record that the digit and Tab chords belong to the main-window class alone.

180. [shell-chrome-and-theme] G22: the critic asserts this family has no protocol/IPC surface: None. The only external contracts are those key strings, that notification name and those two flags.

181. [shell-chrome-and-theme] T1 (thin): R048's Chrome.swift entry is the thinnest relative to its weight: The chrome token table is now complete enough to build from.

182. [shell-chrome-and-theme] T2 (thin): R024's FloatingSidebarPanel entry describes the resize clamp backwards and parks every Chrome constant: Panel geometry and every user-visible string are literal; transition is 0.94 scale + opacity on panelSlide; area is the detail pane's size, not the window's.

183. [shell-chrome-and-theme] X1: the whole chrome geometry resolves to literals: See G9 for the full table and the button-placement arithmetic.

184. [shell-chrome-and-theme] X2: every UserDefaults key in the family goes through one store indirection: One indirection, 7 keys with those exact strings, wallpaperSoftness default 0 clamped 0...1 with the no-write-unless-in-range rule, wallpaperFile a bare name validated for existence at load.

185. [shell-chrome-and-theme] X3: four window-level overlays share the named coordinate space: One window-level overlay host with a single shared origin for the genie layer, the row-glide layer and the command palette, plus a separate list-local space for the reorder drag (min distance 4). The floating panel stays in the detail pane's space.

186. [shell-chrome-and-theme] X4: Reduce Motion is a global kill switch read in nine places: Read SPI_GETCLIENTAREAANIMATION at each query point (the source reads the live flag, never a cached one) and apply it at all nine sites with those exact substitutes: 180 ms panel slide, 200 ms settle flight, 220 ms slide run, 180 ms caption placement, refuse the row glide, return false from both genie entry points, no breath ring, frozen spinner cells, settle badge fades in place.

187. [shell-chrome-and-theme] X5: two animations depend on rasterizing a live SwiftUI tree to a bitmap: Needs a view-to-bitmap capture at the display scale (fallback 2), a second 'arriving' face with different trailing clearance and row height, and a shapes-only still spinner. Without all three the settle/reopen/archive animations cannot be reproduced.

188. [shell-chrome-and-theme] X6: DCDisplayCycleGuardedWindow and DCNotificationGuard are macOS-only AppKit exception nets: Do not re-implement. Port ThreadWindow's Cmd-digit monitor and Tab chords and SecondaryWindow's Escape/close-chord handling; nothing of the base class.

189. [shell-chrome-and-theme] X7: no theme defines a text colour: Choose the Windows equivalents of labelColor and secondaryLabelColor FIRST - every row fill, card fill, divider and panel scrim derives from them by opacity. The 26 palettes supply only surface + accent + 3 status hues. System surface is literally #1E1E20 dark / #FFFFFF light.

190. [shell-chrome-and-theme] X8: sidebar launch state and session state are deliberately separate: sidebarWidth default 268, read-clamped 210...440, release-floored at 210; live drag clamps 0...440; collapse threshold 110. sidebarStartsOpen default false is the only launch input; session visibility is never persisted. persist() rewrites both keys on every toggle and drag release - reproduce the redundant startsOpen write.

191. [settings-usage-tour-diff-terminal] C1: R027 files 'does anything else call Tour.present' unresolved while R023 documents SettingsView.swift:970; both miss AppCommands.swift:101-103.: Help-menu item labelled exactly "Welcome tour"; About-page "Show" button; first-run gate on persisted hasSeenTour (default false) with a 700 ms delay after the main window shows; both finish and close write the flag.

192. [settings-usage-tour-diff-terminal] C2: About page row order and the placement of AboutSoftwareUpdateSection / AboutUpdateCheckRow / AboutUpdateChromeAccessory.: Update section above the app card; update-check row inside the card between the tour row and the body text; version pill in the settings chrome strip only while About is selected; opening About kicks off an update check.

193. [settings-usage-tour-diff-terminal] C3: R028's ui_elements says the second ProviderIcon is unconditional; its feature note says it is drawn whenever headsProvider is non-nil.: Draw the heads icon whenever a heads provider is set, including when it equals the lead's. Reproduce the duplicate icon; do not align it to the body's rule.

194. [settings-usage-tour-diff-terminal] G1: DiffInspector show() guard order stated backwards by R018; session bumped before the isShown guard.: Copy the counter pair exactly, including that a show bumps only the open counter. Reproduce the leak on a self-initiated close; do not add a resynchronizing write in show.

195. [settings-usage-tour-diff-terminal] G2: MCP custom-server Delete is unconfirmed and ignores settings.confirmBeforeDeleting.: Reproduce the single-click destructive delete that ignores the preference. Carry the key "confirmBeforeDeleting" with default true for the paths that do read it.

196. [settings-usage-tour-diff-terminal] G3: MCPKeyPanel.present has no isPresenting re-entry guard while MCPSetupPanel.present does.: Keep the asymmetry: the setup panel is idempotent per entry (refocus only), the key panel re-opens the external keys URL and rebuilds its content on every present. Widths 380 vs 400, minimum height 100.

197. [settings-usage-tour-diff-terminal] G4: R027 files Tour.present's other call sites as unresolvable from TourPages.swift alone.: Suppress the first-run tour in any capture/automation mode; keep the Help-menu and About replays available regardless of hasSeenTour.

198. [settings-usage-tour-diff-terminal] G5: TourCaptures.swift:216-220 drives TourWindowController with no onFinish and no onClose.: The port needs no capture harness. If one is kept, the controller must accept a present with no completion callbacks, and the tour-seen flag must be written by the callbacks, never by the controller.

199. [settings-usage-tour-diff-terminal] G6: UsageFloatingPanel.hasSomething (fraction only) vs UsagePanel.showsContext (fraction AND windowTokens) can render an empty padded VStack instead of the placeholder.: Copy both predicates as written; no reconciliation. Keep the port's context-usage fraction derived from windowTokens so the implication holds.

200. [settings-usage-tour-diff-terminal] G7: AppSettings.modelListLimit left unresolved by R022.: Limit 15; silent no-op on add when full; count line only while the search box is empty; add control disabled and dimmed to 0.35 with tooltip "The picker holds up to 15 models".

201. [settings-usage-tour-diff-terminal] G8: HydraPanel.cornerRadius and HydraPanel.stripHeight left unresolved by R028.: cornerRadius 22, stripHeight derived as 50, reported content height measured + 58 and 94 for the empty state.

202. [settings-usage-tour-diff-terminal] G9: AppSettings.defaultBackdropOpacity and the ChatZoom ladder left unresolved by R023.: Backdrop default 0.5 under "backdropOpacity", off-stock test abs(v-0.5) >= 0.005; zoom slider 0...6 over [80,90,95,100,110,125,135], default index 3, off-stock test index != 3; both resets animated over 0.2 s; keep the zoom==1 short-circuit.

203. [settings-usage-tour-diff-terminal] G10: LicensesRow's three display titles and the exact fallback string are missing from R023.: Ship all three files, render under the display titles as Markdown at 12pt, read once at startup, use the resource-name fallback sentence verbatim, popover 480x440.

204. [settings-usage-tour-diff-terminal] G11: Persisted terminalHeight is applied unclamped; the 140...760 clamp lives only in the drag handler.: Persist under "terminalHeight", default 260, clamp only while dragging (140...760), apply the stored value unclamped on load. Do not add a load-time clamp.

205. [settings-usage-tour-diff-terminal] G12: The AboutVersionPill standing machine and check-row suffix are driven by UpdateChecker's three unnamed UserDefaults keys.: Persist the same three values under the same logical names, keep them out of the app-settings store, reproduce the three pill standings and the exact check-row sentences with their trailing periods and relative-date phrasing.

206. [settings-usage-tour-diff-terminal] G13: 'Disconnect' does not set showMenu = false while 'Edit' does; PopoverItem's dismissal contract is unrecorded.: Menu rows close first, then run the action asynchronously; add a confirmation variant that runs the action first, holds the label 650 ms, then closes. The redundant close-the-menu writes may be kept or dropped without behaviour change.

207. [settings-usage-tour-diff-terminal] G14: Localized Text("...") vs Text(verbatim:) is inconsistent and a port treating them alike diverges wherever the catalog has an entry.: No localization layer is needed for parity. Keep model- and data-derived strings out of any Markdown-rendering text path - that is the distinction the source is actually drawing.

208. [settings-usage-tour-diff-terminal] X1: Escape (keyCode 53) handled in five places in this family with five different swallow policies.: Five separate decisions on Windows, not one translation: capture-phase keydown while the diff panel is open; a terminal handler gated on shell-foreground + no modifiers + focus; two panel handlers; and a recording mode that captures all key/modifier events, cancels on bare Escape only, and cancels on window blur.

209. [settings-usage-tour-diff-terminal] X2: DiffPopoverCoordinator's mouse monitor deliberately does not swallow the dismiss click.: Dismiss on an outside pointer-down WITHOUT consuming it, so the same press also activates its target. A click-eating backdrop breaks the one-tap Review-to-Review flow.

210. [settings-usage-tour-diff-terminal] X3: Four borderless/chromeless AppKit top-level surfaces share one recipe.: Three Electron window recipes: frameless movable tour card (660 wide, centred on the main window), frameless non-activating utility panel for both MCP panels (all workspaces, no window controls, top-right inset 24), and an in-window 640x540 diff panel. Windows will not shape the shadow to the alpha, so the rounded card needs its own CSS shadow.

211. [settings-usage-tour-diff-terminal] X4: Pre-measuring SwiftUI content with a throwaway NSHostingView is used three times and is load-bearing each time.: Size these windows from an off-screen measurement pass before showing them, with the same 100px floors and the tour's max-across-pages rule. Reproduce the setup panel's top-anchored growth.

212. [settings-usage-tour-diff-terminal] X5: Chrome.* tokens deferred as 'defined elsewhere' by five reports.: Emit the whole table once as CSS custom properties including the computed values (23, 60, 32, 72), and derive stripHeight 50 from the tokens. Note sheetInset = 0: the settings detail pane runs to the window edge.

213. [settings-usage-tour-diff-terminal] X6: enum Haptics is defined in ShortcutsSettingsPage.swift, not a utilities file; Windows has no equivalent.: One no-op shim called from the same 9 places so the call sites stay comparable. Do not substitute sound or animation.

214. [settings-usage-tour-diff-terminal] X7: The Terminal subsystem's hardest macOS dependency is foregroundJobIsTheShell, not SwiftTerm.: ConPTY has no process groups or tpgid, so this cannot be translated. Record an explicit ENGINEERING DECISION (track whether a non-shell child is running under the pty, or require a modifier/different key) and document that bare-Escape-at-prompt parity is approximated.

215. [settings-usage-tour-diff-terminal] X8: The login-shell launch contract must be ported literally or replaced consciously.: Windows cannot use `-` argv[0] login semantics. Replace it explicitly: user's shell, project directory as cwd, the same four environment variables, and a script tab that runs the command then hands back to an interactive shell. Record the PATH augmentation as a Windows-specific decision, not a translation.

216. [settings-usage-tour-diff-terminal] X9: Persisted state is AppSettings/UserDefaults with literal keys plus the Keychain and mcp.json; three update-checker keys go unnamed.: Map each key one-for-one with the same names and defaults; MCP secrets in DPAPI-protected storage and non-secret values in mcp.json; keep the update-checker keys in a store a test/capture profile does not redirect, or record the divergence deliberately.

217. [git-and-checkpoints] C1 TIMEOUT: R036 claims 300 s for `add -A`: Give `git add -A` and `git commit -F -` in the commit-all path a 120 s budget; 300 s only to the 12 enumerated git calls plus the 6 forge-CLI calls. Reproduce timeout failure as ShellError(failureMessage) from terminate-then-kill (Shell.swift:110-118).

218. [git-and-checkpoints] C2 UNREACHABLE STRING: R036 lists 'The request could not be merged.' as user-visible error text: Keep the initializer for source parity if wanted, but exclude it from any localization table, test expectation, or user-visible string inventory. Real merge-failure text is the forge CLI's stderr or `The command failed with exit code <n>.`

219. [git-and-checkpoints] C3 SCRATCH-INDEX FALLBACK: R036 says the nil-scratch case deletes the scratch and re-seeds: Two distinct fallbacks must both exist: (a) no stream/cookie lost -> private UUID-named delete-on-exit index; (b) stream present but incremental add failed -> delete and rebuild the SHARED scratch. Keep the copy-the-real-index seeding so git's stat cache survives.

220. [git-and-checkpoints] G1 checkpointRef phases, persisted fields, migration: The port MUST keep the namespace byte-identical (`refs/droppy-code/checkpoints/<lowercase uuid>/<turn>-<phase>`) and the words `start`/`end`. Renaming it silently orphans every existing checkpoint with no code path to notice. Persisted keys `baseCheckpoint`/`endCheckpoint` are fixed. Git refs are case-sensitive on Windows too, so `.uuidString.lowercased()` must be reproduced, not left as an uppercase GUID.

221. [git-and-checkpoints] G2 Exact argument vectors missing for six shipped commands: These vectors ARE the spec. Build each argv as an ordered array, never a joined string: `^{tree}`, `refs/heads/x:refs/heads/x`, `%(refname)` and `--batch-check=%(objecttype)` are all mangled by CMD/PowerShell (`%` expansion, `^` escape). Spawn with shell:false and explicit escaping. NUL-separated stdin must be written as raw bytes.

222. [git-and-checkpoints] G3 diff boundary: `>` not `>=`, and paths:[] gives a full-tree diff: Implement `> 400`, not `>= 400`. Do NOT add an empty-paths early return — that would be a behaviour change even though latent. Keep `--literal-pathspecs` conditional on `paths != nil` and positioned before `diff`. Chunk joining (:568) inserts `\n` only when output is non-empty and does not already end in one.

223. [git-and-checkpoints] G4 isBuildOutput call sites; critic says all seven are silent: Reproduce the 15-element set verbatim with exact case plus the two dynamic rules. Do NOT case-fold on Windows despite NTFS: a repo can hold `Build/` and folding would silently drop user work. Reproduce the one reported site and the six silent ones. Note the leading space in `" was"` vs `"s were"`.

224. [git-and-checkpoints] G5 TouchedPaths.matches consumers: Implement the bidirectional `/`-anchored suffix rule exactly, including the `oldPath` arm; it is load-bearing for auto-expand/scroll AND for de-duplicating synthesized per-edit cards. On Windows, normalize agent-reported paths to forward slashes BEFORE calling it, since the rule is literally `"/" + path`.

225. [git-and-checkpoints] G6 previewRestore path spaces: Reproduce the mixed spaces exactly — CWD-relative pathspecs in, repo-root-relative paths out, repository-root-joined absolutes for blob lookups. Do NOT normalize to one space (it would change which files are marked isKept for a project opened at a repo subdirectory) and do NOT add `-c diff.relative=false`. Highest-risk Windows path in the family: the stdin payloads at Git.swift:609/617 are absolute paths joined by \0 carrying drive letters and backslashes, while the numstat-derived halves stay forward-slashed.

226. [git-and-checkpoints] G7 forge cases: Three cases, no fourth. Reproduce the asymmetric parsing (search for merge_requests at index>=1; fixed parts[2] for pull/pulls) and `number > 0`. Reproduce `!N` for GitLab, `#N` elsewhere. Keep `forge()`'s substring test with .gitea fallthrough: an enterprise GitHub at git.acme.com gets driven with `tea` — upstream behaviour, not a bug to fix.

227. [git-and-checkpoints] G8 WorkingTreeWatch teardown asymmetry: cookie timeout calls the tearDown the registry comment warns about: Reproduce, do not fix: delete the shared scratch on cookie timeout unguarded, and make a second teardown a no-op. Preserve the visible consequence — that turn's revert later throws `"No file checkpoint is available for this message. You can still keep the file changes."` (ThreadRuntime.swift:1771). The cookie mechanism (write file, await its own event, 1 s ceiling at :259) maps directly onto ReadDirectoryChangesW.

228. [git-and-checkpoints] G9 a cookie-timeout-torn-down watch is permanently sticky: Reproduce the stickiness; do NOT add a restart or health check. This matters more on Windows (network shares, mapped drives, VM-shared folders miss the cookie more often), which is correct parity — the source demotes rather than trusting a flaky watcher. Also reproduce the pre-check at :94 `volumeIsLocal == true` (GetDriveType on Windows): a non-local volume yields NO watch at all, i.e. fresh capture from the first snapshot.

229. [git-and-checkpoints] G10 Git.run merges caller `extra` LAST: Implement precedence in this exact order — base environment, then the two forced variables, then caller extras LAST — rather than the intuitive 'forced wins'. Note GIT_OPTIONAL_LOCKS=0 is what keeps background snapshots off index.lock; on Windows, where locking is mandatory rather than advisory, dropping it would make the app collide with the user's own git in a terminal.

230. [git-and-checkpoints] G11 DiffParser `--- ` branch consumes every such line; oldPath never from `---`: Implement the `--- ` arm as a consuming branch whose only effect is the exact-string test against `"--- /dev/null"`, and do NOT derive oldPath from it. Doing so would populate `DiffFile.oldPath` on every modified file, which then feeds `TouchedPaths.matches` (which tests both path and oldPath) and would widen every focus/de-dup match in DiffInspector. Note `-M` IS passed on the app's own diffs (Git.swift:553) so rename headers occur, while previewRestore's numstat passes `--no-renames` (Git.swift:586).

231. [git-and-checkpoints] G12 RevertPreview outer/inner split asymmetry: Split records on NUL DISCARDING empties; split each record on TAB with at most 2 splits KEEPING empties. Empty numstat text yields an empty file list and no error. On Windows, read stdout as bytes and decode UTF-8 with no CRLF translation — `-z` output carries no newlines, and normalizing a path containing \r would corrupt it.

232. [git-and-checkpoints] CC1 Two git identities; commitWork runs WITHOUT Self.identity: Copy the split exactly. Do NOT pass an identity on the commit-work path — a port-branded author on a pushed commit would show up in the user's history and on the forge. DO pass the Droppy Code identity on the two internal commits, so a user with no user.email configured can still be checkpointed (commit-tree refuses without an identity).

233. [git-and-checkpoints] CC2 Checkpoint commits are PARENTLESS: MUST NOT add `-p HEAD` to the checkpoint commit. Doing so would pin the whole history under a hidden ref so gc could never reclaim a deleted thread's checkpoints, and would make them reachable in `--all` traversals, changing `git log --all`, `git fsck` and repack behaviour in the user's repository. Reproduce the ref-deletion path verbatim including that its failure is swallowed.

234. [git-and-checkpoints] CC3 Forge CLIs run in a different environment from git: Do NOT hoist GIT_TERMINAL_PROMPT=0 into a shared spawn helper. gh/glab/tea keep their own credentials and prompting, and the source deliberately lets them prompt. Preserve the practical consequence: an unauthenticated forge CLI can block until the 300 s timeout, then fail with its own stderr. Reproduce argv exactly, including `--description` for glab/tea vs `--body` for gh, and the first-match `https://\S+` scrape over stdout + "\n" + stderr.

235. [git-and-checkpoints] CC4 Missing forge CLI throws an exact string: Reproduce the literal exactly: `<name> was not found on your PATH.` On Windows, resolve against PATHEXT (gh.exe/gh.cmd/gh.bat) as well as the bare name and split PATH on `;` not `:` — but keep the wording with `PATH` in caps, since it is what the user reads. Preserve that a missing CLI short-circuits mergePullRequest before the first attempt, so no retry delay is incurred.

236. [git-and-checkpoints] CC5 macOS-only git resolution (LoginEnvironment.git, developerGit, which's ':' split): Every line is a Windows decision to make explicitly, not transliterate: (1) split PATH on `;`, and replace the execute-bit test with a PATHEXT-aware existence test; (2) the 'this is a path' test must also accept `\` and a drive prefix; (3) the entire developerGit/xcode-select branch has NO Windows analogue and its guard can never be true — resolve from PATH then the well-known Git for Windows install locations, and do NOT hard-code /usr/bin/git as a last resort; (4) the login-shell capture has no equivalent — read the process environment plus the user/machine registry Environment keys, and replace the 12-entry PATH augmentation wholesale; (5) keep LANG = en_US.UTF-8 when absent, git for Windows honours it; (6) keep the single-shot memoization and the pre-load fallback shape.

237. [git-and-checkpoints] CC6 Git.run never passes outputLimit; every git invocation buffers unbounded: Reproduce the unbounded capture for git. Do NOT add a cap: the app writes the complete patch to disk (AppModel+Hydra.swift:940) and re-applies it with `git apply`, and a truncated binary patch would not apply. Size buffers for multi-hundred-megabyte strings on the hydra landing path.

238. [build-assets-and-release] C1: AGENTS.md:25 'no test target and no test scripts' vs docs/performance-2026-09-15.md:5-6 citing scripts/run_tests.sh, seven suites, 117 tests, benchmark scripts and Tests/RuntimePerformanceTests.swift: Upstream ships no test suite to mirror. Do not reconstruct a 7-suite/117-test structure or cite the performance doc as evidence of tested behavior; its numbers are unverifiable against the pinned tree.

239. [build-assets-and-release] C2/G4: MARKETING_VERSION 1.6.0 vs website/index.html:543 'Version 1.5.3': The download-card version is a hand-edited literal with no generator. Copying the staleness is parity; adding a rewriter is new behavior. Carry U+00B7, not the entity.

240. [build-assets-and-release] C3/CC6: welcome tour 'six pages' (README) vs 'eight pages' (CHANGELOG 1.3.0, index.html): 8 pages in this exact order with these exact strings; the themes page must spell the live theme count (26), not hardcode it (TourPages.swift:15-17 records the previous 'twenty-five' drift). README.md:37 is not the spec.

241. [build-assets-and-release] C4/G2: tour_captures.py points at 'Droppy Code.app', website_captures.py at 'Droppy Code Dev.app'; R056 left it open: The only working tour-capture entry point at dd2b86e is scripts/website_captures.py; tour_captures.py standalone cannot launch and only --export-only avoids APP (:287-294). Record the defect; do not replicate the broken path.

242. [build-assets-and-release] C5: stale 'tour/v4' docstring and code.js 'site-assets/droppy-code/themes' comment; critic says index.html uses tour/v5 in 'all 24 worker URLs': Live CDN prefix is site-assets/droppy-code/tour/v5; the theme picker fetches <prefix>/themes/<AppTheme rawValue>.webp lazily for 26 ids in AppTheme.swift order. Take prefixes from code, never comments.

243. [build-assets-and-release] C6: og-home accent span - R056 says comma outside, R063 says comma included: Any regenerated OG card uses `All your <accent>coding agents</accent>,<br>one native <apple glyph>Mac app` with the comma unaccented. Do not derive strings from rendered images.

244. [build-assets-and-release] G1/CC1/Thin-1: R006 never classifies the capture subsystem shipped-vs-debug; audit S3 is one file away: --website-captures <dir> and --tour-captures <dir> are live flags in the signed, notarized Release app and the ScreenCaptureKit/AVFoundation stack links into every shipped binary. Compiling it out is a deliberate divergence, not parity. If kept, reproduce the exact argument grammar.

245. [build-assets-and-release] G2: tour_captures.py broken at the pinned commit, unflagged: Port tooling must wait for the process to exist before starting a budget and derive the dev bundle name from one constant. Record both defects so neither is copied forward.

246. [build-assets-and-release] G3: capture budget mismatch - 600 s in-app watchdog vs 330 s external wait: External budget must exceed the in-process watchdog and both must come from one place. Literals to carry if ported: tour watchdog 600 s, website watchdog 110 s, each ending in immediate exit(0) after a log line.

247. [build-assets-and-release] G4: index.html:543 hardcodes 1.5.3; site version hand-maintained and one minor release stale: See C2.

248. [build-assets-and-release] G5: data-download-link is a dead attribute with no reader: Keep the anchors' href static and let the host redirect resolve the newest asset. A JS href rewriter would be invented behavior. The attribute is inert: copying it is harmless, omitting it changes nothing.

249. [build-assets-and-release] G6: orphaned CDN asset usage/v1/limits.webp with no generator: This image was uploaded out of band and cannot be regenerated from the source tree; treat it as an opaque hosted asset or recapture by hand. Markup size is 812x1146.

250. [build-assets-and-release] G7: R056 treats WINDOW_SIZES as authoritative; the per-still JSON sidecar is authoritative: The app-to-encoder geometry contract is the per-still JSON sidecar with keys rect, window and optional focus, all in points with a top-left origin, scale derived as PNG width / rect width and never assumed to be 2. WINDOW_SIZES is legacy and is not the spec.

251. [build-assets-and-release] G8: R056 lists only 3 of package_dmg.sh's '12' error strings; the AppIcon.icns precondition is load-bearing: 16 distinct failure messages, all stderr with the 'package_dmg: ' prefix and exit 1. Icon compilation must precede installer packaging, because the volume icon is copied OUT of the built bundle. Other literals: VOLNAME 'Droppy Code' (:25), background release/dmg/installer-background.tiff (:17), .DS_Store stored base64 so git treats it as source (:18-19), work dir /tmp/droppy-code-dmg-XXXX (:49), UDRW+HFS+ intermediate (:78), ULMO final (:113), FinderInfo xattr with kHasCustomIcon 0x0400 at byte offset 8 (:92-94), 8 polite detach attempts 1 s apart before force (:97-106).

252. [build-assets-and-release] G9: sync_dev_data.sh mirrors only four API-key accounts while rsyncing mcp.json: MCP secrets and provider API keys share ONE container keyed by the app display name, distinguished only by account string; MCP account format is the literal 'MCP ' + server + ' ' + field with single spaces; API keys use kSecAttrAccessibleWhenUnlocked (APIKeychain.swift:37). The port's SecretStore must keep the same partitioning and the same four account names verbatim.

253. [build-assets-and-release] G10: R059's footer inventory gives the wrong hosts; critic says copying it ships five 404s: The Droppy Code site hosts none of privacy, terms, blog, docs, Droplets, iOS or The Trench; it links out for all seven. Copying the hrefs is technically safe but points users at Droppy's site, which the port's trademark rules make an owner decision, not a mechanical copy.

254. [build-assets-and-release] G11: build_changelog.py silently drops ## [Unreleased]; unrecognised ### headings pollute summary: For parity: regex exactly as above; three mapped headings (lower-cased after strip) plus 'thanks' into a separate array; SECTION_ORDER ['New features','Bug fixes','Refinements'] with empty sections dropped (:106-110); bullets matched by '^\s*-\s+(.*)$' (:97) then strip_emphasis (:54-55) removing '**','__','*','_' by plain replacement in that order; per-release keys exactly version/date/platform(literal 'mac')/summary/sections[{title,items}]/thanks; top level {generated_at: UTC isoformat, releases}, indent=2, ensure_ascii=False, trailing newline (:169-173); sort by tuple of ints from re.findall(r'\d+') reversed (:134-136, :155); date order = heading date, else `git log -1 --format=%cs v<version>`, else `git log -1 --format=%cs -- CHANGELOG.md`, else today (:42-51); --section promotes '### ' to '## ' (:126-129) and that is what About renders. Reproduce both defects, do not fix.

255. [build-assets-and-release] G12/CC7: no .entitlements anywhere; release chain passes none; app ships unsandboxed: This is the app's whole capability model: no App Sandbox and the empty default hardened-runtime set, which is what lets it spawn arbitrary agent CLIs and read the user's home. The Windows port must NOT introduce AppContainer, MSIX containerization or restricted-token child processes if it wants parity. There is no entitlement declaration to translate; the gate is the OS user's privileges plus SmartScreen on an unsigned installer.

256. [build-assets-and-release] Thin-1: R006 is thin exactly where the contract needs it - no shipped/debug classification: Every subsystem the port reads must be classified. Treat the capture flags as release features and carry the CC2 isolation contract into storage, settings and credential layers.

257. [build-assets-and-release] Thin-2: R054 relays performance-doc tooling paths as repository paths without checking: docs/ in this repository is narrative; its paths and line numbers do not resolve against the pinned tree. No port decision may rest on a docs/ path without re-resolving it in source.

258. [build-assets-and-release] CC1: shipped-in-release capture subsystem, unstated by anyone: See G1.

259. [build-assets-and-release] CC2: the capture-run isolation contract exists in the shipped app and nobody states it: If the port keeps the flags it must reproduce the whole contract, not a subset: own store under <outdir>/storage, own settings namespace 'iordv.droppycode.website-captures' wiped each run, empty/nil for every credential, no provider probes, no background update checks, and frame-reporting hooks in chat, Hydra button, effort slider and diff inspector turned ON that are off normally. Any subset leaks real developer data into published captures.

260. [build-assets-and-release] CC3: R002's open question about the three bundled resources is resolvable: Exact spec for the Windows About > Licenses page that LICENSE 7(a) requires: three entries in this order with these labels, and the extension-less LICENSE special case. Ship all three files inside the application and render from the installed copy.

261. [build-assets-and-release] CC4: the release-artifact naming chain is one literal in five places, never drawn end to end: The whole distribution surface hangs on the literal /Droppy-Code.dmg plus project id 86415974. The port needs the same shape: one stable per-release asset path so a 'latest' permalink and the in-app updater both resolve without a code change, and a host whose releases API can be polled unauthenticated. The download card's version literal is NOT part of this chain.

262. [build-assets-and-release] CC5: Keychain layout - one service, four API accounts, MCP accounts alongside: The port's SecretStore needs the same flat account namespace under one container named by the app display name, with the four account strings verbatim and the MCP format 'MCP <server> <field>'. kSecAttrAccessibleWhenUnlocked has no direct Windows analogue; DPAPI user scope via Electron safeStorage is the deliberate substitution and should be labelled as such.

263. [build-assets-and-release] CC6: tour page count is 8, which R001 left unresolved: See C3.

264. [build-assets-and-release] CC7: no entitlements file, no checked-in Info.plist, DMG signed without --entitlements: See G12.

