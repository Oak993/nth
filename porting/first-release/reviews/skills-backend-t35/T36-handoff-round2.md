# T36 handoff: RR-18 GitHub sub-folders are available

Branch `codex/skills-backend` now accepts the locked Add sheet's `owner/repository/sub-folder/path` in the existing five-field `CustomAddRequest.Location`. Public repository URLs with the same path also work. Your adapter signature needs no change. Merge this branch into `codex/skills-ui`; T35 pushes only the backend branch.

Sub-folder backslashes normalize to `/`; dot segments, absolute/empty/encoded paths fail before any HTTP call. Only that folder's commit-pinned contents listings and raw files are fetched; no whole-repository archive is fetched for a subtree. The original location, normalized `Provenance.SubPath`, full commit, pinned contents URL, check time and file hashes persist across reopen. Tree fingerprint semantics are documented in [custom-source-api.md](custom-source-api.md).

Selected Add calls publish private copies concurrently, with serialized name/command reservations and registry commits. The RR-44 ordered-start fix is already on this branch (`ef5ac4d2`). Commands and own skill/plugin generations are bound (`a0437bbf`, `82f61af1`).

57 service checks pass: 53 custom cases, one ordered-start case, three existing traceability checks. Subtree acceptance/validation/transport tests were written first and use fake HTTP plus in-memory source/registry files; no real source download or install runs. Red/green logs are in the neighboring evidence folder.
