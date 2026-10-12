// PORT-ONLY: web surfaces W17, Teaching (DESIGN.md 7.3 "a compact reference and six worked examples"; ED-NEW-web-w17-1).
// No Swift counterpart. The worked examples the models are taught from, in our own words: what a reader asks, the
// sentence the assistant writes first, and the live view. Six go into every system prompt block and every MCP
// instruction text; six more come with the reference (ui_reference, topic examples); three are in the lite subset for
// small local models. The tests parse every view: each one is clean, has no warnings and computes without an error
// value, so a model is never taught a line the parser would degrade.

namespace Marrow.Core.NthUi;

/// <summary>One worked example: the reader's question, the sentence written before the view, and the view.</summary>
public sealed record NthUiExample(string Id, string Ask, string Lead, string View, string Why);

public static class NthUiExamples
{
    /// <summary>The six examples every full teaching block carries.</summary>
    public static IReadOnlyList<NthUiExample> Six { get; } =
    [
        new("savings", "How long until I reach a 12,000 savings goal if I put money aside each month?",
            "At 400 a month you reach 12,000 in 30 months. Move the sliders to try other amounts.",
            """
            card "Savings goal" icon=piggy-bank
              state goal = 12000
              state monthly = 400
              let months = ceil(goal / monthly)
              let years = floor(months / 12)
              stepper goal label="Goal" min=1000 max=100000 step=1000 unit="$"
              slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
              grid cols=2
                stat "Months" = months
                stat "About" = years + " years " + (months % 12) + " months"
              chart line title="Saved over the first year" x=(map(range(1, 13), m => "M" + m)) y=(map(range(1, 13), m => m * monthly))
            followups
              chip "What if I add 500 now?"
              chip "Show it with 3% interest"
            """,
            "A calculator: states the reader moves, lets that follow them, results in stats and a chart. Nothing is computed in your head; the view recomputes."),

        new("databases", "Compare Postgres, MySQL and SQLite for a small web app.",
            "For a small app SQLite is the least work and Postgres is the safest to grow into. Untick a database to hide its row.",
            """
            card "Choosing a database" subtitle="For a small web app"
              chips "Show" bind=picked
                chip "Postgres" value=pg selected
                chip "MySQL" value=my selected
                chip "SQLite" value=lite selected
              table cols="Database,Setup,Scales to,Good for"
                row "Postgres" "A server to run" "Very large" "Complex queries, JSON, extensions" when=(contains(picked, "pg"))
                row "MySQL" "A server to run" "Very large" "Read-heavy sites, wide hosting support" when=(contains(picked, "my"))
                row "SQLite" "One file" "One machine" "Prototypes, apps with a single writer" when=(contains(picked, "lite"))
              callout "Start with SQLite while one process writes the data. Move to Postgres when several services need it." tone=info
            followups
              chip "How do I move from SQLite to Postgres later?"
              chip "Which is cheapest to host?"
            """,
            "A comparison: a table the reader can filter. Rows that depend on a chip carry when=. One callout gives the advice."),

        new("study-hours", "Help me plan how many hours I study each day this week.",
            "Here is a starting plan of 13 hours. Drag any bar to change a day; the totals follow.",
            """
            card "Study plan" icon=book
              state hours = [2, 3, 1, 4, 2, 0, 1]
              let total = sum(hours)
              let busiest = max(hours)
              text "Drag a bar to change a day."
              chart bar x="Mon,Tue,Wed,Thu,Fri,Sat,Sun" editable=points bind=hours min=0 max=8 step=0.5 unit="h"
              grid cols=2
                stat "This week" = fmt(total, "0.#") + " h"
                stat "Busiest day" = fmt(busiest, "0.#") + " h"
              callout "More than 20 hours a week is a lot. Take a rest day." tone=warning when=(total > 20)
            """,
            "An editable chart: the data is a list state bound to the chart, so dragging a bar recomputes every let and stat."),

        new("moving", "I move house in four weeks. What should I do when?",
            "Spread over four weeks it is manageable. The first week matters most, so it is a checklist you can tick.",
            """
            card "Moving in four weeks" icon=truck
              timeline "The plan"
                event "Book movers and sort the paperwork" time="Week 1" done
                event "Pack the rooms you rarely use" time="Week 2"
                event "Change your address and move utilities" time="Week 3" tone=warning
                event "Final pack, clean, hand back the keys" time="Week 4"
              checklist "This week" bind=todo
                item "Get three quotes from movers" value=quotes
                item "Order boxes and tape" value=boxes
                item "Tell your landlord or agent" value=landlord
              text muted = len(todo) + " of 3 done"
            """,
            "A plan: a timeline for the whole span and a checklist for what to do now. The count under it reads the checklist's state."),

        new("oauth-flow", "How does signing in with OAuth work?",
            "The app never sees your password: it sends you to the provider, gets a one-time code back, and trades that code for a token.",
            """
            diagram "Authorization code flow" alt="The app redirects to the provider, which returns a code the app trades for a token"
              sequenceDiagram
                participant U as You
                participant A as The app
                participant P as The provider
                U->>A: Click sign in
                A->>P: Redirect with the app's id
                P->>U: Ask you to approve
                U->>P: Approve
                P->>A: Redirect back with a code
                A->>P: Trade the code for a token
                P->>A: Access token
            callout "The code is useless without the app's own secret, so a stolen redirect is not enough." tone=info
            """,
            "A diagram: Mermaid text indented under diagram, taken as written. Give it a title and an alt sentence."),

        new("laptop-finder", "Help me choose a laptop.",
            "A few details will narrow it down. Fill in what matters and press the button.",
            """
            form "Laptop finder" submit="Find laptops"
              segmented use label="Mostly for" options="work:Work,study:Study,games:Games,video:Video editing"
              slider budget label="Budget" min=400 max=3000 step=100 unit="$"
              select size label="Screen size" options="13:13 inch,14:14 inch,16:16 inch"
              toggle light label="Light enough to carry every day"
            """,
            "A form: when you need details before you can answer well, ask with inputs. Submitting sends the values back as the reader's next message."),
    ];

    /// <summary>Six more, shown with the reference.</summary>
    public static IReadOnlyList<NthUiExample> More { get; } =
    [
        new("lisbon-porto", "Plan a weekend trip from Lisbon to Porto.",
            "Two nights at each end with a lunch stop in Coimbra works well by train.",
            """
            card "Lisbon to Porto" icon=map
              map "The route" zoom=6
                stop "Lisbon" lat=38.72 lon=-9.14 detail="Two nights"
                stop "Coimbra" lat=40.21 lon=-8.43 detail="Lunch stop"
                stop "Porto" lat=41.15 lon=-8.61 detail="Two nights"
                route "By train" via="Lisbon,Coimbra,Porto"
              grid cols=3
                stat "Distance" = "about 310 km"
                stat "By train" = "about 3 h"
                stat "Stops" = 3
            """,
            "A map: stops need lat and lon; a route names the stops it joins in order."),

        new("release-board", "Summarise where our release stands.",
            "Two of three checks are green; the Windows tests are the blocker.",
            """
            heading "Release 2.4" level=2 icon=rocket
            grid cols=3
              stat "Tests" value="412 / 418" tone=warning
              stat "Open bugs" value=3 delta="-2" tone=success
              stat "Days left" value=4
            table cols="Check,Status"
              row "Build" "Passing"
              row "Unit tests" "Passing"
              row "Windows tests" "6 failing"
            callout "Fix the Windows tests first; everything else is ready." tone=warning
            """,
            "A status board: stats for the numbers, a small table for the checks, one callout for what to do."),

        new("plans", "What is the difference between the Starter and Team plans?",
            "Team adds shared projects and priority support for 20 dollars more.",
            """
            tabs bind=plan
              tab "Starter" value=starter
                stat "Price" = "$9 per month"
                list "Includes"
                  item "One project"
                  item "Community support"
              tab "Team" value=team
                stat "Price" = "$29 per month"
                list "Includes"
                  item "Unlimited projects"
                  item "Shared workspaces"
                  item "Priority support"
            """,
            "Tabs: one panel per option when each has its own details. The first tab is open to start with."),

        new("quick-quiz", "Quiz me on HTTP status codes.",
            "Here is one to start. Reveal the answer when you are ready.",
            """
            card "Quick check" icon=help-circle
              text "What does status 418 mean?"
              toggle reveal label="Show the answer"
              callout "I'm a teapot: a joke status from an April Fools RFC." tone=success when=reveal
            followups
              chip "Another one"
              chip "Make it harder"
            """,
            "A reveal: a toggle and a callout with when=. The followups chips send the reader's next message."),

        new("install-steps", "Walk me through installing the tool and running it.",
            "Three steps; the commands are ready to copy.",
            """
            steps "Install and run"
              step "Install"
                code lang=bash
                  npm install -g example-tool
              step "Create a config"
                code lang=bash title="example.config.json"
                  example-tool init
              step "Run it"
                code lang=bash
                  example-tool run --watch
            """,
            "Steps: the reader moves through a procedure one step at a time. Use it when order matters; for two commands, plain text is better."),

        new("quote-form", "I need a quote for a website.",
            "Tell me a little about the site and I will draft the quote.",
            """
            form "Website quote" submit="Draft my quote"
              textfield name label="Your name" placeholder="Alex Rivera"
              stepper pages label="Pages" min=1 max=40
              segmented kind label="Kind of site" options="brochure:Brochure,shop:Shop,blog:Blog"
              textfield notes label="Anything else" multiline
            """,
            "A form with text: textfield for free text, multiline for longer answers. The values come back as one message."),
    ];

    /// <summary>The lite examples for small local models: only the lite subset's kinds.</summary>
    public static IReadOnlyList<NthUiExample> Lite { get; } =
    [
        new("tip-split", "Split an 84 dollar bill between four people.",
            "With a 15 percent tip each person pays 24.15. Change the tip or the number of people.",
            """
            card "Split the bill"
              state bill = 84
              state people = 4
              state tip = 15
              let total = bill * (1 + tip / 100)
              let each = total / people
              stepper people label="People" min=1 max=20
              slider tip label="Tip" min=0 max=30 step=1 unit="%"
              table cols="Item,Amount"
                row "Bill" = fmt(bill, "$0.00")
                row "Tip" = fmt(bill * tip / 100, "$0.00")
                row total "Total" = fmt(total, "$0.00")
                row "Each" = fmt(each, "$0.00")
            followups
              chip "Split it unevenly"
            """,
            "A calculator: state, let, controls, a table of results."),

        new("packing", "What do I pack for a weekend walking trip?",
            "A short list covers it. Tick things off as you pack.",
            """
            card "Weekend packing"
              checklist "Pack these" bind=packed
                item "Phone charger" value=charger
                item "Toothbrush" value=toothbrush done
                item "Rain jacket" value=jacket
                item "Walking shoes" value=shoes
              text muted = len(packed) + " of 4 packed"
            """,
            "A checklist: items with a value, the state is the list of done values."),

        new("two-phones", "Compare the Pocket 4 and the Ultra 2 phones.",
            "The Ultra 2 has the bigger screen and battery, and costs about 400 dollars more.",
            """
            card "Two phones"
              chips "Compare" bind=picked
                chip "Pocket 4" value=a selected
                chip "Ultra 2" value=b selected
              table cols="Spec,Pocket 4,Ultra 2"
                row "Screen" "6.1 inch" "6.8 inch"
                row "Battery" "4,200 mAh" "5,000 mAh"
                row "Price" "$699" "$1,099"
              chart bar x="Pocket 4,Ultra 2" y=[699, 1099] unit="$"
            followups
              chip "Which has the better camera?"
            """,
            "A comparison: a table, a chart of the number that differs most, followups for the next question."),
    ];

    /// <summary>Questions that need no view, with the answer a good reply gives: plain text is the default.</summary>
    public static IReadOnlyList<(string Ask, string Answer)> PlainText { get; } =
    [
        ("What is the capital of Australia?", "Canberra."),
        ("Explain what a mutex is.", "A mutex is a lock that lets one thread at a time into a piece of code, so two threads cannot change the same data together."),
        ("How do I reverse a string in Python?", "Slice it with a step of -1: `text[::-1]`."),
    ];
}
