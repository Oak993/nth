namespace Marrow.Services.Tests.Web;

/// <summary>W16c: one diagram of every Mermaid family, with labels the drawing must carry as text (read by the native runner test).</summary>
internal static class MermaidSamples
{
    public static readonly (string Name, string Source, string[] Labels)[] All =
    [
        ("flowchart", "graph LR\n  A[Client] --> B{Gateway}\n  B -->|ok| C[(Database)]\n  B -->|retry| D([Queue])\n  subgraph Backend\n    C\n    D\n  end", ["Client", "Gateway", "Database", "Queue", "Backend"]),
        ("flowchart-styled", "flowchart TD\n  A[Start] --> B[Stop]\n  style A fill:#f9f,stroke:#333,stroke-width:3px\n  classDef warm fill:#fd8,stroke:#b60\n  class B warm", ["Start", "Stop"]),
        ("sequence", "sequenceDiagram\n  participant U as User\n  participant S as Server\n  U->>S: Request\n  S-->>U: Reply\n  Note over U,S: A note", ["User", "Server", "Request", "Reply", "A note"]),
        ("class", "classDiagram\n  class Animal {\n    +String name\n    +eat()\n  }\n  Animal <|-- Dog", ["Animal", "Dog", "name"]),
        ("state", "stateDiagram-v2\n  [*] --> Idle\n  Idle --> Busy: start\n  Busy --> Idle: done", ["Idle", "Busy", "start", "done"]),
        ("er", "erDiagram\n  CUSTOMER ||--o{ ORDER : places\n  ORDER ||--|{ LINE : contains", ["CUSTOMER", "ORDER", "places"]),
        ("gantt", "gantt\n  title Plan\n  dateFormat YYYY-MM-DD\n  section Build\n  Design :a1, 2026-01-01, 10d\n  Code :after a1, 15d", ["Plan", "Design", "Code"]),
        ("pie", "pie title Pets\n  \"Dogs\" : 386\n  \"Cats\" : 85", ["Pets", "Dogs", "Cats"]),
        ("journey", "journey\n  title My day\n  section Work\n    Write: 5: Me\n    Test: 3: Me", ["My day", "Write", "Test"]),
        ("mindmap", "mindmap\n  root((nth))\n    Maps\n    Diagrams", ["nth", "Maps", "Diagrams"]),
        ("timeline", "timeline\n  title History\n  2024 : First\n  2026 : Second", ["History", "First", "Second"]),
        ("git", "gitGraph\n  commit\n  branch dev\n  checkout dev\n  commit\n  checkout main\n  merge dev", []),
        ("quadrant", "quadrantChart\n  title Reach\n  x-axis Low --> High\n  y-axis Slow --> Fast\n  A: [0.3, 0.6]", ["Reach", "Low", "High"]),
    ];
}
