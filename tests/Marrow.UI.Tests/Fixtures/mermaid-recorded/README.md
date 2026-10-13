Recorded Mermaid drawings (web surfaces W16c, ED-NEW-web-w16c-1, ED-NEW-web-review-25, ED-NEW-web-review-30).

`NthSvgRecordedMermaidTests` is a managed check: it requires all 26 SVG recordings and checks every drawing.
The recordings are committed and the check only reads the files (it starts no Edge), so the ordinary
`FullyQualifiedName!~Native` sweeps run it; `WebReviewRecordedDiagramTests` pins that it stays managed and that all 26 are present.
An empty folder must never count as a passing render check.

`MermaidRunnerNativeTests` in `tests/Marrow.Services.Tests/Web/` re-records the 13 diagram families in light
and dark. The lead runs it in the native pass, under the official heavy and native slots and test wrappers.
This starts Edge and is not part of a managed worker run. Commit the re-recorded files; the managed check then reads them.
See the web feature handoff for the exact native prerequisites and scheduled checks.
