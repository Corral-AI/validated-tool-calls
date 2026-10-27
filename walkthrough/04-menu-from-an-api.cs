#:project ../src/ToolValidation
// Step 4: the menu lives in the caterer's API (start it first: dotnet run --project src/MenuApi).
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ToolValidation;

var http = new HttpClient { BaseAddress = new Uri("http://localhost:5180/") };
var services = new ServiceCollection().AddSingleton(new Menu(http)).BuildServiceProvider();
var menu = services.GetRequiredService<Menu>();
await menu.RefreshAsync();

// [OnMenu] resolves the Menu from the ValidationContext that MiniValidation passes the services into.
var orderCatering = AIFunctionFactory.Create(new ApiStage(menu).OrderCatering,
    new AIFunctionFactoryOptions { Name = "order_catering" }).WithValidation(services);

// No list in the schema any more: the model can't see the menu, so give it a get_menu tool too.
Demo.Show("What the model sees", orderCatering.JsonSchema);

Demo.Show("order_catering(chocolate_fountain, 1)",
    await Demo.WhatTheModelGets(orderCatering, new { item = "chocolate_fountain", quantity = 1 }));

// The caterer adds a dish. No redeploy, no change to the agent.
await http.PostAsJsonAsync("menu", new MenuEntry("chocolate_fountain", "Chocolate fountain", 250m, ["gluten", "nuts"]));
await menu.RefreshAsync();

Demo.Show("order_catering(chocolate_fountain, 1), after the caterer adds it",
    await Demo.WhatTheModelGets(orderCatering, new { item = "chocolate_fountain", quantity = 1 }));

await http.DeleteAsync("menu/chocolate_fountain");
