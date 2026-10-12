Recorded Mermaid drawings (web surfaces W16c, ED-NEW-web-w16c-1).

`NthSvgRecordedMermaidTests` reads every `*.svg` here. They are written by the native test `MermaidRunnerNativeTests` in
`tests/Marrow.Services.Tests/Web/` (the lead runs it at the native pass, alone and with focus: it starts Edge in headless mode
and draws one diagram of every family, in light and dark, through the hidden renderer's runner and the vendored Mermaid):

    dotnet test tests/Marrow.Services.Tests --filter "FullyQualifiedName~MermaidRunnerNativeTests"

Commit the files it writes. An empty folder is fine: the ordinary test then has nothing to read.
