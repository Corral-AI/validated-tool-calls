using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using ToolValidation;

// dotnet run --project src/CateringChat -- --menu enum|regex|api --validation on|off
var stage = Arg("--menu", "api");
var validation = Arg("--validation", "on") == "on";

IChatClient chat = CreateChatClient();

var services = new ServiceCollection()
    .AddSingleton(new Menu(new HttpClient { BaseAddress = new Uri(Env("MENU_API_URL") ?? "http://localhost:5180/") }))
    .BuildServiceProvider();
var menu = services.GetRequiredService<Menu>();

// The chef is a subagent with no menu and big ideas. Its answers come back as a typed SuggestionList.
var chef = new ChatClientAgent(chat,
    instructions: "You are a flamboyant celebrity chef. Suggest show-stopping dishes, the more extravagant the better. " +
                  "Give each a short snake_case id, a quantity and a price per unit in pounds.",
    name: "chef");

AIFunction[] tools = stage switch
{
    "enum" => [Tool(EnumStage.OrderCatering, "order_catering")],
    "regex" => [Tool(RegexStage.CakeOrDeath, "cake_or_death")],
    "api" => ApiTools(new ApiStage(menu, chef)),
    _ => throw new ArgumentException($"Unknown stage '{stage}'. Use enum, regex or api."),
};

if (validation)
{
    tools = [.. tools.Select(t => t.WithValidation(services))];
}

var agent = new ChatClientAgent(chat,
    instructions: "You are the catering assistant for a company picnic. Use your tools to answer questions and place orders.",
    name: "assistant",
    tools: [.. tools.Select(t => new TracingAIFunction(t))]);

var session = await agent.CreateSessionAsync();

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"Stage: {stage}. Validation: {(validation ? "on" : "off")}. Type 'exit' to quit.");
Console.ResetColor();

while (true)
{
    Console.Write("\n> ");
    var input = Console.ReadLine();
    if (input is null || input.Trim() == "exit")
    {
        break;
    }

    if (stage == "api")
    {
        await menu.RefreshAsync();
    }

    var response = await agent.RunAsync(input, session);
    Console.WriteLine($"\n{response.Text}");
}

AIFunction[] ApiTools(ApiStage api) =>
[
    Tool(api.GetMenu, "get_menu"),
    Tool(api.OrderCatering, "order_catering"),
    Tool(api.CateringSuggestions, "catering_suggestions"),
];

static AIFunction Tool(Delegate method, string name) =>
    AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name });

static IChatClient CreateChatClient()
{
    // An OpenAI key if you have one; otherwise your Azure login against an Azure OpenAI or Foundry endpoint.
    if (Env("OPENAI_API_KEY") is { } key)
    {
        return new OpenAIClient(key).GetChatClient(Env("OPENAI_MODEL") ?? "gpt-4.1-mini").AsIChatClient();
    }

    var endpoint = Env("AZURE_OPENAI_ENDPOINT")
        ?? throw new InvalidOperationException("Set OPENAI_API_KEY, or AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_DEPLOYMENT.");
    var deployment = Env("AZURE_OPENAI_DEPLOYMENT")
        ?? throw new InvalidOperationException("Set AZURE_OPENAI_DEPLOYMENT to the name of a chat model deployment.");

    // The token-credential constructor is marked experimental (OPENAI001); fine for a demo.
#pragma warning disable OPENAI001
    var client = new OpenAIClient(
        new BearerTokenPolicy(new DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"),
        new OpenAIClientOptions { Endpoint = new Uri(new Uri(endpoint.TrimEnd('/') + "/"), "openai/v1/") });
#pragma warning restore OPENAI001
    return client.GetChatClient(deployment).AsIChatClient();
}

static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

string Arg(string name, string fallback) =>
    Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;

/// <summary>Prints every tool call the model makes and exactly what it gets back.</summary>
sealed class TracingAIFunction(AIFunction inner) : DelegatingAIFunction(inner)
{
    private static readonly JsonSerializerOptions Indented = new(AIJsonUtilities.DefaultOptions) { WriteIndented = true };

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        Write(ConsoleColor.Cyan, $"\n→ {Name}({JsonSerializer.Serialize(arguments, AIJsonUtilities.DefaultOptions)})");
        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            Write(ConsoleColor.Yellow, $"← {JsonSerializer.Serialize(result, Indented)}");
            return result;
        }
        catch (Exception ex)
        {
            // What the model is told by default when a tool throws.
            Write(ConsoleColor.Red, $"← {ex.GetType().Name}: {ex.Message}\n  (the model only sees \"Error: Function failed.\")");
            throw;
        }
    }

    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
