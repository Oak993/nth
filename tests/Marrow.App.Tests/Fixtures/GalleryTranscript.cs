using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Window.Native;

// The conversation the gallery's thread scenes show: what a person would plausibly have in a thread, written as the timeline
// the app stores (Core/Models/Timeline.swift: items, turns, usage). Every row kind the thread view draws appears once: a user
// message, a reasoning block, tool calls of each kind (read, search, command, edit), an assistant reply in Markdown (headings,
// lists, a code block, a table, links), Hydra's report, a checklist, the turn-end changes row, and a second short turn.
internal static class GalleryTranscript
{
    private const string ThemeToggle = """
        import { Switch } from '../components/Switch'
        import { useLocalStorage } from '../hooks/useLocalStorage'

        export function ThemeToggle() {
          const [theme, setTheme] = useLocalStorage<'light' | 'dark'>('theme', 'light')
          return (
            <Switch
              label="Dark mode"
              checked={theme === 'dark'}
              onChange={(on) => setTheme(on ? 'dark' : 'light')}
            />
          )
        }
        """;

    private const string ReplyMarkdown = """
        ## Dark mode toggle

        I added a **Theme** switch to the settings page. It reuses the existing `Switch` row, so it picks up the same spacing and focus ring as the other options.

        ### What changed

        - `ThemeToggle.tsx` renders the row and reads the saved value through `useLocalStorage`.
        - `settingsSchema.ts` lists the new `theme` entry between *Notifications* and *Language*.
        - `settings.test.tsx` covers the toggle, including a reload that keeps the choice.

        ```tsx
        export function ThemeToggle() {
          const [theme, setTheme] = useLocalStorage<'light' | 'dark'>('theme', 'light')
          return (
            <Switch
              label="Dark mode"
              checked={theme === 'dark'}
              onChange={(on) => setTheme(on ? 'dark' : 'light')}
            />
          )
        }
        ```

        ### Test results

        | Suite | Tests | Result |
        | --- | ---: | --- |
        | settings | 14 | passed |
        | theme | 6 | passed |
        | accessibility | 3 | passed |

        1. Run `pnpm test --run settings` to repeat the check.
        2. Open the page and flip the switch; the choice survives a reload.

        The reasoning behind the token names is in the [theming guide](https://example.com/docs/theming), and following the system setting by default is tracked in [issue 482](https://example.com/issues/482).
        """;

    private const string SecondReply = """
        Done. The switch now starts from the system setting (`prefers-color-scheme`) the first time and writes the explicit choice after that. A stored choice always wins over the system.
        """;

    private const string HydraReport = """
        ## Hank: Audit the contrast ratios
        Checked the dark and light tokens against WCAG AA. All text passes; one border token (`--line-faint`) is 2.1:1 in light mode and is only decorative.
        ## Walter: Add Storybook stories
        Added Dark and Light stories for `ThemeToggle`. Storybook builds clean.
        """;

    /// <summary>The document of the main thread (turn one, then a short second turn), as it lies on disk.</summary>
    internal static ThreadDocument Document(Guid threadId, DateTimeOffset now)
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var start = now.AddMinutes(-42);
        var userOne = new TimelineItem(first, new TimelineItem.Content.User(new UserMessage
        {
            Text = "Add a dark-mode toggle to the settings page, keep the choice in localStorage and cover it with a test. Follow the existing Switch component.",
        }), date: start);
        var items = new List<TimelineItem>
        {
            userOne,
            new(first, new TimelineItem.Content.Reasoning(new ReasoningBlock
            {
                Text = "The settings page already renders a list of Switch rows from `settingsSchema.ts`, so the toggle belongs there as one more entry and not as a one-off component. " +
                       "Persistence goes through `useLocalStorage`, which the notifications row already uses. I should read the page and the schema first, then add the row, then extend the Vitest file.",
            }), date: start.AddSeconds(4)),
            new(first, new TimelineItem.Content.Tool(Tool(ToolCall.Kind.Read, "Read src/settings/SettingsPage.tsx", null, "", start.AddSeconds(9), 1)), date: start.AddSeconds(9)),
            new(first, new TimelineItem.Content.Tool(Tool(ToolCall.Kind.Search, "Search \"useLocalStorage\"", "src/", "src/settings/NotificationsRow.tsx:12\nsrc/hooks/useLocalStorage.ts:3\nsrc/settings/LanguageRow.tsx:9", start.AddSeconds(14), 1)), date: start.AddSeconds(14)),
            new(first, new TimelineItem.Content.Tool(Tool(ToolCall.Kind.Edit, "Edit src/settings/ThemeToggle.tsx", "src/settings/ThemeToggle.tsx", "", start.AddSeconds(31), 3) with
            {
                Edits = new ValueList<FileEdit>([
                    new FileEdit("src/settings/ThemeToggle.tsx", "", ThemeToggle),
                    new FileEdit("src/settings/settingsSchema.ts",
                        "  { id: 'notifications', label: 'Notifications' },\n  { id: 'language', label: 'Language' },\n",
                        "  { id: 'notifications', label: 'Notifications' },\n  { id: 'theme', label: 'Dark mode' },\n  { id: 'language', label: 'Language' },\n"),
                ]),
            }), date: start.AddSeconds(31)),
            new(first, new TimelineItem.Content.Tool(Tool(ToolCall.Kind.Command, "Run pnpm test --run settings", "pnpm test --run settings",
                " RUN  v1.6.0 /work/droppy-web\n\n ✓ src/settings/settings.test.tsx (14)\n ✓ src/settings/theme.test.tsx (6)\n ✓ src/settings/a11y.test.tsx (3)\n\n Test Files  3 passed (3)\n      Tests  23 passed (23)\n   Duration  2.84s", start.AddSeconds(58), 4) with { ExitCode = 0 }), date: start.AddSeconds(58)),
            new(first, new TimelineItem.Content.Assistant(new AssistantMessage { Text = ReplyMarkdown }), date: start.AddSeconds(66)),
            new(first, new TimelineItem.Content.User(new UserMessage { Text = HydraReport, HydraHeads = new ValueList<long>([0, 1]) }), date: start.AddSeconds(80)),
            new(first, new TimelineItem.Content.Todos(new ValueList<TodoStep>([
                new TodoStep { Text = "Read the settings page and schema", TodoStepStatus = TodoStep.Status.Done },
                new TodoStep { Text = "Add ThemeToggle and the schema entry", TodoStepStatus = TodoStep.Status.Done },
                new TodoStep { Text = "Extend the Vitest file", TodoStepStatus = TodoStep.Status.Done },
                new TodoStep { Text = "Follow the system setting by default", TodoStepStatus = TodoStep.Status.Pending },
            ])), date: start.AddSeconds(82)),
            new(first, new TimelineItem.Content.TurnEnd(new TurnSummary
            {
                TurnID = first, Status = TurnStatus.Completed, Duration = 94, FilesChanged = 3, Additions = 58, Deletions = 7,
                Changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([
                    new FileChangeSummary.File("src/settings/ThemeToggle.tsx", 17, 0, false),
                    new FileChangeSummary.File("src/settings/settingsSchema.ts", 1, 0, false),
                    new FileChangeSummary.File("src/settings/settings.test.tsx", 40, 7, false),
                ]), 58, 7),
            }), date: start.AddSeconds(94)),
        };
        var userTwo = new TimelineItem(second, new TimelineItem.Content.User(new UserMessage { Text = "Looks good. Can you also make it follow the system setting by default?" }), date: now.AddMinutes(-6));
        items.Add(userTwo);
        items.Add(new TimelineItem(second, new TimelineItem.Content.Assistant(new AssistantMessage { Text = SecondReply }), date: now.AddMinutes(-5)));
        items.Add(new TimelineItem(second, new TimelineItem.Content.TurnEnd(new TurnSummary
        {
            TurnID = second, Status = TurnStatus.Completed, Duration = 31, FilesChanged = 1, Additions = 9, Deletions = 2,
            Changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("src/settings/ThemeToggle.tsx", 9, 2, false)]), 9, 2),
        }), date: now.AddMinutes(-5)));
        return new ThreadDocument(threadId)
        {
            Items = new ValueList<TimelineItem>(items),
            Turns = new ValueList<TurnRecord>([
                new TurnRecord { Index = 1, Status = TurnStatus.Completed, StartedAt = start, CompletedAt = start.AddSeconds(94), UserItemID = userOne.Id, Id = first },
                new TurnRecord { Index = 2, Status = TurnStatus.Completed, StartedAt = now.AddMinutes(-6), CompletedAt = now.AddMinutes(-5), UserItemID = userTwo.Id, Id = second },
            ]),
            Usage = new ContextUsage { UsedTokens = 61_400, WindowTokens = 200_000 },
        };
    }

    private static ToolCall Tool(ToolCall.Kind kind, string title, string? detail, string output, DateTimeOffset started, double seconds) => new()
    {
        ToolCallKind = kind, Title = title, Detail = detail, Output = output, ToolCallStatus = ToolCall.Status.Completed,
        StartedAt = started, FinishedAt = started.AddSeconds(seconds),
    };

    /// <summary>A short document for a thread that only needs something to show.</summary>
    internal static ThreadDocument Short(Guid threadId, DateTimeOffset now, string user, string reply)
    {
        var turn = Guid.NewGuid();
        var userItem = new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = user }), date: now.AddMinutes(-3));
        return new ThreadDocument(threadId)
        {
            Items = new ValueList<TimelineItem>([
                userItem,
                new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = reply }), date: now.AddMinutes(-2)),
                new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 12, FilesChanged = 0, Additions = 0, Deletions = 0 }), date: now.AddMinutes(-2)),
            ]),
            Turns = new ValueList<TurnRecord>([new TurnRecord { Index = 1, Status = TurnStatus.Completed, StartedAt = now.AddMinutes(-3), CompletedAt = now.AddMinutes(-2), UserItemID = userItem.Id, Id = turn }]),
        };
    }
}
