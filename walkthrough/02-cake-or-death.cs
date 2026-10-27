#:package Microsoft.Extensions.AI@10.11.0
// Step 2: a validation attribute. AIFunctionFactory copies it into the schema, and that's all it does.
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

var cakeOrDeath = AIFunctionFactory.Create(CakeOrDeath, new AIFunctionFactoryOptions { Name = "cake_or_death" });

// [RegularExpression("^(cake|death)$")] shows up as "pattern".
Show("What the model sees", cakeOrDeath.JsonSchema);

Show("cake_or_death(cake)", await WhatTheModelGets(cakeOrDeath, new { choice = "cake" }));

// "Or chicken?" Nothing checks the pattern, so CakeOrDeath gets it.
Show("cake_or_death(chicken)", await WhatTheModelGets(cakeOrDeath, new { choice = "chicken" }));

// ── The tool ─────────────────────────────────────────────────────────────

[Description("Cake or death?")]
static string CakeOrDeath(
    [Description("Your choice.")] [RegularExpression("^(cake|death)$")] string choice) =>
    $"You chose {choice}.";

// ── Plumbing ─────────────────────────────────────────────────────────────

// Has a stand-in model call the tool once, through the real FunctionInvokingChatClient,
// and returns exactly what the model is sent back.
static async Task<object?> WhatTheModelGets(AIFunction tool, object arguments)
{
    using var client = new FunctionInvokingChatClient(new OneToolCall(tool.Name, arguments));
    var response = await client.GetResponseAsync("Go.", new ChatOptions { Tools = [tool] });
    return response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result;
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
