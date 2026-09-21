# AGENTS.md

You are working in the Marrow Windows port: an exact-parity port of the macOS app Droppy Code to
Windows 11 (C#, .NET 10, Avalonia 12.1.2).

**If you are the orchestrator (the lead, typically Codex):** read `HANDOFF.md` first. It holds the
setup, the per-packet loop, the review checklist and the order of work. You assign packets, review,
and are the only one who commits.

**If you are a worker (a head):** read `WORKER_BRIEF.md` and do only the packet you were given from
`porting/tasks/`. Never commit.

Authority, in order: `porting/PORT_CONTRACT.md`, then `porting/contract_amendments.md`, then the
pinned source at `E:\Droppy\reference\droppy-code` (read-only), then these files.

The single verification entrypoint is `node porting/tools/verify.mjs`. A task is not done because
you think it is; it is done when `node porting/tools/mark.mjs <id> DONE` accepts it.
