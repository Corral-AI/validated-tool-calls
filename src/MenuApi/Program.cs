// The caterer's system. The menu lives here, not in the agent's code, so it can change without a redeploy.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var menu = new List<MenuEntry>
{
    new("gf_sandwich_platter", "GF sandwich platter (serves 10)", 60m, ["gluten", "nuts"]),
    new("sandwich_platter", "Sandwich platter (serves 10)", 50m, ["nuts"]),
    new("fruit_platter", "Fruit platter (serves 10)", 30m, ["nuts", "gluten", "dairy", "eggs"]),
    new("cheese_board", "Cheese board (serves 10)", 45m, ["nuts", "gluten", "eggs"]),
    new("lemonade_jug", "Lemonade jug (serves 8)", 3m, ["nuts", "gluten", "dairy", "eggs"]),
};

app.MapGet("/menu", () => menu);

// Add a dish live, mid-demo: the next tool call accepts it with no change to the agent.
app.MapPost("/menu", (MenuEntry entry) =>
{
    menu.RemoveAll(m => m.Id == entry.Id);
    menu.Add(entry);
    return Results.Created($"/menu/{entry.Id}", entry);
});

app.MapDelete("/menu/{id}", (string id) => menu.RemoveAll(m => m.Id == id) > 0 ? Results.NoContent() : Results.NotFound());

app.Run();

record MenuEntry(string Id, string Name, decimal Price, string[] FreeFrom);
