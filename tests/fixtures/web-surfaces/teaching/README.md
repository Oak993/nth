Recorded answers for the W17 teaching tests (DESIGN.md 14 row W17, ED-NEW-web-w17-1).

These are Marrow-authored synthetic transcripts, never captured from a live provider: every answer is written in our own words
in `teaching_data.py` and turned into each provider's wire format by `generate.py` (`python generate.py` rewrites the five files).
The wire shapes follow the code that reads them: `ClaudeSession.Handle` (stream-json), `CodexSession` item notifications,
`CopilotSession` session events, and OpenAI-style chat-completion chunks for `OpenAICompatibleSession` and `LocalSession`.

One recording per line: `{"id", "prompt", "expect", "events"}`. `expect` is `plain` (text only), `view` (one sentence and exactly one live
view) or `artifact` (one artifact).

| File | Provider | How the answer reaches nth |
| --- | --- | --- |
| claude.jsonl | Claude | stream-json; `ui_show` and `artifact_create` as MCP tool calls on the nth-ui server |
| codex.jsonl | Codex | app-server notifications; the same two calls as `mcpToolCall` items |
| copilot.jsonl | Copilot | session events; fenced `nth-ui` and `nth-artifact` blocks in the answer text |
| deepseek.jsonl | DeepSeek (an API provider) | chat-completion chunks; the two calls as native function calls |
| local.jsonl | a Local model that cannot call tools | chat-completion chunks; lite fenced blocks only |

The tests (`TeachingRecordingTests`) replay each file through the real session code and check every block. To use a real capture instead,
replace a line with the provider's own events in the same format; nothing else changes. Real captures are for the native pass.
