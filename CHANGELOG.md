# Changelog

Marrow Windows release notes. Each packaged version needs its own `## [version]` entry and at least one populated section using the headings below. Version numbers come from `src/Marrow.App/AssemblyInfo.cs`. These are repository notes for the Windows port; they do not record a published release.

## [1.0.0] - 2026-10-08

### New features

- See updates for Marrow and supported tools in Settings and the sidebar.
- Read the offered Marrow version's What's new before updating, using the New features, Bug fixes and Refinements cards.

### Bug fixes

- Refuse release packaging when notes are missing, empty, for a different version or have no populated recognised section.

### Refinements

- Keep the installed version's release notes readable in About after restarting and checking for updates.
- Derive release notes from the exact version's changelog entry by default.
