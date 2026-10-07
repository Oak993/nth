# MA-3b-E1 engine integration

E1 supplies `LinkEngine : ILinkPort`, `OpinionsEngine : IOpinionsPort`, the concrete `AccountConversationHeadHost`, persistent `ConversationTransferConsent`, and an Avalonia `ConversationTransferPrompt`. The job-0 `ThreadPortSeams` default remains unbound; job W owns live app/view integration. These are PORT-ONLY surfaces governed by OD-0115, OD-0116 and OD-0120, with safe choices recorded in ED-454 through ED-457.

## Required live boundaries

Construct the host on MainActor and invoke its engines there, as with the existing provider sessions. Supply all connected account identities, including each Local server/model account. `Enabled`, `HasRoom` and `MonthlyCapUsd` must come from the actual account/key state: reported limits, credit/out state, shared pool availability and the configured monthly USD cap. The usage bar alone is insufficient. A null `MonthlyCapUsd` means genuinely uncapped, not an unavailable reading.

The required `Revision(thread)` is a monotonic context/ownership epoch. Advance it for transcript edits/resume, source ownership, selected model/effort, Local endpoint/model, launch settings, pin or pill changes that invalidate the captured request. Call `NotifyAccountsChanged(thread)` on those changes and on account off/out/cap changes. Capacity changes need not advance the epoch when the recorded context is unchanged. Engines check both epoch and live account identity/availability during preparation, dispatch, streaming, completion and Combine; the concrete host independently captures ownership/epoch at launch.

Supply the full recorded context and a fresh configuration for each selected account. Supply the settings-backed consent model using the real modal prompt. A different-service transfer requires explicit acceptance unless its directed service pair was previously accepted with the unchecked-by-default **Don't ask again** checkbox selected. This preference does not enable Handshake or automatic key/account switching. `AskAgain(source, destination)` revokes just that direction. Dismissal, refusal and cancellation send nothing; corrupt preference bytes are preserved and fail closed.

For API heads the host's `bindKeys` callback must call `app.ApiKeys.BindPrivate(session, accountId)`, which binds the shared pool to that selected account without automatic movement. Missing routing fails before send; the extra wrapper refuses fallback to another account and forwards reported spend/disposal. For a capped token-only API, also supply a pure, authoritative `KeySpendEstimator` verified for its selected model. OpenRouter supplies charged USD in its usage report; other capped APIs without verified pricing fail before sending. E1 installs no guessed prices. Pool availability is checked again at real API dispatch, so a stale `HasRoom` snapshot cannot bypass a spent cap.

The timeline callback must atomically check the cancellation/ownership/context boundary at the actual append, append the text and byline together, and deduplicate by round id. Do not defer an unchecked append across an await. The engine permits only one append attempt, including when an exception could have followed a committed append. Posting must not change the account, pin or pill step.

## Link and opinion notifications

Bind `ILinkPort` to the engine for explicit tap link/unlink. `Linked(thread)` exposes only currently eligible accounts; retained tap intent is hidden while active/out/off and is removed by an explicit unlink. This never initiates a transfer itself.

Forward each real completed primary answer to `CheckCompletedAnswerAsync` with its stable answer id, author id, user message and final answer. This checks the author's final answer even if it just exhausted its cap; no model turn is sent to the author. A disabled, changed or stale author is still refused. The stricter `CheckAnswerAsync` additionally requires source room. `Checks(thread)` supplies independent checker text/completion/error snapshots. Repeated answer ids cannot start duplicate work.

For **Ask others**, call `AskAsync` once with this message and the selected other accounts. Map `@all` to `EveryoneWithRoom(thread)`, including every eligible Local identity. Do not silently drop unsupported providers. The active Answer and other Opinions stream in parallel; selection is stored only in that round. Present `OpinionAnswer.Error` as a failed/cancelled head rather than a successful empty answer. Await completion (`WaitAsync` on the concrete engine) before offering Combine. Failed, cancelled, empty, stale or unavailable rounds cannot combine. A successful Combine uses the original source and posts `<name> · combined <N> answers` once.

Dispose engines when their owner ends; cancel rounds/checks with the thread's lifetime token. Cancellation and invalidation release pending confirmation/context waits and suppress late text. Engine result history and cancellation-source retention live with the owning engine; shortening retained history/resources is a later cleanup opportunity, not permission to reuse an expired head.

## Verified head capabilities and remaining work

Heads are private, fresh text-only sessions and never become the working session. Existing restricted Claude startup/runtime verification and API/Local `IsFinalReport` enforcement are reused. Hydra/skill launch state and resume history are removed; tool, approval, question and agent events fail closed, including startup/inter-turn events. Safe supported services are Claude, DeepSeek, Meta, Zai, OpenRouter and Local. All other CLI services fail explicitly until a verified text-only model turn exists; `StandbyKind` Primed/Injected/Deferred alone is not that proof.

Job W still must implement the live boundaries above, bind the views and forward actual completed-answer events. Authoritative pricing for capped token-only models and safe text-only turns for the other CLIs remain explicit integration questions. Windows-native tests, real Windows UI/CLI behavior and packaging were not verified by this Linux E1 task. Exact build/test results and the release verdict are recorded in the E1 verification report.
