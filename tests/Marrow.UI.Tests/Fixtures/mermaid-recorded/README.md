Recorded Mermaid drawings (web surfaces W16c, ED-NEW-web-w16c-1, ED-NEW-web-review-25).

`NthSvgRecordedMermaidNativeTests` requires all 26 SVG recordings and checks every drawing.
An empty folder is pending native evidence and must never count as a passing render check.

The lead records the 13 diagram families in light and dark with `MermaidRunnerNativeTests`
in `tests/Marrow.Services.Tests/Web/`, under the official heavy and native slots and test
wrappers. This starts Edge and is not part of a managed worker run. Commit the recorded
files, then run the UI Native checker through the same official verification workflow.
See the web feature handoff for the exact native prerequisites and scheduled checks.
