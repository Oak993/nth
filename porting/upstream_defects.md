# Upstream defects register

Real defects and awkward interactions found in the pinned source at dd2b86ef.

**Contract section 8: a parity port REPRODUCES these. It does not fix them.** Silently repairing
unrelated upstream behavior during a parity port is forbidden. Each entry is recorded so the
Windows implementation copies the behavior deliberately rather than accidentally improving on it.

If reproducing one would expose credentials, destroy data, or create another serious security
problem, it is isolated and escalated to the owner instead (contract section 8). Those are marked
ESCALATE below.

Source: the repair pass, 9 subsystem adjudicators reading the real source. 99 entries.

1. [providers-and-protocols] ACPSession.swift:330-331 vs :338/:364 - applyConfigOptions resets effortConfigID and fastConfigID to nil on every call but does NOT reset modeConfigID or modelConfigID. Because it also runs on the reply to the app's own session/set_config_option (:484), an agent answering a model switch with a configOptions array lacking the thought_level or the `fast` model_config entry permanently drops the effort slider and the Fast toggle for the session: the guards at :456 and :460 never fire again. Self-inflicted, triggered by the app's own request. Reproduce.

2. [providers-and-protocols] AntigravitySession.swift:23-24, :57-58, :88, :150-151 - runtimeMode and interactionMode are assigned in init and on every send but never read; start() builds its mode flags from configuration.* instead. A mid-thread mode or Plan/Build change cannot reach `agy` at all, and even a relaunch uses the original modes while picking up the UPDATED model/effort (read at :85-87). Half the per-turn state is applied, half discarded. Reproduce.

3. [providers-and-protocols] CopilotSession.swift:456 - the fallback percent branch is min(100, used / entitlement * 100) with no lower clamp, while the primary branch is max(0, min(100, 100 - remaining)). A quota payload reporting a negative usedRequests produces a negative percent that reaches the usage bar. Reproduce.

4. [providers-and-protocols] ProviderRegistry.swift:621-640 - refreshCredits has neither the 25-second watchdog nor the defer that refreshPlanLimits uses (:557/:559 and :569/:571), so a CreditsReader.read that never returns leaves the provider stuck in loadingCredits for the rest of the run and its spinner never stops. It also has no forced-read queue, so force:true arriving during an in-flight read is silently dropped at :622 (refreshPlanLimits queues it via forcedLimitReads, :548 and :584-587). Reproduce both.

5. [providers-and-protocols] ProviderRegistry.swift:627-630 - the empty-API-key path sets credits[provider] = nil and returns WITHOUT clearing creditsFetchedAt[provider]. The stale timestamp then makes the 60-second gate at :631 suppress the next read, so a just-pasted key can leave the balance blank for up to a minute. Reproduce.

6. [providers-and-protocols] ClaudeSession.swift:203-205 vs ProviderRegistry.swift:501-505 - handshake() throws "Claude could not load its commands." when the initialize response has no `commands` array, which also destroys the Claude MODEL catalog even though loadClaudeCatalog only reads .models. The failure is swallowed by try? and leaves no user-visible cause. Reproduce the coupling.

7. [providers-and-protocols] ProviderRegistry.swift:567 - `guard PlanLimitsReader.exposesLimits(provider), !loadingLimits.contains(provider) else { return }` re-checks two conditions already guaranteed at :547-548 with nothing able to invalidate them in between on the non-API-key path. A never-taken guard; not a safety net.

8. [providers-and-protocols] PiCLI.swift:14-18 hard-codes the support-directory literal "Droppy Code/pi" while CommandCodeAPI.swift:443 and MCPBridgeSource.swift:243 interpolate AppInfo.name. A build whose CFBundleDisplayName differs from "Droppy Code" scatters its helper files across two different roots. Resolve deliberately, but note the source is inconsistent.

9. [mcp] MCPProxy.swift:118-131 + MCPStore.swift:26-28 + :286 - a port repick after the listener fails leaves every provider config written this launch pointing at the dead port. exportAll() runs synchronously right after start(), before the listener can report .failed, and nothing in the repick path re-runs MCPProviderConfig.writeAll (its only call site is MCPStore.swift:286). Agents launched in that session reach a closed port until some other action triggers exportAll.

10. [mcp] MCPProxy.swift:387-392 - the SSE relay emits ONE chunked-transfer chunk PER BYTE: for try await byte in stream { frame = Data("1\r\n") + byte + Data("\r\n"); await send(connection, frame) }, each an awaited NWConnection.send. Every payload byte costs 6 bytes on the wire plus one async round trip.

11. [mcp] MCPProxy.swift:398-402 - the non-SSE path accumulates the whole upstream body one byte at a time before sending anything, so there is no streaming and an O(n) per-byte await for large tool results.

12. [mcp] MCPProxy.swift:260-282 - a request that ends early (isComplete or error) below its declared Content-Length has its TRUNCATED body relayed upstream with no error (:276-277). The 400/411/413 refusals do not cover this case.

13. [mcp] MCPProxy.swift:369-380 - WWW-Authenticate is not copied back, so a 401 from an OAuth upstream reaches the client bare and RFC 9728 discovery through a proxy route cannot work.

14. [mcp] MCPBridgeSource.swift:50-56, :76-152, :218-234 - the bridge never kills a spawned child. A server hanging in initialize or tools/list is abandoned after CONNECT_TIMEOUT_MS = 15000 with console.error("droppy mcp: skipping " + server + ": " + ...) and its process lives for the whole lifetime of the Pi / Command Code host. Zero kill / unref / SIGTERM in the file.

15. [mcp] MCPBridgeSource.swift:218-232 - loadMCPTools connects servers strictly sequentially with await inside the loop, so N hung servers cost N * 15 seconds of startup.

16. [mcp] MCPStore.swift:335-341 - on probe success, states[id] = .connected is written unconditionally while connectedAt/serverVersion/tools are written only if a connection row exists. Reachable via refresh(_:) after the row was removed: the card shows connected with a tool count, enabledServers excludes it (:43), no provider config gets it, nothing is persisted.

17. [mcp] MCPCustom.swift:15-19 + :46 :48 + MCPStore.swift:245-249 - Variable uses the synthesized decoder, so a stored variable object missing "isSecret" throws keyNotFound; the throw escapes decodeIfPresent and the outer try? drops EVERY custom server silently. The doc comment at MCPCustom.swift:37-38 promises tolerance the nested type does not deliver.

18. [mcp] MCPOAuth.swift:172 + :699-701 + MCPProxy.swift:355-361 - a token response without expires_in leaves no expires_at, accessToken then treats the session as never-expiring, and the proxy's 401 -> get-token-again -> replay becomes a byte-identical retry that gets the same 401. There is no forced refresh anywhere.

19. [mcp] MCPHub.swift:59 vs :78-97 - the failed set is read only by tools(). A server whose listTools threw is dropped from the tool list but relaunched on any direct call(), because call() never consults failed and the toolCache guard is skipped when no cache entry exists.

20. [mcp] MCPHub.swift:187 - initialized is cleared only by stop(), and the HTTP path has no restart or dead state at all (:421-452). An HTTP client whose remote session was dropped keeps believing it is initialized until MCPStore.exportAll triggers reload().

21. [mcp] MCPKeychain.swift:37-49 - SecItemUpdate's query carries no kSecAttrAccessible, so an item created by an earlier build keeps its original accessibility forever; only the SecItemAdd fallback applies kSecAttrAccessibleWhenUnlocked. Accessibility never migrates.

22. [mcp] MCPOAuth.swift:815-821 - randomToken fills bytes with UInt8.random(in:), Swift's default RNG, rather than SecRandomCopyBytes, for the PKCE verifier (32 bytes) and the CSRF state (24 bytes). The byte counts and base64url encoding are parity-relevant; the RNG choice is the port's own.

23. [mcp] MCPProviderConfig.swift:10 :18 :34 :41 :59 :66 :131 :136 - a missing url or command becomes the EMPTY STRING in the emitted config rather than skipping the entry, so a malformed resolved server still produces a syntactically valid but unusable provider entry.

24. [mcp] MCPProxy.swift:15 :23 :31 - proxy-port is never deleted by anything. A reinstall over an existing data folder reuses the previously chosen port, which is the collision the repick logic exists to paper over.

25. [mcp] MCPProxy.swift:105 :117 :119 :313 and MCPHub.swift:67 - unconditional print() logging to stdout, not behind any debug flag: "MCPProxy: listening on <port>", "MCPProxy: could not listen on <port>", "MCPProxy: listener on <port> failed: <error>", "MCPProxy: request to '<id>' failed: <message>", "MCPHub: <id>: <message>".

26. [hydra] HydraReportsPopover.swift:190-201 vs Hydra.swift:1266-1272 -- the lead's whole closing instruction block can be rendered inside the last head's report card. reportMessage joins its closing array into one paragraph; withoutLeadInstructions drops that paragraph only when it starts with one of nine leadPrefixes. When no head landed cleanly the paragraph can begin with '<A> and <B> both changed <path>: ...' (Hydra.swift:1267) or "<Name>'s changes would not apply on their own; ..." (Hydra.swift:1271), neither of which is a prefix, so 'Do not check any of this...' and the report style rule are shown to the user as if a head had written them. REPRODUCE, DO NOT FIX.

27. [hydra] Hydra.swift:939-940 -- mergeStatus's .failed branch silently drops the pending-files sentence. The .merged and .stray branches both append `pending`; .failed does not, so after a failed merge the lead is never told how many files are still unmerged even though the caller computed the number. REPRODUCE, DO NOT FIX.

28. [hydra] HydraDelegationStream.swift:113-117 -- an empty-string 'name' or 'project' in a delegation entry shadows its alias instead of falling through. `{"name": "", "head": "Otto"}` produces an unnamed head; `{"project": "", "repo": "gaze-site"}` puts the head in the chat's own project. The ?? is over the optional, not over emptiness. REPRODUCE, DO NOT FIX.

29. [hydra] AppModel+Hydra.swift:200-214, reached from HydraButton.swift:212-222 -- 'Turn Hydra on for this chat' silently adopts the remembered pair's orchestratorModel and orchestratorEffort, changing the chat's model. Nothing in the menu says so and there is no undo. REPRODUCE, DO NOT FIX.

30. [hydra] AppModel+Hydra.swift:158, :164, :177 -- a pair whose worker provider is not installed or is switched off loses its workerModel entirely (heads fall back to the lead's model), but hydraHeadsFallbackNote only tells the user the heads run on the lead's provider for now. The dropped head model is never mentioned. REPRODUCE, DO NOT FIX.

31. [hydra] HydraPanel.swift:278-284 -- 'Clear finished heads' is the only heads-popover action that does not set isPresented = false, so the popover stays open after clearing while 'Stop all heads' and head selection close it. REPRODUCE, DO NOT FIX.

32. [hydra] ThreadRuntime.swift:2889 -- `while !hydraWaiting.isEmpty, launch?.hasRoom(running:) ?? true`. If Hydra is switched off (app-wide or per chat) between a delegation block arriving and the waiting queue being swept, hydraLaunch returns nil and every queued head is dispatched in one loop with no concurrency ceiling at all. REPRODUCE, DO NOT FIX.

33. [hydra] AppModel+Hydra.swift:117 vs :126 -- hydraHeadsProvider decides the fallback with providers.countsAsInstalled(provider) while hydraHeadsFallbackNote chooses its wording with providers.status(provider).isInstalled. The two predicates can disagree, producing a note whose stated reason does not match why the fallback happened. REPRODUCE, DO NOT FIX.

34. [hydra] HydraMergePopover.swift:28 vs HydraReportsPopover.swift:159 -- two readers of two very similar app-written lines have different tolerance for the minus sign. The merge card's regex accepts only U+2212 and degrades to plain text on any variance; the report card's accepts U+2212 or an ASCII hyphen. REPRODUCE, DO NOT FIX.

35. [hydra] Hydra.swift:1069-1073 -- delegations(fromBody:) cannot distinguish 'the JSON did not parse' from 'every entry was rejected': both return nil. A well-formed array whose entries all lack a prompt makes delegations(in:) walk back over every earlier fence and finally the whole message tail, which can accidentally parse something unrelated, before reporting the block unreadable. REPRODUCE, DO NOT FIX.

36. [hydra] AppModel+HydraMerge.swift:315-316 -- in the generic catch the HydraMergeRecord.detail is built WITHOUT the project prefix ("Hydra could not merge the team's work: <error>") while the timeline note title on the very next line IS prefixed via title(). With several projects merging, the record and the note disagree about which project failed. REPRODUCE, DO NOT FIX.

37. [runtime-and-state] ThreadRuntime.swift:2142-2143 with :3429-3431 - the `.notice` arm calls appendNotice(notice.level, notice.message), and appendNotice constructs a brand new Notice(level:message:), silently discarding every other field, in particular Notice.limit (Timeline.swift:211). AutoContinue.swift:146 sets notice.limit = LimitNotice(threadID:) and pushes it through rehearse(.notice(notice)); the mark never reaches the timeline item, so the stored usage-limit row is an anonymous .info notice.

38. [runtime-and-state] AutoContinue.swift:83 - `outcomes[threadID] = .handedOff(made.id)` stores the new thread's UUID and the only reader (LimitResumeBadge.swift:57) matches `.handedOff` without binding it. Write-only dead payload.

39. [runtime-and-state] ThreadRuntime.swift:1159-1167 - the three deferral exits in startTurn call enqueueFollowUp(text:attachments:) and therefore DISCARD hydraHeads and hydraBrief. A head's report or a lead's brief deferred while the runtime is busy, reverting or archived comes back out of the queue as a plain user bubble, losing its pill rendering and heads attribution.

40. [runtime-and-state] ThreadRuntime.swift:1163 - `guard let app, let initialThread = app.thread(threadID), let project = app.project(initialThread.projectID) else { return }` returns WITHOUT enqueueing, unlike the guards on either side. A send issued while the thread or project is momentarily unresolvable is dropped with no notice and no queue entry.

41. [runtime-and-state] ThreadRuntime.swift:1380-1392 - warmSession leaves `warmup` holding a finished Task when ensureSession throws (:1390 clears it only on success), and :1381's `warmup == nil` precondition then permanently disables warm starts for that runtime instance. The comment at :1389 shows the non-retry is intended; the permanent latch reads as a side effect of it.

42. [runtime-and-state] Text.swift:80 and ThreadRuntime.swift:3370 - both comments assert the 200_000 cap is 'the same cap' as the other's, but the two constants bound different operands (input sum vs produced hunk). The comments are wrong; reproduce the behaviour, not the comments.

43. [runtime-and-state] RecentCache.swift:43-46 - insert calls removeValue(for: key) BEFORE `guard cost <= costLimit else { return }`. Inserting an over-budget value for an already-cached key evicts the good entry and stores nothing, leaving the cache worse than before the call.

44. [runtime-and-state] RecentCache.swift:47-53 - on rollover the old `previous` map is overwritten by `current` in one assignment, so a key that had just been promoted out of previous can be dropped at the very next rollover. Intended generational behaviour, but surprising.

45. [runtime-and-state] ProviderRegistry.swift:161 and :698 route "recentSlashCommands" through `CaptureRun.defaults ?? .standard`, while :163, :177, :541 and :672 in the same class use UserDefaults.standard directly for "providerModelCatalogs" and "providerModelCatalogDates". A capture run therefore gets a clean recent-command list but the real machine's model catalogs, contradicting WebsiteCaptures.swift:10-12.

46. [runtime-and-state] LegacyMigration.swift:21 hardcodes `support.appendingPathComponent("Droppy Code")` instead of AppInfo.name (Text.swift:4), unlike Storage.root (Storage.swift:22). The `guard !AppInfo.isDevelopment else { return }` at :10 currently masks the divergence, but a display-name change would migrate the old library into a folder Storage never reads.

47. [runtime-and-state] Text.swift:101 - the input-size guard in SimpleDiff.unified is evaluated AFTER the prefix/suffix scans and slice construction at :87-100, so a multi-megabyte pair is fully line-split and walked before being rejected. A cost defect, not a correctness one.

48. [chat-and-composer] LinkTitles.swift:98-109 - the `claimed` set is inserted into on claim (:98) and again on finish (:109) and is NEVER cleared, while `titles` is flushed wholesale at 400 entries (:106). After a flush, any video id already claimed can never be claimed again (`claimed.insert(id).inserted` returns false), so its title is permanently unavailable for the rest of the launch even though the cache that held it is empty. `finish` also inserts into `claimed` when `title` is nil, so a failed fetch is never retried. Reproduce, do not fix.

49. [chat-and-composer] MarkdownView.swift:931-932 (FaviconCache.memory) and LinkTitles.swift:106 (titles) - both are dictionaries flushed with `removeAll(keepingCapacity: true)` the moment they reach 300 / 400, so a single insertion past the threshold discards every cached favicon / title rather than evicting one. Observable as every favicon in a long thread blanking and re-fetching at once. Reproduce.

50. [chat-and-composer] ComposerView.swift:320-322 - `case .tab, .submit, .steer: pick(suggestions.items[suggestions.selected])` subscripts `items` with `selected` under no bounds check. `selected` is clamped only in the `.up`/`.down` arms (:315, :318), and `min(suggestions.items.count - 1, ...)` yields -1 for an empty list. If `suggestions.isVisible` were ever true with an empty `items`, this traps. The Windows port should treat the out-of-range case as a no-op, the closest non-crashing equivalent.

51. [chat-and-composer] AttachmentFlight.swift:55-59 - the reduce-motion guard returns BEFORE the `if let existing = window { existing.orderOut(nil); window = nil }` teardown. If reduced motion is turned on while a flight is in the air, the in-flight ghost window is never ordered out by a subsequent call; only its own CATransaction completion removes it. Narrow, but reproduce the ordering.

52. [chat-and-composer] AttachmentFlight.swift:195-203 - `AttachmentDrops.shared.take()` consumes the armed drop before `fly(...)` is called, and `fly` then returns without animating under reduced motion or on a nil cgImage / nil NSScreen.main. The drop is silently lost. Harmless as shipped (the only call site passes no completion and the chip is drawn by layout regardless), but it is the source's behavior and must not be 'fixed' by deferring `take()`.

53. [chat-and-composer] FollowUpQueueTab.swift:156-164 vs :386-399 - cursor-stack leak when a queued follow-up is removed mid-drag. `onChange(of: runtime.followUps.map(\.id))` calls `dragEnded()` (:159), which pops the closedHand (:294). The openHand pushed on grip hover (:389) is popped only by hover-leave (suppressed during a drag by `guard !isDragged`, :388), by `.onChange(of: isDragged)` when the row is released away from the grip (:398), or by `.onDisappear` when `isHoveringGrip` is still true (:392). A row deleted or sent while dragged with the pointer off the grip satisfies none of these, leaving one unbalanced NSCursor push for the rest of the launch. Mirror the same push/pop sites rather than silently balancing.

54. [chat-and-composer] ThreadRuntime.swift:206 - `private(set) var followUps: [FollowUpPrompt] = []` is indented four spaces deeper than its siblings. Cosmetic only; noted so a port's diff reviewer does not read it as a nesting change.

55. [chat-and-composer] ProseRunView.swift:120-124 - `ProseRunRepresentable.dismantleNSView` clears delegate, onOpenHead and mergeTarget but does NOT call `view.streamVeil.detach(view)`, unlike LinkParagraphView.dismantleNSView (LinkParagraphView.swift:73-77). Latent rather than live: the prose-run path never activates the veil (see X5), so no display link is left running today. Reproduce the asymmetry; do not add the missing detach.

56. [shell-chrome-and-theme] FloatingSidebarPanel.swift:75-76 - resize clamp applies the outer min last (min(max(240, w), areaW - originX - 12), min(max(320, h), areaH - originY - 12)), so in a narrow chat area the panel is written SMALLER than minSize 240x320 and line 77 persists that sub-minimum value to settings.sidebarPanelSize on the same drag tick. The read path (:29-32) floors it again, hiding the bad value until the window grows.

57. [shell-chrome-and-theme] FloatingSidebarPanel.swift:34 with :75-76 - the resize clamp is anchored on `origin = dragOrigin ?? rest`, so resizing while the title strip is held clamps against the dragged origin rather than the resting one.

58. [shell-chrome-and-theme] SidebarView.swift:2470-2472 - `frames.first { $0.value.contains(point) }?.key` over a [UUID: CGRect]. With two overlapping row frames (mid-animation or during a settle handover) the grabbed row is picked in Swift's per-process dictionary order, which is neither insertion nor z-order and is unstable between runs.

59. [shell-chrome-and-theme] SidebarView.swift:846-848 vs :1443-1469 - the drag-slot fallback uses `let rows = (headCount + 5) / 6` while the card lays out at seven per row until measured (:1453) and adds a whole extra glyph row for the chevron when heads.count > 12 (:1463 `let cardRows = rows.count + (capped ? 1 : 0)`). The reorder slot therefore under-measures a capped card and mis-measures any card with 7+ heads until the real height lands in rowHeights['heads-<id>'].

60. [shell-chrome-and-theme] AppTheme.swift:223-227 - ThemeManager.current's didSet has no equality guard, so reassigning the same theme re-parses the whole ThemeSpec (5 hex colours) and posts ThemeManager.didChange, re-tinting every layer-backed spinner cell and HydraButton observer for nothing.

61. [shell-chrome-and-theme] AppSettings.swift:133 - the Swift property is `settledCollapsed` but the persisted key string is 'settledSectionCollapsed'. Compatibility-critical mismatch; persist the string, not the property name.

62. [shell-chrome-and-theme] Popovers.swift:259-274 vs :372 - the same RowAction runs at three different times: synchronously BEFORE the close for a confirmation popover row (:262), deferred one main-loop turn AFTER the close for a plain popover row (:271-273), and synchronously inside the AppKit menu callback for a context-menu row (:372). The sidebar depends on it: SidebarView.swift:2229-2242 calls the row's own settle/reopen, SidebarView.swift:2298-2306 posts model.finishRequest.

63. [shell-chrome-and-theme] WindowChrome.swift:366-370 - persist() writes 'sidebarStartsOpen' on every sidebar toggle and every drag release although neither changes it, materialising the default false on disk after the first interaction.

64. [shell-chrome-and-theme] WindowChrome.swift:215-217 - drag(by:) clamps the live width to 0...440, below the 210 minimum; only endDrag() (:344-356) decides between collapsing (width < 110) and flooring at 210. A width between 0 and 110 during the drag is a real transient state the chat pane lays out against.

65. [shell-chrome-and-theme] SidebarView.swift:1358 - SidebarHelperRow draws a running Hydra head's glyph without isRunning, so the breath ring the same head shows on its card (:1478) is missing on its helper row.

66. [shell-chrome-and-theme] Popovers.swift:8-12 - presentedChrome() tints with `ThemeManager.spec.accent`, the raw optional, not Chrome.accent which falls back to the system control accent. For the system, light and dark themes this applies .tint(nil), so a presented popover tree's controls do not receive the explicit tint the window root applies.

67. [settings-usage-tour-diff-terminal] DiffInspector.swift:627-636 and :640-657 - show() increments `session` (line 629) without assigning `pendingSession`, which is only resynchronized in close() (:576-577) and reopen()'s anchor-changed branch (:607-608). Any popover close the coordinator did not initiate (.semitransient self-close at :536, or the anchor view leaving its window) reaches popoverDidClose with session != pendingSession, so the guard at :649 is false: it does not clear `shownRuntime.isDiffVisible` (:650) and does not drop `popover.contentViewController` (:653). The thread stays flagged as showing its diff and the inspector view tree stays alive observing the runtime. Reachable from the very first show() (session 1 vs pendingSession 0). Reproduce, do not fix.

68. [settings-usage-tour-diff-terminal] MCPSettingsPage.swift:564-566 - the custom-server ellipsis menu's Delete runs `model.mcp.deleteCustom(server)` on one click with no confirmation, ignoring `settings.confirmBeforeDeleting` (default true, AppSettings.swift:702) which every other destructive path honours (SettingsView.swift:851, :892; SidebarView.swift:192, :360, :891). deleteCustom also runs disconnect(), which deletes the server's Keychain secrets (MCPStore.swift:124, :193-199). Reproduce, do not fix.

69. [settings-usage-tour-diff-terminal] MCPKeyPanel.swift:15-31 - present() has no `isPresenting` re-entry guard (unlike MCPSetupPanel.swift:18-26) and close() (:33-36) clears no presented-entry id. A second present for the same entry re-opens `entry.keysURL` in the user's browser (:16-18), re-measures, rebuilds the hosting controller and repositions the panel, discarding anything typed into the fields. Reachable from MCPSettingsPage.swift:356, :538 and :973. Reproduce, do not fix.

70. [settings-usage-tour-diff-terminal] TerminalPanel.swift:80 with AppSettings.swift:722 - `.frame(height: dragHeight ?? model.settings.terminalHeight)` applies the persisted height with no clamp, and the load path is `defaults.object(forKey:) as? Double ?? 260` with no clamp; the 140...760 clamp exists only inside the drag's onChange (:89). An out-of-range stored value is applied verbatim and can only be corrected by dragging. Contrast ChatZoom.clamped (ChatZoom.swift:16-18), which does clamp on read. Reproduce, do not fix.

71. [settings-usage-tour-diff-terminal] UsageFloatingPanel.swift:82-84 vs UsagePanel.swift:29 - the floating panel's title capsule draws a second ProviderIcon whenever `headsProvider != nil`, while the body suppresses the heads section unless `headsProvider != provider`. A Hydra pair whose heads run on the lead's own provider shows the same provider icon twice in the strip. Reproduce, do not fix.

72. [settings-usage-tour-diff-terminal] UsagePanel.swift:32 - `showsContext = usage?.fraction != nil && usage?.windowTokens != nil` is a redundant conjunction: ContextUsage.fraction (Timeline.swift:301-304) already returns nil unless windowTokens is non-nil and > 0. Harmless dead condition; do not treat the operands as independent.

73. [settings-usage-tour-diff-terminal] MCPSettingsPage.swift:182-192 and :556-566 - four of six PopoverItem actions omit `showMenu = false` while two set it. Since PopoverItem always closes its popover before running the action (Popovers.swift:270-273), the writes are redundant in both directions: dead defensive code, not a behaviour difference.

74. [settings-usage-tour-diff-terminal] SettingsView.swift:926-929 - AboutSoftwareUpdateSection is the first child of the About page's VStack(spacing: Chrome.sectionSpacing) but renders nothing unless `checker.updateAvailable` (AboutUpdateViews.swift:731-733). On the common path it is a zero-height container that the parent VStack still separates from the app card by section spacing, leaving an extra gap above the app card whenever no update is waiting.

75. [git-and-checkpoints] Git.swift:159 - workingChanges() tests BYTES but trims CHARACTERS: `if patch.utf8.count > 40_000 { patch = String(patch.prefix(40_000)) }`. String.prefix(_:) counts Characters, so a non-ASCII patch can keep several times 40,000 bytes (~160 KB for 4-byte scalars, more for multi-scalar graphemes). The port must copy the mismatch: test UTF-8 byte count, then take a 40,000-GRAPHEME prefix.

76. [git-and-checkpoints] Git.swift:213-225 - mergePullRequest burns a pointless final retry delay. On attempt == 5 a GitLab failure matching the retry predicate still sleeps `3 + attempt * 2` = 13 seconds, then the loop ends and :226 throws. A full GitLab retry sequence sleeps 3+5+7+9+11+13 = 48 s before reporting failure, the last 13 wasted. Reproduce; do not restructure the loop.

77. [git-and-checkpoints] Git.swift:223 - the retry predicate is a lowercased substring test over stdout+stderr: `text.contains("405") || text.contains("not allowed") || text.contains("checking")`. "405" matches those digits anywhere, including inside a request number, line number, byte count or commit hash prefix. Reproduce verbatim.

78. [git-and-checkpoints] Git.swift:625 (blobs), :607-609 and :616-617 (restore), :395 (checkoutPaths), :400 (removePaths) - these pass user file paths to git as PATHSPECS WITHOUT `--literal-pathspecs`, while diff (Git.swift:552) adds it whenever paths are given. A file named with `*`, `?`, `[` or a leading `:` is a glob on the restore/checkout/remove/ls-tree paths but a literal on the diff path. Reproduce the inconsistency; adding the flag would change which files a revert touches.

79. [git-and-checkpoints] Git.swift:253 vs :448 - the two --pathspec-from-file adds differ: captureTree(paths:) passes `--ignore-errors`, the incremental scratch add does not. An unreadable path is skipped in the former but takes the scratch capture down (falling through to delete-and-reseed at :453-459) in the latter. Reproduce both flag sets exactly.

80. [git-and-checkpoints] Git.swift:347-353 - presentPaths pairs answers to paths with `zip(paths, answers)`, which silently TRUNCATES to the shorter sequence. If `cat-file --batch-check` emits fewer records than paths (crash mid-stream, 120 s timeout kill), the unanswered tail is silently reported as ABSENT rather than as an error, and trackedPaths then excludes those files from a hydra merge. Contrast Git.swift:645-646 where workingBlobs DOES check and throws `ShellError("git hash-object answered for \(hashes.count) of \(existing.count) files.")`. Asymmetry is upstream; reproduce it.

81. [git-and-checkpoints] WorkingTreeWatch.swift:271 vs :96-100 - the cookie-timeout tearDown() deletes the shared scratch index while another caller's detached capture may be writing through it, the exact hazard the registry-eviction path documents and avoids. Observable effect: a lost checkpoint for that turn and, later, `"No file checkpoint is available for this message. You can still keep the file changes."` (ThreadRuntime.swift:1771).

82. [git-and-checkpoints] WorkingTreeWatch.swift:159-170 + :92-108 - a watch torn down by a cookie timeout is never rebuilt (FSEventStreamStart occurs only at :148, in init) and is handed back to every later caller by shared's fast path at :93, so that directory is permanently demoted to a full fresh capture per snapshot until FIFO eviction at `limit = 32` happens to reach it.

83. [git-and-checkpoints] Git.swift:547-573 - `diff(paths: [])` (non-nil, empty) emits `--literal-pathspecs ... <from> <to> --` and produces a FULL-TREE diff instead of an empty one, with no early return of the kind resetIndex (:388), checkoutPaths (:394), removePaths (:399), restore (:602) and previewRestore (:582) all have. Unreachable today (all three paths: call sites guard on non-emptiness: AppModel+Hydra.swift:726, :936; ThreadRuntime.swift:3289). Reproduce the missing guard.

84. [git-and-checkpoints] Git.swift:586-590 - previewRestore feeds CWD-relative pathspecs to `git diff --numstat` but consumes the REPO-ROOT-relative paths it returns, joining them onto repositoryRoot(). Self-consistent only because git's default diff output is repo-root-relative; a user with `diff.relative = true` breaks it, and Git.run does not neutralize it (its only -c options are core.quotepath=false and color.ui=false, Git.swift:43).

85. [git-and-checkpoints] Git.swift:650-656 - deleteCheckpoints discards its own result: `_ = try? await run(["update-ref", "--stdin"], input: ...)`. A failed batch delete leaves the refs, and therefore the checkpoint commits, in the user's repository with nothing reported anywhere. Reproduce the silence.

86. [git-and-checkpoints] Git.swift:67-74 - repositoryRoot() derives the root by calling deleteLastPathComponent() once per '/'-separated component of `rev-parse --show-prefix`, starting from the Git instance's own `directory` URL; it never asks git directly (`--show-toplevel` exists). Any discrepancy between the URL Foundation built and the path git resolved (symlinked project directory, case-differing path, a component git normalized) yields a wrong root, and that root is what blobs, workingBlobs and restore join every path onto. Most likely Windows path-normalization divergence point (8.3 short names, drive-letter case, junctions). Reproduce the component-counting algorithm rather than substituting --show-toplevel.

87. [build-assets-and-release] scripts/tour_captures.py:36 - APP = DERIVED / "Build/Products/Debug/Droppy Code.app" names a bundle the Debug configuration never produces. The same file builds -configuration Debug at :101, and the Debug PRODUCT_NAME is "Droppy Code Dev" in project.yml:54 AND in the checked-in project.pbxproj:326. subprocess.run(["open","-n",str(APP),...], check=True) at :117 raises CalledProcessError on a clean checkout, so tour_captures.py cannot run standalone at dd2b86e. website_captures.py:34 has the correct path.

88. [build-assets-and-release] scripts/website_captures.py:164 - wait_for_run(marker, 330) gives the --tour-captures run 330 s while the app's own watchdog is 600 s (TourCaptures.swift:53, comment at :51 budgets 'ten minutes'), then pkill -9s it (:150). The default invocation kills a healthy tour capture 270 s early, after which encode_tour() exits 'No <name>.png from the last tour run; rerun without --encode' (:213). tour_captures.py:118 uses 630 s and is correct.

89. [build-assets-and-release] scripts/tour_captures.py:109-129 capture() has no launch-detection wait: it starts the 630 s deadline immediately after open -n (:117-118) and breaks the loop the first time pgrep -f <marker> is empty (:119-123), possibly 2 s in, before LaunchServices has started the app. website_captures.py:133-151 wait_for_run is the fixed version of the same loop (docstring :136-137) and tour_captures.py never received the fix.

90. [build-assets-and-release] scripts/build_changelog.py:20 + :58-72 - the ## [Unreleased] section and all its bullets are discarded silently. RELEASE_RE requires digits inside the brackets and split_releases only collects after the first match, so nothing before the first version heading is ever seen and no diagnostic is emitted. At dd2b86e this drops CHANGELOG.md:7-12 (a ### Bug fixes heading and 3 bullets). A typo in a version heading would vanish the same way.

91. [build-assets-and-release] scripts/build_changelog.py:87-95 - after any ### heading outside {new features, bug fixes, refinements, thanks}, current becomes None and every following non-blank line is appended to summary_lines with line.strip(), leading '- ' included, and joined into the release summary at :105. Unrecognised sections' bullets land inline in the summary prose with no warning.

92. [build-assets-and-release] scripts/sync_dev_data.sh:36 + :49 - rsync -a --delete copies the whole Application Support library including mcp.json and custom-mcp.json (MCP.swift:244, :246), but only four Keychain accounts are re-added ("DeepSeek API Key", "Meta API Key", "Z.ai API Key", "Command Code API Key"). MCP secrets live in the SAME service under accounts 'MCP ' + server + ' ' + field (MCPKeychain.swift:7-9, :15) and are not mirrored, so Dev ends up with the release app's full MCP configuration and no secrets. quick_run.sh:10-11 does not say this.

93. [build-assets-and-release] website/index.html:543 - the download-card literal 'Version 1.5.3 · Signed and notarized' (literal U+00B7) is hand-maintained and stale against MARKETING_VERSION 1.6.0 (project.yml:13, pbxproj:208, :271) and changelog.json's newest release 1.6.0. grep -rn index.html scripts/ returns nothing and deploy_site.sh regenerates only changelog.json.

94. [build-assets-and-release] website/index.html:369 - https://droppy-releases.jordylegrand.workers.dev/site-assets/droppy-code/usage/v1/limits.webp has no generator or uploader in the repository. The only declared prefixes are 'site-assets/droppy-code' (website_captures.py:39) and 'site-assets/droppy-code/tour/v5' (:48); no TOUR_STILLS/STILLS/FILMS entry produces a usage/ key.

95. [build-assets-and-release] website/assets/js/code.js:252-254 - the theme-picker comment is stale in both halves: it names 'site-assets/droppy-code/themes' when THEME_BASE at :284 is .../tour/v5/themes/, and it claims 'the tour prefix has no per-theme stills' when website_captures.py:217-220 writes exactly those from TourCaptures.swift:200-209's 26 web-theme-<rawValue> captures.

96. [build-assets-and-release] docs/audit-2026-09-15.md:78 - finding S3 cites `Support/WebsiteCaptures.swift` and '900 lines'. Neither resolves: the file is DroppyCode/App/Captures/WebsiteCaptures.swift and it is 1122 lines. Sibling rows drift the same way (:77 `Support/Shell.swift:208` for DroppyCode/Core/Support/Shell.swift).

97. [build-assets-and-release] docs/performance-2026-09-15.md:5-6, :31, :33, :34 - cites scripts/run_tests.sh ('all seven suites (117 tests)'), scripts/benchmark_performance.sh, Tests/RuntimePerformanceTests.swift (test boundedReadRejectsOversizedSparseFiles) and scripts/benchmark_file_matcher.sh. None exists at dd2b86e and AGENTS.md:25 states there is no test target and no test scripts.

98. [build-assets-and-release] README.md:37 - 'A welcome tour on first launch, six pages of real captures; the Help menu opens it again.' is stale against TourPages.swift:4-13 (8 pages), CHANGELOG.md:251 and website/index.html:454.

99. [build-assets-and-release] project.yml:46 and project.pbxproj:295 / :319 - INFOPLIST_KEY_NSHumanReadableCopyright = "Droppy Code, the coding app by Droppy. MIT License." states MIT while LICENSE:4 is the GNU Affero General Public License version 3, index.html:547 shows 'Free / AGPL', index.html:766 labels the repo link 'AGPL-3.0 License' and SettingsView.swift:1003 says 'AGPL-3.0 with attribution terms'. FLAGGED AS AN OWNER DECISION, NOT A MECHANICAL COPY: reproducing a false licence statement is not a parity requirement the Windows port should take on blindly.


---

## Corrections applied to this register after independent verification

The repair pass is evidence, not gospel. These entries were checked against source by the lead and
amended. Both amendments are recorded rather than silently edited.

### Entry 22 (MCPOAuth randomToken) - security framing corrected

The entry is factually right that `MCPOAuth.swift:815-821` fills PKCE verifier (32 bytes) and CSRF
state (24 bytes) using `UInt8.random(in: UInt8.min...UInt8.max)` in a loop rather than
`SecRandomCopyBytes`. Verified at source.

**But the implied weakness is wrong and must not be propagated.** `UInt8.random(in:)` without an
explicit generator uses Swift`s `SystemRandomNumberGenerator`, which is documented as
cryptographically secure and is backed by the platform CSPRNG on Apple platforms. The upstream code
is therefore NOT generating weak tokens, and this is not an upstream security defect.

**The real finding is a port hazard, not an upstream defect:** the byte counts (32 and 24) and the
base64url encoding are parity-relevant and must be reproduced exactly; the random source is the
port`s own choice and MUST be a platform CSPRNG on Windows. Using a non-cryptographic generator
there would introduce a vulnerability the original does not have. Recorded as an ENGINEERING
DECISION in the adaptation register, not as behavior to copy.

### Entry 68 (unconfirmed custom-MCP delete) - escalated, not reproduced

Verified at source: `MCPSettingsPage.swift:564-566` deletes on one click; `deleteCustom` also
removes Keychain secrets (`MCPStore.swift:124`, `:193-199`); `confirmBeforeDeleting` defaults true
(`AppSettings.swift:123`, `:209-210`) and is honored by six other destructive paths
(`SettingsView.swift:851`, `:892`; `SidebarView.swift:192`, `:360`, `:891`).

Because reproducing it destroys credentials the user cannot recover from the app, contract section 8
requires escalation instead of silent reproduction OR silent repair. See UNK-0010. The
"Reproduce, do not fix" instruction in entry 68 is suspended pending the owner`s decision.

### Git.swift 300-second timeout count - corrected

The git-command extract states "19 sites at 300 s (13 git ...)" and then self-corrects to 12 git
lines mid-sentence. Counted from source by the lead:
`grep -n "timeout: 300" Git.swift` yields **18 sites**, of which **12 are git**
(169, 253, 286, 290, 404, 448, 459, 484, 558, 566, 679, 707) and **6 are forge CLI**
(181, 186, 191, 216, 217, 218). The extract`s `add -A` split was correct: 6 sites
(164, 253, 448, 459, 484, 707), one at the 120 s default (164) and five at 300 s.

---

# Register corrections from the 2026-09-20 triage

The triage adjudicators read the cited source and found 19 entries in this
register that misstate what the code does. Each correction is recorded rather than silently applied,
because a wrong defect entry propagates into the port as either a copied non-bug or an invented one.

## RC-01

Entry 2 (AntigravitySession dead mode fields): the dead-store fact is correct but the stated consequence is wrong and must not be propagated. The register says 'A mid-thread mode or Plan/Build change cannot reach agy at all, and even a relaunch uses the original modes while picking up the UPDATED model/effort'. CORRECTION: ThreadRuntime.swift:504-519 includes launchRuntimeMode and launchInteraction in SessionSignature, and :1414/:1418 populate both from thread.* for .antigravity, so the session-reuse check at :1422 fails on any mode or Plan/Build change and :1496-1523 constructs a brand-new AntigravitySession from a fresh SessionConfiguration carrying the CURRENT modes. AntigravitySession is constructed at exactly one site (ThreadRuntime.swift:1516) and start() is only ever called on a freshly made object (:1478, :1486, :1547, :1563), so a relaunch cannot carry stale modes. launchModel and launchEffort are in the signature too, so there is no 'half applied, half discarded' asymmetry. The true finding is a harmless pair of write-only properties. This matters for the port: the Antigravity entries in SessionSignature are what keep the --dangerously-skip-permissions gate (AntigravitySession.swift:135-137) honest, and dropping them would turn this harmless quirk into a real C3/C5 defect that upstream does not have.

## RC-02

Entry 21 (MCPKeychain accessibility never migrates): the description of the code is accurate, but the implied security weakness is not realizable and should not be propagated. CORRECTION: git log -p --follow on DroppyCode/Services/MCP/MCPKeychain.swift shows exactly two commits (739518735 'Add MCP server support', 75e30f217) and kSecAttrAccessibleWhenUnlocked is present on the SecItemAdd path in both, so no shipped build ever created these items with a weaker accessibility for the update path to preserve; the macOS default for an add without the attribute is the same value in any case. The observation is true and currently inert. Recorded so the port does not carry it forward as a security finding, and noted that Windows has no kSecAttrAccessible analogue at all since secrets go through DPAPI.

## RC-03

Entry 17 (custom-MCP decode drops every custom server): not wrong, but INCOMPLETE in a way that changes its classification. The register records only the silent drop. It does not record that the drop is followed by an overwrite: customs stays empty and the next saveCustom (MCPStore.swift:175) or deleteCustom (:196) calls saveCustoms, which atomically writes the empty array over mcp-customs.json (:275). That is what takes the defect out of 'no lasting harm' and into C4, and it is why this entry is escalated rather than reproduced. Two smaller citation corrections in the same entry: the tolerance doc comment is at MCPCustom.swift:36-37, not :37-38, and the outer try? is at MCPStore.swift:247, inside the :245-248 load block.

## RC-04

Entry 27: the line cite is wrong. The register cites Hydra.swift:939-940, which is the .stray arm's comment and its 'var parts' line. The .failed arm that omits the pending sentence is at Hydra.swift:936-937. The substance of the entry is correct.

## RC-05

Entry 35: the line range starts one line late. The register cites Hydra.swift:1069-1073; the 'JSON did not parse' nil is at :1068 and the 'every entry was rejected' nil is at :1072. The conflation the entry describes is real.

## RC-06

Entry 39: 'the three deferral exits in startTurn' is loose. There are two enqueueFollowUp call sites inside startTurn (ThreadRuntime.swift:1160 and :1165) covering three conditions (busy, reverting, archived). The entry's own prose names the three conditions, so this is wording rather than a false claim.

## RC-07

Entry 42: 'The comments are wrong' overstates it. Both constants really are 200_000, so 'the same cap' is true of the value; what differs is the operand (Text.swift:101 bounds old.utf8.count + new.utf8.count, ThreadRuntime.swift:3373 bounds the produced diff.utf8.count). It is a misleading true statement, which is why it survives reading.

## RC-08

Entry 44: the stated consequence is wrong. The register says 'a key that had just been promoted out of previous can be dropped at the very next rollover'. It cannot. RecentCache.swift:22-27 promotes the key by re-inserting it, and the write at :54 happens after the rollover block at :47-53, so a just-promoted key always lands in the fresh current, survives the next rollover into previous, and is dropped only at the rollover after that if untouched. The wholesale discard of the older generation is real; the timing claim is off by one generation.

## RC-09

Entry 45: the conclusion is wrong and must not be carried into the port. The register says 'A capture run therefore gets a clean recent-command list but the real machine's model catalogs, contradicting WebsiteCaptures.swift:10-12'. Every UserDefaults.standard site named in the entry is gated on !CaptureRun.isEnabled: ProviderRegistry.swift:162 guards the read at :163, :176 guards the read at :177, :538 guards the write at :541, and :669 guards the write at :672. A capture run therefore neither reads nor writes the real machine's catalogs or catalog dates, and the isolation promised at WebsiteCaptures.swift:10-12 holds. What remains is an inconsistency of mechanism (a separate defaults suite for recentSlashCommands, an outright skip for the catalogs), not a leak.

## RC-10

Entry 50: the entry presents an unreachable branch as a latent trap without saying it is unreachable. ComposerView.swift:1048 defines isVisible as !items.isEmpty, so the switch at :312 cannot be entered with an empty items array, and the clamp at :471 runs only after the non-empty guard at :466-469, so selected is always in range when :321 subscripts. The missing bounds check at :320-322 is real, but it is a dead condition, and the register's suggested Windows no-op changes no reachable behaviour.

## RC-11

Entry 64 cites 'WindowChrome.swift:215-217' for drag(by:). Wrong location: :214-217 is inside the titlebar constraint update loop ('guard changed else { return }' at :214). 'func drag(by translation: CGFloat)' is at :340-342, body 'width = min(Self.maximumWidth, max(0, anchorWidth + translation))'. The rest of entry 64 is correct: endDrag() at :344-356, minimumWidth 210 (:240), maximumWidth 440 (:241), collapseThreshold 110 (:242).

## RC-12

Entry 74 cites 'SettingsView.swift:926-929'. The VStack(spacing: Chrome.sectionSpacing) opens at :927, not :926 (:926 is 'var body: some View {'). Correct span: :927-929. Substance unchanged.

## RC-13

Entry 55 presents LinkParagraphView.dismantleNSView as the complete counterpart to ProseRunView's. Minor imprecision: LinkParagraphView.swift:73-77 clears delegate and mergeTarget and calls streamVeil.detach, but does NOT clear onOpenHead. The asymmetry the entry names (the missing streamVeil.detach in ProseRunView) is real and correctly described.

## RC-14

Entry 62 omits what PopoverItem.confirmation actually is, which invites a wrong reading during the port. Correction for the record: it is an after-the-fact acknowledgement label ('Copied'), documented at Popovers.swift:205-207 as running the action at once, and every use site (RowAction.confirms; TimelineRows.swift:219, :220, :1405, :1406, :1409, :1413) is a Copy or Add. It is NOT a pre-action safety confirmation, so entry 62's synchronous-before-close ordering bypasses no destructive gate and the entry stays ORDINARY. This also means the 'confirmation:' parameter would not have protected entry 68's Delete row even if it had been passed.

## RC-15

Entry 80: the "120 s timeout kill" example does not reach the zip. Shell.terminate (Shell.swift:110-118) leaves a non-zero exit status, so ShellResult.succeeded is false (Shell.swift:9) and the guard at Git.swift:347 returns an empty set before the zip at :350 runs. The one real short-answer-with-exit-0 path is the post-exit drain deadline, Shell.swift:105 `await collector.finished(within: 3)` with OutputCollector.finished at Shell.swift:286-293. The asymmetry the entry records (no count check at :350, a thrown ShellError at :645-646) is correct as written.

## RC-16

Entry 78: two over-statements. A leading ':' can only reach checkoutPaths (Git.swift:395) and removePaths (:400); restore (:609, :617) and blobs (:625) map every path through root.appendingPathComponent(), so their pathspecs always begin with '/' and pathspec magic cannot fire. And previewRestore's own numstat call at :586 also omits --literal-pathspecs, so the revert preview globs exactly as the revert does; the inconsistency is between the diff() helper (:552) and everything else, not between what the user is shown and what is done.

## RC-17

Entry 99: "index.html:547 shows 'Free / AGPL'" is the wrong line. The text is at website/index.html:545 (<div class="free__price">Free<small>AGPL</small></div>); line 547 is a system-requirements bullet. The entry's other cites (project.yml:46, pbxproj:295 and :319, LICENSE:4, index.html:766, SettingsView.swift:1003) are exact.

## RC-18

Entry 96: the register says the sibling row at docs/audit-2026-09-15.md:77 drifts in its PATH. It also drifts in its LINE number: Shell.swift:208 is part of the read-only-command classifier, while LoginEnvironment.load, the subject of finding S2, is at Shell.swift:353 and its interactive login shell run is at :369.

## RC-19

Entry 86 (scope, not an error): the entry is right about the algorithm, but repositoryRoot() has only two callers (Git.swift:588 and :603) and both pass the joined paths to blobs() first, whose ls-tree rejects a pathspec outside the repository. A wrong root that lands outside the repository therefore throws before anything is written or deleted, and the revert reports "files could not be restored" (ThreadRuntime.swift:1874-1875). The harm is a failed revert, not a destructive one.


---

# Defects found during Gate B specification (2026-09-20)

Found by the spec and repair passes, not present in the original 99. Each was verified against
source. Ordinary quirks are REPRODUCED; any that met the AM-0001 consequence criteria were
quarantined instead and are named in .

100. AppModel.swift:638 vs :642 — forwardThread appends the new thread to `threads` (:638), expands its project (:639) and records it as the last project (:640) BEFORE awaiting installContinuation, then returns nil on failure (:642) without removing it. Realistic worst outcome: after a forward whose continuation fails to install, an empty thread titled "Continued: <source title>" is left in the sidebar, unselected and unexplained; the next scheduleSave from any other cause persists it. No data is lost and nothing is overwritten, so this is a stray-record defect, not a destructive one. Specified as written in PR-LAUNCH-082 rather than repaired.

101. AttachmentPreview.swift:82 — `PreviewImages.attachments` is an unbounded `[String: Attachment]` keyed by path with no eviction, in direct contrast to `FileIcons` twenty lines below it (:112-125), which is explicitly bounded at 64 with a comment saying why ("Bounded: icons are small but unbounded kinds are not"). Realistic worst outcome: a long session that previews many distinct images grows the map without limit. The records are three small strings each, so the growth is slow and this is a minor leak rather than a user-visible failure; the per-path identity it provides is load-bearing (the preview panel's toggle compares it), so it cannot simply be dropped. Recorded in PR-COMPOSER-206 as unbounded.

102. AttachmentPreview.swift:217-221 (latent) — `AttachmentVideoThumbnail`'s `.task(id: attachment.path)` opens with `guard image == nil else { return }`, so a view instance rebound to a different path keeps the previous video's frame on screen. The photo path handles the same case correctly: TimelineRows.swift:1346-1355 re-resolves the memoized image for the NEW path and assigns it. I could not demonstrate a reachable failure: all three call sites (ComposerView.swift:1001, EditorAttachments.swift:22, TimelineRows.swift:1229) use `ForEach(attachments)` keyed by Attachment's own Identifiable id, so a different attachment gets fresh @State. Reporting it as a latent inconsistency, not a confirmed defect, so it is on record if a future strip is keyed by index or if an Attachment's path can change under a stable id.

103. ThreadRuntime.swift:2890 and :2898 - spawnWaitingHeads removes a delegated task from hydraWaiting BEFORE attempting the spawn, then `guard let head = app.spawnDroppyHead(...) else { continue }` drops it with no note, no retry and no record. Because settleBatches only waits on the batch's pending set and on hydraWaiting (ThreadRuntime.swift:2611), the batch then settles without that head, the lead is handed the surviving reports and reports as though the whole block finished. Realistic worst outcome: a user delegates ten tasks, one spawn fails (no head slot resolvable, thread insert refused), and the work is silently never done while the chat says it is. upstream_defects.md entry 32 covers a DIFFERENT line of the same loop (:2889, the uncapped sweep when hydraLaunch is nil) and does not cover this. Recorded on PR-RUNTIME-233 as REPRODUCE, DO NOT FIX.

104. AppSettings.swift:363, :374 and the two sibling didSet blocks, with APIKeychain.swift:22-39 - when the Keychain refuses an API key write, the key is written to UserDefaults in plain text under deepseekAPIKey / metaAPIKey / zaiAPIKey / commandcodeAPIKey (AppSettings.swift:151-154), with nothing telling the user. Realistic worst outcome: a provider API key sits unencrypted in the app's defaults, readable by any process running as the user, on any machine where protected storage is unavailable. Not in upstream_defects.md. Recorded on the new PR-PROVREG-239.

105. AppSettings.swift:363 versus APIKeychain.swift:23 - the plaintext mirror is written UNTRIMMED from the input property while the Keychain stores the value trimmed of whitespace and newlines, so the two stores can hold different strings for the same key and a key pasted with trailing whitespace behaves differently depending on which store answered. Minor but observable. Not in upstream_defects.md. Recorded on PR-PROVREG-239.

106. AntigravitySession.swift:473 - the current-model test `currentID?.hasPrefix(parsed.baseSlug) == true` is not anchored on a separator, so a base slug that happens to be a prefix of an unrelated model id marks the wrong model as the default in the catalog. Realistic worst outcome: a new thread opens on a model the user did not choose. Not in upstream_defects.md. Recorded inside PR-PROVCLI-431 as upstream behaviour to reproduce, not fix.

107. DroppyCode/Core/Support/Shell.swift:200-201 - the sort classifier tests `$0.hasPrefix("-o")`, which a double-dashed long option can never satisfy. `sort --output=victim.txt in.txt` is therefore classified as a READER. Worst outcome: in Command Code's .auto permission mode the command is auto-approved without the user seeing it (CommandCodeSession.swift:710), and ThreadRuntime.swift:3232 takes no checkpoint for it, so the overwritten file has no base to revert to. Not in upstream_defects.md; no Shell.swift classifier entry exists there at all.

108. DroppyCode/Core/Support/Shell.swift:198-199 with :162 - searchActions holds only -delete, -exec, -execdir, -ok, -okdir, -x, -X. `find . -fprintf out.txt '%p\n'` writes a file and is classified as a READER. Same two consequences as the sort case (auto-approval plus no checkpoint). Not in upstream_defects.md.

109. DroppyCode/Core/Support/Shell.swift:202-204 with :153-159 - gitReaders contains tag, config and remote, so `git config user.email x`, `git tag v1` and `git remote add origin <url>` are classified as READERS. The spec already records this inside PR-TERM-098's failure_cases as upstream behaviour, but the defect register has no entry for it, so it reads as a spec aside rather than a known defect. Worst outcome: a repository-config write is auto-approved in .auto mode and never checkpointed.

110. DroppyCode/Services/Git/WorkingTreeWatch.swift:191-192 - the cookie rule accepts ANY path under <gitdir>/droppy-code-sync- and raises cookieSeen to its decimal suffix, with no check that this watch wrote it. Any process that can create a file in the git directory (a hook, another tool, a second copy of the app) can release a pending waiter early by naming a large number. Worst outcome: settledGeneration returns while events are still in flight, the capture trusts a stale changed set, and the resulting checkpoint records only part of the tree - which is exactly the harm BLK-0003 is open over, reachable without any cookie timeout. Not in upstream_defects.md.

111. DroppyCode/Services/MCP/MCPKeychain.swift:61-67 - deleteAll iterates entry.fields only, so a secret written under a field key the entry no longer declares is never deleted and never enumerated. Worst outcome: a credential the user believes they removed by disconnecting stays in protected storage indefinitely, under an account string nothing in the app will ever look up again. Not in upstream_defects.md (entry 21 covers a different MCPKeychain problem, the missing kSecAttrAccessible on update).

112. DroppyCode/App/AppModel+HydraMerge.swift:715-716 - the gitLabAuth string interpolates {detail} between two hard '\n\n' pairs. When glab's failure message is empty the user reads four consecutive newlines, i.e. a visible gap, in the chat note. Cosmetic only. Not in upstream_defects.md.

113. NOT AN UPSTREAM DEFECT, a porting-artifact error: porting/extracts/mcp-protocol-and-oauth.md section 1.9 cites MCPHub.swift:200 for the tools/list timeout and :237 for tools/call. The real call sites are :198 and :224-228. The extract is otherwise byte-accurate (I verified its section 4 SVG against MCPOAuth.swift:903 character for character). The new MCP rows cite the source lines and flag the stale ones, but the extract itself is outside this pass's editable set.

114. NOT AN UPSTREAM DEFECT, a register error confirming audit finding Wrong #5: porting/blocked_requirements.json BLK-0002 concrete_harm and porting/unknowns.md UNK-0012 both name 'mcp-customs.json'. The source path is custom-mcp.json (MCP.swift:246, read at MCPStore.swift:246, atomically overwritten at :275). Gate C reads that register, and its stated harm currently names a file that does not exist. Neither file is editable by this pass.

115. DroppyCode/App/Views/Settings/SettingsView.swift:48-60 - SettingsPage.tint declares nine colours and NOTHING in the pinned tree reads the property. The sidebar builds every row from title and symbol only (:143-152); a repository-wide grep for a reader returns none (the `.tint` hits at SettingsView.swift:464, AboutUpdateViews.swift:660 and ShortcutsSettingsPage.swift:436 are the SwiftUI environment tint, standing.tint and session.tint respectively), and SettingsPage is referenced outside its own file only by WindowManager.showSettings(page:) at WindowManager.swift:32. It is dead and has no entry in dead_code.md. Realistic worst outcome: a Windows port reproduces nine sRGB triples that paint no pixel, and, worse, a reviewer reading PR-SETTINGS-007's old title ("used by the command palette and anything else asking for a page's colour") expects tinted settings sidebar rows and files the correct, untinted port as a parity failure. I corrected PR-SETTINGS-007 in SETTINGS.json; the dead_code.md entry is still owed and that file is outside my editable set.

116. DroppyCode/UI/Common/RenderBudget.swift:30 - setScrolling's `reporter` parameter defaults to nil, so `setScrolling(true, timeline: id)` is a legal call that registers an active timeline with no reporter. The watchdog at :49-54 then drops that timeline on its first 2 second sweep because `reporters[id]?.object` is nil, un-pausing every animation while the reader is still scrolling. No caller in the pinned tree does this (SettingsScrollTracker.swift:25 and ThreadTimeline.swift:1037 both pass reporter: self), so it is latent rather than shipped, but the signature invites it. Realistic worst outcome: a port adds one start report without a reporter and scroll-pausing silently stops working app-wide after two seconds. Recorded in PR-SETTINGS-263's windows_path.

117. DroppyCode/App/Runtime/LimitHandoff.swift:1-78 - the whole file is transitively dead (only caller AutoContinue.swift:80, whose only caller LimitResumeBadge.swift:31 is proven dead by dead_code.md entry 21) yet has no dead_code.md entry, unlike its two callers. Not strictly new (critic finding 40 raises it) but it is still unrecorded in the ledger. Realistic worst outcome: a later reader treats the silence as an omission and ports eleven user-visible strings and four caps that no user can reach, or conversely a future port of LimitResumeBadge reinstates the badge without the handoff payload.

