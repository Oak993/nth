# Teaching models nth-ui and artifacts

This page says how nth teaches a model to answer with a live view (`nth-ui`) or an artifact, and where each piece lives. The words
are ours and the examples are ours; nothing is copied from another product. The text itself is in
`src/Marrow.Core/NthUi/NthUiTeaching.cs` and `NthUiExamples.cs`; the tests parse every example, so a model is never taught a line the
parser would degrade.

## What a model is told

1. **When.** Plain text is the default. A live view when the reader will change numbers or options, compare, see a chart or a map, tick
   things off or step through a plan: one sentence that answers first, then at most one view, and nothing after it that repeats it. An
   artifact when the reader will keep, run, edit or reuse what is made. An HTML widget only when no component fits; `ask_user` only when
   it cannot go on without the reader's choice. Settings > Tabula > Visual answers (Fewer, Balanced, More) changes one line of this.
2. **How.** With the tool: `ui_show` (its `ui` argument holds the view). Without it, or when the model cannot call tools: a fenced block
   that starts with three backticks and `nth-ui`. Artifacts: `artifact_create` and `artifact_update` (with `edits` for small changes), or
   a fenced `nth-artifact` block.
3. **The language.** A compact reference (every component in one line of its own), the expression functions, the actions, and a short
   list of mistakes to avoid.
4. **Six worked examples**, each with the reader's question, the sentence written first and the view, and three plain questions that
   need no view.

The text follows what the session is offered (`NthUiTeachingOptions`): the tool names decide between calling a tool and writing a fence,
and `html_widget` and `ask_user` are mentioned only when they exist. A small local model gets the lite subset
(`card`, `text`, `table`, `chart`, `stepper`, `slider`, `chips`, `checklist`, `followups`, `state`, `let`), three examples and a shorter
block.

## Where it arrives, by provider

| Provider | Channel | Notes |
| --- | --- | --- |
| Claude, Codex, Copilot, ACP agents | `initialize.instructions` of the `nth-ui` MCP server, and the tools on that server | The first 1.4 KB are a core that stands alone (some hosts cut a server's instructions at 2 KB); the compact reference and the six examples follow for hosts that keep the rest. `UiMcpServer.Instructions` |
| DeepSeek, Meta, Z.ai, OpenRouter (API providers) | a block in the system prompt, after the skills catalogue and before the project's own guidelines, and native function tools | `NativeSkillTools.UiTeachingBlock`, injected in `OpenAICompatibleSession.SystemPrompt`. A session of no chat, or a process with artifacts off, gets no block: its prompt is byte for byte what it was |
| Local (Ollama, LM Studio, llama.cpp, others) | the same block in the lite profile, in fenced blocks when the model cannot call tools | `LocalSession` overrides `TeachingProfile` and `TeachingToolsUsable` |

Every model can read more through **`ui_reference`**, a read-only tool on the `nth-ui` server and a native function. It is nth's skill
for this language: `language` (lines, values, expressions, `fmt`, `state` and `let`, streaming), `components`, `actions`, `examples` (twelve
worked views), `lite` and `errors` (what happens to a wrong line, and the limits). The first four topics are `docs/nth-ui.md` by section,
compiled into `Marrow.Core`; so the reference a model reads is the page this repository tests.

`NthUiTeaching.ShowToolDescription` is the text for `ui_show`'s own description (W24), so a provider that does not show a server's
instructions still sees the essentials with the tool.

## Local models and the grammar

`NthUiGrammar.Gbnf` is a GBNF grammar of the lite subset, written from `NthUiSchema` so it cannot drift from the language: the kinds,
their values, attributes (in the order the reference shows, each once), flags, the tail after `=`, the indent of every level, and the
expression language with its argument counts. A llama.cpp server is recognised by `GET /props` (`LocalServerKind.LlamaCpp`). For such a
server, a request that carries no tools gets the grammar as a lazy grammar triggered by the opening fence (`grammar`, `grammar_lazy`,
`grammar_triggers`; `LocalViewGrammar`), so the reply is free text until the model writes ` ```nth-ui ` and a grammar-checked view from
there to the closing fence. llama.cpp refuses a custom grammar next to tools, so a model that calls tools is not sent one.

Ollama and LM Studio take structured output only as a JSON schema, which would wrap the whole reply in JSON and break the line-by-line
streaming a view depends on; those servers, and servers nth cannot identify, are taught by the prompt alone.

## Joining the other units

- **W24 (live-answer tools), landed.** `ui_show`, `ui_update` and `html_widget` are in `UiMcpServer.ToolNames` and `ArtifactNativeTools.Names`, so the
  instructions and the block now say "call ui_show" with no change to the text; `ShowToolDescription` is `ui_show`'s own description. The fenced
  `nth-ui` block stays as the fallback, and `nth-html` for a widget. See `nth-ui-live-answers.md`.
- **W25 (`ask_user`).** Add `ask_user` to the same two lists; the rules then mention it.
- **Settings (W10).** Bind the Visual answers setting with `UiTeaching.Visual.SetProcessDefault(() => level)`; it is read whenever a
  session starts.

## Tests and fixtures

- `NthUiTeachingTests`, `NthUiRecordedLocalViewTests`, `NthUiGrammarTests` (Core): every example parses clean and computes; the text for
  every combination of options; the core under 2,000 bytes; every kind and function named; the lite text names only the lite subset; no
  other product's name; the grammar reads as llama.cpp reads it, and a thousand random derivations of it are clean lite views.
- `UiTeachingWiringTests`, `LocalViewGrammarTests` (Services): how the text reaches each provider, `ui_reference` over MCP and as a
  function, the Visual answers seam, the llama.cpp request fields.
- `TeachingRecordingTests` (Services): ten prompts for each of five providers (Claude, Codex, Copilot, DeepSeek, a Local model), replayed
  through the real session code; two plain questions, six views and two artifacts each, every block valid. The recordings are in
  `tests/fixtures/web-surfaces/teaching` (see its README): Marrow-authored synthetic transcripts in each provider's wire format. A real
  capture can replace any line in the same format.
