using System.Text.Json;
using JsonRepair;
using Spectre.Console;

// ============================================================================
// BIFTEKI CREW PRESENTS: JsonRepair.NET CLI
// ============================================================================

try {
    AnsiConsole.Clear();
}
catch {
    // Ignore clear when running in non-interactive redirected TTY
}

// 1. Header Banner
var headerGrid = new Grid();
headerGrid.AddColumn();
headerGrid.AddRow(new FigletText("BIFTEKI").Color(Color.Orange1));
headerGrid.AddRow(new FigletText("JSON REPAIR").Color(Color.Gold1));

var bannerPanel = new Panel(headerGrid)
    .Header("[bold gold1]🥩 BIFTEKI CREW HIGH-PERFORMANCE LABS 🥩[/]")
    .Border(BoxBorder.Double)
    .BorderColor(Color.Orange1)
    .Padding(1, 1);

AnsiConsole.Write(bannerPanel);
AnsiConsole.MarkupLine("[bold white]🔥 Flame-Grilling Malformed LLM JSON into Valid .NET 10 Specs 🔥[/]\n");

// 2. Sample Malformed Input
string rawBurntJson = """
```json
{
    // Unquoted keys and single quotes from LLM
    crew_member: 'Bifteki Chef',
    grill_temp_celsius: 450.5,
    is_flame_grilled: True,
    secret_seasoning: None,
    orders: [
        'Bifteki Special',
        'Garlic Butter Fries',
    ], // Trailing comma!
    restaurant_info: {
        location: 'Athens & Cyberpunk Alley',
        rating: 5.0,
    
```
""";

AnsiConsole.Write(new Panel(Markup.Escape(rawBurntJson.Trim()))
    .Header("[bold red]🥩 Raw / Burnt Input (Malformed LLM Output)[/]")
    .BorderColor(Color.Red)
    .Padding(1, 0));

AnsiConsole.WriteLine();

// 3. Interactive Grilling Simulation
AnsiConsole.Status()
    .Spinner(Spinner.Known.Star)
    .SpinnerStyle(Style.Parse("orange1 bold"))
    .Start("[bold yellow]Grilling malformed JSON on the Bifteki engine...[/]", ctx => {
        Thread.Sleep(300);
        ctx.Status("[bold yellow]Stripping markdown code fences & comments...[/]");
        Thread.Sleep(200);
        ctx.Status("[bold yellow]Normalizing single quotes & wrapping unquoted keys...[/]");
        Thread.Sleep(200);
        ctx.Status("[bold yellow]Converting Python literals (True -> true, None -> null)...[/]");
        Thread.Sleep(200);
        ctx.Status("[bold yellow]Auto-closing unclosed braces...[/]");
        Thread.Sleep(200);
    });

// 4. Execution & Output
string repairedJson = JsonRepairEngine.Repair(rawBurntJson);

// Prettify repaired JSON for terminal display
using var doc = JsonDocument.Parse(repairedJson);
string prettyRepaired = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

AnsiConsole.Write(new Panel(Markup.Escape(prettyRepaired))
    .Header("[bold green]✨ Flame-Grilled Output (Valid Standard JSON)[/]")
    .BorderColor(Color.Green)
    .Padding(1, 0));

AnsiConsole.WriteLine();

// 5. Bifteki Crew Status Badge Table
var table = new Table();
table.Border = TableBorder.Rounded;
table.BorderColor(Color.Gold1);
table.AddColumn("[bold gold1]Metric[/]");
table.AddColumn("[bold green]Status / Value[/]");

table.AddRow("Status", "[bold green]✓ REPAIRED & VALIDATED[/]");
table.AddRow("Target Framework", "[bold cyan].NET 10 (net10.0)[/]");
table.AddRow("State Machine Engine", "[bold orange1]Zero-Allocation Span Parsing[/]");
table.AddRow("Bifteki Crew Rating", "⭐⭐⭐⭐⭐ [bold gold1]100% PERFECTLY GRILLED[/]");

AnsiConsole.Write(table);
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[bold gold1]🥩 Powered by the Bifteki Crew - High Performance .NET Engineering 🥩[/]");
