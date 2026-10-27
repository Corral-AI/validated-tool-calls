#:package Microsoft.Extensions.AI@10.11.0
// Step 1: a plain tool, made the way Agent Framework makes every tool.
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

var orderCatering = AIFunctionFactory.Create(OrderCatering, new AIFunctionFactoryOptions { Name = "order_catering" });

// This is all the model knows about the tool. The enum names are the only "display" it gets.
Show("What the model sees", orderCatering.JsonSchema);

Pause();
Show("order_catering(FruitPlatter, 4)",
    await WhatTheModelGets(orderCatering, new { item = "FruitPlatter", quantity = 4 }));

// A value that isn't in the enum. Binding fails before OrderCatering runs, and this is all the model is told.
Pause();
Show("order_catering(ChocolateFountain, 1)",
    await WhatTheModelGets(orderCatering, new { item = "ChocolateFountain", quantity = 1 }));

// ── The tool ─────────────────────────────────────────────────────────────

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

// The menu, as an enum.
enum MenuItem { GfSandwichPlatter, SandwichPlatter, FruitPlatter, CheeseBoard, LemonadeJug }

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
