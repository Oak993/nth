# Live answers: how a model's view reaches the thread

This page says how an answer written in `nth-ui` (or as an HTML widget) becomes a row in the thread, whichever way the model made it and
whichever provider it runs on, and where each piece lives. The language itself is in `nth-ui.md`; how models are taught it is in
`nth-ui-teaching.md`. The words and the tool descriptions are ours.

## Four ways to make one

| Way | For | What the model does |
| --- | --- | --- |
| `ui_show { id?, ui }` | Claude, Codex, Copilot and ACP agents through the `nth-ui` MCP server; API providers as a native function | Passes the view's lines as `ui`. An `id` names it; showing the same id again replaces the view. |
| `ui_update { id, patch }` | the same | Changes a view it showed: `patch` is an object with `state` (state names to new values), `nodes` (a node id to the lines that replace it, empty to remove it) and `append` (a node id to lines added at its end). A node has an id when its line says `id=name`. |
| `html_widget { html, height?, title?, id? }` | the same, except a small local model | One self-contained HTML page for an inline sandboxed widget (a game, a simulation). |
| a fenced block | any model, and the fallback for one that cannot call tools | `` ```nth-ui `` (optionally `id=` and `title=` and the word `lite`) or `` ```nth-html `` (optionally `height=` and `title=`), closed by a bare fence. The block comes last in its message. |

A model that writes a fenced block cannot call `ui_update`; it writes the whole block again with the same `id=`, which replaces the view.

## The pieces

- **`LiveAnswerTools`** (Marrow.Services/Web): the three tools' names, descriptions, input schemas, the checks and what the model reads back. A
  view in which no line could be read is an error the model can fix; a view with a few unreadable lines is shown and the lines are named.
- **`LiveAnswerHost`** (reached as `ArtifactHost.Live`): the process's record of the views each chat has shown, by id. The tools write to it; the
  chat's runtime hears of every change (`Subscribe`) on the tool's own thread and applies it on the main actor. After a restart the record is
  empty and `Find` asks the chat's saved timeline through a seam the app binds (`ArtifactRuntimeStartup`).
- **`UiMcpServer`** and **`ArtifactNativeTools`**: the tools on the `nth-ui` server (CLI providers) and as function tools (API providers). A small
  local model is offered `ui_show` and `ui_update` only, and its views are the lite subset.
- **`LiveAnswerFlow`** (Marrow.Services/Web): between a session and its timeline. It reads the answer text for fenced blocks, turns a provider's
  tool row for a live-answer call into the view's row (same row id), reads the tools' changes, and passes every other event through untouched.
  An answer with no block comes out byte for byte as it went in.
- **`ToolPayload.LiveView`** (Marrow.Core/Models): where the row holds the view, so it is saved with the thread and comes back after a restart.
- **`LiveAnswerRow`** (Marrow.App/Views/Chat): draws the row. An nth-ui view is drawn natively by `NthUiView`; an HTML widget is a card that
  opens it in Tabula until the inline widget host (W19) takes its place through `LiveAnswerHtml.Host`.
- **`ThreadRuntime`** (partial, `ThreadRuntimeLiveAnswers.cs`): the one hook into the session's events, the subscription to the host, the lookup
  of a row by view id, the reader's states, and what a view's actions do.

## How a call becomes a row

1. The provider reports the call's row (`ToolStarted`, titled `nth-ui · ui_show`). The flow rewrites it into a live-answer row with an empty
   view. A provider that streams the call's arguments (Claude's `input_json_delta`; the native loop's tool-call chunks) reports each piece, and
   `PartialJson` reads the `ui` string as far as it has arrived, so the view builds while the model writes.
2. The tool runs (on the `nth-ui` server's relay thread, or in the native loop), checks the view and records it in the host.
3. The host's change reaches the runtime. It binds to the oldest row still waiting for a view of that kind; a replacement of a view already shown
   marks the earlier row superseded; an update changes the view's row in place and lays the patch's states over what the reader set. If the tool
   was quicker than the provider's report, the change makes a row of its own and the provider's row is dropped as the same view.
4. A `ui_update` call's own row is hidden.

A provider whose row titles nth cannot recognise still gets its views: the host's change makes the row.

## What the row does

- It streams: lines are placed as they arrive and nothing already drawn moves (`NthUiView`, W16a). It follows the working line in a turn that is
  running and stays in the folded answer, after the words that introduce it, in a turn that is finished.
- A view shown again, or rewritten by `ui_update`, replaces the old one with a crossfade and keeps the reader's choices; an update that only sets
  states keeps the same view.
- The reader's choices are saved on the row (half a second after the last change) and the view reopens as they left it.
- Actions: `copy` copies, `open` goes to the reader's link setting (`LiveAnswerEffects.OpenLink`), `send` posts a message of the reader's,
  `context` rides silently with their next message, `export` saves a csv or a png, and `call` asks the model to make the tool call itself, so the
  reader's normal tool approvals and guards apply.

## Limits

A view is at most 262,144 characters; an HTML page 524,288; a saved state 16,000. A chat's record keeps the last 64 views in memory.
