# Web surfaces threat model

W26 of the web surfaces plan (DESIGN.md 8.5, at `E:/Droppy/work/design/web-surfaces/DESIGN.md`). Review date 2026-10-13,
branch `claude/web-w26`. Decisions: ED-NEW-web-w26-1 to -8 in `porting/engineering_decisions.md`.

This document covers every surface where nth shows web content or lets content act:

- **Visus**, the browser pane: `browse`, `preview` and private tabs, which belong to the user.
- **Agent tabs**: Visus tabs the model drives through the `web_*` tools.
- **Artifacts (Tabula)**: pages the model writes, with the `window.nth` bridge.
- **Inline HTML widgets**: live answers drawn in the thread.
- **MCP Apps**: UI resources that a connected MCP server ships, drawn in the thread or in a pane.

It names what is worth protecting, who attacks it, where the trust lines run and which test holds each mitigation. It
then lists what the review found and fixed, what risk remains, and which checks only a real WebView2 can make.

## 1. Assets

| # | Asset | Where it lives |
|---|---|---|
| A1 | The user's files, projects and folders | disk; reached by the provider's tools and by file pickers |
| A2 | The user's logged-in web sessions: cookies, storage, saved passwords, passkeys | the `browse` profile |
| A3 | The chat itself: history, project context, memory, other chats | ThreadRuntime, thread files |
| A4 | Credentials nth holds: provider logins, API keys, MCP server tokens | the account store, provider environments |
| A5 | The user's MCP servers and their tools (deploys, mail, tickets) | the MCP hub |
| A6 | The user's computer and local network: localhost dev servers, routers, printers, the cloud metadata address | the network the browser process can reach |
| A7 | The user's money and quota: provider spend, plan limits | provider calls |
| A8 | The user's trust in nth's own UI: cards, approvals, the trust strip | native windows over web rectangles |
| A9 | Data each artifact, widget or App keeps for itself | per-site storage, `nth.storage` |
| A10 | The OS: protocol handlers (`ms-msdt:`, `search-ms:`, `ms-settings:`), downloads that run code | Windows |

## 2. Attackers

| Attacker | Controls | Wants | Main surfaces |
|---|---|---|---|
| **T1. A malicious page** | any site the user or the model opens in Visus; any frame inside it | A2, A4, A10; to fool A8; to inject instructions into the model through what the model reads | Visus tabs, agent tabs |
| **T2. A malicious artifact prompt** | the HTML, script and `<meta name="nth-connect">` of an artifact, written by a model that read hostile text, or pasted in by a user who was tricked | to get out of the sandbox to the network (data theft), to A1, A3, A5, A7, or into another artifact (A9) | artifacts, inline widgets |
| **T3. A malicious MCP server** | its tool results, its UI resource (HTML), its declared CSP domains, and the messages, context and links its App sends | the same as T2, plus driving the model on its own, posing as the user, and reaching A6 through its declared domains | MCP Apps, app messages, model context |
| **T4. A prompt-injected model** | the model's tool calls after it read hostile page, artifact or App text | to act for the attacker with the user's sessions: send, pay, sign up, upload, open new sites carrying data in the address, read shared tabs, write hostile artifacts | agent tabs, `web_*` tools, artifact tools |

The attackers chain together. A page (T1) injects the model (T4), and the model writes a hostile artifact (T2). The
mitigations below are built so that no single link in that chain gets past a native card the user answers.

## 3. Trust boundaries

1. **B1. Web content to the nth process.**
   - Host objects are off everywhere.
   - Web messages are accepted only from nth's sandbox origins, and each message's origin is checked.
   - Navigation and new-window events are handled for every view.
   - `WebResourceRequested` answers every request from a sandboxed view and refuses the rest (fail closed).
   - Document-created scripts check `location.origin` first.
2. **B2. One sandbox to another.**
   - Each artifact, widget and App gets its own site under `.invalid`: `a-…`, `w-…`, `m-…`.
   - The shared library host is `lib-nth.invalid`.
   - `--site-per-process` keeps each site in its own process.
   - Every sandboxed response carries `Origin-Agent-Cluster`, COOP `same-origin` and CORP `same-origin`.
3. **B3. Profile to profile.**
   - The user's `browse` logins, previews, private tabs, the model's agent tabs (one profile per chat folder) and the
     InPrivate `artifacts` profile never share storage.
4. **B4. Content to model context.**
   - Page text, App messages and App context reach the model wrapped as untrusted.
   - The wrapper's tags cannot be closed or opened from inside.
   - The instructions tell the model that page text is never an instruction.
5. **B5. Model to action.**
   - New sites, guarded words (send, pay, delete …), uploads, cross-origin moves after a read, and shared tabs all go
     through native cards the user answers.
   - Some actions are never done at all: passwords, payments, sign-ups, CAPTCHAs.
6. **B6. MCP server to nth.**
   - A UI resource gets only the domains it declared, after the user allows them; broad access is a separate choice.
   - Its tool calls go through normal approval.
   - Its messages are untrusted, allowed once per chat, and capped in a row.
   - Its own links ask first.
7. **B7. nth to the OS.**
   - External protocols: sandboxes and agent tabs are always denied; in Visus, only a user gesture can launch one, and
     only after a card.
   - Downloads always ask, and risky files need a second click.
   - File picks give bytes, never paths.
8. **B8. Native chrome to web pixels.**
   - The trust strip and the approval cards are native and sit outside the web rectangle. Content cannot draw over
     them (DESIGN 8.2).

## 4. Mitigations and the tests that hold them

Test classes are in `tests/Marrow.Core.Tests/Web/` (Core), `tests/Marrow.Services.Tests/Web/` (Services) and
`tests/Marrow.App.Tests/` (App). Every test is headless unless it is marked Native. Native tests are
scheduled by the lead and run alone (section 8).

### 4.1 Sandboxes: artifacts, inline widgets, MCP Apps (T2, T3)

| Mitigation | Boundary | Tests |
|---|---|---|
| One site per artifact, widget and App; the same id in two chats gives two sites; hosts are one DNS label | B2 | SandboxSitesTests: Each_artifact_has_its_own_site_even_with_the_same_id_in_two_chats, An_artifact_host_is_stable_and_one_dns_label, Labels_never_meet_after_cutting_and_never_end_in_a_hyphen, Surfaces_origins_and_addresses_are_recognised_exactly |
| CSP: `default-src 'none'`, `connect-src 'none'`, `frame-src 'none'`, `form-action 'none'`, `base-uri 'none'`, `worker-src 'none'` and the rest of 6.5 | B1 | CspBuilderTests.The_artifact_policy_is_the_designs; ArtifactRuntimeTests.The_page_carries_the_csp_the_isolation_headers_and_the_import_map_first |
| Isolation headers, nosniff, no referrer, no DNS prefetch, and a Permissions-Policy that denies every device | B2 | SandboxHeadersTests: A_sandboxed_page_carries_the_policy_and_every_isolation_header, Permissions_policy_denies_every_device_and_the_features_the_design_names, The_library_host_is_cross_origin_readable_and_runs_nothing_as_a_page |
| The resource handler serves only the view's own site and the vendored libraries; another artifact's site, a file, the local network and undeclared hosts are refused | B1, B2 | ArtifactRuntimeTests: The_library_host_serves_only_the_vendored_files_and_nths_runtime_files, Every_other_address_is_refused_including_another_artifacts_site; SandboxEscapeTests.Every_resource_kind_to_an_undeclared_host_the_local_network_a_file_or_another_sandbox_is_refused |
| The network grant opens exactly the declared sites of that version; wildcards and IP addresses are dropped and named; an export keeps the grant | B1 | ArtifactNetworkTests (all four); CspBuilderTests.A_network_grant_opens_connect_and_images_to_exactly_those_sites; ArtifactRuntimeTests.A_network_grant_opens_exactly_the_declared_sites_of_that_version; ArtifactExporterTests.An_export_keeps_the_versions_network_grant_and_nothing_more |
| Navigation never leaves the site, whether by click or script, at top level or in a frame; a user's click on a web link is offered to the link setting | B1 | SandboxNavigationTests: Leaving_the_site_is_cancelled_and_only_a_users_web_link_is_offered, New_windows_are_always_denied_and_a_users_click_is_offered_as_a_link; SandboxEscapeTests.Every_navigation_off_the_site_is_cancelled_by_click_or_script_top_level_or_frame |
| No new window, protocol launch, download, permission or screen share, even when another listener says yes; never continue past a bad certificate | B1, B7 | WebBaselinePolicyTests: Sandboxed_content_never_downloads_opens_windows_launches_protocols_or_continues_past_a_bad_certificate, The_agent_profile_and_sandboxed_content_never_get_a_permission; SandboxEscapeTests.No_window_protocol_download_prompt_or_screen_share_gets_out_even_when_a_listener_says_yes |
| InPrivate `artifacts` profile; no host objects, dev tools, context menus or status bar; web messages only for sandboxes | B1, B3 | SandboxEscapeTests.Sandboxes_run_in_the_artifacts_profile_isolated_with_no_host_objects_dev_tools_or_browser_ui; WebProfilesTests: Each_surface_gets_its_profile, Visus_surfaces_create_their_environment_with_identical_options, Developer_tools_open_only_in_preview_tabs |
| Visus's browser arguments are exactly `--site-per-process --force-webrtc-ip-handling-policy=disable_non_proxied_udp`; there is no remote debugging port | B2 | WebProfilesTests.Visus_surfaces_create_their_environment_with_identical_options; SandboxEscapeTests (the same arguments) |
| **R1 (ED-NEW-web-sec-res-1):** artifacts, inline widgets and MCP Apps run in an environment of their own (`WebView2\Sandbox`), started with the first of them and let go with the last, whose arguments are exactly `--site-per-process --force-webrtc-ip-handling-policy=disable_non_proxied_udp --webrtc-ip-handling-policy=disable_non_proxied_udp` (runtime 154 reads only the second spelling) | B1, B2 | WebSandboxEnvironmentTests (all four); Native: WebView2EngineNativeTests.Sandboxed_views_get_an_environment_of_their_own_beside_Visus_on_the_real_runtime, SandboxWebRtcEngineNativeTests |
| The hardening script removes WebRTC, `window.open` and prefetch hints; it runs only on nth's sandbox origins | B1 | SandboxScriptBehaviourTests.Hardening_removes_webrtc_and_windows_and_strips_prefetch_hints; SandboxScriptsTests.The_document_scripts_act_only_on_nths_sandboxed_origins; WebFeatureGuardTests.Sandboxed_content_gets_none_of_them |
| **F2:** the hardening also reaches sandboxed (opaque-origin) frames and every frame the page can reach, after every insertion | B1 | SandboxEscapeScriptTests.Hardening_reaches_sandboxed_frames_and_every_frame_the_page_can_reach; Native: SandboxEscapeNativeTests |
| The bridge talks only through web messages that carry its document token; a message from any other origin is ignored | B1 | SandboxScriptBehaviourTests.The_bridge_talks_only_through_webview_messages_with_its_document_token; ArtifactBridgeTests.A_message_from_any_other_origin_is_ignored; McpAppsHostTests.Messages_from_another_origin_are_ignored; SandboxEscapeTests.A_message_under_any_other_origin_is_never_answered |
| `nth.storage` is kept per artifact with a 20 MB cap | B2 | ArtifactBridgeTests: Storage_round_trips_per_artifact_and_replies_carry_the_document_token, Storage_is_capped_at_20_mb_per_artifact |
| `nth.complete` is context free, asks once per artifact, can be refused, and is limited (20 calls a minute, 100,000 tokens an hour, one call at a time) | B4, A7 | ArtifactBridgeTests: Complete_is_context_free_and_asks_once_per_artifact, Complete_is_refused_by_not_now_by_the_setting_and_without_a_model, Twenty_calls_a_minute_then_rate_limited_and_paused_until_the_window_moves, A_hundred_thousand_tokens_an_hour_and_one_call_at_a_time |
| **F1:** `nth.complete` runs with no tools at all: no built-in tool, MCP server, plugin or slash command; providers that cannot promise this have no artifact model | A1, A5 | ArtifactModelNoToolsTests (all five) |
| `nth.callTool` asks once per tool, then goes through normal approval | B5 | ArtifactBridgeTests.Call_tool_asks_once_per_tool_then_calls_through_the_tool_client |
| `nth.files.pick` gives name, type and bytes, never a path; `nth.send` only drafts | B7 | ArtifactBridgeTests: A_picked_file_reaches_the_page_as_name_type_and_bytes_never_a_path, Send_drafts_text_and_the_theme_is_nths_tokens_only |
| An inline widget has its own site, no network and only the theme | B2 | McpAppScriptsTests.An_inline_widget_has_its_own_site_no_network_and_only_the_theme |
| Each artifact's storage, cookies, IndexedDB and BroadcastChannel are its own; each runs in its own renderer process | B2 | Native: SandboxEscapeNativeTests.Two_artifacts_from_two_chats_share_no_storage_cookie_database_or_channel; process check by hand (section 8) |

### 4.2 MCP Apps (T3)

| Mitigation | Boundary | Tests |
|---|---|---|
| Declared domains stay closed until the user allows them; the card names exact domains and holds back broad ones; a changed list asks again | B6 | McpAppConsentTests: The_card_names_exact_domains_and_holds_back_the_broad_ones, Until_allowed_the_directives_stay_closed_and_after_it_never_grow_past_the_declaration, Broad_access_lets_the_wildcards_through_and_nothing_else, A_changed_list_asks_again; McpAppsHostTests: The_page_is_served_from_its_own_site_and_its_declared_domains_stay_closed_until_the_user_allows_them, Allowing_reloads_with_exactly_the_declared_domains_and_broad_access_adds_the_wildcards, A_grant_is_remembered_and_a_changed_list_asks_again |
| Declared sources that could inject or widen the CSP are dropped with a reason | B6 | CspBuilderTests.Declared_sources_that_could_inject_or_widen_are_dropped_with_a_reason; CspBuilderTests.An_mcp_app_gets_only_what_was_declared_and_allowed |
| **F4:** a host on this computer or its local network counts as broad access, not a domain | B6, A6 | McpAppHardeningTests.A_host_on_this_computer_or_its_local_network_needs_broad_access; SandboxEscapeTests.An_apps_local_network_domains_need_broad_access |
| **F5:** a frame reaches only a declared frame domain | B6 | McpAppHardeningTests.Only_a_declared_frame_domain_may_be_framed; SandboxEscapeTests.An_apps_frames_reach_only_its_declared_frame_domains |
| Nothing reaches the App before its handshake; tool calls reach only this server's app tools, through normal approval | B5, B6 | McpAppsHostTests: Nothing_reaches_the_app_before_its_handshake_and_its_tool_data_follows_in_order, Tool_calls_reach_only_this_servers_app_tools_through_the_normal_approval |
| App messages ask once per chat, post as the App and never approve anything; model context goes to a visible chip | B4, B6 | McpAppsHostTests: An_app_message_asks_once_per_chat_posts_as_the_app_and_never_approves_anything, Model_context_goes_to_the_visible_chip_and_links_go_through_the_link_setting; McpAppConsentTests.App_context_is_capped_last_write_wins_and_the_chip_says_so |
| **F6:** an App's text cannot open or close nth's untrusted wrapper in any spelling | B4 | McpAppHardeningTests: An_apps_text_cannot_open_or_close_the_untrusted_wrapper, Ordinary_app_words_still_read_as_written; McpAppConsentTests.App_words_reach_the_model_wrapped_as_untrusted_and_cannot_close_the_wrapper |
| **F7:** App messages start at most three turns in a row before the user writes | B6, A7 | McpAppHardeningTests.App_messages_start_at_most_three_turns_in_a_row_until_the_user_writes; McpAppsSecurityTests.App_messages_stop_starting_turns_after_three_in_a_row |
| **F3:** an App's own link opens only after the user's Open on a card that names the site; one card at a time | B6, B7 | McpAppsHostTests.Model_context_goes_to_the_visible_chip_and_links_go_through_the_link_setting; SandboxEscapeTests.An_apps_own_link_waits_for_the_users_open_one_card_at_a_time; McpAppsSecurityTests.An_apps_own_link_asks_on_a_card_naming_the_site_and_only_Open_opens_it |
| **R2 (ED-NEW-web-sec-res-2):** a click on a link inside an App's view only offers it on the App's own card (the whole address, Open and dismiss), inline, in the App pane and in the pop-out window; a script-started navigation offers nothing | B6, B7 | McpAppLinkOfferTests (all three); McpAppLinkNoticeTests.An_apps_link_opens_only_from_the_offer_that_shows_its_whole_address_wherever_the_app_is |
| Display modes never go where the App cannot; camera and microphone ask, and everything else is denied | B6 | McpAppsHostTests: Display_modes_follow_the_app_session_and_never_go_where_the_app_cannot, Declared_camera_and_microphone_ask_the_user_and_everything_else_is_denied |
| A page that cannot be read fails visibly and loads nothing | B1 | McpAppsHostTests.A_page_that_cannot_be_read_fails_visibly_and_loads_nothing |

### 4.3 Visus tabs (T1)

| Mitigation | Boundary | Tests |
|---|---|---|
| Top-level pages are http, https and the blank page only; internal and code-running schemes are blocked | B1, B7 | WebPolicyTests: Top_level_pages_are_http_https_and_the_blank_page_only, The_scheme_is_read_strictly, The_design_block_list_is_blocked, Other_code_running_and_internal_schemes_are_blocked_too |
| Protocol launches are denied, asked, or asked with a remember option; only a user gesture launches one; the block list is never offered | B7 | WebPolicyTests: A_launch_is_denied_asked_or_asked_with_remember, The_prompt_title_names_the_site_the_app_and_the_scheme; WebBaselinePolicyTests.A_Visus_tab_launches_a_protocol_only_from_a_user_gesture |
| A lookalike Unicode host shows in its ASCII form | B8 | WebPolicyTests.A_lookalike_unicode_host_shows_in_its_ascii_form |
| Profiles: private tabs keep nothing; agent tabs remember nothing | B3 | WebPolicyTests: A_private_tab_keeps_nothing_comes_back_never_and_is_never_seen, An_agent_tab_is_the_models_and_remembers_nothing; PermissionStoreTests.Private_agent_and_artifact_profiles_keep_nothing |
| Permissions are always the user's decision; unanswered requests take the safe default; a kind outside the design list is denied | B5 | WebBaselinePolicyTests: Unanswered_requests_take_their_safe_default, A_kind_outside_the_design_list_is_denied_with_no_prompt_even_in_a_Visus_tab; PermissionStoreTests: Always_keeps_allow_block_keeps_block_and_this_time_keeps_nothing, Only_http_and_https_origins_are_kept, Answers_survive_a_restart_and_a_damaged_or_edited_file_loses_only_what_breaks_the_rules |
| Downloads that run code need a second click; names are made safe; bidi and trailing-dot tricks are caught | B7 | DownloadGateTests: The_design_list_is_risky, Other_windows_types_that_run_code_are_risky_too, An_archive_that_names_a_program_or_that_the_server_calls_a_program_is_risky, A_bidi_override_or_a_trailing_dot_cannot_hide_a_program, Names_are_made_safe_for_a_folder_of_nths_choosing, A_risky_file_saves_on_a_second_click_within_3_s_and_a_late_one_arms_again |
| Each browser feature is decided per surface (8.4): fullscreen, pointer lock, autofill, passkeys, clipboard | B1 | WebFeaturePolicyTests (all); WebFeatureGuardTests: Autofill_and_password_saving_follow_the_8_4_row_for_every_kind, Pointer_lock_waits_for_nths_answer_and_answers_with_a_user_gesture_in_the_asking_frame, A_locked_pointer_is_reported_and_Esc_always_releases_it |
| Web data is removed on uninstall, and only nth's own WebView2 folder is touched | A2 | WebDataUninstallTests (all) |

### 4.4 The model in the browser (T4)

| Mitigation | Boundary | Tests |
|---|---|---|
| Page text is fenced as untrusted and cannot close its own block; the instructions say page text is never an instruction | B4 | WebAgentPolicyTests: PageContentIsFencedAndCannotCloseItsOwnBlock, TheInstructionsSayPageTextIsNeverAnInstruction; WebAgentEngineTests.APageReadsAsUntrustedLinesWithRefsAndItsSiteCounts |
| **R3 (ED-NEW-web-sec-res-3):** the tab's own address in `web_open`, `web_navigate`, `web_read` and an action's loading line is inside the untrusted block, not on the first line | B4 | WebAgentAddressFenceTests (all four) |
| The model sees its own tabs and shared ones, never private tabs; reading a shared tab asks and never offers Always | B3, B5 | WebAgentEngineTests: TheModelSeesItsOwnTabsAndSharedOnesNeverPrivateOnes, ReadingASharedTabAsksAndNeverOffersAlways, ASharedTabIsNotMovedToAnotherSiteWithoutTheCard |
| An address the model invented is not opened; internal schemes and the deny list are refused | B5 | WebAgentEngineTests: AnAddressTheModelInventedIsNotOpened, InternalSchemesAndTheDenyListAreRefused |
| The first action on a site asks; Don't allow stops before any input | B5 | WebAgentEngineTests: TheFirstActionOnASiteAsksAndAllowOnceLastsWhileTheTabStaysThere, DontAllowStopsTheActionBeforeAnyInput |
| Guarded words (send, delete, localised forms) ask; payments, sign-ups and CAPTCHAs are never done | B5 | WebAgentPolicyTests: GuardedWordsAskTheUser, LocalisedSendWordsAskTheUser, PaymentsAreNeverDone, AccountSignUpsAreNeverDone, CaptchasAreNeverAnswered; WebAgentEngineTests: ASendButtonShowsTheGuardCardAndCancelStopsIt, APaymentIsNeverClickedAndNothingAsks, ACoverOverTheTargetIsJudgedToo |
| Password, card and code fields never take text | B5 | WebAgentPolicyTests: PasswordFieldsNeverTakeText, NewPasswordCardAndCodeFieldsNeverTakeText; WebAgentEngineTests: APageThatMovesFocusToAPasswordFieldGetsNoText, LettersNeverGoIntoAFocusedPasswordField |
| Another site after a read asks, with the query spelled out (no silent exfiltration in the address) | B5 | WebAgentEngineTests: AnotherSiteAfterAReadAsksWithTheQuerySpelledOut, ALinkTheModelClicksToAnotherSiteAfterAReadIsStopped, GoingBackToAnotherSiteAfterAReadIsStoppedLikeALink |
| Uploads go only through nth's card, and a model never drops a local file into a page | B5, B7 | WebAgentPolicyTests.AFileInputAsksBeforeTheUploadChooserOpens; WebFeatureGuardTests.An_agent_tabs_file_input_is_intercepted_and_only_nths_card_gives_it_files; WebFeaturePolicyTests.A_model_never_drops_a_local_file_into_a_page |
| A model step cannot make the user's calls (permissions, fullscreen, clipboard) | B5 | WebFeaturePolicyTests.A_model_step_cannot_take_what_is_the_users; WebFeatureGuardTests.A_model_step_cannot_make_the_users_calls_and_nothing_reaches_the_page |
| Stop ends the running step and the queued ones for the rest of the turn | B5 | WebAgentEngineTests.StopEndsTheRunningStepAndTheQueuedOnesForTheRestOfTheTurn |
| Cookies, Authorization and password values never reach the model; network lines keep no headers or bodies | A2, A4 | CdpRedactorTests (all); CdpBrokerTests.Results_and_events_reach_callers_with_cookies_and_authorization_redacted; WebAgentEngineTests.NetworkLinesKeepNoHeadersOrBodiesAndTheNextCallGetsOnlyNewOnes |

## 5. Findings of this review (all fixed)

| # | Severity | Finding | Fix | Decision |
|---|---|---|---|---|
| F1 | High | `nth.complete` ran in plan mode, which still let Claude Code read files with no approval and reach the user's MCP servers and plugins. A hostile artifact prompt could read files outside its empty folder, or call a server | no-tools sessions (`--tools ""`, strict empty MCP config, no slash commands, no plugins; API sessions send no tool list); providers that cannot promise this have no artifact model | ED-NEW-web-w26-1 |
| F2 | Medium | WebRTC was still reachable from a sandboxed (opaque-origin) child frame, and from an iframe's first about:blank document through `window[0]`. With UDP blocked, a TURN-over-TCP relay was a way past `connect-src 'none'` | hardening runs in opaque frames inside nth sandboxes, and scrubs every reachable frame after each insertion, on load and on each mutation batch | ED-NEW-web-w26-2 |
| F3 | Medium | `ui/open-link` opened any address with no click and no card, so an App could carry what it holds to any site past the domain consent | a card naming the site, with the whole address, Open and Cancel; one card at a time | ED-NEW-web-w26-3 |
| F4 | Low-Medium | an App's declared localhost, private, link-local or `.local` hosts were plain domains under a plain Allow | they count as broad access, and the risk line names the local network | ED-NEW-web-w26-4 |
| F5 | Low | nth's frame check matched every granted host, not just the frame domains (only the CSP held the line) | `McpAppDomains.FrameHosts` | ED-NEW-web-w26-5 |
| F6 | Low | the untrusted wrapper escaped only the exact closing tags; other spellings, or a new opening tag, passed | every spelling of both tag names is defused | ED-NEW-web-w26-6 |
| F7 | Medium-Low | once allowed, an App could start a turn every time the chat went idle, keeping the model working and spending with nobody watching | at most three turns in a row until the user writes | ED-NEW-web-w26-7 |

## 6. Residual risks (accepted, or for a later unit)

1. **Claude's user-level memory and hooks.** A no-tools Claude call still loads the user's own `~/.claude` memory
   file and settings hooks. The call runs in an empty temp folder, so no project memory loads. nth already uses no CLI
   flag that turns these off. A hook that adds context could put user text into an artifact's answer.
2. **A script inside the frame-adding insertion.**
   - A script inside the same fragment that adds a frame (`createContextualFragment` holding an iframe and a script)
     runs before the post-insertion scrub, so it could take WebRTC from the new frame.
   - The indexed setters `select[i] = option` and `select.options[i] = option` (an iframe inside the option) add a
     frame no hook sees until the next microtask.
   - ED-NEW-web-sec-res-1: the engine layer now holds there too. The sandboxed views run in their own environment, and
     its arguments block non-proxied UDP in the spelling runtime 154 reads (`--webrtc-ip-handling-policy`; the older
     `--force-webrtc-ip-handling-policy` is not in its msedge.dll, so Visus's own UDP block does nothing on that runtime).
     Native check: SandboxWebRtcEngineNativeTests.
3. **TURN over TCP depends on the hardening script.** The browser argument blocks only non-proxied UDP. No WebView2
   switch turns WebRTC off (runtime 154 has none: no `disable-webrtc`, no peer-connection feature, no TCP switch). A dead
   proxy for the sandbox environment would stop it, but it would also stop the network an artifact or App was granted.
   A frame that keeps RTCPeerConnection can still reach a TURN server over TCP or TLS.
4. **Secrets in addresses.** A tab's address, query included, is shown to the model by `web_tabs`. A site that puts a
   token in its query string gives it to the model. The cross-origin card stops the model from carrying it to another
   site.
5. **Social engineering inside a sandbox.**
   - An artifact, widget or App can draw a convincing fake card or login form inside its own rectangle.
   - The trust strip above it names the true source, and real cards are native, use Mica and point to the strip (8.2).
   - `nth.send` only drafts; the user sends.
6. **The artifact tool client is not bound yet.** `nth.callTool` fails closed (the bridge has no tool client) until a
   later unit binds it. When it is bound, it must go through the same per-tool consent and approvals
   (ArtifactBridgeTests holds the bridge side).
7. **"Open in Visus" for artifacts is not bound.** If it is bound later, it must open in a sandboxed surface, never a
   `browse` tab: an artifact's page in the user's own profile would see their logins.
8. **Certificate errors in Visus tabs always cancel.** DESIGN 8.1 asks for a full-pane warning with "Continue anyway"
   for dev hosts only. No card is bound yet, so a self-signed dev server cannot be opened. This is fail safe, not a
   hole. Sandboxes and agent tabs never continue (tested).
9. **Port-specific MCP domains.** A declared `https://host:8443` is matched by host. If a later change keeps ports,
   the frame and resource checks must keep them too. Today the resource handler fails closed on a mismatch.
10. **Resource use.** A sandboxed page can burn CPU or memory in its own process. Suspension and discard of hidden
    views limit this, but nothing caps a shown page.
11. **Prompt injection through `web_read`.** It cannot be closed at the model, only contained:
    - page text is fenced and marked untrusted;
    - every action that matters goes through a card;
    - the hard denies (passwords, payments, sign-ups, CAPTCHAs) do not depend on the model at all.

## 7. The sandbox-escape suite

DESIGN 13.4 lists one hostile page per channel. Every channel that has a seam runs headless, through
FakeWebEngine and Node.js. The rest runs on the real engine, natively.

| Channel (13.4) | Headless | Native (SandboxEscapeNativeTests) |
|---|---|---|
| Top-level and frame navigation away, `location`, meta refresh | SandboxEscapeTests (navigation Theory: artifact, widget, App) | the page's own `location` changes to a web address and to `ms-settings:`, `search-ms:`, `ms-msdt:`; it stays on its site |
| `window.open`, `target=_blank` | SandboxEscapeTests (window Theory); SandboxNavigationTests | `window.open` returns null in the page and in each child frame |
| WebRTC: STUN, data channel | SandboxScriptBehaviourTests; SandboxEscapeScriptTests | the page, `window[0]`, `window.peer`, a frame's onload, the same-fragment script, a sandboxed srcdoc frame |
| DNS prefetch, preconnect, prefetch | SandboxScriptBehaviourTests | link hints are removed |
| `fetch`, `sendBeacon`, `EventSource`, WebSocket, image, font | SandboxEscapeTests (resource Theory) | each fails; `sendBeacon` returns false |
| Fullscreen, pointer lock | SandboxHeadersTests (Permissions-Policy); WebFeatureGuardTests | both are refused |
| External protocols | SandboxEscapeTests (window Theory); WebBaselinePolicyTests | three Windows protocols are cancelled |
| `postMessage` with a spoofed origin | SandboxEscapeTests.A_message_under_any_other_origin_is_never_answered; ArtifactBridgeTests; McpAppsHostTests | (the engine sets the origin; headless covers nth's check) |
| CSP bypass: `<base>`, `data:` scripts, `import()` from data URLs, `srcdoc` frames, service workers | CspBuilderTests; SandboxHeadersTests | `<base>` does not move `baseURI`; a `data:` script and `import()` do not run; srcdoc has no WebRTC; a service worker and a shared worker fail |
| Cross-artifact `localStorage`, IndexedDB, `BroadcastChannel`, `SharedWorker`, cookies | SandboxSitesTests; ArtifactBridgeTests (`nth.storage`) | Two_artifacts_from_two_chats_share_no_storage_cookie_database_or_channel |
| Two artifacts in different renderer processes | `--site-per-process` (WebProfilesTests) | by hand, section 8 |
| Local network, another sandbox's site | SandboxEscapeTests (resource Theory); McpAppHardeningTests | `fetch` to 127.0.0.1 and to `w-other.invalid` fail |

### How to run it

Headless runs use the lean filters, through the heavy slot, with native tests excluded:

```
Core:     (FullyQualifiedName~Marrow.Core.Tests.Web.|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native
Services: (FullyQualifiedName~Marrow.Services.Tests.Web.|FullyQualifiedName~ClaudeSession|FullyQualifiedName~OpenAICompatibleSession|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native
App:      (FullyQualifiedName~McpAppsSecurityTests|FullyQualifiedName~McpAppsAppTests|FullyQualifiedName~Tabula|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native
```

SandboxEscapeScriptTests needs `node.exe` on PATH and `MARROW_PORT_DATA_DIR` set.

The native tests are scheduled by the lead. Each one takes `native.lock`, runs alone with focus, and runs one Fact at
a time:

```
FullyQualifiedName~SandboxEscapeNativeTests.A_hostile_artifact_reaches_nothing_on_the_real_engine
FullyQualifiedName~SandboxEscapeNativeTests.A_hostile_inline_widget_reaches_nothing_on_the_real_engine
FullyQualifiedName~SandboxEscapeNativeTests.Two_artifacts_from_two_chats_share_no_storage_cookie_database_or_channel
```

## 8. Native checks (lead's window)

1. The three SandboxEscapeNativeTests Facts above, each alone, and each failure checked alone twice.
   - A failure on the "fragment" frame is residual risk 2, not a test bug.
   - A failure anywhere else is a hole: stop the release and fix it.
2. **Renderer processes.** Open two artifacts from two chats. In Task Manager (Details, with the command line column
   on), there should be two `msedgewebview2.exe --type=renderer` processes, one for each `a-…invalid` site. An App and a
   widget should each get their own as well.
3. **Fail-closed resource handler.** If the handler throws, WebView2View answers 403 (`WebView2View.cs`, near line
   602). There is no headless seam for a throw inside the real event. Check it once with a debugger break that throws.
4. **srcdoc and frame-src.** A sandboxed srcdoc frame reports no WebRTC (covered by the artifact Fact). An MCP App with
   a declared frame domain can frame only that host (F5, on the real engine).
5. **Trust strip and cards** over a hostile page: the native strip names the source, and a page-drawn lookalike card
   sits below it (DESIGN 8.2). This is checked on the final installer.
6. **The sandbox environment (ED-NEW-web-sec-res-1).** Each alone: SandboxWebRtcEngineNativeTests (both Facts: a frame
   whose WebRTC the hooks missed gathers no UDP candidate, and only `WebView2\Sandbox` is used) and
   WebView2EngineNativeTests.Sandboxed_views_get_an_environment_of_their_own_beside_Visus_on_the_real_runtime. In Task
   Manager, a second `msedgewebview2.exe` browser process (its `--user-data-dir` ending in `\Sandbox\EBWebView`) runs
   only while an artifact, widget or App has a live page (none after the last one closes or is discarded).
