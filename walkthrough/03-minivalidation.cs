#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.Extensions.AI@10.11.0
#:package MiniValidation@0.10.0
// Step 3: the same two tools, wrapped so their attributes actually run.
// The wrapper is ValidatingAIFunction, at the bottom of this file.
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MiniValidation;

var cakeOrDeath = new ValidatingAIFunction(
    AIFunctionFactory.Create(CakeOrDeath, new AIFunctionFactoryOptions { Name = "cake_or_death" }));

Show("What the model sees", cakeOrDeath.JsonSchema);

// CakeOrDeath never runs. The model gets problem details it can act on.
Pause();
Show("cake_or_death(chicken)", await WhatTheModelGets(cakeOrDeath, new { choice = "chicken" }));

var orderCatering = new ValidatingAIFunction(
    AIFunctionFactory.Create(OrderCatering, new AIFunctionFactoryOptions { Name = "order_catering" }));

// The enum case too: "Error: Function failed." becomes a list of what's allowed.
Pause();
Show("order_catering(ChocolateFountain, 1)",
    await WhatTheModelGets(orderCatering, new { item = "ChocolateFountain", quantity = 1 }));

// The catch: both lists are compiled in. A new dish means a redeploy, and every customer gets the same menu.

// ── The tools, unchanged from steps 1 and 2 ──────────────────────────────

[Description("Cake or death?")]
static string CakeOrDeath(
    [Description("Your choice.")] [RegularExpression("^(cake|death)$")] string choice) =>
    $"You chose {choice}.";

[Description("Order catering for the company picnic.")]
static string OrderCatering(
    [Description("A dish from the menu.")] MenuItem item,
    [Description("How many to order.")] int quantity) =>
    $"Ordered {quantity} x {item}.";

// ── Plumbing ─────────────────────────────────────────────────────────────

// Has a stand-in model call the tool once, through the real FunctionInvokingChatClient,
// and returns exactly what the model is sent back.
static async Task<object?> WhatTheModelGets(AIFunction tool, object arguments)
{
    using var client = new FunctionInvokingChatClient(new OneToolCall(tool.Name, arguments));
    var response = await client.GetResponseAsync("Go.", new ChatOptions { Tools = [tool] });
    return response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result;
}

// Waits for Enter, so each example appears when you're ready to talk about it.
static void Pause()
{
    Console.Write("\n[Enter for the next example]");
    Console.ReadLine();
}

static void Show(string title, object? value) =>
    Console.WriteLine($"\n── {title}\n{(value is string s ? s : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))}");

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

enum MenuItem { GfSandwichPlatter, SandwichPlatter, FruitPlatter, CheeseBoard, LemonadeJug }

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
