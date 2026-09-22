# Contract amendments

`porting/PORT_CONTRACT.md` is preserved verbatim and is never edited. Amendments come only from an
explicit later instruction by the commissioning owner and are recorded here, dated, in the owner's
own words, with the effect they have on the work.

---

## AM-0001, 2026-09-20 - Gate B authorized; three standing constraints

**Owner instruction, verbatim:**

> Go ahead with Gate B.
>
> Keep UNK-0010 explicitly unresolved and isolated pending my decision. Do not silently change the
> MCP deletion behavior, and do not treat either alternative as approved. This issue must not block
> unrelated specification or implementation work.
>
> The "99 upstream defects to reproduce" record is not blanket approval for destructive or
> security-sensitive behavior. Preserve ordinary source-defined quirks for parity, while continuing
> to escalate consequential exceptions individually under the original contract.
>
> Once Gate B genuinely passes, proceed directly to Windows architecture selection and
> implementation without another routine approval checkpoint. Keep progress reports clear about
> specification work versus actual Windows functionality implemented and tested.

### Effect A: UNK-0010 is isolated, not parked

UNK-0010 (one-click custom-MCP delete that also destroys Keychain secrets, ignoring
`confirmBeforeDeleting`) stays OPEN. Neither reproduction nor repair is approved. The isolation is
made mechanical rather than left as a note:

- The affected behavior is carried in `porting/blocked_requirements.json` as a machine-readable
  quarantine list.
- A Gate C check in `porting/tools/verify.mjs` **fails** if the Windows implementation contains a
  delete path for a custom MCP server while the quarantine entry is unresolved. The gate cannot be
  satisfied by implementing either alternative.
- Every other MCP requirement proceeds normally. The quarantine is scoped to the delete affordance
  and its Keychain teardown, nothing wider.

### Effect B: the defect register is not a blanket approval

The 99 entries in `porting/upstream_defects.md` are re-triaged into:

- **ORDINARY_QUIRK -> REPRODUCE.** The default. Contract section 8 forbids silently repairing
  unrelated upstream behavior during a parity port, so ordinary oddities are the product and are
  copied faithfully.
- **CONSEQUENTIAL -> ESCALATE.** Neither reproduced nor fixed. Isolated individually, added to
  `porting/blocked_requirements.json`, and surfaced to the owner one at a time with the concrete
  harm stated.

Consequence criteria, applied to the realistic worst outcome of the actual code path:

| Code | Consequence |
| --- | --- |
| C1 | destroys or permanently loses user data, work or project state |
| C2 | deletes, exposes, leaks or transmits credentials, tokens or other secrets |
| C3 | weakens authentication, transport verification, permission enforcement, process or filesystem confinement, or lets input reach a shell or process launch |
| C4 | causes silent data loss or corruption the user cannot detect or undo |
| C5 | overrides an explicit user safety preference that the rest of the app honors |
| C6 | sends data to a network destination the user did not choose |

Anything whose worst realistic outcome is confusing, ugly, stale, slow, redundant or suboptimal,
with no lasting harm, is an ordinary quirk and is reproduced. Inflating the escalation list is a
failure mode in its own right; escalation is per-defect and must carry the concrete harm.

### Effect C: no routine approval checkpoint after Gate B

Once Gate B genuinely passes its own mechanical checks, the work proceeds directly into the Windows
architecture decision record and then implementation, without pausing for approval. This does not
weaken anything else: gates still fail honestly, consequential defects are still escalated
individually, and UNK-0002 (branding), UNK-0004 (Liquid Glass deviation), UNK-0007 (icons and type)
and UNK-0010 remain owner decisions that block only what they actually block.

### Effect D: reports separate specification from working software

Progress reports must state plainly which part is specification and which part is Windows
functionality actually implemented and tested, and must never let the former imply the latter.
A requirement that is SPECIFIED is not IMPLEMENTED; IMPLEMENTED is not VERIFIED.

---

## AM-0002, 2026-09-22 - The pinned baseline moves to live 1.6.2

**Owner instruction:** "I want a complete 100% port" of https://gitlab.com/droppyformac1/droppy-code,
then, asked which version, the selected option "Switch to live 1.6.2 (Recommended)" (OD-0006).

### Effect

- Contract section 4's frozen baseline is now `1fbf81e61e28a079508a074c01ee1830a9cc23dc` (B2). The
  contract text is unchanged; "the pinned source" in every section means B2 from this date.
- Gate A and Gate B are re-earned for the delta between B1 and B2, by the same rules as the original
  reading: every changed or added file fully read with an evidence-bearing report accepted by
  `ledger_apply.mjs`; every requirement and UI record citing a changed file re-classified against
  B2; every new behaviour specified before it is implemented.
- Work already marked DONE is re-verified against B2 before it counts.
- The verification tools read the baseline from `porting/baseline.json` instead of constants, so
  this change is one auditable record (`porting/tools/baseline.mjs`, `porting/tools/rebaseline.py`).
