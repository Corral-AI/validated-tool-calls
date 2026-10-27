#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.Extensions.AI@10.11.0
#:package MiniValidation@0.10.0
// Step 5: a subagent called as a tool. Agent Framework's agent.AsAIFunction() takes a free-text
// query and returns free text. Here the chef takes typed input and returns a typed SuggestionList,
// and that result is validated before the calling agent sees it. Needs the menu API running.
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MiniValidation;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

var menu = new Menu(new HttpClient { BaseAddress = new Uri("http://localhost:5180/") });
var services = new ServiceCollection().AddSingleton(menu).BuildServiceProvider();
await menu.RefreshAsync();

var cateringSuggestions = new ValidatingAIFunction(
    AIFunctionFactory.Create(CateringSuggestions, new AIFunctionFactoryOptions { Name = "catering_suggestions" }), services);

Show("What the model sees", cateringSuggestions.JsonSchema);

// The calling agent gets problem details, not the chef's fantasy.
Example("catering_suggestions(40, [Nuts, Gluten]), flamboyant chef",
    await WhatTheModelGets(cateringSuggestions, new { headcount = 40, allergies = new[] { "Nuts", "Gluten" } }));

// A sensible chef: everything on the menu, at the caterer's prices. Validation passes it straight through.
var sensibleSuggestions = new ValidatingAIFunction(
    AIFunctionFactory.Create(SensibleCateringSuggestions, new AIFunctionFactoryOptions { Name = "catering_suggestions" }), services);

Example("catering_suggestions(40, [Nuts, Gluten]), sensible chef",
    await WhatTheModelGets(sensibleSuggestions, new { headcount = 40, allergies = new[] { "Nuts", "Gluten" } }));

// ── The tool ─────────────────────────────────────────────────────────────

// In the app this calls the chef subagent: await chef.RunAsync<SuggestionList>(...).
// Here it returns what a flamboyant chef might: a dish that isn't on the menu, and a price it made up.
[Description("Ask the chef for catering suggestions for the picnic.")]
static SuggestionList CateringSuggestions(
    [Description("How many people are coming.")] [Range(1, 500)] int headcount,
    [Description("Allergies to avoid.")] Allergen[] allergies) =>
    new(
    [
        new Suggestion("chocolate_fountain", 1, 500m),
        new Suggestion("fruit_platter", headcount / 10, 25m),
    ]);

// The same tool from a chef who read the menu.
[Description("Ask the chef for catering suggestions for the picnic.")]
static SuggestionList SensibleCateringSuggestions(
    [Description("How many people are coming.")] [Range(1, 500)] int headcount,
    [Description("Allergies to avoid.")] Allergen[] allergies) =>
    new(
    [
        new Suggestion("gf_sandwich_platter", headcount / 10, 60m),
        new Suggestion("fruit_platter", headcount / 10, 30m),
    ]);

// ── Plumbing ─────────────────────────────────────────────────────────────

// Has a stand-in model call the tool once, through the real FunctionInvokingChatClient,
// and returns exactly what the model is sent back.
static async Task<object?> WhatTheModelGets(AIFunction tool, object arguments)
{
    using var client = new FunctionInvokingChatClient(new OneToolCall(tool.Name, arguments));
    var response = await client.GetResponseAsync("Go.", new ChatOptions { Tools = [tool] });
    return response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result;
}

// Prints the tool call, waits for Enter, then shows what the model gets back.
static void Example(string call, object? whatTheModelGets)
{
    Console.Write($"\n── {call}");
    Console.ReadLine();
    Console.WriteLine(Json(whatTheModelGets));
}

static void Show(string title, object? value) => Console.WriteLine($"\n── {title}\n{Json(value)}");

static string Json(object? value) => value is string s ? s
    : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

// A stand-in for a model: it calls one tool with the arguments you give it, then stops.
sealed class OneToolCall(string tool, object arguments) : IChatClient
{
    private bool _called;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_called)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        }

        _called = true;
        var args = JsonSerializer.SerializeToElement(arguments).Deserialize<Dictionary<string, object?>>();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", tool, args)])));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

// ── The chef's answer ────────────────────────────────────────────────────

enum Allergen { Nuts, Gluten, Dairy, Eggs }

record SuggestionList([property: MinLength(1)] Suggestion[] Suggestions);

record Suggestion(
    [property: OnMenu] string Item,
    [property: Range(1, 50)] int Quantity,
    decimal PricePerUnit) : IValidatableObject
{
    // The price must match the caterer's own system, not whatever the chef wrote down.
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var menu = (Menu)context.GetService(typeof(Menu))!;
        if (menu.Find(Item) is { } entry && entry.Price != PricePerUnit)
        {
            yield return new ValidationResult($"The price of {Item} is {entry.Price}, not {PricePerUnit}.", [nameof(PricePerUnit)]);
        }
    }
}

// ── The caterer's menu ───────────────────────────────────────────────────

record MenuEntry(string Id, string Name, decimal Price, string[] FreeFrom);

// The current menu, fetched from the menu API. ValidationAttribute.IsValid is synchronous,
// so refresh this before validating and let the attribute read the snapshot.
sealed class Menu(HttpClient http)
{
    public IReadOnlyList<MenuEntry> Items { get; private set; } = [];

    public async Task RefreshAsync() => Items = await http.GetFromJsonAsync<List<MenuEntry>>("menu") ?? [];

    public MenuEntry? Find(string id) => Items.FirstOrDefault(i => i.Id == id);
}

// The value must be the id of a dish on the caterer's current menu.
// MiniValidation passes your services into the ValidationContext, so the attribute can find the Menu.
sealed class OnMenuAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var menu = (Menu)context.GetService(typeof(Menu))!;
        return value is null || menu.Find(value.ToString()!) is not null
            ? ValidationResult.Success
            : new ValidationResult($"'{value}' is not on the menu. Choose one of: {string.Join(", ", menu.Items.Select(i => i.Id))}.");
    }
}

// ── The validation wrapper ───────────────────────────────────────────────

// Runs the validation attributes that AIFunctionFactory only copies into the schema.
// Invalid arguments never reach the tool: the model gets RFC 9457 problem details instead.
// The tool's result is checked the same way before it goes back to the model.
sealed class ValidatingAIFunction(AIFunction inner, IServiceProvider? services = null) : DelegatingAIFunction(inner)
{
    private readonly MethodInfo _method = inner.UnderlyingMethod!;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var errors = await ValidateArgumentsAsync(arguments);
        if (errors.Count > 0)
        {
            return Problem("The tool call has invalid arguments. Fix them and call the tool again.", errors);
        }

        var result = await base.InvokeCoreAsync(arguments, cancellationToken);

        var resultErrors = await ValidateResultAsync(result);
        if (resultErrors.Count > 0)
        {
            return Problem("The tool ran but its result failed validation, so it was not used.", resultErrors);
        }

        return result;
    }

    private async Task<Dictionary<string, string[]>> ValidateArgumentsAsync(AIFunctionArguments arguments)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var parameter in _method.GetParameters())
        {
            var name = parameter.Name!;
            if (!arguments.TryGetValue(name, out var raw) || raw is null)
            {
                if (!parameter.HasDefaultValue)
                {
                    errors[name] = [$"The {name} field is required."];
                }
                continue;
            }

            // Bind the way AIFunctionFactory will, but turn a failure into a message the model can act on.
            object? value;
            try
            {
                value = JsonSerializer.SerializeToElement(raw, JsonSerializerOptions).Deserialize(parameter.ParameterType, JsonSerializerOptions);
            }
            catch (JsonException)
            {
                var allowed = parameter.ParameterType.IsEnum ? $" Allowed values: {string.Join(", ", Enum.GetNames(parameter.ParameterType))}." : "";
                errors[name] = [$"{JsonSerializer.Serialize(raw)} is not a valid {parameter.ParameterType.Name}.{allowed}"];
                continue;
            }

            // Attributes on the parameter itself, such as [RegularExpression] or [Range].
            var results = new List<ValidationResult>();
            var context = new ValidationContext(value ?? new object(), services, null) { MemberName = name, DisplayName = name };
            if (!Validator.TryValidateValue(value!, context, results, parameter.GetCustomAttributes<ValidationAttribute>()))
            {
                errors[name] = [.. results.Select(r => r.ErrorMessage ?? "Invalid value.")];
                continue;
            }

            // Records and classes: MiniValidation walks their properties, nested objects and IValidatableObject.
            if (value is not null && MiniValidator.RequiresValidation(value.GetType()))
            {
                var (isValid, nested) = await MiniValidator.TryValidateAsync(value, services!, recurse: true);
                if (!isValid)
                {
                    foreach (var (key, messages) in nested)
                    {
                        errors[$"{name}.{key}"] = messages;
                    }
                }
            }
        }

        return errors;
    }

    private async Task<Dictionary<string, string[]>> ValidateResultAsync(object? result)
    {
        // AIFunctionFactory has already serialised the return value, so read it back as the declared type.
        var returnType = _method.ReturnType.IsGenericType && _method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
            ? _method.ReturnType.GetGenericArguments()[0]
            : _method.ReturnType;
        if (result is not JsonElement json || !MiniValidator.RequiresValidation(returnType))
        {
            return [];
        }

        var value = json.Deserialize(returnType, JsonSerializerOptions)!;
        var (isValid, errors) = await MiniValidator.TryValidateAsync(value, services!, recurse: true);
        return isValid ? [] : new Dictionary<string, string[]>(errors);
    }

    // ASP.NET Core's own problem details type, returned as JSON so the model reads exactly what you see.
    private JsonElement Problem(string detail, IDictionary<string, string[]> errors) =>
        JsonSerializer.SerializeToElement(
            new HttpValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest, Detail = detail },
            JsonSerializerOptions);
}
