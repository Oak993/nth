# nth: instructions for AI coding agents

nth is a Windows desktop app (C#, .NET SDK 10.0.401, Avalonia 12.1.2 pinned, no XAML) that includes code from Droppy
Code. Version 1.0.0 shipped from branch `release/1.0.0` (tag `nth-1.0.0`); work for the next release happens on branch
`next-release`.

Read `docs/next-release/README.md` on branch `next-release` before you change anything. It is the authority for next-release work: branches,
build, smart testing, where the glass code lives, the speed rules, the UI rules and the pull request checklist.
`docs/next-release/OWNER-LIST.md` holds the owner's findings (each row is a specification) and
`docs/next-release/SPEED-PLAN.md` the performance plan.

## Rules that always apply

- Branch from `next-release`; push only your own branch; never force-push; never push to `release/1.0.0`,
  `next-release`, `claude/*` or `codex/*`. Open a pull request into `next-release`.
- Never weaken, skip or delete a test, and never edit an expectation to match the output. Run the tests that cover what
  you changed, with a filter, and report the Passed and Failed totals.
- Tests never call a real AI provider, a real command-line tool or the network; they use the fakes in `tests/` and
  `src/*/Testing`.
- Never commit a secret, a token, an account or any personal data.
- Write code like the code around it. Keep the `// :97` comments: they point at lines of the original Swift source.
  Decisions are recorded as OD-nnnn (owner, `porting/owner_decisions.md`) and ED-nnnn (engineering,
  `porting/engineering_decisions.md`).
- No new NuGet package and no Avalonia upgrade without the owner's approval. Keep line endings as they are
  (`.gitattributes` is `* -text`).
- Keep the license notices: `LICENSE`, `TRADEMARK.md`, `THIRD_PARTY_NOTICES.md`, `src/Marrow.App/Legal/` and the
  credit in Settings > About ("Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app").

## History

The 1.0 port's exact-parity instructions (the old `CLAUDE.md`, `AGENTS.md`, `HANDOFF.md` and `WORKER_BRIEF.md`) are in
`docs/history/port-1.0/`. The ledgers in `porting/` stay the decision record that the code comments cite.
