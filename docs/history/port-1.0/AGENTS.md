# AGENTS.md

You are working in the Marrow Windows port: an exact-parity port of the macOS app Droppy Code to
Windows 11 (C#, .NET 10, Avalonia 12.1.2).

Read `CLAUDE.md` first: it holds the pinned baseline, the rules and the current gate. Implementing agents follow
`porting/IMPLEMENTING.md` and the work packet they were given. Workers never commit; only the lead commits.

Authority, in order: `porting/PORT_CONTRACT.md`, then `porting/contract_amendments.md`, then the
pinned source at `E:\Droppy\reference\droppy-code-v1.7.0` (read-only), then these files.

The single verification entrypoint is `node porting/tools/verify.mjs`. A task is not done because
you think it is; it is done when `node porting/tools/mark.mjs <id> DONE` accepts it.
