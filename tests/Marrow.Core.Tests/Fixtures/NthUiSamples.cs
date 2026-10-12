namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// DESIGN.md 13.1 NthUiTests (web surfaces W15; ED-NEW-web-w15-1): thirty nth-ui views covering every component of
/// the language, written in our own words. Each one parses with no degraded line in the full profile; the last-but-one
/// is the lite subset's sample and parses clean in the lite profile too. The first is the design's 7.2 sample and the
/// second the approved canvas's live answer (index.html section 5) with the three lets its abbreviated source leaves out.
/// </summary>
public static class NthUiSamples
{
    public const string DesignHosting = """
        card "Hosting estimate" icon=server
          state instances = 3
          state traffic = 40
          state region = "eu"
          let price = region == "eu" ? 0.046 : region == "us" ? 0.042 : 0.051
          stepper instances label="Instances" min=1 max=24
          slider traffic label="Traffic" min=0 max=200 step=5 unit="GB/day"
          segmented region options="eu:Europe,us:US,ap:Asia"
          table cols="Item,Monthly"
            row "Compute" = fmt(instances * price * 730, "$0")
            row "Egress" = fmt(traffic * 30 * 0.02, "$0")
            row total "Total" = fmt(instances * price * 730 + traffic * 30 * 0.02, "$0")
          chart bar x="Compute,Egress" y=[instances * price * 730, traffic * 30 * 0.02]
          actions
            copy "Copy as table" value=table
            send "Plan for twice the traffic"
        followups
          chip "Compare with a single large instance"
          chip "Add a CDN"
        """;

    public const string CanvasHosting = """
        card "Hosting estimate" icon=server
          state instances = 3
          state traffic = 40
          state region = "eu"
          let compute = instances * {eu: 0.046, us: 0.042, ap: 0.051}[region] * 730
          let egress = traffic * 30 * 0.02
          stepper instances label="Instances" min=1 max=24
          slider traffic label="Traffic" min=0 max=200 step=5 unit="GB/day"
          segmented region options="eu:Europe,us:US,ap:Asia"
          table cols="Item,Monthly"
            row "Compute" = fmt(compute, "$0")
            row "Egress" = fmt(egress, "$0")
            row "Storage" = "$9"
            row total "Total per month" = fmt(compute + egress + 9, "$0")
          chart bar x="Compute,Egress,Storage" y=[compute, egress, 9]
          actions
            copy "Copy as table" value=table
            send "Plan for twice the traffic"
        followups
          chip "Compare with one large instance"
          chip "Add a CDN"
          chip "Show a yearly view"
        """;

    public const string Mortgage = """
        card "Mortgage" icon=home
          state principal = 300000
          state rate = 4.5
          state years = 25
          let r = rate / 100 / 12
          let n = years * 12
          let monthly = principal * r / (1 - pow(1 + r, -n))
          slider principal label="Loan" min=50000 max=1000000 step=5000 unit="$"
          slider rate label="Rate" min=0.5 max=10 step=0.1 unit="%"
          stepper years label="Years" min=5 max=40 step=5
          grid cols=2
            stat "Monthly payment" = fmt(monthly, "$#,##0")
            stat "Total interest" = fmt(monthly * n - principal, "$#,##0")
          chart line title="Balance by year" x=(range(1, years + 1)) y=(map(range(1, years + 1), y => principal * (pow(1 + r, n) - pow(1 + r, y * 12)) / (pow(1 + r, n) - 1)))
        """;

    public const string Converter = """
        card "Unit converter"
          state amount = 1
          state from = "km"
          let factors = {km: 1000, mi: 1609.344, m: 1, ft: 0.3048}
          textfield amount label="Amount" placeholder="1"
          select from label="From" options="km:Kilometres,mi:Miles,m:Metres,ft:Feet"
          table cols="Unit,Value"
            row "Metres" = fmt(num(amount) * factors[from], "#,##0.##")
            row "Feet" = fmt(num(amount) * factors[from] / 0.3048, "#,##0.##")
            row "Miles" = fmt(num(amount) * factors[from] / 1609.344, "0.####")
        """;

    public const string Recipe = """
        card "Pancakes" icon=chef-hat
          text "Scale the recipe to the number of people."
          stepper people label="People" min=1 max=12
          state people = 4
          list title="Ingredients"
            item = fmt(people * 50, "0") + " g flour"
            item = fmt(people * 0.5, "0.#") + " eggs"
            item = fmt(people * 120, "0") + " ml milk"
          callout "Rest the batter for 20 minutes." tone=info
        """;

    public const string Checklist = """
        checklist "Before launch" bind=done
          item "Run the test suite" value=tests done
          item "Update the changelog" value=changelog
          item "Tag the release" value=tag
        state done type=selection = ["tests"]
        text = len(done) + " of 3 done"
        """;

    public const string Timeline = """
        timeline "Release 2.4"
          event "Code freeze" time="Mon 9:00" done
          event "Release candidate" time="Tue" detail="Built and signed"
          event "Staged rollout" time="Wed" tone=warning
          event "General availability" time="Thu"
        """;

    public const string Steps = """
        heading "Set up the dev server"
        steps "Setup" bind=step
          step "Install"
            text "Install the dependencies."
            code lang=bash
              npm install
          step "Configure"
            text "Copy the example settings."
            code lang=bash
              cp .env.example .env
          step "Run"
            code lang=bash
              npm run dev
            callout "The preview opens in Visus." tone=success
        """;

    public const string Tabs = """
        tabs bind=plan
          tab "Starter" value=starter
            stat "Price" = "$9 per month"
            list
              item "One project"
              item "Community support"
          tab "Team" value=team
            stat "Price" = "$29 per month"
            list ordered
              item "Unlimited projects"
              item "Shared workspaces"
        text = "You are looking at " + plan
        """;

    public const string Filter = """
        card "Languages"
          chips "Show" bind=langs
            chip "C#" value=cs selected
            chip "Swift" value=swift
            chip "Rust" value=rust
          table cols="Language,Typing"
            row "C#" "Static" when=(contains(langs, "cs"))
            row "Swift" "Static" when=(contains(langs, "swift"))
            row "Rust" "Static" when=(contains(langs, "rust"))
          text muted = len(langs) + " shown"
        """;

    public const string EditableChart = """
        card "Demand plan" icon=chart
          state demand = [12, 18, 25, 30, 28, 35]
          let total = sum(demand)
          let peak = max(demand)
          text "Drag a point to change a month."
          chart line x="Jan,Feb,Mar,Apr,May,Jun" editable=points bind=demand min=0 max=60 step=1 unit="units"
          grid cols=2
            stat "Total" = fmt(total, "#,##0")
            stat "Peak" = peak
        """;

    public const string Pie = """
        card "Budget split"
          state income = 4200
          stepper income label="Monthly income" min=1000 max=20000 step=100
          chart pie x="Needs,Wants,Savings" y=[income * 0.5, income * 0.3, income * 0.2]
          table cols="Share,Amount"
            row "Needs (50%)" = fmt(income * 0.5, "$#,##0")
            row "Wants (30%)" = fmt(income * 0.3, "$#,##0")
            row "Savings (20%)" = fmt(income * 0.2, "$#,##0")
        """;

    public const string Scatter = """
        chart scatter title="Price and rating"
          series "Phones" x=[199, 349, 499, 799, 999] y=[3.9, 4.2, 4.4, 4.6, 4.5]
          series "Tablets" x=[249, 449, 649] y=[4.0, 4.3, 4.7]
        text "Higher prices rate a little higher, up to about $800."
        """;

    public const string Area = """
        heading "Active users by quarter"
        chart area x="Q1,Q2,Q3,Q4" series="Web,Mobile" y=[[120, 140, 160, 210], [80, 95, 130, 170]] stacked
        text muted "Mobile grew faster in the second half."
        """;

    public const string Form = """
        form "Book a table" submit="Request booking"
          textfield name label="Name" placeholder="Your name"
          stepper guests label="Guests" min=1 max=10
          date day label="Day" min="2026-10-12" max="2026-12-31"
          select time label="Time" options="early:6 pm,mid:7 pm,late:8 pm"
          toggle outside label="Outside if the weather allows"
        """;

    public const string Status = """
        heading "Build status" level=2 icon=check
        row gap=8
          badge "main" tone=success
          badge "3 warnings" tone=warning
          badge "nightly" tone=neutral
        divider
        callout "Two tests are flaky on Windows." title="Heads up" tone=warning
        callout "Coverage is 87%." tone=info
        """;

    public const string Stats = """
        card "This week" subtitle="Compared with last week"
          grid cols=3
            stat "Visitors" value="12,480" delta="+8%" icon=users
            stat "Sign-ups" value=342 delta="-2%" tone=danger
            stat "Revenue" delta="+12%" tone=success = fmt(18230, "$#,##0")
        """;

    public const string Image = """
        card "Logo"
          image "data:image/png;base64,iVBORw0KGgo=" alt="The nth logo" width=64 height=64
          text "The mark at 64 pixels."
        """;

    public const string Diagram = """
        diagram "Request flow" alt="Client to server to database"
          graph LR
            A[Client] --> B[Server]
            B --> C[(Database)]
        text "The server is the only thing that talks to the database."
        """;

    public const string Svg = """
        svg alt="A rising line" width=120 height=40
          <svg viewBox="0 0 120 40"><polyline points="0,35 30,28 60,20 90,12 120,4" fill="none" stroke="currentColor"/></svg>
        text "Line art draws in the text colour."
        """;

    public const string Code = """
        text "Here is the function."
        code lang=python title="fib.py"
          def fib(n):
              a, b = 0, 1

              for _ in range(n):
                  a, b = b, a + b
              return a
        text "It runs in linear time."
        """;

    public const string Map = """
        map "Lisbon to Paris" zoom=4
          stop "Lisbon" lat=38.72 lon=-9.14
          stop "Madrid" lat=40.42 lon=-3.70 detail="Overnight"
          stop "Paris" lat=48.86 lon=2.35
          route "By train" via="Lisbon,Madrid,Paris"
        """;

    public const string Actions = """
        card "Release r42" icon=rocket
          state approved = false
          toggle approved label="I checked the staging build"
          actions
            button "Promote" primary when=approved
              call shipyard.promote {"release": "r42", "channel": "stable"}
              set approved = false
            open "Release notes" url="https://example.com/releases/r42"
            export csv "Download the table"
            context "release" = {release: "r42", approved: approved}
            send "Roll back r42"
            toggle approved "Undo the check"
          table cols="Check,Status"
            row "Tests" "Passed"
            row "Canary" = approved ? "Approved" : "Waiting"
        """;

    public const string Simple = """
        heading "Three ways to speed up the build"
        text "Each one is independent, so you can try them in any order."
        list ordered
          item "Turn on the build cache" detail="Saves about 40%"
          item "Split the test project"
          item "Skip the docs step in debug builds"
        followups
          chip "Show me how to turn on the cache"
          chip "Which one helps the most?"
        """;

    public const string Layout = """
        stack gap=12
          row align=between
            text strong "Plan"
            badge "Pro" tone=info
          row align=between
            text "Seats"
            text = fmt(seats, "0")
          row align=between
            text "Price"
            text = fmt(seats * 12, "$0")
          stepper seats label="Seats" min=1 max=50
        """;

    public const string Savings = """
        card "Savings plan"
          state monthly = 200
          state rate = 5
          slider monthly label="Monthly" min=50 max=2000 step=50 unit="$"
          slider rate label="Return" min=0 max=12 step=0.5 unit="%"
          table cols=["Year", "Saved", "Balance"] rows=(map(range(1, 11), y => [y, fmt(monthly * 12 * y, "$#,##0"), fmt(monthly * 12 * ((pow(1 + rate / 100, y) - 1) / (rate / 100 + 0.000001)), "$#,##0")]))
        """;

    public const string Tip = """
        card "Split the bill"
          state bill = 84
          state tip = 0.15
          state people = 3
          stepper bill label="Bill" min=0 max=1000 step=1 unit="$"
          segmented tip label="Tip" options=[[0.1, "10%"], [0.15, "15%"], [0.2, "20%"]]
          stepper people label="People" min=1 max=20
          stat "Each pays" = fmt(bill * (1 + tip) / people, "$0.00")
          text muted = "Tip " + fmt(tip, "0%") + " included"
        """;

    public const string Dates = """
        card "Trip"
          state start = "2026-11-02"
          date start label="Leave on"
          text = "You leave on " + fmt(start, "dddd, MMMM d")
          text = "Back by " + fmt("2026-11-16", "MMM d, yyyy")
        """;

    public const string Lite = """
        card "Room temperature"
          state target = 21
          let cost = (target - 18) * 4.5
          stepper target label="Target" min=16 max=26 unit="C"
          slider target label="Fine tune" min=16 max=26 step=0.5
          text = "About " + fmt(cost, "$0") + " a month above 18 degrees."
          table cols="Setting,Cost"
            row "Target" = fmt(target, "0.0")
            row total "Monthly" = fmt(cost, "$0")
          chart bar x="Heating" y=[cost]
          chips "Rooms" bind=rooms
            chip "Living room" selected
            chip "Bedroom"
          checklist "Tips"
            item "Close the curtains at night"
            item "Bleed the radiators" done
        followups
          chip "How do I bleed a radiator?"
        """;

    public const string Quiz = """
        card "Quick check"
          text "What does HTTP status 418 mean?"
          toggle reveal label="Show the answer"
          callout "I'm a teapot. It started as an April Fools' joke in 1998." tone=info when=reveal
          text muted "Try another one below." when=(not reveal)
        followups
          chip "Ask me another"
        """;

    /// <summary>All thirty, by name.</summary>
    public static IReadOnlyList<(string Name, string Text)> All { get; } =
    [
        (nameof(DesignHosting), DesignHosting),
        (nameof(CanvasHosting), CanvasHosting),
        (nameof(Mortgage), Mortgage),
        (nameof(Converter), Converter),
        (nameof(Recipe), Recipe),
        (nameof(Checklist), Checklist),
        (nameof(Timeline), Timeline),
        (nameof(Steps), Steps),
        (nameof(Tabs), Tabs),
        (nameof(Filter), Filter),
        (nameof(EditableChart), EditableChart),
        (nameof(Pie), Pie),
        (nameof(Scatter), Scatter),
        (nameof(Area), Area),
        (nameof(Form), Form),
        (nameof(Status), Status),
        (nameof(Stats), Stats),
        (nameof(Image), Image),
        (nameof(Diagram), Diagram),
        (nameof(Svg), Svg),
        (nameof(Code), Code),
        (nameof(Map), Map),
        (nameof(Actions), Actions),
        (nameof(Simple), Simple),
        (nameof(Layout), Layout),
        (nameof(Savings), Savings),
        (nameof(Tip), Tip),
        (nameof(Dates), Dates),
        (nameof(Lite), Lite),
        (nameof(Quiz), Quiz),
    ];
}
