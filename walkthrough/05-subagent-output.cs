#:project ../src/ToolValidation
// Step 5: a subagent called as a tool. Agent Framework's agent.AsAIFunction() takes a free-text
// query and returns free text. Here the chef takes typed input and returns a typed SuggestionList,
// and that result is validated before the calling agent ever sees it.
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ToolValidation;

var services = new ServiceCollection()
    .AddSingleton(new Menu(new HttpClient { BaseAddress = new Uri("http://localhost:5180/") }))
    .BuildServiceProvider();
await services.GetRequiredService<Menu>().RefreshAsync();

// Stands in for the chef subagent's answer: an off-menu dish, and a price it made up.
SuggestionList ChefSays([Range(1, 500)] int headcount, Allergen[] allergies) => new(
[
    new Suggestion("chocolate_fountain", 1, 500m),
    new Suggestion("fruit_platter", headcount / 10, 25m),
]);

var cateringSuggestions = AIFunctionFactory.Create(ChefSays, new AIFunctionFactoryOptions { Name = "catering_suggestions" })
    .WithValidation(services);

Demo.Show("What the model sees", cateringSuggestions.JsonSchema);

// The calling agent gets problem details, not the chef's fantasy.
Demo.Show("catering_suggestions(40, [Nuts, Gluten])",
    await Demo.WhatTheModelGets(cateringSuggestions, new { headcount = 40, allergies = new[] { "Nuts", "Gluten" } }));
